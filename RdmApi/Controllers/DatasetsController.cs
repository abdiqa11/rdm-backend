using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RdmApi.Contracts.Datasets;
using RdmApi.Data;
using RdmApi.Data.Entities;
using System.Text.Json;
using RdmApi.Services;

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

    [HttpPost]
    public async Task<ActionResult<CreateDatasetResponse>> Create([FromBody] CreateDatasetRequest req)
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
            Actor = "anonymous",
            Action = "DATASET_REGISTERED",
            DatasetId = dataset.Id,
            DetailJson = JsonSerializer.Serialize(new { dataset.Title, dataset.Creator })
        });

        await _db.SaveChangesAsync();

        return Created($"/datasets/{dataset.Id}", new CreateDatasetResponse(
            dataset.Id,
            dataset.Title,
            dataset.Creator,
            dataset.Description,
            dataset.CreatedAt
        ));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var dataset = await _db.Datasets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (dataset is null) return NotFound();

        return Ok(new
        {
            dataset.Id,
            dataset.Title,
            dataset.Creator,
            dataset.Description,
            dataset.CreatedAt
        });
    }
    [HttpPost("{id:guid}/versions")]
    [RequestSizeLimit(1024L * 1024L * 1024L)] // 1GB (adjust later)
    public async Task<IActionResult> UploadVersion(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "File is required." });

        var dataset = await _db.Datasets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (dataset is null) return NotFound(new { error = "Dataset not found." });

        // Next version number
        var latest = await _db.DatasetVersions
            .Where(v => v.DatasetId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => (int?)v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var nextVersion = (latest ?? 0) + 1;

        var objectKey = $"datasets/{id}/v{nextVersion}/{file.FileName}";

        await using var stream = file.OpenReadStream();
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        await _store.PutAsync(objectKey, stream, contentType, file.Length, ct);

        var version = new DatasetVersion
        {
            DatasetId = id,
            VersionNumber = nextVersion,
            StorageKey = objectKey,
            SizeBytes = file.Length
        };

        _db.DatasetVersions.Add(version);

        _db.AuditEvents.Add(new AuditEvent
        {
            Actor = "anonymous",
            Action = "DATASET_VERSION_UPLOADED",
            DatasetId = id,
            DetailJson = JsonSerializer.Serialize(new { version = nextVersion, objectKey, size = file.Length })
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new { datasetId = id, version = nextVersion, storageKey = objectKey, size = file.Length });
    }
    [HttpGet("{id:guid}/versions/{version:int}")]
    public async Task<IActionResult> DownloadVersion(Guid id, int version, CancellationToken ct)
    {
        var dv = await _db.DatasetVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v =>
                v.DatasetId == id && v.VersionNumber == version, ct);

        if (dv is null || string.IsNullOrWhiteSpace(dv.StorageKey))
            return NotFound(new { error = "Version not found." });

        var (stream, contentType) = await _store.GetAsync(dv.StorageKey, ct);

        // If you want a nicer filename, we can store original name later.
        var fileName = Path.GetFileName(dv.StorageKey);

        return File(stream, contentType, fileName);
    }



}