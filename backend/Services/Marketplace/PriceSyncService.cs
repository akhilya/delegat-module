using Microsoft.EntityFrameworkCore;
using PrintO.Enums;
using PrintO.Models.Marketplace;
using PrintO.Models.Marketplace.DTOs;

namespace PrintO.Services.Marketplace;

/// <summary>
/// Service for synchronizing prices and stock with marketplaces
/// </summary>
public class PriceSyncService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;

    public PriceSyncService(DataContext context, IMarketplaceAdapterRegistry adapterRegistry)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
    }

    /// <summary>
    /// Get a price comparison between local products and the marketplace
    /// </summary>
    public async Task<PriceComparisonResult> ComparePricesAsync(
        int connectionId,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return PriceComparisonResult.Failure("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceComparisonResult.Failure($"Адаптер '{connection.adapterKey}' не найден");

        // Get all product links
        var links = await _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId)
            .ToListAsync(ct);

        if (!links.Any())
            return PriceComparisonResult.Success(new List<PriceComparisonItem>(), "Нет связанных товаров");

        // If the adapter supports price import - get current prices
        List<PriceInfo>? externalPrices = null;
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportPrices))
        {
            try
            {
                var externalIds = links.Select(l => l.externalProductId).ToList();
                externalPrices = await adapter.ImportPricesAsync(connection, externalIds, ct);
            }
            catch
            {
                // Ignore error and use cached data
            }
        }

        var comparisons = new List<PriceComparisonItem>();

        foreach (var link in links)
        {
            var externalPrice = externalPrices?.FirstOrDefault(p => p.ExternalProductId == link.externalProductId);

            // Update cached data if we received current data
            if (externalPrice != null)
            {
                link.externalPrice = externalPrice.Price;
                link.externalStock = externalPrice.Stock;
            }

            var item = new PriceComparisonItem
            {
                LinkId = link.Id,
                ProductId = link.productId,
                ProductSku = link.product?.sku,
                ProductName = link.product?.name,
                ExternalProductId = link.externalProductId,
                LocalPrice = link.product?.price,
                ExternalPrice = link.externalPrice,
                LocalStock = link.product?.storageQuantity,
                ExternalStock = link.externalStock,
                HasPriceDifference = link.product?.price != link.externalPrice,
                HasStockDifference = link.product?.storageQuantity != link.externalStock
            };

            comparisons.Add(item);
        }

        // Save updated data
        if (externalPrices != null)
            await _context.SaveChangesAsync(ct);

        return PriceComparisonResult.Success(
            comparisons,
            $"Сравнение для {comparisons.Count} товаров"
        );
    }

    /// <summary>
    /// Import prices from the marketplace into local products
    /// </summary>
    public async Task<PriceSyncResult> ImportPricesAsync(
        int connectionId,
        List<int>? linkIds = null,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return PriceSyncResult.Failure("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceSyncResult.Failure($"Адаптер '{connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportPrices))
            return PriceSyncResult.Failure("Адаптер не поддерживает импорт цен");

        // Get links
        var linksQuery = _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId);

        if (linkIds != null && linkIds.Any())
            linksQuery = linksQuery.Where(l => linkIds.Contains(l.Id));

        var links = await linksQuery.ToListAsync(ct);

        if (!links.Any())
            return PriceSyncResult.Success(0, 0, "Нет товаров для синхронизации");

        try
        {
            // Get prices from the marketplace
            var externalIds = links.Select(l => l.externalProductId).ToList();
            var externalPrices = await adapter.ImportPricesAsync(connection, externalIds, ct);

            int updated = 0;
            int skipped = 0;

            foreach (var link in links)
            {
                var externalPrice = externalPrices.FirstOrDefault(p => p.ExternalProductId == link.externalProductId);
                if (externalPrice == null)
                {
                    skipped++;
                    continue;
                }

                // Update local product
                if (link.product != null && externalPrice.Price.HasValue)
                {
                    link.product.price = externalPrice.Price;
                    if (externalPrice.Stock.HasValue)
                        link.product.storageQuantity = externalPrice.Stock;
                }

                // Update cache in the link
                link.externalPrice = externalPrice.Price;
                link.externalStock = externalPrice.Stock;
                link.lastSyncAt = DateTime.UtcNow;

                updated++;
            }

            await _context.SaveChangesAsync(ct);

            return PriceSyncResult.Success(updated, skipped, $"Обновлено {updated} товаров");
        }
        catch (Exception ex)
        {
            return PriceSyncResult.Failure($"Ошибка импорта цен: {ex.Message}");
        }
    }

    /// <summary>
    /// Export prices to the marketplace
    /// </summary>
    public async Task<PriceSyncResult> ExportPricesAsync(
        int connectionId,
        List<int>? linkIds = null,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return PriceSyncResult.Failure("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceSyncResult.Failure($"Адаптер '{connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
            return PriceSyncResult.Failure("Адаптер не поддерживает экспорт цен");

        // Get links
        var linksQuery = _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId);

        if (linkIds != null && linkIds.Any())
            linksQuery = linksQuery.Where(l => linkIds.Contains(l.Id));

        var links = await linksQuery.ToListAsync(ct);

        if (!links.Any())
            return PriceSyncResult.Success(0, 0, "Нет товаров для синхронизации");

        // Build data for export
        var pricesToExport = links
            .Where(l => l.product?.price != null)
            .Select(l => new PriceInfo(
                l.externalProductId,
                l.product!.price,
                null, // oldPrice
                l.product.storageQuantity
            ))
            .ToList();

        if (!pricesToExport.Any())
            return PriceSyncResult.Success(0, links.Count, "Нет товаров с ценами для экспорта");

        try
        {
            var result = await adapter.ExportPricesAsync(connection, pricesToExport, ct);

            if (result.IsSuccess)
            {
                // Update cache
                foreach (var link in links.Where(l => l.product?.price != null))
                {
                    link.externalPrice = link.product!.price;
                    link.externalStock = link.product.storageQuantity;
                    link.lastSyncAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync(ct);

                return PriceSyncResult.Success(
                    result.SuccessCount,
                    result.FailedCount,
                    $"Экспортировано {result.SuccessCount} цен"
                );
            }

            var errorMessages = result.Errors?.Select(e => e.Message).ToList();
            return PriceSyncResult.Failure(errorMessages?.FirstOrDefault() ?? "Ошибка экспорта цен");
        }
        catch (Exception ex)
        {
            return PriceSyncResult.Failure($"Ошибка экспорта цен: {ex.Message}");
        }
    }

    /// <summary>
    /// Export stock to the marketplace
    /// </summary>
    public async Task<PriceSyncResult> ExportStocksAsync(
        int connectionId,
        List<int>? linkIds = null,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return PriceSyncResult.Failure("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceSyncResult.Failure($"Адаптер '{connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks))
            return PriceSyncResult.Failure("Адаптер не поддерживает экспорт остатков");

        // Get links
        var linksQuery = _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId);

        if (linkIds != null && linkIds.Any())
            linksQuery = linksQuery.Where(l => linkIds.Contains(l.Id));

        var links = await linksQuery.ToListAsync(ct);

        if (!links.Any())
            return PriceSyncResult.Success(0, 0, "Нет товаров для синхронизации");

        // Load variants with marketplace links for this connection
        var productIds = links.Select(l => l.productId).ToList();
        var allVariants = await _context.productVariants
            .Where(v => productIds.Contains(v.productId))
            .Include(v => v.marketplaceLinks.Where(l => l.connectionId == connectionId))
            .ToListAsync(ct);
        var variantsByProductId = allVariants
            .GroupBy(v => v.productId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build data for export — per-variant if variants exist, otherwise single stock per link
        var stocksToExport = new List<StockInfo>();
        foreach (var l in links.Where(l => l.product != null))
        {
            if (variantsByProductId.TryGetValue(l.productId, out var variants) && variants.Count > 0)
            {
                // One StockInfo per variant (only those with marketplace link)
                foreach (var v in variants)
                {
                    var link = v.marketplaceLinks.FirstOrDefault();
                    if (link != null)
                    {
                        stocksToExport.Add(new StockInfo(
                            l.externalProductId,
                            v.quantity ?? l.product!.storageQuantity ?? 0,
                            VariantId: link.externalVariantId,
                            Barcode: v.barcode
                        ));
                    }
                }
            }
            else if (l.product!.storageQuantity != null)
            {
                stocksToExport.Add(new StockInfo(
                    l.externalProductId,
                    l.product.storageQuantity.Value
                ));
            }
        }

        if (!stocksToExport.Any())
            return PriceSyncResult.Success(0, links.Count, "Нет товаров с остатками для экспорта");

        try
        {
            var result = await adapter.ExportStocksAsync(connection, stocksToExport, ct);

            if (result.IsSuccess)
            {
                // Update cache
                foreach (var link in links.Where(l => l.product?.storageQuantity != null))
                {
                    link.externalStock = link.product!.storageQuantity;
                    link.lastSyncAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync(ct);

                return PriceSyncResult.Success(
                    result.SuccessCount,
                    result.FailedCount,
                    $"Экспортировано {result.SuccessCount} остатков"
                );
            }

            var errorMessages = result.Errors?.Select(e => e.Message).ToList();
            return PriceSyncResult.Failure(errorMessages?.FirstOrDefault() ?? "Ошибка экспорта остатков");
        }
        catch (Exception ex)
        {
            return PriceSyncResult.Failure($"Ошибка экспорта остатков: {ex.Message}");
        }
    }
}

#region Result DTOs

public class PriceComparisonResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<PriceComparisonItem> Items { get; private set; } = new();
    public string? Message { get; private set; }

    public static PriceComparisonResult Success(List<PriceComparisonItem> items, string message)
    {
        return new PriceComparisonResult
        {
            IsSuccess = true,
            Items = items,
            Message = message
        };
    }

    public static PriceComparisonResult Failure(string error)
    {
        return new PriceComparisonResult
        {
            IsSuccess = false,
            ErrorMessage = error
        };
    }
}

public class PriceComparisonItem
{
    public int LinkId { get; set; }
    public int ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public string ExternalProductId { get; set; } = null!;
    public decimal? LocalPrice { get; set; }
    public decimal? ExternalPrice { get; set; }
    public int? LocalStock { get; set; }
    public int? ExternalStock { get; set; }
    public bool HasPriceDifference { get; set; }
    public bool HasStockDifference { get; set; }
}

public class PriceSyncResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int UpdatedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public string? Message { get; private set; }

    public static PriceSyncResult Success(int updated, int skipped, string message)
    {
        return new PriceSyncResult
        {
            IsSuccess = true,
            UpdatedCount = updated,
            SkippedCount = skipped,
            Message = message
        };
    }

    public static PriceSyncResult Failure(string error)
    {
        return new PriceSyncResult
        {
            IsSuccess = false,
            ErrorMessage = error
        };
    }
}

#endregion
