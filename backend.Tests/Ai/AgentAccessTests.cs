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
}
