namespace RdmApi.Contracts.Datasets;

public record CreateAnnotationRequest(
    string Text,
    Guid? DatasetVersionId = null
);
