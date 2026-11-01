using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Models;

namespace Delegat.Services.Ai.Agent;

/// <summary>JWT cookie session or Public API key (<c>dk_</c>).</summary>
public sealed class AgentAuthorizeAttribute : AuthorizeAttribute
{
    public AgentAuthorizeAttribute()
    {
        AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{PublicApiAuthorizeAttribute.Scheme}";
    }
}

public sealed class AgentSession
{
    public required int UserId { get; init; }
    public required int StoreId { get; init; }
    public required IReadOnlySet<string> Permissions { get; init; }
}

public sealed class AgentSessionResolver
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly DataContext _db;
    private readonly IHttpContextAccessor _http;

    public AgentSessionResolver(
        ICurrentUserService currentUser,
        IPermissionService permissions,
        DataContext db,
        IHttpContextAccessor http)
    {
        _currentUser = currentUser;
        _permissions = permissions;
        _db = db;
        _http = http;
    }

    public async Task<AgentSession> ResolveAsync(int? routeStoreId, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw new UnauthorizedAccessException("unauthorized");

        var storeId = routeStoreId
            ?? HeaderStoreId()
            ?? await _currentUser.GetSelectedStoreIdAsync(ct)
            ?? throw new InvalidOperationException("store_required");

        var user = await _currentUser.GetUserAsync(ct)
            ?? throw new UnauthorizedAccessException("unauthorized");
        return await ResolveForUserAsync(user, userId, storeId, ct);
    }

    public async Task<AgentSession> ResolveForUserAsync(int userId, int storeId, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(candidate => candidate.memberships)
            .FirstOrDefaultAsync(candidate => candidate.Id == userId && candidate.isActive, ct);
        return await ResolveForUserAsync(user, userId, storeId, ct);
    }

    private async Task<AgentSession> ResolveForUserAsync(User? user, int userId, int storeId, CancellationToken ct)
    {
        if (user == null || user.Id != userId || !user.isActive)
            throw new UnauthorizedAccessException("unauthorized");

        var member = user.isAdmin || (user.memberships?.Any(s => s.Id == storeId) ?? false);
        if (!member)
        {
            member = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .AnyAsync(_db.stores.Where(s => s.Id == storeId && s.members.Any(m => m.Id == userId && m.isActive)), ct);
        }

        if (!member)
            throw new UnauthorizedAccessException("access_denied_store");

        var perms = new HashSet<string>(
            await _permissions.GetEffectivePermissionsAsync(userId, ct),
            StringComparer.OrdinalIgnoreCase);

        if (!AgentAccess.CanUseAgent(perms))
            throw new UnauthorizedAccessException("ai.use required");

        return new AgentSession { UserId = userId, StoreId = storeId, Permissions = perms };
    }

    private int? HeaderStoreId()
    {
        var raw = _http.HttpContext?.Request.Headers["X-Delegat-Store-Id"].ToString();
        return int.TryParse(raw, out var id) ? id : null;
    }
}
