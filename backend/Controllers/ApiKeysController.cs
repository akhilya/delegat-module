using Microsoft.AspNetCore.Mvc;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;

namespace Delegat.Controllers;

/// <summary>
/// SPA endpoints for managing the current user's Public API keys and request logs (JWT auth).
/// </summary>
[ApiController]
[JwtAuthorize]
[RequiresPermission(Permissions.ApiAccess)]
public class ApiKeysController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly UserApiKeyService _apiKeys;
    private readonly IApiRequestLogQueryService _logs;

    public ApiKeysController(
        ICurrentUserService currentUser,
        UserApiKeyService apiKeys,
        IApiRequestLogQueryService logs)
    {
        _currentUser = currentUser;
        _apiKeys = apiKeys;
        _logs = logs;
    }

    [HttpGet]
    [Route("/api-keys")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();
        var items = await _apiKeys.ListAsync(userId, ct);
        return Ok(new { items });
    }

    [HttpPost]
    [Route("/api-keys")]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();
        var (dto, plaintext) = await _apiKeys.CreateAsync(userId, request.name ?? "", ct);
        return Ok(new { key = dto, plaintext });
    }

    [HttpDelete]
    [Route("/api-keys/{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();
        await _apiKeys.RevokeAsync(userId, id, ct);
        return Ok(new { success = true });
    }

    [HttpGet]
    [Route("/api-keys/logs")]
    public async Task<IActionResult> ListLogs(
        [FromQuery] int? apiKeyId = null,
        [FromQuery] string? method = null,
        [FromQuery] string? statusClass = null,
        [FromQuery] string? q = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();

        var (items, total, errorCount) = await _logs.QueryAsync(userId, new ApiRequestLogQuery
        {
            ApiKeyId = apiKeyId,
            Method = method,
            StatusClass = statusClass,
            Q = q,
            Limit = limit,
            Offset = offset,
        }, ct);

        return Ok(new { items, total, errorCount });
    }

    [HttpGet]
    [Route("/api-keys/logs/{id:int}")]
    public async Task<IActionResult> GetLogDetail(int id, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();

        var detail = await _logs.GetDetailAsync(userId, id, ct);
        if (detail == null)
            throw ApiException.NotFound("Log entry not found");

        return Ok(detail);
    }

    public class CreateApiKeyRequest
    {
        public string? name { get; set; }
    }
}
