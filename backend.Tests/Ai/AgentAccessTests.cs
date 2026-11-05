using Delegat.Infrastructure.Auth;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentAccessTests
{
    [Fact]
    public void ViewOnly_BlocksWritesAndPython()
    {
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Permissions.WorkspaceView,
        };

        Assert.True(AgentAccess.IsReadOnly(perms));
        Assert.False(AgentAccess.CanRunPython(perms));
        Assert.False(AgentAccess.CanMutate(perms, Permissions.CatalogManage));
        Assert.True(AgentAccess.CanRead(perms, Permissions.CatalogManage));
    }

    [Fact]
    public void Operator_CanMutateWhenGranted()
    {
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Permissions.AiUse,
            Permissions.CatalogManage,
            Permissions.FilesManage,
        };

        Assert.False(AgentAccess.IsReadOnly(perms));
        Assert.True(AgentAccess.CanRunPython(perms));
        Assert.True(AgentAccess.CanMutate(perms, Permissions.CatalogManage));
        Assert.False(AgentAccess.CanMutate(perms, Permissions.OrdersManage));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, true)]
    public void Viewer_AgentAndPythonAccessDependOnEffectiveGrants(
        bool workspaceView, bool aiUse, bool canUseAgent, bool canRunPython)
    {
        var permissions = AppRoles.RolePermissions[AppRoles.Viewer]
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(Permissions.AiUse, permissions);
        Assert.DoesNotContain(Permissions.WorkspaceView, permissions);
        if (workspaceView) permissions.Add(Permissions.WorkspaceView);
        if (aiUse) permissions.Add(Permissions.AiUse);

        Assert.Equal(canUseAgent, AgentAccess.CanUseAgent(permissions));
        Assert.Equal(canRunPython, AgentAccess.CanRunPython(permissions));
        Assert.Equal(workspaceView && !aiUse, AgentAccess.IsReadOnly(permissions));
    }
}
