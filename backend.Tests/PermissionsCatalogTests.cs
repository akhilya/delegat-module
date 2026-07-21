using Delegat.Infrastructure.Auth;

namespace Delegat.Tests;

public class PermissionsCatalogTests
{
    [Fact]
    public void Owner_HasAllPermissions()
    {
        var owner = AppRoles.RolePermissions[AppRoles.Owner];
        foreach (var perm in Permissions.All)
            Assert.Contains(perm, owner);
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
