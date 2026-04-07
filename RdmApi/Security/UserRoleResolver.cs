using Microsoft.Extensions.Configuration;

namespace RdmApi.Security;

public class UserRoleResolver
{
    private readonly IConfiguration _configuration;

    public UserRoleResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ResolveRole(string? actor)
    {
        var defaultRole =
            _configuration["Authorization:DefaultRole"] ?? Roles.Viewer;

        if (!Roles.IsValid(defaultRole))
            defaultRole = Roles.Viewer;

        if (string.IsNullOrWhiteSpace(actor))
            return defaultRole;

        var trimmedActor = actor.Trim();

        var mappedRole =
            _configuration[$"Authorization:RoleMappings:{trimmedActor}"];

        if (Roles.IsValid(mappedRole))
            return mappedRole!;

        return defaultRole;
    }
}
