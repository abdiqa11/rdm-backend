using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;
using RdmApi.Data;
using RdmApi.Data.Entities;

namespace RdmApi.Security;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequireRoleAttribute : Attribute, IAsyncActionFilter
{
    private readonly string[] _allowed;

    public RequireRoleAttribute(params string[] allowedRoles)
    {
        _allowed = allowedRoles;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var role = http.Items["role"] as string ?? Roles.Viewer;
        var actor = http.Items["actor"] as string ?? "anonymous";

        if (_allowed.Contains(role))
        {
            await next();
            return;
        }

        // Try to capture dataset id if present (best effort)
        Guid? datasetId = null;
        if (context.RouteData.Values.TryGetValue("id", out var idObj) &&
            Guid.TryParse(idObj?.ToString(), out var parsed))
        {
            datasetId = parsed;
        }

        // Log denial (best effort)
        var db = http.RequestServices.GetService<RdmDbContext>();
        if (db != null)
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Actor = actor,
                Action = "ACCESS_DENIED",
                DatasetId = datasetId ?? Guid.Empty,
                DetailJson = JsonSerializer.Serialize(new
                {
                    required = _allowed,
                    role,
                    path = http.Request.Path.Value
                })
            });

            await db.SaveChangesAsync();
        }

        context.Result = new ObjectResult(new { error = "Forbidden", actor, role, required = _allowed })
{
    StatusCode = StatusCodes.Status403Forbidden
};
    }
}
