namespace RdmApi.Data.Entities;

public class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string Actor { get; set; } = "anonymous";
    public string Action { get; set; } = null!;
    public Guid? DatasetId { get; set; }
    public string? DetailJson { get; set; }
}