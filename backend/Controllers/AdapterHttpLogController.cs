using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers;

[ApiController]
[JwtAuthorize]
public class AdapterHttpLogController : ControllerBase
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IAdapterHttpLogQueryService _logs;
    private readonly DataContext _db;

    public AdapterHttpLogController(
        ICurrentUserService currentUserService,
        IAdapterHttpLogQueryService logs,
        DataContext db)
    {
        _currentUserService = currentUserService;
        _logs = logs;
        _db = db;
    }


    /// <summary>
    /// List outbound adapter HTTP logs for the selected store.
    /// </summary>
    [HttpGet]
    [Route("/adapter-http-logs")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? connectionId,
        [FromQuery] string? adapterKey,
        [FromQuery] string? method,
        [FromQuery] int? statusCode,
        [FromQuery] string? statusClass,
        [FromQuery] string? q,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var storeId = await RequireStoreIdAsync();

        if (connectionId.HasValue)
        {
            var ok = await _db.marketplaceConnections.AsNoTracking()
                .AnyAsync(c => c.Id == connectionId.Value && c.storeId == storeId, ct);
            if (!ok)
                throw ApiException.NotFound("Connection not found", code: "connection_not_found");
        }

        var (items, total, errorCount) = await _logs.QueryAsync(storeId, new AdapterHttpLogQuery
        {
            From = from,
            To = to,
            ConnectionId = connectionId,
            AdapterKey = adapterKey,
            Method = method,
            StatusCode = statusCode,
            StatusClass = statusClass,
            Q = q,
            Limit = limit,
            Offset = offset
        }, ct);

        return Ok(new
        {
            items,
            total,
            errorCount,
            limit,
            offset
        });
    }

    /// <summary>
    /// Full detail for one log entry (headers + bodies). Large bodies are preview-truncated;
    /// use the download endpoints for the full stored body.
    /// </summary>
    [HttpGet]
    [Route("/adapter-http-logs/{id:int}")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> Detail(int id, CancellationToken ct = default)
    {
        var storeId = await RequireStoreIdAsync();
        var detail = await _logs.GetDetailAsync(storeId, id, ct);
        if (detail == null)
            throw ApiException.NotFound("Log entry not found");
        return Ok(detail);
    }

    [HttpGet]
    [Route("/adapter-http-logs/{id:int}/request-body")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> DownloadRequestBody(int id, CancellationToken ct = default)
        => await DownloadBodyAsync(id, response: false, ct);

    [HttpGet]
    [Route("/adapter-http-logs/{id:int}/response-body")]
    [RequiresPermission(Permissions.SettingsPlatform)]
    public async Task<IActionResult> DownloadResponseBody(int id, CancellationToken ct = default)
        => await DownloadBodyAsync(id, response: true, ct);

    private async Task<IActionResult> DownloadBodyAsync(int id, bool response, CancellationToken ct)
    {
        var storeId = await RequireStoreIdAsync();
        var body = await _logs.GetStoredBodyAsync(storeId, id, response, ct);
        if (body == null)
            throw ApiException.NotFound("Log entry not found");

        var kind = response ? "response" : "request";
        var bytes = Encoding.UTF8.GetBytes(body);
        return File(bytes, "application/json; charset=utf-8", $"adapter-http-log-{id}-{kind}.json");
    }

    private async Task<int> RequireStoreIdAsync()
    {
        var storeId = await _currentUserService.GetSelectedStoreIdAsync();
        if (storeId == null)
            throw ApiException.BadRequest("Store is not selected", code: "store_not_selected");
        return storeId.Value;
    }
}
