using System.Text;
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

    [HttpPatch]
    [Route("/api-keys/{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();
        await _apiKeys.RenameAsync(userId, id, request.name ?? "", ct);
        return Ok(new { success = true });
    }

    [HttpGet]
    [Route("/api-keys/logs")]
    public async Task<IActionResult> ListLogs(
        [FromQuery] int? apiKeyId = null,
        [FromQuery] string? method = null,
        [FromQuery] string? statusClass = null,
        [FromQuery] string? q = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
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
            SortBy = sortBy,
            SortDir = sortDir,
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
            throw ApiException.NotFound(code: "log_entry_not_found");

        return Ok(detail);
    }

    /// <summary>Delete selected Public API request logs for the current user (max 200).</summary>
    [HttpPost]
    [Route("/api-keys/logs/delete")]
    public async Task<IActionResult> DeleteLogs([FromBody] DeleteLogIdsRequest? body, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();

        var ids = (body?.Ids ?? [])
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();
        if (ids.Count == 0)
            return Ok(new { deleted = 0 });

        var deleted = await _logs.DeleteManyAsync(userId, ids, ct);
        return Ok(new { deleted });
    }

    [HttpGet]
    [Route("/api-keys/logs/{id:int}/request-body")]
    public async Task<IActionResult> DownloadRequestBody(int id, CancellationToken ct)
        => await DownloadBodyAsync(id, response: false, ct);

    [HttpGet]
    [Route("/api-keys/logs/{id:int}/response-body")]
    public async Task<IActionResult> DownloadResponseBody(int id, CancellationToken ct)
        => await DownloadBodyAsync(id, response: true, ct);

    private async Task<IActionResult> DownloadBodyAsync(int id, bool response, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId()
            ?? throw ApiException.Unauthorized();

        var body = await _logs.GetStoredBodyAsync(userId, id, response, ct);
        if (body == null)
            throw ApiException.NotFound(code: "log_entry_not_found");

        var kind = response ? "response" : "request";
        var bytes = Encoding.UTF8.GetBytes(body);
        return File(bytes, "application/json; charset=utf-8", $"api-request-log-{id}-{kind}.json");
    }

    public class CreateApiKeyRequest
    {
        public string? name { get; set; }
    }
}
