namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Built-in Identity role templates and their permission claims.
/// </summary>
public static class AppRoles
{
    public const string Owner = "Owner";
    public const string Operator = "Operator";
    public const string StoreManager = "StoreManager";
    public const string Viewer = "Viewer";

    public static IReadOnlyList<string> All { get; } =
    [
        Owner,
        Operator,
        StoreManager,
        Viewer,
    ];

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissions { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // Admin-only claims (roles.manage, settings.dump) are injected only for User.isAdmin.
            [Owner] = Permissions.Assignable,
            [Operator] =
            [
                Permissions.CatalogManage,
                Permissions.MarketplaceManage,
                Permissions.FilesManage,
                Permissions.OperationsManage,
                Permissions.AiUse,
                Permissions.ReferenceRead,
            ],
            [StoreManager] =
            [
                Permissions.StoreMembersManage,
                Permissions.StoreSettingsManage,
                Permissions.CatalogManage,
                Permissions.MarketplaceManage,
                Permissions.FilesManage,
                Permissions.OperationsManage,
                Permissions.AiUse,
                Permissions.ReferenceRead,
            ],
            [Viewer] =
            [
                Permissions.ReferenceRead,
            ],
        };
}
