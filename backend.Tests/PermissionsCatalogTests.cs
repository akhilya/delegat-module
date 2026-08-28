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

        // Admin-only claims are instance-owner (isAdmin) only - injected at runtime, not on the role template.
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
        Assert.DoesNotContain(Permissions.WorkspaceView, viewer);
    }

    [Fact]
    public void WorkspaceView_IsAssignableAndNotOnViewer()
    {
        Assert.Contains(Permissions.WorkspaceView, Permissions.Assignable);
        Assert.Contains(Permissions.WorkspaceView, Permissions.Platform);
        Assert.Contains(Permissions.WorkspaceView, AppRoles.RolePermissions[AppRoles.Owner]);
        Assert.False(Permissions.IsStoreScoped(Permissions.WorkspaceView));
    }

    [Fact]
    public void IsCabinetViewOnly_RequiresViewAndNoWriteClaims()
    {
        Assert.True(Permissions.IsCabinetViewOnly(
            [Permissions.WorkspaceView, Permissions.ReferenceRead]));
        Assert.True(Permissions.IsCabinetViewOnly([Permissions.WorkspaceView]));
        Assert.False(Permissions.IsCabinetViewOnly([Permissions.ReferenceRead]));
        Assert.False(Permissions.IsCabinetViewOnly(
            [Permissions.WorkspaceView, Permissions.CatalogManage]));
        Assert.False(Permissions.IsCabinetViewOnly(
            AppRoles.RolePermissions[AppRoles.Owner]));
    }

    [Fact]
    public void AllowsWorkspaceViewRead_BlocksPluginsAndDatabaseDump()
    {
        Assert.True(Permissions.AllowsWorkspaceViewRead(Permissions.CatalogManage));
        Assert.True(Permissions.AllowsWorkspaceViewRead(Permissions.UsersManage));
        Assert.True(Permissions.AllowsWorkspaceViewRead(Permissions.SettingsPlatform));
        Assert.True(Permissions.AllowsWorkspaceViewRead(Permissions.RolesManage));
        Assert.False(Permissions.AllowsWorkspaceViewRead(Permissions.PluginsManage));
        Assert.False(Permissions.AllowsWorkspaceViewRead(Permissions.SettingsDump));
    }

    [Fact]
    public void SettingsDump_IsAdminOnly()
    {
        Assert.Contains(Permissions.SettingsDump, Permissions.AdminOnly);
        Assert.DoesNotContain(Permissions.SettingsDump, Permissions.Assignable);
        Assert.Contains(Permissions.SettingsDump, Permissions.Platform);
        Assert.DoesNotContain(Permissions.SettingsDump, AppRoles.RolePermissions[AppRoles.Owner]);
    }

    [Fact]
    public void IsStoreScoped_DetectsStorePermissions()
    {
        Assert.True(Permissions.IsStoreScoped(Permissions.CatalogManage));
        Assert.False(Permissions.IsStoreScoped(Permissions.UsersManage));
    }
}
