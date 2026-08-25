using System.Security.Claims;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Tests;

public class CabinetViewOnlyGuardTests
{
    [Fact]
    public void ThrowIfRestricted_Noops_WhenUserCanWrite()
    {
        var user = Principal(Permissions.WorkspaceView, Permissions.CatalogManage);
        CabinetViewOnlyGuard.ThrowIfRestricted(user);
    }

    [Fact]
    public void ThrowIfRestricted_Noops_WithoutWorkspaceView()
    {
        var user = Principal(Permissions.ReferenceRead);
        CabinetViewOnlyGuard.ThrowIfRestricted(user);
    }

    [Fact]
    public void ThrowIfRestricted_Forbids_ViewOnlyAccount()
    {
        var user = Principal(Permissions.WorkspaceView, Permissions.ReferenceRead);
        var ex = Assert.Throws<ApiException>(() => CabinetViewOnlyGuard.ThrowIfRestricted(user));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("cabinet_view_only", ex.Code);
    }

    private static ClaimsPrincipal Principal(params string[] permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
            identity.AddClaim(new Claim(Permissions.ClaimType, permission));
        return new ClaimsPrincipal(identity);
    }
}
