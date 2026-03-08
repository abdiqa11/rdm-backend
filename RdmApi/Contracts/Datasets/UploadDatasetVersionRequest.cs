using Microsoft.AspNetCore.Http;

namespace RdmApi.Contracts.Datasets;

public sealed class UploadDatasetVersionRequest
{
    public IFormFile File { get; set; } = default!;
    public string? ChangeDescription { get; set; }
}