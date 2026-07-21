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
    public const string StoresCreate = "stores.create";
    public const string ReferenceRead = "reference.read";
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
        StoresCreate,
        ReferenceRead,
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
        StoresCreate,
        ReferenceRead,
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
}
