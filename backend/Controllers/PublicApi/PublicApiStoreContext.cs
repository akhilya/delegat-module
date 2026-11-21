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
            throw ApiException.Unauthorized(code: "unauthorized");
        return userId.Value;
    }

    public async Task EnsureStoreAccessAsync(int storeId, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var isMember = await _db.stores
            .AsNoTracking()
            .AnyAsync(s => s.Id == storeId && s.members.Any(m => m.Id == userId && m.isActive), ct);

        if (!isMember)
            throw ApiException.Forbidden(code: "access_denied_store");
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

    public static IActionResult AcceptedIntegration(
        Delegat.Services.Integration.IntegrationAdmitResult admitted,
        int storeId)
    {
        var statusUrl = $"/api/v1/stores/{storeId}/operations/{admitted.OperationId}";
        return new AcceptedResult(
            statusUrl,
            new
            {
                operationId = admitted.OperationId,
                commandId = admitted.CommandId,
                status = "running",
                integrationState = "pending",
                statusUrl,
                duplicate = admitted.IsDuplicate,
                links = new
                {
                    self = statusUrl
                }
            });
    }

    public static string? ReadIdempotencyKey(HttpContext? httpContext)
    {
        var raw = httpContext?.Request.Headers["Idempotency-Key"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    public static bool PrefersAsync(HttpContext? httpContext)
    {
        var prefer = httpContext?.Request.Headers["Prefer"].ToString();
        return !string.IsNullOrWhiteSpace(prefer)
            && prefer.Contains("respond-async", StringComparison.OrdinalIgnoreCase);
    }

    public static object Page<T>(IEnumerable<T> items, int page, int pageSize, int total) => new
    {
        items,
        page,
        pageSize,
        total
    };

    public static object Page<T>(
        IEnumerable<T> items,
        int page,
        int pageSize,
        int total,
        string? nextCursor,
        bool hasMore) => new
    {
        items,
        page,
        pageSize,
        total,
        nextCursor,
        hasMore
    };
}
