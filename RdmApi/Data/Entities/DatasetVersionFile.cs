namespace RdmApi.Data.Entities;

public class DatasetVersionFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetVersionId { get; set; }
    public DatasetVersion DatasetVersion { get; set; } = null!;

    public string FileName { get; set; } = null!;
    public string? RelativePath { get; set; }

    public string ObjectKey { get; set; } = null!;
    public string? ContentHashSha256 { get; set; }
    public long SizeBytes { get; set; }
    public string? ContentType { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
