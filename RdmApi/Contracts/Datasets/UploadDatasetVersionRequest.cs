using Microsoft.AspNetCore.Http;

namespace RdmApi.Contracts.Datasets;

public sealed class UploadDatasetVersionRequest
{
    public List<IFormFile> Files { get; set; } = new();
    public IFormFile? ZipFile {get; set;} 
    public List<string>? RelativePaths { get; set; }
    public List<string>? RemovedPaths { get; set; }
    public string? ChangeDescription { get; set; }
}