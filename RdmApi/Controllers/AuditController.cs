using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RdmApi.Data;

namespace RdmApi.Controllers;

[ApiController]
[Route("audit")]
public class AuditController : ControllerBase
{
    private readonly RdmDbContext _db;

    public AuditController(RdmDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? datasetId = null, [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);

        var q = _db.AuditEvents.AsNoTracking();

        if (datasetId is not null)
            q = q.Where(x => x.DatasetId == datasetId);

        var items = await q
            .OrderByDescending(x => x.Timestamp)
            .Take(take)
            .ToListAsync();

        return Ok(items);
    }
}