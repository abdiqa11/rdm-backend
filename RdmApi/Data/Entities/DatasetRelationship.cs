using System;

namespace RdmApi.Data.Entities;

public class DatasetRelationship
{
    public long Id { get; set; }

    public Guid SourceDatasetId { get; set; }

    public Guid TargetDatasetId { get; set; }

    public string RelationType { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Dataset SourceDataset { get; set; } = default!;

    public Dataset TargetDataset { get; set; } = default!;
}
