namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Permission claim type and catalog. Stored as Identity role/user claims.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    // Platform
    public const string UsersManage = "users.manage";
    /// <summary>Assign roles/permissions. Instance owner (isAdmin) only - never stored as a grant.</summary>
    public const string RolesManage = "roles.manage";
    public const string PluginsManage = "plugins.manage";
    public const string SettingsPlatform = "settings.platform";
    /// <summary>Download a full MySQL dump. Instance owner (isAdmin) only - never stored as a grant.</summary>
    public const string SettingsDump = "settings.dump";
    public const string StoresCreate = "stores.create";
    public const string ReferenceRead = "reference.read";
    /// <summary>Browse the cabinet without mutating. Does not cover plugins.</summary>
    public const string WorkspaceView = "workspace.view";
    /// <summary>Create/manage Public API keys and view API request logs.</summary>
    public const string ApiAccess = "api.access";

    // Store-scoped (require store membership at runtime)
    public const string StoreMembersManage = "store.members.manage";
    public const string StoreSettingsManage = "store.settings.manage";
    public const string CatalogManage = "catalog.manage";
    public const string MarketplaceManage = "marketplace.manage";
    public const string FilesManage = "files.manage";
    public const string OperationsManage = "operations.manage";
    public const string AiUse = "ai.use";

    public static IReadOnlyList<string> All { get; } =
    [
        UsersManage,
        RolesManage,
        PluginsManage,
        SettingsPlatform,
        SettingsDump,
        StoresCreate,
        ReferenceRead,
        WorkspaceView,
        ApiAccess,
        StoreMembersManage,
        StoreSettingsManage,
        CatalogManage,
        MarketplaceManage,
        FilesManage,
        OperationsManage,
        AiUse,
    ];

    /// <summary>Never assignable via roles/grants - injected only for User.isAdmin.</summary>
    public static IReadOnlyList<string> AdminOnly { get; } =
    [
        RolesManage,
        SettingsDump,
    ];

    /// <summary>Permissions that may appear on role templates or user grants.</summary>
    public static IReadOnlyList<string> Assignable { get; } =
        All.Where(p => !AdminOnly.Contains(p)).ToList();

    public static IReadOnlyList<string> Platform { get; } =
    [
        UsersManage,
        RolesManage,
        PluginsManage,
        SettingsPlatform,
        SettingsDump,
        StoresCreate,
        ReferenceRead,
        WorkspaceView,
        ApiAccess,
    ];

    public static IReadOnlyList<string> StoreScoped { get; } =
    [
        StoreMembersManage,
        StoreSettingsManage,
        CatalogManage,
        MarketplaceManage,
        FilesManage,
        OperationsManage,
        AiUse,
    ];

    public static bool IsStoreScoped(string permission) =>
        StoreScoped.Contains(permission);

    public static bool IsAdminOnly(string permission) =>
        AdminOnly.Contains(permission);

    public static bool IsKnown(string permission) =>
        All.Contains(permission);

    public static bool IsSafeHttpMethod(string? method)
    {
        if (string.IsNullOrEmpty(method)) return false;
        return HttpMethods.IsGet(method)
            || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method);
    }

    public static bool AllowsWorkspaceViewRead(string requiredPermission) =>
        !string.Equals(requiredPermission, PluginsManage, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(requiredPermission, SettingsDump, StringComparison.OrdinalIgnoreCase);

    public static bool UserMayRead(IReadOnlySet<string> userPermissions, string requiredPermission)
    {
        if (userPermissions.Contains(requiredPermission))
            return true;
        return userPermissions.Contains(WorkspaceView)
            && AllowsWorkspaceViewRead(requiredPermission);
    }

    public static bool UserMayReadAny(IReadOnlySet<string> userPermissions, IEnumerable<string> requiredPermissions)
    {
        foreach (var required in requiredPermissions)
        {
            if (UserMayRead(userPermissions, required))
                return true;
        }
        return false;
    }

    public static bool IsCabinetViewOnly(IEnumerable<string>? permissions)
    {
        if (permissions is null) return false;
        var set = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(WorkspaceView)) return false;
        set.Remove(WorkspaceView);
        set.Remove(ReferenceRead);
        return set.Count == 0;
    }
}
