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
using System.Linq;

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
            Description = req.Description?.Trim(),
            // Status default is in entity: Draft
            // Tags can remain null or empty depending on your entity definition
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

        // Versions overview
        var versionCount = await _db.DatasetVersions.AsNoTracking()
            .Where(v => v.DatasetId == id)
            .CountAsync(ct);

        var latestVersion = await _db.DatasetVersions.AsNoTracking()
            .Where(v => v.DatasetId == id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct); // null if none

        var totalSizeBytes = await _db.DatasetVersions.AsNoTracking()
            .Where(v => v.DatasetId == id)
            .Select(v => (long?)v.SizeBytes)
            .SumAsync(ct); // null if none

        // Annotations overview
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

            // ✅ Day 3 additions
            versionCount,
            latestVersion,                 // e.g. 3 (null if no versions)
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
    var file = req.File;
    var changeDescription = req.ChangeDescription;
    
    if (file is null || file.Length == 0)
        return BadRequest(new { error = "File is required." });

    var dataset = await _db.Datasets.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (dataset is null)
        return NotFound(new { error = "Dataset not found." });

    // Next version number
    var latest = await _db.DatasetVersions
        .Where(v => v.DatasetId == id)
        .OrderByDescending(v => v.VersionNumber)
        .Select(v => (int?)v.VersionNumber)
        .FirstOrDefaultAsync(ct);

    var nextVersion = (latest ?? 0) + 1;

    var objectKey = $"datasets/{id}/v{nextVersion}/{file.FileName}";

    await using var input = file.OpenReadStream();

    // Copy to memory so we can hash + upload (prototype OK)
    using var ms = new MemoryStream();
    await input.CopyToAsync(ms, ct);

    ms.Position = 0;

    // Compute SHA256
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

    // Upload to object store
    await _store.PutAsync(objectKey, ms, contentType, file.Length, ct);

    var version = new DatasetVersion
    {
        DatasetId = id,
        VersionNumber = nextVersion,
        StorageKey = objectKey,
        SizeBytes = file.Length,
        ContentHashSha256 = hashHex,
        ChangeDescription = changeDescription
    };

    _db.DatasetVersions.Add(version);

    _db.AuditEvents.Add(new AuditEvent
    {
        Actor = CurrentActor,
        Action = "DATASET_VERSION_UPLOADED",
        DatasetId = id,
        DetailJson = JsonSerializer.Serialize(new
        {
            version = nextVersion,
            objectKey,
            size = file.Length,
            changeDescription
        })
    });

    await _db.SaveChangesAsync(ct);

    return Ok(new
    {
        datasetId = id,
        version = nextVersion,
        storageKey = objectKey,
        size = file.Length,
        changeDescription
    });
}
    // -----------------------
    // Download version + integrity check
    // -----------------------
    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<IActionResult> DownloadVersion(Guid id, int version, CancellationToken ct)
    {
        var dv = await _db.DatasetVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.DatasetId == id && v.VersionNumber == version, ct);

        if (dv is null || string.IsNullOrWhiteSpace(dv.StorageKey))
            return NotFound(new { error = "Version not found." });

        var (stream, contentType) = await _store.GetAsync(dv.StorageKey, ct);

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        ms.Position = 0;

        // Verify integrity only if we have a stored hash
        if (!string.IsNullOrWhiteSpace(dv.ContentHashSha256))
        {
            string computed;
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(ms);
                computed = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            if (!string.Equals(computed, dv.ContentHashSha256, StringComparison.OrdinalIgnoreCase))
            {
                _db.AuditEvents.Add(new AuditEvent
                {
                    Actor = CurrentActor,
                    Action = "DATASET_VERSION_HASH_MISMATCH",
                    DatasetId = id,
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        version,
                        storageKey = dv.StorageKey,
                        expected = dv.ContentHashSha256,
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
            Action = "DATASET_VERSION_DOWNLOADED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new { version, storageKey = dv.StorageKey })
        });

        await _db.SaveChangesAsync(ct);

        var fileName = Path.GetFileName(dv.StorageKey);
        return File(ms.ToArray(), contentType, fileName);
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

        // Text search
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(d =>
                EF.Functions.Like(d.Title, $"%{term}%") ||
                EF.Functions.Like(d.Creator, $"%{term}%") ||
                (d.Description != null && EF.Functions.Like(d.Description, $"%{term}%")));
        }

        // ✅ Filter by status (governance maturity)
        if (status.HasValue)
            q = q.Where(d => d.Status == status.Value);

        // ✅ Filter by tag (category/discoverability)
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim();
            q = q.Where(d => d.Tags != null && d.Tags.Contains(t));
            // If Tags is initialized as Array.Empty<string>(), you can drop `d.Tags != null`
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
}