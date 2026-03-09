namespace RdmApi.Contracts.Datasets;

public sealed class CreateDatasetRelationshipRequest
{
    public Guid TargetDatasetId { get; set; }

    public string RelationType { get; set; } = default!;
}
