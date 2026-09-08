using Delegat.Infrastructure.Auth;

namespace Delegat.Services.Ai.Agent;

public static class AgentAccess
{
    public static bool IsReadOnly(IReadOnlySet<string> permissions) =>
        Permissions.IsCabinetViewOnly(permissions);

    public static bool CanRead(IReadOnlySet<string> permissions, string resourcePermission)
    {
        if (permissions.Contains(resourcePermission, StringComparer.OrdinalIgnoreCase))
            return true;
        return permissions.Contains(Permissions.WorkspaceView, StringComparer.OrdinalIgnoreCase)
               && resourcePermission != Permissions.PluginsManage
               && resourcePermission != Permissions.SettingsDump;
    }

    public static bool CanMutate(IReadOnlySet<string> permissions, string resourcePermission)
    {
        if (IsReadOnly(permissions)) return false;
        return permissions.Contains(resourcePermission, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanRunPython(IReadOnlySet<string> permissions)
    {
        if (IsReadOnly(permissions)) return false;
        return permissions.Contains(Permissions.AiUse, StringComparer.OrdinalIgnoreCase);
    }

    public static bool CanUseAgent(IReadOnlySet<string> permissions) =>
        permissions.Contains(Permissions.AiUse, StringComparer.OrdinalIgnoreCase)
        || permissions.Contains(Permissions.WorkspaceView, StringComparer.OrdinalIgnoreCase);
}
