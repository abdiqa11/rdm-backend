using System.Security.Claims;
using RdmApi.Data.Entities;

namespace RdmApi.Security;

public class DatasetOwnershipAuthorizer
{
    public bool CanManageDataset(HttpContext httpContext, Dataset dataset)
    {
        return CanManageDataset(httpContext, dataset.OwnerId);
    }

    public bool CanManageDataset(HttpContext httpContext, string? datasetOwnerId)
    {
        var role = httpContext.Items["role"] as string ?? Roles.Viewer;

        if (role == Roles.Admin)
            return true;

        if (role != Roles.Researcher)
            return false;

        // Legacy datasets can have null owner; only admins can manage those.
        if (string.IsNullOrWhiteSpace(datasetOwnerId))
            return false;

        var currentOwnerId = GetCurrentOwnerId(httpContext);
        if (string.IsNullOrWhiteSpace(currentOwnerId))
            return false;

        return string.Equals(datasetOwnerId, currentOwnerId, StringComparison.Ordinal);
    }

    public string? GetCurrentOwnerId(HttpContext httpContext)
    {
        return httpContext.User.FindFirst("sub")?.Value
               ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? httpContext.Items["actor"] as string;
    }
}
