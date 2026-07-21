using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;

namespace Delegat.Infrastructure.Auth;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionAuthorizationHandler(
        IServiceScopeFactory scopeFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _scopeFactory = scopeFactory;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        if (!user.HasClaim(Permissions.ClaimType, requirement.Permission))
            return;

        if (!requirement.RequireStoreMembership)
        {
            context.Succeed(requirement);
            return;
        }

        // Platform owners / store creators may manage any store without membership
        // (Settings → Stores). Store-scoped roles still require membership.
        if (user.HasClaim("is_admin", "true")
            || user.HasClaim(Permissions.ClaimType, Permissions.StoresCreate))
        {
            context.Succeed(requirement);
            return;
        }

        var http = _httpContextAccessor.HttpContext;
        if (http == null)
            return;

        var storeId = ResolveStoreId(http);
        if (storeId == null)
            return;

        var userIdValue = user.FindFirst("userId")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
        if (!int.TryParse(userIdValue, out var userId))
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();

        var isMember = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.isActive)
            .SelectMany(u => u.memberships)
            .AnyAsync(s => s.Id == storeId.Value);

        if (isMember)
            context.Succeed(requirement);
    }

    private static int? ResolveStoreId(HttpContext http)
    {
        // Route values commonly used for store id
        foreach (var key in new[] { "storeId", "id" })
        {
            if (http.Request.RouteValues.TryGetValue(key, out var raw) && raw != null
                && int.TryParse(raw.ToString(), out var fromRoute)
                && IsLikelyStoreRoute(http, key, fromRoute))
            {
                // For /stores/{id}/... treat id as storeId; for /products/{id} do not.
                if (key == "storeId")
                    return fromRoute;
                if (key == "id" && http.Request.Path.StartsWithSegments("/stores"))
                    return fromRoute;
            }
        }

        if (http.Request.Cookies.TryGetValue("selected_store_id", out var cookie)
            && int.TryParse(cookie, out var fromCookie))
            return fromCookie;

        return null;
    }

    private static bool IsLikelyStoreRoute(HttpContext http, string key, int _) =>
        key == "storeId" || http.Request.Path.StartsWithSegments("/stores");
}
