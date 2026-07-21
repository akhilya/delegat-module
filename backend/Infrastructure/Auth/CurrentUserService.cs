using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using System.Security.Claims;

namespace Delegat.Infrastructure.Auth;

/// <summary>
/// Interface for accessing current user information
/// </summary>
public interface ICurrentUserService
{
    int? GetUserId();
    Task<User?> GetUserAsync(CancellationToken ct = default);
    int? GetSelectedStoreId();
    Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default);
    bool IsAuthenticated { get; }
}

/// <summary>
/// Service for accessing current authenticated user
/// Replaces Zorro Query GetUser and CheckStoreMembership
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<User> _userManager;
    private readonly DataContext _context;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        UserManager<User> userManager,
        DataContext context)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _context = context;
    }

    /// <summary>
    /// Check if the current request is authenticated
    /// </summary>
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Get the current user ID from claims
    /// </summary>
    public int? GetUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("userId")
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
            return userId;

        return null;
    }

    /// <summary>
    /// Get the current user entity
    /// </summary>
    public async Task<User?> GetUserAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId == null)
            return null;

        return await _context.Users
            .AsNoTracking()
            .Include(u => u.memberships)
            .FirstOrDefaultAsync(u => u.Id == userId.Value && u.isActive, ct);
    }

    /// <summary>
    /// Cookie value only - do not use for authorization. Prefer <see cref="GetSelectedStoreIdAsync"/>.
    /// </summary>
    public int? GetSelectedStoreId()
    {
        var storeIdCookie = _httpContextAccessor.HttpContext?.Request.Cookies["selected_store_id"];
        if (storeIdCookie != null && int.TryParse(storeIdCookie, out var storeId))
            return storeId;

        return null;
    }

    /// <summary>
    /// Get selected store ID and validate membership
    /// Returns null if user is not a member of the selected store
    /// </summary>
    public async Task<int?> GetSelectedStoreIdAsync(CancellationToken ct = default)
    {
        var storeId = GetSelectedStoreId();
        if (storeId == null)
            return null;

        var userId = GetUserId();
        if (userId == null)
            return null;

        // Check if user is a member of the selected store
        var isMember = await _context.stores
            .AnyAsync(s => s.Id == storeId.Value && s.members.Any(m => m.Id == userId.Value && m.isActive), ct);

        return isMember ? storeId : null;
    }
}
