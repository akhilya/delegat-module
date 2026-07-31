using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Delegat.Models;

namespace Delegat.Infrastructure.Auth;

public sealed class UserAccessDto
{
    public IReadOnlyList<string> Roles { get; init; } = [];
    public IReadOnlyList<string> PermissionsFromRoles { get; init; } = [];
    public IReadOnlyList<string> PermissionGrants { get; init; } = [];
    public IReadOnlyList<string> EffectivePermissions { get; init; } = [];
    public bool IsAdmin { get; init; }
}

public interface IPermissionService
{
    Task EnsureRolesSeededAsync(CancellationToken ct = default);
    Task MigrateLegacyAccessAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default);
    Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default);
    Task SetUserAccessAsync(int userId, IReadOnlyList<string> roleNames, IReadOnlyList<string> permissionGrants, CancellationToken ct = default);
    Task EnsureOwnerAccessAsync(User user, CancellationToken ct = default);
    Task HydrateClaimsAsync(ClaimsIdentity identity, int userId, CancellationToken ct = default);
    void InvalidateUserAccess(int userId);
}

public sealed class PermissionService : IPermissionService
{
    private static readonly TimeSpan AccessCacheDuration = TimeSpan.FromMinutes(2);

    private readonly UserManager<User> _userManager;
    private readonly RoleManager<UserRole> _roleManager;
    private readonly DataContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(
        UserManager<User> userManager,
        RoleManager<UserRole> roleManager,
        DataContext db,
        IMemoryCache cache,
        ILogger<PermissionService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    private static string AccessCacheKey(int userId) => $"perm:user-access:{userId}";

    public void InvalidateUserAccess(int userId)
    {
        _cache.Remove(AccessCacheKey(userId));
        _cache.Remove($"perm:user-active:{userId}");
    }
    public async Task EnsureRolesSeededAsync(CancellationToken ct = default)
    {
        foreach (var roleName in AppRoles.All)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null)
            {
                role = new UserRole { Name = roleName };
                var create = await _roleManager.CreateAsync(role);
                if (!create.Succeeded)
                {
                    _logger.LogError("Failed to create role {Role}: {Errors}",
                        roleName, string.Join(", ", create.Errors.Select(e => e.Description)));
                    continue;
                }
            }

            var desired = AppRoles.RolePermissions[roleName];
            var existing = await _roleManager.GetClaimsAsync(role);
            var existingPerms = existing
                .Where(c => c.Type == Permissions.ClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var perm in desired)
            {
                if (existingPerms.Contains(perm)) continue;
                await _roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, perm));
            }

            foreach (var claim in existing.Where(c => c.Type == Permissions.ClaimType && !desired.Contains(c.Value)))
                await _roleManager.RemoveClaimAsync(role, claim);
        }
    }

    public async Task MigrateLegacyAccessAsync(CancellationToken ct = default)
    {
        await EnsureRolesSeededAsync(ct);

        var admins = await _userManager.Users.Where(u => u.isAdmin).ToListAsync(ct);
        foreach (var admin in admins)
            await EnsureOwnerAccessAsync(admin, ct);

        await MigrateStoreAdministratorsAsync(ct);

        var users = await _userManager.Users.Where(u => u.isActive).ToListAsync(ct);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Count == 0)
                await _userManager.AddToRoleAsync(user, AppRoles.Viewer);
        }
    }

    private async Task MigrateStoreAdministratorsAsync(CancellationToken ct)
    {
        // After DropStoreAdministrators / Baseline squash the table is gone.
        const string dropMigration = "20260721134624_DropStoreAdministrators";
        const string baselineMigration = "20260731183033_Baseline";
        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet();
        if (applied.Contains(dropMigration) || applied.Contains(baselineMigration))
            return;

        if (_db.Model.FindEntityType(typeof(StoreAdministrator)) == null)
            return;

        List<int> storeAdminUserIds;
        try
        {
            storeAdminUserIds = await _db.Set<StoreAdministrator>()
                .AsNoTracking()
                .Select(sa => sa.UserId)
                .Distinct()
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "StoreAdministrators not readable; skipping legacy store-admin migration");
            return;
        }

        if (storeAdminUserIds.Count == 0)
            return;

        foreach (var userId in storeAdminUserIds)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user == null) continue;
            if (await _userManager.IsInRoleAsync(user, AppRoles.Owner)) continue;
            if (!await _userManager.IsInRoleAsync(user, AppRoles.StoreManager))
            {
                await _userManager.AddToRoleAsync(user, AppRoles.StoreManager);
                _logger.LogInformation("Migrated user {UserId} from StoreAdministrators to StoreManager", userId);
            }
        }

        try
        {
            var rows = await _db.Set<StoreAdministrator>().ToListAsync(ct);
            if (rows.Count > 0)
            {
                _db.Set<StoreAdministrator>().RemoveRange(rows);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clear StoreAdministrators after role migration");
        }
    }

    public async Task EnsureOwnerAccessAsync(User user, CancellationToken ct = default)
    {
        await EnsureRolesSeededAsync(ct);
        if (!await _userManager.IsInRoleAsync(user, AppRoles.Owner))
            await _userManager.AddToRoleAsync(user, AppRoles.Owner);
        InvalidateUserAccess(user.Id);
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(int userId, CancellationToken ct = default)
    {
        var access = await GetUserAccessAsync(userId, ct);
        return access.EffectivePermissions;
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesAsync(int userId, CancellationToken ct = default)
    {
        var access = await GetUserAccessAsync(userId, ct);
        return access.Roles;
    }

    public async Task<UserAccessDto> GetUserAccessAsync(int userId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(AccessCacheKey(userId), out UserAccessDto? cached) && cached != null)
            return cached;

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found");

        var access = await BuildUserAccessAsync(user, ct);
        _cache.Set(AccessCacheKey(userId), access, AccessCacheDuration);
        return access;
    }

    private async Task<UserAccessDto> BuildUserAccessAsync(User user, CancellationToken ct)
    {
        var roleNames = (await _userManager.GetRolesAsync(user)).OrderBy(r => r).ToList();
        var fromRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in roleNames)
        {
            if (AppRoles.RolePermissions.TryGetValue(roleName, out var perms))
            {
                foreach (var p in perms)
                    fromRoles.Add(p);
                continue;
            }

            var role = await _roleManager.FindByNameAsync(roleName);
            if (role == null) continue;
            var claims = await _roleManager.GetClaimsAsync(role);
            foreach (var c in claims.Where(c => c.Type == Permissions.ClaimType))
                fromRoles.Add(c.Value);
        }

        var userClaims = await _userManager.GetClaimsAsync(user);
        var grants = userClaims
            .Where(c => c.Type == Permissions.ClaimType)
            .Select(c => c.Value)
            .Where(Permissions.IsKnown)
            .Where(p => !Permissions.IsAdminOnly(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        // Strip legacy admin-only claims from fromRoles display (Owner no longer templates them).
        fromRoles.RemoveWhere(Permissions.IsAdminOnly);

        var effective = new HashSet<string>(fromRoles, StringComparer.OrdinalIgnoreCase);
        foreach (var g in grants)
            effective.Add(g);

        // roles.manage is instance-owner only - never from roles/grants.
        if (user.isAdmin)
            effective.Add(Permissions.RolesManage);
        else
            effective.Remove(Permissions.RolesManage);

        return new UserAccessDto
        {
            Roles = roleNames,
            PermissionsFromRoles = fromRoles.OrderBy(p => p).ToList(),
            PermissionGrants = grants,
            EffectivePermissions = effective.OrderBy(p => p).ToList(),
            IsAdmin = user.isAdmin,
        };
    }

    public async Task SetUserAccessAsync(
        int userId,
        IReadOnlyList<string> roleNames,
        IReadOnlyList<string> permissionGrants,
        CancellationToken ct = default)
    {
        await EnsureRolesSeededAsync(ct);

        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found");

        var desiredRoles = roleNames
            .Where(r => AppRoles.All.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Where(r => !desiredRoles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        var toAdd = desiredRoles.Where(r => !currentRoles.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();

        if (toRemove.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0)
            await _userManager.AddToRolesAsync(user, toAdd);

        var fromRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in desiredRoles)
        {
            if (!AppRoles.RolePermissions.TryGetValue(roleName, out var perms)) continue;
            foreach (var p in perms)
                fromRoles.Add(p);
        }

        var desiredGrants = permissionGrants
            .Where(Permissions.IsKnown)
            .Where(p => !Permissions.IsAdminOnly(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(g => !fromRoles.Contains(g))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingClaims = (await _userManager.GetClaimsAsync(user))
            .Where(c => c.Type == Permissions.ClaimType)
            .ToList();

        foreach (var claim in existingClaims.Where(c => !desiredGrants.Contains(c.Value)))
            await _userManager.RemoveClaimAsync(user, claim);

        var existingValues = existingClaims.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in desiredGrants.Where(g => !existingValues.Contains(g)))
            await _userManager.AddClaimAsync(user, new Claim(Permissions.ClaimType, grant));

        InvalidateUserAccess(userId);
    }

    public async Task HydrateClaimsAsync(ClaimsIdentity identity, int userId, CancellationToken ct = default)
    {
        var access = await GetUserAccessAsync(userId, ct);
        foreach (var role in access.Roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        foreach (var perm in access.EffectivePermissions)
            identity.AddClaim(new Claim(Permissions.ClaimType, perm));

        if (access.IsAdmin)
            identity.AddClaim(new Claim("is_admin", "true"));
    }

}
