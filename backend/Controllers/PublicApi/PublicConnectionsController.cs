using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 - marketplace connections (no secrets), product links, and operation polling.
/// </summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}")]
[Tags("Connections & operations")]
public class PublicConnectionsController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;

    public PublicConnectionsController(
        PublicApiStoreContext storeCtx,
        DataContext db,
        IMarketplaceAdapterRegistry adapters)
    {
        _storeCtx = storeCtx;
        _db = db;
        _adapters = adapters;
    }

    /// <summary>Lists marketplace connections. Credentials are never returned.</summary>
    [HttpGet("connections")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(int storeId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var connections = await _db.marketplaceConnections
            .AsNoTracking()
            .Where(c => c.storeId == storeId)
            .OrderBy(c => c.name)
            .ToListAsync(ct);

        var items = connections.Select(c =>
        {
            var adapter = _adapters.GetByKey(c.adapterKey);
            return new
            {
                id = c.Id,
                name = c.name,
                adapterKey = c.adapterKey,
                adapterDisplayName = adapter?.DisplayName,
                isActive = c.isActive,
                lastSyncAt = c.lastSyncAt,
                createdAt = c.createdAt
            };
        }).ToList();

        return Ok(PublicApiResults.Page(items, 1, items.Count, items.Count));
    }

    /// <summary>Gets connection metadata (no credentials).</summary>
    [HttpGet("connections/{connectionId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int storeId, int connectionId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var c = await _db.marketplaceConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == connectionId && x.storeId == storeId, ct);

        if (c == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        var adapter = _adapters.GetByKey(c.adapterKey);
        return Ok(new
        {
            id = c.Id,
            name = c.name,
            adapterKey = c.adapterKey,
            adapterDisplayName = adapter?.DisplayName,
            isActive = c.isActive,
            lastSyncAt = c.lastSyncAt,
            createdAt = c.createdAt,
            // credentials intentionally omitted
        });
    }

    /// <summary>Lists product-marketplace links.</summary>
    [HttpGet("product-links")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProductLinks(
        int storeId,
        [FromQuery] int? connectionId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        (page, pageSize) = PublicApiPaging.Normalize(page, pageSize);

        var q = _db.productMarketplaceLinks
            .AsNoTracking()
            .Include(l => l.connection)
            .Where(l => l.connection.storeId == storeId);

        if (connectionId.HasValue)
            q = q.Where(l => l.connectionId == connectionId.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                id = l.Id,
                productId = l.productId,
                connectionId = l.connectionId,
                externalProductId = l.externalProductId,
                externalSku = l.externalSku,
                status = l.status.ToString(),
                lastSyncAt = l.lastSyncAt,
                lastError = l.lastErrorMessage,
                externalPrice = l.externalPrice,
                externalStock = l.externalStock,
                categoryMappingId = l.categoryMappingId,
                categoryMappingName = l.categoryMapping != null ? l.categoryMapping.name : null,
                externalCategoryName = l.categoryMapping != null ? l.categoryMapping.externalCategoryName : null,
            })
            .ToListAsync(ct);

        return Ok(PublicApiResults.Page(items, page, pageSize, total));
    }

    /// <summary>Lists async operations for the store.</summary>
    [HttpGet("operations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOperations(
        int storeId,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);
        (page, pageSize) = PublicApiPaging.Normalize(
            page,
            pageSize,
            PublicApiPaging.DefaultOperationsPageSize,
            PublicApiPaging.MaxOperationsPageSize);

        var q = _db.operations.AsNoTracking().Where(o => o.storeId == storeId);
        if (!string.IsNullOrEmpty(status))
            q = q.Where(o => o.status == status);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(o => o.createdAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.operationId,
                o.type,
                o.title,
                o.status,
                o.connectionId,
                o.connectionName,
                o.batchId,
                o.batchType,
                o.totalItems,
                o.completedItems,
                o.failedItems,
                o.summary,
                o.createdAt,
                o.finishedAt
            })
            .ToListAsync(ct);

        return Ok(PublicApiResults.Page(items, page, pageSize, total));
    }

    /// <summary>Gets operation status and recent logs (poll after 202 sync starters).</summary>
    [HttpGet("operations/{operationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOperation(int storeId, string operationId, CancellationToken ct)
    {
        await _storeCtx.EnsureStoreAccessAsync(storeId, ct);

        var op = await _db.operations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.operationId == operationId && o.storeId == storeId, ct);

        if (op == null)
            throw ApiException.NotFound("Operation not found", code: "operation_not_found");

        var logs = await OperationLogQuery.LatestAsync(
            _db.operationLogEntries.AsNoTracking().Where(l => l.operationId == operationId),
            skip: 0,
            take: 200,
            ct);

        return Ok(new
        {
            op.operationId,
            op.type,
            op.title,
            op.status,
            op.connectionId,
            op.connectionName,
            op.batchId,
            op.batchType,
            op.totalItems,
            op.completedItems,
            op.failedItems,
            op.summary,
            op.createdAt,
            op.finishedAt,
            logs
        });
    }
}
