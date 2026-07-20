using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Resolves and validates storeId from the Public API route for the authenticated API-key user.
/// </summary>
public class PublicApiStoreContext
{
    private readonly ICurrentUserService _currentUser;
    private readonly DataContext _db;

    public PublicApiStoreContext(ICurrentUserService currentUser, DataContext db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    public int RequireUserId()
    {
        var userId = _currentUser.GetUserId();
        if (userId == null)
            throw ApiException.Unauthorized("Invalid or missing API key");
        return userId.Value;
    }

    public async Task EnsureStoreAccessAsync(int storeId, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var isMember = await _db.stores
            .AsNoTracking()
            .AnyAsync(s => s.Id == storeId && s.members.Any(m => m.Id == userId && m.isActive), ct);

        if (!isMember)
            throw ApiException.Forbidden("No access to this store", code: "access_denied_store");
    }

    public async Task<(int userId, int storeId)> RequireStoreAsync(int storeId, CancellationToken ct = default)
    {
        await EnsureStoreAccessAsync(storeId, ct);
        return (RequireUserId(), storeId);
    }
}

public static class PublicApiResults
{
    public static IActionResult AcceptedOperation(string operationId, int storeId, string status = "running")
    {
        return new AcceptedResult(
            $"/api/v1/stores/{storeId}/operations/{operationId}",
            new
            {
                operationId,
                status,
                links = new
                {
                    self = $"/api/v1/stores/{storeId}/operations/{operationId}"
                }
            });
    }

    public static object Page<T>(IEnumerable<T> items, int page, int pageSize, int total) => new
    {
        items,
        page,
        pageSize,
        total
    };
}
