using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 — current user and accessible stores.
/// </summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1")]
[Tags("Meta")]
public class PublicMetaController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly ICurrentUserService _currentUser;

    public PublicMetaController(
        PublicApiStoreContext storeCtx,
        DataContext db,
        ICurrentUserService currentUser)
    {
        _storeCtx = storeCtx;
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Returns the user bound to the API key.</summary>
    /// <response code="200">User profile</response>
    /// <response code="401">Invalid or missing API key</response>
    [HttpGet("me")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _currentUser.GetUserAsync(ct)
            ?? throw ApiException.Unauthorized();

        return Ok(new
        {
            id = user.Id,
            userName = user.UserName,
            email = user.Email,
            isAdmin = user.isAdmin
        });
    }

    /// <summary>Lists stores the API key user can access (membership).</summary>
    /// <response code="200">Paginated store list</response>
    [HttpGet("stores")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListStores(CancellationToken ct)
    {
        var userId = _storeCtx.RequireUserId();

        var stores = await _db.stores
            .AsNoTracking()
            .Where(s => s.members.Any(m => m.Id == userId && m.isActive))
            .OrderBy(s => s.name)
            .Select(s => new { id = s.Id, name = s.name })
            .ToListAsync(ct);

        return Ok(new { items = stores, page = 1, pageSize = stores.Count, total = stores.Count });
    }
}
