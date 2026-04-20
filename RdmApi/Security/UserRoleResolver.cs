using Microsoft.Extensions.Configuration;

namespace RdmApi.Security;

public class UserRoleResolver
{
    private readonly IConfiguration _configuration;

    public UserRoleResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ResolveRole(params string?[] identityCandidates)
    {
        var defaultRole =
            _configuration["Authorization:DefaultRole"] ?? Roles.Viewer;

        if (!Roles.IsValid(defaultRole))
            defaultRole = Roles.Viewer;

        foreach (var candidate in identityCandidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var mappedRole =
                _configuration[$"Authorization:RoleMappings:{candidate.Trim()}"];

            if (Roles.IsValid(mappedRole))
                return mappedRole!;
        }

        return defaultRole;
    }
}
