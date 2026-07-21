using Delegat.Infrastructure.Auth;

namespace Delegat.Tests;

public class PermissionsCatalogTests
{
    [Fact]
    public void Owner_HasAllAssignablePermissions()
    {
        var owner = AppRoles.RolePermissions[AppRoles.Owner];
        foreach (var perm in Permissions.Assignable)
            Assert.Contains(perm, owner);

        // roles.manage is instance-owner (isAdmin) only - injected at runtime, not on the role template.
        foreach (var perm in Permissions.AdminOnly)
            Assert.DoesNotContain(perm, owner);
    }

    [Fact]
    public void StoreManager_IncludesOperatorSet_PlusStoreManage()
    {
        var sm = AppRoles.RolePermissions[AppRoles.StoreManager];
        Assert.Contains(Permissions.StoreMembersManage, sm);
        Assert.Contains(Permissions.StoreSettingsManage, sm);
        Assert.Contains(Permissions.CatalogManage, sm);
        Assert.DoesNotContain(Permissions.UsersManage, sm);
    }

    [Fact]
    public void Viewer_IsMinimal()
    {
        var viewer = AppRoles.RolePermissions[AppRoles.Viewer];
        Assert.Contains(Permissions.ReferenceRead, viewer);
        Assert.DoesNotContain(Permissions.CatalogManage, viewer);
    }

    [Fact]
    public void IsStoreScoped_DetectsStorePermissions()
    {
        Assert.True(Permissions.IsStoreScoped(Permissions.CatalogManage));
        Assert.False(Permissions.IsStoreScoped(Permissions.UsersManage));
    }
}
