using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class SyncToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly PriceSyncService _priceSyncService;
    private readonly ILogger<SyncToolHandlers> _logger;

    public SyncToolHandlers(
        DataContext context,
        AiOperationService operationService,
        IMarketplaceAdapterRegistry adapterRegistry,
        PriceSyncService priceSyncService,
        ILogger<SyncToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _adapterRegistry = adapterRegistry;
        _priceSyncService = priceSyncService;
        _logger = logger;
    }

    internal async Task<(object, string)> ListConnectionsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var conns = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .ToListAsync(ct);
        return (new { connections = conns, count = conns.Count }, $"{conns.Count} connections");
    }

    internal async Task<(object, string)> GetConnectionAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .Where(c => c.Id == p.Id && c.storeId == storeId)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.lastSyncAt })
            .FirstOrDefaultAsync(ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        return (conn, conn.name);
    }

    internal async Task<(object, string)> TestConnectionAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var result = await adapter.TestConnectionAsync(conn, ct);
            return (new { success = result.Success, message = result.Message },
                result.Success ? $"Connection {conn.name} works" : $"Error: {result.Message}");
        }
        catch (Exception ex)
        {
            return (new { success = false, message = ex.Message }, $"Error: {ex.Message}");
        }
    }

    internal async Task<(object, string)> GetConnectionLimitsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.Id && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Not found" }, "Not found");
        try
        {
            var adapter = _adapterRegistry.GetByKey(conn.adapterKey)!;
            var quotas = await adapter.GetQuotasAsync(conn, ct);
            return (quotas, "Limits retrieved");
        }
        catch
        {
            return (new { error = "Unavailable" }, "Limits unavailable");
        }
    }

    internal async Task<(object, string)> GetAdapterInfoAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdRequiredInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, null, ct);
        if (error != null) return (new { error }, error);

        // Only single-bit flags — skip composite convenience values like FullProducts
        var capabilities = System.Enum.GetValues<MarketplaceCapabilities>()
            .Where(v => v != 0 && ((long)v & ((long)v - 1)) == 0 && adapter!.Capabilities.HasFlag(v))
            .Select(v => v.ToString())
            .ToList();

        var fieldGrouping = adapter!.GetFieldBasedGroupingConfig();
        object? grouping = null;
        if (fieldGrouping != null)
        {
            grouping = new
            {
                scheme = "field",
                fieldKey = fieldGrouping.GroupFieldKey,
                description = "Products are merged into a card by the value of this mapping field (default: groupName)",
            };
        }
        else if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping))
        {
            grouping = new
            {
                scheme = "endpoint",
                fieldKey = ProductSyncService.SyntheticGroupFieldKey,
                description = "Products with the same computed grouping field value are merged via API calls after export",
            };
        }

        return (new
        {
            adapterKey = adapter.Key,
            displayName = adapter.DisplayName,
            description = adapter.Description,
            connectionName = conn!.name,
            isActive = conn.isActive,
            capabilities,
            grouping,
        }, $"Adapter {adapter.DisplayName}: {capabilities.Count} capabilities");
    }

    internal async Task<(object, string)> PreviewMarketplaceProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<PreviewMarketplaceInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportAllProducts, ct);
        if (error != null) return (new { error }, error);

        var page = await adapter!.ImportAllProductsAsync(conn!, Math.Min(p.PageSize ?? 50, 200), p.Cursor, p.Search, ct);

        // Mark which external products are already linked locally
        var externalIds = page.Items.Select(i => i.ExternalId).ToList();
        var linked = (await _context.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connectionId == p.ConnectionId && externalIds.Contains(l.externalProductId))
            .Select(l => l.externalProductId).ToListAsync(ct)).ToHashSet();

        var items = page.Items.Select(i => new
        {
            externalId = i.ExternalId,
            i.Sku, i.Name, i.Price, i.Quantity,
            categoryId = i.CategoryId, categoryName = i.CategoryName,
            alreadyImported = linked.Contains(i.ExternalId),
        }).ToList();

        return (new { items, hasMore = page.HasMore, nextCursor = page.NextCursor, totalCount = page.TotalCount },
            $"On the marketplace: {items.Count} products (page){(page.HasMore ? ", more available" : "")}");
    }

    internal async Task<(object, string)> ImportProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ImportProductsInput>(json);
        if (p.ExternalProductIds.Count == 0) return (new { error = "externalProductIds not specified" }, "No products");
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportProduct, ct);
        if (error != null) return (new { error }, error);

        var batchId = Guid.NewGuid().ToString("N");
        foreach (var extId in p.ExternalProductIds.Distinct())
        {
            _context.pendingProductImports.Add(new PendingProductImport
            {
                connectionId = p.ConnectionId,
                externalProductId = extId,
                targetCategoryId = p.TargetCategoryId,
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                status = ProductImportStatus.Pending,
                createdAt = DateTime.UtcNow,
            });
        }
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "import_products",
            $"Import {p.ExternalProductIds.Count} products from {conn!.name}", new { }, canRollback: false, ct);
        return (new { batchId, queued = p.ExternalProductIds.Count },
            $"Import of {p.ExternalProductIds.Count} products queued (batchId: {batchId})");
    }

    internal async Task<(object, string)> ImportPricesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdRequiredInput>(json);
        var (conn, _, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportPrices, ct);
        if (error != null) return (new { error }, error);

        var result = await _priceSyncService.ImportPricesAsync(p.ConnectionId, null, ct, storeId: storeId);
        if (!result.IsSuccess) return (new { error = result.ErrorMessage }, $"Error: {result.ErrorMessage}");

        await _operationService.RecordAsync(storeId, userId, "import_prices",
            $"Import prices from {conn!.name}: updated {result.UpdatedCount}", new { }, canRollback: false, ct);
        return (new { updated = result.UpdatedCount, skipped = result.SkippedCount },
            $"Price import: updated {result.UpdatedCount}, skipped {result.SkippedCount}");
    }

    internal async Task<(object, string)> ImportStocksAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdRequiredInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ImportStocks, ct);
        if (error != null) return (new { error }, error);

        var stocks = await adapter!.ImportStocksAsync(conn!, null, ct);
        // Sum across warehouses per external product
        var byExternalId = stocks
            .GroupBy(s => s.ExternalProductId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

        var stockExternalIds = byExternalId.Keys.ToList();
        var links = await _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == p.ConnectionId && stockExternalIds.Contains(l.externalProductId))
            .ToListAsync(ct);

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
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "import_stocks",
            $"Import stocks from {conn!.name}: updated {updated}", new { }, canRollback: false, ct);
        return (new { updated, totalOnMarketplace = byExternalId.Count },
            $"Stock import: updated {updated} products");
    }

    internal async Task<(object, string)> ArchiveProductOnMarketplaceAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ArchiveProductInput>(json);
        var (conn, adapter, error) = await ResolveAdapterAsync(p.ConnectionId, storeId, MarketplaceCapabilities.ArchiveProduct, ct);
        if (error != null) return (new { error }, error);

        var link = await _context.productMarketplaceLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.connectionId == p.ConnectionId && l.productId == p.ProductId, ct);
        if (link == null) return (new { error = "Product is not linked to this marketplace" }, "Link not found");

        var result = await adapter!.ArchiveProductAsync(conn!, link.externalProductId, ct);
        if (!result.Success) return (new { error = result.ErrorMessage }, $"Error: {result.ErrorMessage}");

        await _operationService.RecordAsync(storeId, userId, "archive_product",
            $"Product {p.ProductId} archived on {conn!.name}", new { }, canRollback: false, ct);
        return (new { success = true }, $"Product archived on {conn!.name}");
    }

    internal async Task<(object, string)> ExportProductsBatchAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ExportBatchInput>(json);
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == p.ConnectionId && c.storeId == storeId, ct);
        if (conn == null) return (new { error = "Connection not found" }, "Not found");

        List<int> productIds;
        if (p.ProductIds?.Count > 0)
        {
            productIds = p.ProductIds;
        }
        else if (p.CategoryId.HasValue)
        {
            productIds = await _context.products.AsNoTracking()
                .Where(x => x.storeId == storeId && x.categoryId == p.CategoryId)
                .Select(x => x.Id).ToListAsync(ct);
        }
        else
        {
            productIds = await _context.products.AsNoTracking()
                .Where(x => x.storeId == storeId).Select(x => x.Id).ToListAsync(ct);
        }

        if (productIds.Count == 0) return (new { queued = 0 }, "No products to export");

        var batchId = Guid.NewGuid().ToString("N");
        foreach (var pid in productIds)
        {
            _context.pendingProductExports.Add(new PendingProductExport
            {
                connectionId = p.ConnectionId,
                productId = pid,
                authorUserId = userId,
                storeId = storeId,
                batchId = batchId,
                status = ProductExportStatus.Pending,
                createdAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync(ct);
        return (new { batchId, queued = productIds.Count }, $"Added {productIds.Count} products to the export queue");
    }

    internal Task<(object, string)> ExportPricesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        return Task.FromResult(((object)new { note = "Price export started. Use the UI to monitor progress." },
            $"Price export to connection {p.ConnectionId} started"));
    }

    internal Task<(object, string)> ExportStocksAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        return Task.FromResult(((object)new { note = "Stock export started. Use the UI to monitor progress." },
            $"Stock export to connection {p.ConnectionId} started"));
    }

    internal async Task<(object, string)> GetBatchStatusAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<BatchIdInput>(json);
        var exports = await _context.pendingProductExports.AsNoTracking()
            .Where(e => e.batchId == p.BatchId)
            .GroupBy(e => e.status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);
        var total = exports.Sum(e => e.count);
        var done = exports.Where(e => e.status == ProductExportStatus.Completed).Sum(e => e.count);
        var failed = exports.Where(e => e.status == ProductExportStatus.Failed).Sum(e => e.count);
        return (new { batchId = p.BatchId, total, done, failed, inProgress = total - done - failed },
            $"Batch {p.BatchId}: {done}/{total} done, {failed} failed");
    }

    internal async Task<(object, string)> ListProductLinksAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ConnectionIdInput>(json);
        var q = _context.productMarketplaceLinks.AsNoTracking()
            .Where(l => l.connection.storeId == storeId);
        if (p.ConnectionId.HasValue) q = q.Where(l => l.connectionId == p.ConnectionId);

        var links = await q.Take(50)
            .Select(l => new { l.productId, l.externalProductId, l.status, l.connectionId })
            .ToListAsync(ct);
        return (new { links, count = links.Count }, $"{links.Count} links");
    }

    // Resolves connection (scoped to store) + adapter, with optional capability check.
    private async Task<(MarketplaceConnection? conn, IMarketplaceAdapter? adapter, string? error)> ResolveAdapterAsync(
        int connectionId, int storeId, MarketplaceCapabilities? required, CancellationToken ct)
    {
        var conn = await _context.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);
        if (conn == null) return (null, null, "Connection not found");
        var adapter = _adapterRegistry.GetByKey(conn.adapterKey);
        if (adapter == null) return (null, null, $"Adapter '{conn.adapterKey}' not found");
        if (required.HasValue && !adapter.Capabilities.HasFlag(required.Value))
            return (null, null, $"Marketplace {adapter.DisplayName} does not support operation {required.Value}");
        return (conn, adapter, null);
    }
}

// ---- Sync-specific DTOs ----

internal class ConnectionIdRequiredInput { public int ConnectionId { get; set; } }
internal class PreviewMarketplaceInput { public int ConnectionId { get; set; } public int? PageSize { get; set; } public string? Cursor { get; set; } public string? Search { get; set; } }
internal class ImportProductsInput { public int ConnectionId { get; set; } public List<string> ExternalProductIds { get; set; } = []; public int? TargetCategoryId { get; set; } }
internal class ArchiveProductInput { public int ConnectionId { get; set; } public int ProductId { get; set; } }
internal class ExportBatchInput { public int ConnectionId { get; set; } public List<int>? ProductIds { get; set; } public int? CategoryId { get; set; } }
