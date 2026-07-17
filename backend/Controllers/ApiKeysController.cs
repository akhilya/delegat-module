using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Services;

namespace Delegat.Controllers;

/// <summary>
/// SPA endpoints for managing the current user's Public API keys (JWT auth).
/// </summary>
[ApiController]
[JwtAuthorize]
public class ApiKeysController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly UserApiKeyService _apiKeys;

    public ApiKeysController(ICurrentUserService currentUser, UserApiKeyService apiKeys)
    {
        _currentUser = currentUser;
        _apiKeys = apiKeys;
    }

    [HttpGet]
    [Route("/api-keys")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw Infrastructure.Middleware.ApiException.Unauthorized();
        var items = await _apiKeys.ListAsync(userId, ct);
        return Ok(new { items });
    }

    [HttpPost]
    [Route("/api-keys")]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw Infrastructure.Middleware.ApiException.Unauthorized();
        var (dto, plaintext) = await _apiKeys.CreateAsync(userId, request.name ?? "", ct);
        return Ok(new { key = dto, plaintext });
    }

    [HttpDelete]
    [Route("/api-keys/{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw Infrastructure.Middleware.ApiException.Unauthorized();
        await _apiKeys.RevokeAsync(userId, id, ct);
        return Ok(new { success = true });
    }

    public class CreateApiKeyRequest
    {
        public string? name { get; set; }
    }
}
