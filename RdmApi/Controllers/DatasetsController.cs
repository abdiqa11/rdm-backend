using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RdmApi.Contracts.Datasets;
using RdmApi.Data;
using RdmApi.Data.Entities;
using RdmApi.Security;
using RdmApi.Services;
using System.Security.Cryptography;
using System.Text.Json;

namespace RdmApi.Controllers;

[ApiController]
[Route("datasets")]
public class DatasetsController : ControllerBase
{
    private readonly RdmDbContext _db;
    private readonly S3ObjectStore _store;

    public DatasetsController(RdmDbContext db, S3ObjectStore store)
    {
        _db = db;
        _store = store;
    }

    private string CurrentActor =>
        HttpContext.Items["actor"] as string ?? "anonymous";

    // -----------------------
    // Create dataset
    // -----------------------
    [HttpPost]
    public async Task<ActionResult<CreateDatasetResponse>> Create([FromBody] CreateDatasetRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Creator))
            return BadRequest(new { error = "Title and Creator are required." });

        var dataset = new Dataset
        {
            Title = req.Title.Trim(),
            Creator = req.Creator.Trim(),
            Description = req.Description?.Trim()
        };

        _db.Datasets.Add(dataset);

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_REGISTERED",
            DatasetId = dataset.Id,
            DetailJson = JsonSerializer.Serialize(new { dataset.Title, dataset.Creator })
        });

        await _db.SaveChangesAsync(ct);

        return Created($"/datasets/{dataset.Id}", new CreateDatasetResponse(
            dataset.Id,
            dataset.Title,
            dataset.Creator,
            dataset.Description,
            dataset.CreatedAt
        ));
    }

    // -----------------------
    // Get dataset by id
    // -----------------------
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var dataset = await _db.Datasets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (dataset is null) return NotFound();

        var versionCount = await _db.DatasetVersions.AsNoTracking()
            .Where(v => v.DatasetId == id)
            .CountAsync(ct);

        var latestVersion = await _db.DatasetVersions.AsNoTracking()
            .Where(v => v.DatasetId == id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);

        var totalSizeBytes = await _db.DatasetVersionFiles.AsNoTracking()
            .Where(f => f.DatasetVersion.DatasetId == id)
            .Select(f => (long?)f.SizeBytes)
            .SumAsync(ct);

        var annotationCount = await _db.Annotations.AsNoTracking()
            .Where(a => a.DatasetId == id)
            .CountAsync(ct);

        return Ok(new
        {
            dataset.Id,
            dataset.Title,
            dataset.Creator,
            dataset.Description,
            dataset.CreatedAt,
            dataset.Status,
            dataset.Tags,
            versionCount,
            latestVersion,
            totalSizeBytes = totalSizeBytes ?? 0,
            annotationCount
        });
    }

    // -----------------------
    // Upload version
    // -----------------------
    [HttpPost("{id:guid}/versions")]
