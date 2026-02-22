namespace RdmApi.Contracts.Datasets;

public record UpdateDatasetRequest(
    string Title,
    string? Description
);