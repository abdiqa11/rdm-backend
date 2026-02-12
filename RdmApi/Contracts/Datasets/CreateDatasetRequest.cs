namespace RdmApi.Contracts.Datasets;

public record CreateDatasetRequest(
    string Title,
    string Creator,
    string? Description
);