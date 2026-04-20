using Microsoft.Extensions.Configuration;
using RdmApi.Security;
using Xunit;

namespace RdmApi.Tests;

public class RoleResolutionTests
{
    [Fact]
    public void ResolveRole_UsesSubMappingBeforeEmailFallback()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["Authorization:DefaultRole"] = Roles.Viewer,
            ["Authorization:RoleMappings:sub-123"] = Roles.Admin,
            ["Authorization:RoleMappings:user@uia.no"] = Roles.Researcher
        });
        var resolver = new UserRoleResolver(cfg);

        var role = resolver.ResolveRole("sub-123", null, "user@uia.no");

        Assert.Equal(Roles.Admin, role);
    }

    [Fact]
    public void ResolveRole_FallsBackToEmailWhenSubNotMapped()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["Authorization:DefaultRole"] = Roles.Viewer,
            ["Authorization:RoleMappings:user@uia.no"] = Roles.Researcher
        });
        var resolver = new UserRoleResolver(cfg);

        var role = resolver.ResolveRole("sub-without-mapping", null, "user@uia.no");

        Assert.Equal(Roles.Researcher, role);
    }

    [Fact]
    public void ResolveRole_UsesDefaultViewerWhenNoTokenIdentity()
    {
        var cfg = BuildConfig(new Dictionary<string, string?>
        {
            ["Authorization:DefaultRole"] = Roles.Viewer
        });
        var resolver = new UserRoleResolver(cfg);

        var role = resolver.ResolveRole(null, null, null, null, null);

        Assert.Equal(Roles.Viewer, role);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
