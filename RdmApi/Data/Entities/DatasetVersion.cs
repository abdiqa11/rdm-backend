namespace RdmApi.Data.Entities;

public class DatasetVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;

    public int VersionNumber { get; set; }
    public string? StorageKey { get; set; }
    public string? ContentHashSha256 { get; set; }
    public long? SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}