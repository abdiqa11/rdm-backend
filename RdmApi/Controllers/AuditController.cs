using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RdmApi.Data;
using RdmApi.Security;
using System.Text.Json;

namespace RdmApi.Controllers;

[ApiController]
[Route("audit")]
public class AuditController : ControllerBase
{
    private readonly RdmDbContext _db;

    public AuditController(RdmDbContext db) => _db = db;

    [HttpGet]
    [RequireRole(Roles.Admin)]
    public async Task<IActionResult> List([FromQuery] Guid? datasetId = null, [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);

        var q = _db.AuditEvents
            .AsNoTracking()
            .AsQueryable();

        if (datasetId is not null)
            q = q.Where(x => x.DatasetId == datasetId);

        var raw = await q
            .OrderByDescending(x => x.Timestamp)
            .Take(take)
            .ToListAsync();

        var items = raw.Select(x => new
        {
            x.Timestamp,
            x.Actor,
            x.Action,
            Details = string.IsNullOrEmpty(x.DetailJson)
                ? null
                : JsonSerializer.Deserialize<object>(x.DetailJson)
        });

        return Ok(items);
    }
}