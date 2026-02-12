namespace RdmApi.Contracts.Datasets;

public record CreateDatasetResponse(
    Guid Id,
    string Title,
    string Creator,
    string? Description,
    DateTimeOffset CreatedAt
);