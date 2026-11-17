using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;

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

        return await ResolveForUserAsync(userId, storeId, ct);
    }

    public async Task<AgentSession> ResolveForUserAsync(int userId, int storeId, CancellationToken ct)
    {
        // Authorization must read current database facts, not tracked navigation state.
        var actor = await _db.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId && candidate.isActive)
            .Select(candidate => new
            {
                candidate.isAdmin,
                IsMember = candidate.memberships.Any(store => store.Id == storeId),
            })
            .SingleOrDefaultAsync(ct);
        if (actor == null)
            throw new UnauthorizedAccessException("unauthorized");
        if (!actor.isAdmin && !actor.IsMember)
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
