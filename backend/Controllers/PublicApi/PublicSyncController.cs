using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Auth;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Controllers.PublicApi;

/// <summary>
/// Public API v1 — marketplace sync starters. All mutations return 202 Accepted + operationId for polling.
/// </summary>
[ApiController]
[PublicApiAuthorize]
[EnableRateLimiting("public-api")]
[Route("/v1/stores/{storeId:int}/connections/{connectionId:int}")]
[Tags("Sync")]
public class PublicSyncController : ControllerBase
{
    private readonly PublicApiStoreContext _storeCtx;
    private readonly DataContext _db;
    private readonly IOperationLogger _operationLogger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PublicSyncController> _logger;

    public PublicSyncController(
        PublicApiStoreContext storeCtx,
        DataContext db,
        IOperationLogger operationLogger,
        IServiceScopeFactory scopeFactory,
        ILogger<PublicSyncController> logger)
    {
        _storeCtx = storeCtx;
        _db = db;
        _operationLogger = operationLogger;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public class ProductIdsRequest
    {
        public List<int>? productIds { get; set; }
    }

    public class ImportProductsRequest
    {
        public List<ImportProductItem>? products { get; set; }
        /// <summary>When true and products empty — import all mappable external products is not supported; require products.</summary>
        public bool forceImportWithoutMapping { get; set; }
    }

    public class ImportProductItem
    {
        public string externalId { get; set; } = null!;
        public string? externalCategoryId { get; set; }
        public string? productName { get; set; }
        public int? targetCategoryId { get; set; }
        public bool forceImportWithoutMapping { get; set; }
    }

    public class LinkIdsRequest
    {
        public List<int>? linkIds { get; set; }
    }

    /// <summary>Queues product export. Returns 202; poll operations/{operationId}.</summary>
    /// <remarks>If <c>productIds</c> is omitted or empty, all store products are queued.</remarks>
    [HttpPost("export-products")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportProducts(
        int storeId,
        int connectionId,
        [FromBody] ProductIdsRequest? body,
        CancellationToken ct)
    {
        var (userId, _) = await _storeCtx.RequireStoreAsync(storeId, ct);
        var connection = await RequireConnectionAsync(storeId, connectionId, ct);

        var productIds = body?.productIds?.Distinct().ToList() ?? new List<int>();
        if (productIds.Count == 0)
        {
            productIds = await _db.products
                .AsNoTracking()
                .Where(p => p.storeId == storeId)
                .Select(p => p.Id)
                .ToListAsync(ct);
        }
        else
        {
            productIds = await _db.products
                .AsNoTracking()
                .Where(p => p.storeId == storeId && productIds.Contains(p.Id))
                .Select(p => p.Id)
                .ToListAsync(ct);
        }

        if (productIds.Count == 0)
            throw ApiException.BadRequest("Нет товаров для экспорта");

        var batchId = Guid.NewGuid().ToString("N");
        var names = await _db.products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.name, ct);

        foreach (var productId in productIds)
        {
            _db.pendingProductExports.Add(new PendingProductExport
            {
                connectionId = connectionId,
                productId = productId,
                productName = names.GetValueOrDefault(productId),
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                status = ProductExportStatus.Pending,
                createdAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);

        await _operationLogger.StartAsync(batchId, storeId, userId, "batch-export",
            $"Экспорт {productIds.Count} товаров",
            connectionId: connectionId, connectionName: connection.name,
            batchId: batchId, batchType: "export", totalItems: productIds.Count);

        return PublicApiResults.AcceptedOperation(batchId, storeId);
    }

    /// <summary>Queues product import by external ids. Returns 202; poll operations/{operationId}.</summary>
    [HttpPost("import-products")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportProducts(
        int storeId,
        int connectionId,
        [FromBody] ImportProductsRequest body,
        CancellationToken ct)
    {
        var (userId, _) = await _storeCtx.RequireStoreAsync(storeId, ct);
        var connection = await RequireConnectionAsync(storeId, connectionId, ct);

        if (body.products == null || body.products.Count == 0)
            throw ApiException.BadRequest("Укажите products[] с externalId");

        var mappings = await _db.categoryMappings
            .AsNoTracking()
            .Where(m => m.connectionId == connectionId)
            .GroupBy(m => m.externalCategoryId)
            .ToDictionaryAsync(g => g.Key, g => g.First(), ct);

        var batchId = Guid.NewGuid().ToString("N");
        var queued = 0;

        foreach (var item in body.products)
        {
            if (string.IsNullOrWhiteSpace(item.externalId))
                continue;

            int? targetCategoryId = item.targetCategoryId;
            if (!targetCategoryId.HasValue && !string.IsNullOrEmpty(item.externalCategoryId))
            {
                if (mappings.TryGetValue(item.externalCategoryId, out var mapping))
                    targetCategoryId = mapping.localCategoryId;
                else if (!item.forceImportWithoutMapping && !body.forceImportWithoutMapping)
                    continue;
            }

            _db.pendingProductImports.Add(new PendingProductImport
            {
                connectionId = connectionId,
                externalProductId = item.externalId,
                targetCategoryId = targetCategoryId,
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                productName = item.productName,
                status = ProductImportStatus.Pending,
                createdAt = DateTime.UtcNow
            });
            queued++;
        }

        if (queued == 0)
            throw ApiException.BadRequest("Ни один товар не поставлен в очередь (проверьте маппинги категорий)");

        await _db.SaveChangesAsync(ct);

        await _operationLogger.StartAsync(batchId, storeId, userId, "batch-import",
            $"Импорт {queued} товаров",
            connectionId: connectionId, connectionName: connection.name,
            batchId: batchId, batchType: "import", totalItems: queued);

        return PublicApiResults.AcceptedOperation(batchId, storeId);
    }

    /// <summary>Starts price export (async). Returns 202; poll operations/{operationId}.</summary>
    [HttpPost("export-prices")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ExportPrices(
        int storeId, int connectionId, [FromBody] LinkIdsRequest? body, CancellationToken ct)
    {
        return await StartPriceStockOpAsync(storeId, connectionId, "price-export", "Экспорт цен",
            async (scope, opId, sid, ct2) =>
            {
                var svc = scope.ServiceProvider.GetRequiredService<PriceSyncService>();
                return await svc.ExportPricesAsync(connectionId, body?.linkIds, ct2, opId, sid);
            }, ct);
    }

    /// <summary>Starts price import (async). Returns 202; poll operations/{operationId}.</summary>
    [HttpPost("import-prices")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportPrices(
        int storeId, int connectionId, [FromBody] LinkIdsRequest? body, CancellationToken ct)
    {
        return await StartPriceStockOpAsync(storeId, connectionId, "price-import", "Импорт цен",
            async (scope, opId, sid, ct2) =>
            {
                var svc = scope.ServiceProvider.GetRequiredService<PriceSyncService>();
                return await svc.ImportPricesAsync(connectionId, body?.linkIds, ct2, opId, sid);
            }, ct);
    }

    /// <summary>Starts stock export (async). Returns 202; poll operations/{operationId}.</summary>
    [HttpPost("export-stocks")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ExportStocks(
        int storeId, int connectionId, [FromBody] LinkIdsRequest? body, CancellationToken ct)
    {
        return await StartPriceStockOpAsync(storeId, connectionId, "stock-export", "Экспорт остатков",
            async (scope, opId, sid, ct2) =>
            {
                var svc = scope.ServiceProvider.GetRequiredService<PriceSyncService>();
                return await svc.ExportStocksAsync(connectionId, body?.linkIds, ct2, opId, sid);
            }, ct);
    }

    /// <summary>Starts stock import (async). Returns 202; poll operations/{operationId}.</summary>
    [HttpPost("import-stocks")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ImportStocks(
        int storeId, int connectionId, CancellationToken ct)
    {
        var (userId, _) = await _storeCtx.RequireStoreAsync(storeId, ct);
        var connection = await RequireConnectionAsync(storeId, connectionId, ct);
        var operationId = Guid.NewGuid().ToString("N");

        await _operationLogger.StartAsync(operationId, storeId, userId, "stock-import",
            "Импорт остатков", connectionId: connectionId, connectionName: connection.name);

        var capturedConnectionId = connectionId;
        var capturedStoreId = storeId;
        var capturedOpId = operationId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DataContext>();
                var adapters = scope.ServiceProvider.GetRequiredService<IMarketplaceAdapterRegistry>();
                var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();

                var conn = await db.marketplaceConnections
                    .FirstOrDefaultAsync(c => c.Id == capturedConnectionId && c.storeId == capturedStoreId);
                if (conn == null)
                {
                    await logger.FinishAsync(capturedOpId, capturedStoreId, "failed", "Подключение не найдено");
                    return;
                }

                var adapter = adapters.GetByKey(conn.adapterKey);
                if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportStocks))
                {
                    await logger.FinishAsync(capturedOpId, capturedStoreId, "failed", "Адаптер не поддерживает импорт остатков");
                    return;
                }

                var stocks = await adapter.ImportStocksAsync(conn, null, CancellationToken.None);
                var byExternalId = stocks
                    .GroupBy(s => s.ExternalProductId)
                    .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

                var externalIds = byExternalId.Keys.ToList();
                var links = await db.productMarketplaceLinks
                    .Include(l => l.product)
                    .Where(l => l.connectionId == capturedConnectionId && externalIds.Contains(l.externalProductId))
                    .ToListAsync();

                var updated = 0;
                foreach (var link in links)
                {
                    var qty = byExternalId[link.externalProductId];
                    if (link.product.storageQuantity != qty)
                    {
                        link.product.storageQuantity = qty;
                        updated++;
                    }
                    link.externalStock = qty;
                }
                await db.SaveChangesAsync();
                await logger.FinishAsync(capturedOpId, capturedStoreId, "completed",
                    $"Импорт остатков: обновлено {updated}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Public API import-stocks failed {OperationId}", capturedOpId);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
                    await logger.FinishAsync(capturedOpId, capturedStoreId, "failed", ex.Message);
                }
                catch { /* ignore */ }
            }
        });

        return PublicApiResults.AcceptedOperation(operationId, storeId);
    }

    private async Task<IActionResult> StartPriceStockOpAsync(
        int storeId,
        int connectionId,
        string type,
        string title,
        Func<IServiceScope, string, int, CancellationToken, Task<PriceSyncResult>> work,
        CancellationToken ct)
    {
        var (userId, _) = await _storeCtx.RequireStoreAsync(storeId, ct);
        var connection = await RequireConnectionAsync(storeId, connectionId, ct);
        var operationId = Guid.NewGuid().ToString("N");

        await _operationLogger.StartAsync(operationId, storeId, userId, type, title,
            connectionId: connectionId, connectionName: connection.name);

        var capturedStoreId = storeId;
        var capturedOpId = operationId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
                var result = await work(scope, capturedOpId, capturedStoreId, CancellationToken.None);
                var status = result.IsSuccess ? "completed" : "failed";
                await logger.FinishAsync(capturedOpId, capturedStoreId, status,
                    result.Message ?? result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Public API {Type} failed {OperationId}", type, capturedOpId);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var logger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();
                    await logger.FinishAsync(capturedOpId, capturedStoreId, "failed", ex.Message);
                }
                catch { /* ignore */ }
            }
        });

        return PublicApiResults.AcceptedOperation(operationId, storeId);
    }

    private async Task<MarketplaceConnection> RequireConnectionAsync(int storeId, int connectionId, CancellationToken ct)
    {
        var connection = await _db.marketplaceConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);

        if (connection == null)
            throw ApiException.NotFound("Подключение не найдено");

        return connection;
    }
}
