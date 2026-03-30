namespace RdmApi.Data.Entities;

public class DatasetVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;

    public int VersionNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ChangeDescription { get; set; }

    public ICollection<DatasetVersionFile> Files { get; set; } = new List<DatasetVersionFile>();
}