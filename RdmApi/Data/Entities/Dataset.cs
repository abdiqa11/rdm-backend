namespace RdmApi.Data.Entities;

public enum DatasetStatus
{
    Draft = 0,
    InReview = 1,
    Validated = 2,
    Published = 3,
    Archived = 4
}

public class Dataset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Creator { get; set; } = null!;
    public string? OwnerId { get; set; }
    public string? OwnerEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DatasetStatus Status { get; set; } = DatasetStatus.Draft;

    // Use empty array instead of null to avoid null headaches
    public string[] Tags { get; set; } = Array.Empty<string>();

    
    public ICollection<DatasetVersion> Versions { get; set; } = new List<DatasetVersion>();
}