[RequireRole(Roles.Admin, Roles.Researcher)]
[RequestSizeLimit(1024L * 1024L * 1024L)]
[Consumes("multipart/form-data")]
public async Task<IActionResult> UploadVersion(
    [FromRoute] Guid id,
    [FromForm] UploadDatasetVersionRequest req,
    CancellationToken ct)
{
    var files = req.Files ?? new List<IFormFile>();
    var relativePaths = req.RelativePaths ?? new List<string>();
    var removedPathsRaw = req.RemovedPaths ?? new List<string>();
    var changeDescription = req.ChangeDescription;

    if (files.Count == 0 && removedPathsRaw.Count == 0)
        return BadRequest(new { error = "At least one uploaded file or one removed path is required." });

    if (relativePaths.Count > 0 && relativePaths.Count != files.Count)
        return BadRequest(new { error = "RelativePaths count must match Files count." });

    var dataset = await _db.Datasets.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dataset is null)
        return NotFound(new { error = "Dataset not found." });

    var latestVersion = await _db.DatasetVersions
        .Include(v => v.Files)
        .Where(v => v.DatasetId == id)
        .OrderByDescending(v => v.VersionNumber)
        .FirstOrDefaultAsync(ct);

    var nextVersion = (latestVersion?.VersionNumber ?? 0) + 1;

    string NormalizeRelativePath(string? path, string fallbackFileName)
    {
        var value = string.IsNullOrWhiteSpace(path) ? fallbackFileName : path.Trim();
        value = value.Replace("\\", "/").TrimStart('/');

        while (value.Contains("//"))
            value = value.Replace("//", "/");

        return value;
    }

    string GetFileNameFromPath(string path)
    {
        var normalized = path.Replace("\\", "/");
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    var removedPaths = new HashSet<string>(
        removedPathsRaw
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => NormalizeRelativePath(p, p)),
        StringComparer.OrdinalIgnoreCase);

    var incomingFilesByPath = new Dictionary<string, IFormFile>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < files.Count; i++)
    {
        var file = files[i];

        if (file is null || file.Length == 0)
            return BadRequest(new { error = "All uploaded files must be non-empty." });

        var relativePath = NormalizeRelativePath(
            relativePaths.Count > 0 ? relativePaths[i] : null,
            file.FileName);

        if (removedPaths.Contains(relativePath))
            return BadRequest(new { error = $"Path '{relativePath}' cannot be both uploaded and removed in the same request." });

        if (incomingFilesByPath.ContainsKey(relativePath))
            return BadRequest(new { error = $"Duplicate uploaded path '{relativePath}'." });

        incomingFilesByPath[relativePath] = file;
    }

    var version = new DatasetVersion
    {
        DatasetId = id,
        VersionNumber = nextVersion,
        ChangeDescription = changeDescription
    };

    async Task<DatasetVersionFile> BuildFileEntryFromUploadAsync(IFormFile file, string relativePath)
    {
        var objectKey = $"datasets/{id}/v{nextVersion}/{relativePath}";

        await using var input = file.OpenReadStream();
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms, ct);
        ms.Position = 0;

        string hashHex;
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(ms);
            hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        ms.Position = 0;

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        await _store.PutAsync(objectKey, ms, contentType, file.Length, ct);

        return new DatasetVersionFile
        {
            FileName = GetFileNameFromPath(relativePath),
            RelativePath = relativePath,
            ObjectKey = objectKey,
            ContentHashSha256 = hashHex,
            SizeBytes = file.Length,
            ContentType = contentType
        };
    }

    var handledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var copiedPaths = new List<string>();
    var uploadedPaths = new List<string>();
    var deletedPaths = removedPaths.ToList();

    if (latestVersion is not null)
    {
        foreach (var existingFile in latestVersion.Files.OrderBy(f => f.RelativePath ?? f.FileName))
        {
            var existingPath = NormalizeRelativePath(existingFile.RelativePath, existingFile.FileName);

            if (removedPaths.Contains(existingPath))
                continue;

            if (incomingFilesByPath.TryGetValue(existingPath, out var replacementFile))
            {
                var uploadedEntry = await BuildFileEntryFromUploadAsync(replacementFile, existingPath);
                version.Files.Add(uploadedEntry);
                handledPaths.Add(existingPath);
                uploadedPaths.Add(existingPath);
            }
            else
            {
                var newObjectKey = $"datasets/{id}/v{nextVersion}/{existingPath}";
                await _store.CopyAsync(existingFile.ObjectKey, newObjectKey, ct);

                version.Files.Add(new DatasetVersionFile
                {
                    FileName = GetFileNameFromPath(existingPath),
                    RelativePath = existingPath,
                    ObjectKey = newObjectKey,
                    ContentHashSha256 = existingFile.ContentHashSha256,
                    SizeBytes = existingFile.SizeBytes,
                    ContentType = existingFile.ContentType
                });

                handledPaths.Add(existingPath);
                copiedPaths.Add(existingPath);
            }
        }
    }

    foreach (var pair in incomingFilesByPath)
    {
        if (handledPaths.Contains(pair.Key))
            continue;

        var uploadedEntry = await BuildFileEntryFromUploadAsync(pair.Value, pair.Key);
        version.Files.Add(uploadedEntry);
        handledPaths.Add(pair.Key);
        uploadedPaths.Add(pair.Key);
    }

    if (version.Files.Count == 0)
        return BadRequest(new { error = "The resulting dataset version would contain no files." });

    _db.DatasetVersions.Add(version);

    _db.AuditEvents.Add(new AuditEvent
    {
        Actor = CurrentActor,
        Action = "DATASET_VERSION_UPLOADED",
        DatasetId = id,
        DetailJson = JsonSerializer.Serialize(new
        {
            version = nextVersion,
            uploadedPaths,
            copiedPaths,
            removedPaths = deletedPaths,
            changeDescription
        })
    });

    await _db.SaveChangesAsync(ct);

    return Ok(new
    {
        datasetId = id,
        version = nextVersion,
        files = version.Files
            .OrderBy(f => f.RelativePath)
            .Select(f => new
            {
                fileName = f.FileName,
                relativePath = f.RelativePath,
                objectKey = f.ObjectKey,
                size = f.SizeBytes,
                contentHashSha256 = f.ContentHashSha256,
                contentType = f.ContentType
            }),
        uploadedPaths,
        copiedPaths,
        removedPaths = deletedPaths,
        changeDescription
    });
}

        // -----------------------
    // Get version details (metadata + files)
    // -----------------------
    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<IActionResult> GetVersion(Guid id, int version, CancellationToken ct)
    {
        var dv = await _db.DatasetVersions
            .AsNoTracking()
            .Include(v => v.Files)
            .FirstOrDefaultAsync(v => v.DatasetId == id && v.VersionNumber == version, ct);

        if (dv is null)
            return NotFound(new { error = "Version not found." });

        var files = dv.Files
            .OrderBy(f => f.RelativePath)
            .Select(f => new
            {
                fileName = f.FileName,
                relativePath = f.RelativePath,
                sizeBytes = f.SizeBytes,
                contentType = f.ContentType,
                downloadUrl = $"/datasets/{id}/versions/{version}/files/download?path={Uri.EscapeDataString(f.RelativePath ?? f.FileName)}"
            });

        return Ok(new
        {
            datasetId = id,
            version = dv.VersionNumber,
            dv.CreatedAt,
            dv.ChangeDescription,
            fileCount = dv.Files.Count,
            files
        });
    }

    // -----------------------
    // Download one specific file from a version + integrity check
    // -----------------------
    [HttpGet("{id:guid}/versions/{version:int}/files/download")]
    public async Task<IActionResult> DownloadVersionFile(
        Guid id,
        int version,
        [FromQuery] string path,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest(new { error = "Query parameter 'path' is required." });

        string NormalizeRelativePath(string value)
        {
            value = value.Trim().Replace("\\", "/").TrimStart('/');

            while (value.Contains("//"))
                value = value.Replace("//", "/");

            return value;
        }

        var normalizedPath = NormalizeRelativePath(path);

        var dv = await _db.DatasetVersions
            .Include(v => v.Files)
            .FirstOrDefaultAsync(v => v.DatasetId == id && v.VersionNumber == version, ct);

        if (dv is null)
            return NotFound(new { error = "Version not found." });

        var versionFile = dv.Files
            .FirstOrDefault(f =>
                string.Equals(
                    NormalizeRelativePath(f.RelativePath ?? f.FileName),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase));

        if (versionFile is null || string.IsNullOrWhiteSpace(versionFile.ObjectKey))
            return NotFound(new { error = "File not found in this version." });

        var (stream, contentType) = await _store.GetAsync(versionFile.ObjectKey, ct);

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        ms.Position = 0;

        if (!string.IsNullOrWhiteSpace(versionFile.ContentHashSha256))
        {
            string computed;
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(ms);
                computed = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (!string.Equals(computed, versionFile.ContentHashSha256, StringComparison.OrdinalIgnoreCase))
            {
                _db.AuditEvents.Add(new AuditEvent
                {
                    Actor = CurrentActor,
                    Action = "DATASET_VERSION_FILE_HASH_MISMATCH",
                    DatasetId = id,
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        version,
                        relativePath = versionFile.RelativePath,
                        fileName = versionFile.FileName,
                        storageKey = versionFile.ObjectKey,
                        expected = versionFile.ContentHashSha256,
                        actual = computed
                    })
                });

                await _db.SaveChangesAsync(ct);

                return Conflict(new { error = "Integrity check failed (hash mismatch)." });
            }

            ms.Position = 0;
        }

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_VERSION_FILE_DOWNLOADED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new
            {
                version,
                relativePath = versionFile.RelativePath,
                fileName = versionFile.FileName,
                storageKey = versionFile.ObjectKey
            })
        });

        await _db.SaveChangesAsync(ct);

        return File(ms.ToArray(), contentType, versionFile.FileName);
    }

  
    // -----------------------
    // Search (basic)
    // -----------------------
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] DatasetStatus? status,
        [FromQuery] string? tag,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(offset, 0);

        var q = _db.Datasets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(d =>
                EF.Functions.Like(d.Title, $"%{term}%") ||
                EF.Functions.Like(d.Creator, $"%{term}%") ||
                (d.Description != null && EF.Functions.Like(d.Description, $"%{term}%")));
        }

        if (status.HasValue)
            q = q.Where(d => d.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim();
            q = q.Where(d => d.Tags != null && d.Tags.Contains(t));
        }

        var results = await q
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.Creator,
                d.Description,
                d.CreatedAt,
                d.Status,
                d.Tags
            })
            .ToListAsync(ct);

        return Ok(new
        {
            query,
            status,
            tag,
            limit,
            offset,
            count = results.Count,
            results
        });
    }

    // -----------------------
    // Update metadata
    // -----------------------
    [HttpPut("{id:guid}")]
    [RequireRole(Roles.Admin, Roles.Researcher)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDatasetRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { error = "Title is required." });

        var dataset = await _db.Datasets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (dataset is null) return NotFound(new { error = "Dataset not found." });

        dataset.Title = req.Title.Trim();
        dataset.Description = req.Description?.Trim();

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_METADATA_UPDATED",
            DatasetId = dataset.Id,
            DetailJson = JsonSerializer.Serialize(new { dataset.Title, dataset.Description })
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            dataset.Id,
            dataset.Title,
            dataset.Creator,
            dataset.Description,
            dataset.CreatedAt,
            dataset.Status,
            dataset.Tags
        });
    }

    // -----------------------
    // Annotations
    // -----------------------
    [HttpPost("{id:guid}/annotations")]
    [RequireRole(Roles.Admin, Roles.Researcher)]
    public async Task<IActionResult> CreateAnnotation(Guid id, [FromBody] CreateAnnotationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "Text is required." });

        var datasetExists = await _db.Datasets.AsNoTracking().AnyAsync(d => d.Id == id, ct);
        if (!datasetExists)
            return NotFound(new { error = "Dataset not found." });

        if (req.DatasetVersionId is not null)
        {
            var ok = await _db.DatasetVersions.AsNoTracking()
                .AnyAsync(v => v.Id == req.DatasetVersionId && v.DatasetId == id, ct);

            if (!ok)
                return BadRequest(new { error = "DatasetVersionId is invalid for this dataset." });
        }

        var annotation = new Annotation
        {
            DatasetId = id,
            DatasetVersionId = req.DatasetVersionId,
            Actor = CurrentActor,
            Text = req.Text.Trim()
        };

        _db.Annotations.Add(annotation);

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_ANNOTATION_CREATED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new
            {
                annotationId = annotation.Id,
                datasetVersionId = req.DatasetVersionId
            })
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            annotation.Id,
            annotation.DatasetId,
            annotation.DatasetVersionId,
            annotation.Actor,
            annotation.Text,
            annotation.CreatedAt
        });
    }

    [HttpGet("{id:guid}/annotations")]
    public async Task<IActionResult> ListAnnotations(Guid id, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var datasetExists = await _db.Datasets.AsNoTracking().AnyAsync(d => d.Id == id, ct);
        if (!datasetExists)
            return NotFound(new { error = "Dataset not found." });

        var items = await _db.Annotations.AsNoTracking()
            .Where(a => a.DatasetId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.DatasetId,
                a.DatasetVersionId,
                a.Actor,
                a.Text,
                a.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(new { datasetId = id, take, count = items.Count, items });
    }

    // -----------------------
    // Status
    // -----------------------
    [HttpPut("{id:guid}/status")]
    [RequireRole(Roles.Researcher, Roles.Admin)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateDatasetStatusRequest req, CancellationToken ct)
    {
        var ds = await _db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (ds is null) return NotFound(new { error = "Dataset not found." });

        ds.Status = req.Status;

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_STATUS_UPDATED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new { status = ds.Status.ToString() })
        });

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // -----------------------
    // Tags
    // -----------------------
    [HttpPut("{id:guid}/tags")]
    [RequireRole(Roles.Researcher, Roles.Admin)]
    public async Task<IActionResult> UpdateTags(Guid id, [FromBody] UpdateDatasetTagsRequest req, CancellationToken ct)
    {
        var ds = await _db.Datasets.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (ds is null) return NotFound(new { error = "Dataset not found." });

        var tags = (req.Tags ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ds.Tags = tags;

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_TAGS_UPDATED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new { tags = ds.Tags })
        });

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // -----------------------
    // Create dataset relationship
    // -----------------------
    [HttpPost("{id:guid}/relationships")]
    [RequireRole(Roles.Admin, Roles.Researcher)]
    public async Task<IActionResult> CreateRelationship(
        [FromRoute] Guid id,
        [FromBody] CreateDatasetRelationshipRequest req,
        CancellationToken ct)
    {
        var source = await _db.Datasets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (source is null)
            return NotFound(new { error = "Source dataset not found." });

        var target = await _db.Datasets.FirstOrDefaultAsync(x => x.Id == req.TargetDatasetId, ct);
        if (target is null)
            return NotFound(new { error = "Target dataset not found." });

        var rel = new DatasetRelationship
        {
            SourceDatasetId = id,
            TargetDatasetId = req.TargetDatasetId,
            RelationType = req.RelationType,
        };

        _db.DatasetRelationships.Add(rel);

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = CurrentActor,
            Action = "DATASET_RELATIONSHIP_CREATED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new
            {
                sourceDatasetId = id,
                targetDatasetId = req.TargetDatasetId,
                relationType = req.RelationType
            })
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            sourceDatasetId = id,
            targetDatasetId = req.TargetDatasetId,
            relationType = req.RelationType
        });
    }

    [HttpGet("{id:guid}/audit")]
    [RequireRole(Roles.Admin, Roles.Researcher)]
    public async Task<IActionResult> GetDatasetAudit(
        [FromRoute] Guid id,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var datasetExists = await _db.Datasets
            .AnyAsync(d => d.Id == id, ct);

        if (!datasetExists)
            return NotFound(new { error = "Dataset not found." });

        var raw = await _db.AuditEvents
            .AsNoTracking()
            .Where(a => a.DatasetId == id)
            .OrderByDescending(a => a.Timestamp)
            .Take(take)
            .ToListAsync(ct);

        var events = raw.Select(a => new
        {
            a.Timestamp,
            a.Actor,
            a.Action,
            Details = string.IsNullOrEmpty(a.DetailJson)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<object>(a.DetailJson)
        });

        return Ok(events);
    }
}