using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>Executes the fixed marketplace operations shared by manual tools and scheduled tasks.</summary>
public sealed class MarketplaceActionExecutor
{
    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly PriceSyncService _prices;
    private readonly ProductSyncService _products;

    public MarketplaceActionExecutor(
        DataContext db,
        IMarketplaceAdapterRegistry adapters,
        PriceSyncService prices,
        ProductSyncService products)
    {
        _db = db;
        _adapters = adapters;
        _prices = prices;
        _products = products;
    }

    public async Task<string> ExecuteAsync(
        string actionId,
        MarketplaceConnection connection,
        int storeId,
        int userId,
        IReadOnlyDictionary<string, int>? parameters,
        string operationId,
        CancellationToken ct)
    {
        actionId = actionId.ToLowerInvariant();
        var adapter = _adapters.GetByKey(connection.adapterKey)
            ?? throw new InvalidOperationException("The adapter for this connection is unavailable.");
        var required = RequiredCapability(actionId);
        if (!adapter.Capabilities.HasFlag(required))
            throw new InvalidOperationException($"The connection does not support {required}.");

        switch (actionId)
        {
            case "export-prices":
            {
                var result = await _prices.ExportPricesAsync(connection.Id, null, ct, operationId, storeId);
                EnsureSuccess(result.IsSuccess, result.ErrorMessage ?? "Price export failed.");
                return result.Message ?? "Price export completed.";
            }
            case "export-stocks":
            {
                var result = await _prices.ExportStocksAsync(connection.Id, null, ct, operationId, storeId);
                EnsureSuccess(result.IsSuccess, result.ErrorMessage ?? "Stock export failed.");
                return result.Message ?? "Stock export completed.";
            }
            case "import-prices":
            {
                var result = await _prices.ImportPricesAsync(connection.Id, null, ct, operationId, storeId);
                EnsureSuccess(result.IsSuccess, result.ErrorMessage ?? "Price import failed.");
                return result.Message ?? "Price import completed.";
            }
            case "import-products":
                return await ImportProductsAsync(connection, adapter, storeId, userId,
                    parameters ?? new Dictionary<string, int>(), operationId, ct);
            case "verify-product-links":
            {
                var result = await _products.VerifyLinksAsync(connection.Id, null, ct);
                return $"Checked {result.Checked}; confirmed {result.Confirmed}; missing {result.Broken}; recovered {result.Recovered}.";
            }
            case "check-export-readiness":
            {
                const int sampleLimit = 500;
                var ids = await _db.products.AsNoTracking()
                    .Where(x => x.storeId == storeId)
                    .OrderBy(x => x.Id)
                    .Select(x => x.Id)
                    .Take(sampleLimit)
                    .ToListAsync(ct);
                var items = await _products.CheckExportReadinessAsync(ids, connection.Id, ct);
                var ready = items.Count(x => x.IsReady);
                return $"Checked {items.Count} products (maximum {sampleLimit} per run): ready {ready}; blocked {items.Count - ready}.";
            }
            case "compare-prices":
            {
                var result = await _prices.ComparePricesAsync(connection.Id, ct);
                EnsureSuccess(result.IsSuccess, result.ErrorMessage ?? "Price comparison failed.");
                var prices = result.Items.Count(x => x.HasPriceDifference);
                var stocks = result.Items.Count(x => x.HasStockDifference);
                return $"Compared {result.Items.Count} products; price differences {prices}; stock differences {stocks}.";
            }
            default:
                throw new InvalidOperationException($"Unknown marketplace action '{actionId}'.");
        }
    }

    public static MarketplaceCapabilities RequiredCapability(string actionId) => actionId.ToLowerInvariant() switch
    {
        "export-prices" => MarketplaceCapabilities.ExportPrices,
        "export-stocks" => MarketplaceCapabilities.ExportStocks,
        "import-prices" => MarketplaceCapabilities.ImportPrices,
        "import-products" => MarketplaceCapabilities.ImportAllProducts,
        "verify-product-links" => MarketplaceCapabilities.ImportProduct,
        "check-export-readiness" => MarketplaceCapabilities.ExportProduct,
        "compare-prices" => MarketplaceCapabilities.ImportPrices,
        _ => throw new InvalidOperationException($"Unknown marketplace action '{actionId}'."),
    };

    private async Task<string> ImportProductsAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        int storeId,
        int userId,
        IReadOnlyDictionary<string, int> parameters,
        string operationId,
        CancellationToken ct)
    {
        var maxProducts = parameters.GetValueOrDefault("maxProducts", 50);
        var categoryId = parameters.GetValueOrDefault("targetCategoryId", 0);
        if (maxProducts is < 1 or > 200)
            throw new InvalidOperationException("Maximum products per run must be between 1 and 200.");
        if (categoryId < 0)
            throw new InvalidOperationException("Target category ID must be positive.");
        if (categoryId > 0 && !await _db.categories.AsNoTracking()
                .AnyAsync(x => x.Id == categoryId && x.storeId == storeId, ct))
            throw new InvalidOperationException("The target category does not belong to this store.");

        string? cursor = null;
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        while (imported + skipped + failed < maxProducts)
        {
            ct.ThrowIfCancellationRequested();
            var page = await adapter.ImportAllProductsAsync(
                connection, Math.Min(50, maxProducts - imported - skipped - failed), cursor, null, ct);
            foreach (var external in page.Items)
            {
                if (imported + skipped + failed >= maxProducts) break;
                var result = await _products.ImportProductAsync(
                    connection.Id, external.ExternalId, categoryId > 0 ? categoryId : null,
                    userId, ct, operationId, storeId);
                if (result.IsSuccess) imported++;
                else if (result.AlreadyLinked) skipped++;
                else failed++;
            }
            if (!page.HasMore || string.IsNullOrEmpty(page.NextCursor)) break;
            cursor = page.NextCursor;
        }
        return $"Products imported: {imported}; already linked: {skipped}; failed: {failed}.";
    }

    private static void EnsureSuccess(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }
}
