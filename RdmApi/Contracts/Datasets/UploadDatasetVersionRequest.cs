using Microsoft.AspNetCore.Http;

namespace RdmApi.Contracts.Datasets;

public sealed class UploadDatasetVersionRequest
{
    /// <summary>
    /// Direct file upload mode input. Use this for non-ZIP uploads.
    /// When <see cref="ZipFile"/> is provided, this collection must be empty.
    /// </summary>
    public List<IFormFile> Files { get; set; } = new();

    /// <summary>
    /// ZIP upload mode input. Internal ZIP entry paths are authoritative and used for path-based
    /// replacement against the previous version snapshot.
    /// </summary>
    public IFormFile? ZipFile { get; set; }

    /// <summary>
    /// Relative paths for <see cref="Files"/> in direct file upload mode.
    /// This field is invalid when <see cref="ZipFile"/> is provided.
    /// </summary>
    public List<string>? RelativePaths { get; set; }

    /// <summary>
    /// Relative paths to remove from the new version snapshot.
    /// Can be used with ZIP mode or direct file upload mode.
    /// </summary>
    public List<string>? RemovedPaths { get; set; }

    /// <summary>
    /// Optional changelog text for the created dataset version.
    /// </summary>
    public string? ChangeDescription { get; set; }
}