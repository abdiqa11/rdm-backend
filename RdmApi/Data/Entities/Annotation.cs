namespace RdmApi.Data.Entities;

public class Annotation
{
    public long Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Ownership / provenance (prototype: store actor string)
    public string Actor { get; set; } = "anonymous";

    // Scope: annotation targets a dataset (and optionally a specific version)
    public Guid DatasetId { get; set; }
    public Dataset Dataset { get; set; } = null!;

    public Guid? DatasetVersionId { get; set; } // optional
    public DatasetVersion? DatasetVersion { get; set; }

    // Content
    public string Text { get; set; } = null!;
}
