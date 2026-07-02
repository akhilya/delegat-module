using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for synchronizing prices and stock with marketplaces
/// </summary>
public class PriceSyncService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly IOperationLogger _operationLogger;

    public PriceSyncService(DataContext context, IMarketplaceAdapterRegistry adapterRegistry, IOperationLogger operationLogger)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _operationLogger = operationLogger;
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
        CancellationToken ct = default,
        string? operationId = null,
        int? storeId = null)
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

        var canLog = operationId != null && storeId != null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (canLog)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Найдено {links.Count} связанных товаров, запрос цен с {connection.adapterKey}...");

        try
        {
            // Get prices from the marketplace
            var externalIds = links.Select(l => l.externalProductId).ToList();
            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            var externalPrices = await adapter.ImportPricesAsync(connection, externalIds, ct);
            apiSw.Stop();

            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                    $"Получено {externalPrices.Count} цен с маркетплейса за {apiSw.ElapsedMilliseconds}мс");

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
            sw.Stop();

            var msg = $"Обновлено {updated} товаров, пропущено {skipped} — {sw.ElapsedMilliseconds}мс";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "success", msg);
            return PriceSyncResult.Success(updated, skipped, msg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Ошибка импорта цен: {ex.Message}";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} — {sw.ElapsedMilliseconds}мс");
            return PriceSyncResult.Failure(errMsg);
        }
    }

    /// <summary>
    /// Export prices to the marketplace
    /// </summary>
    public async Task<PriceSyncResult> ExportPricesAsync(
        int connectionId,
        List<int>? linkIds = null,
        CancellationToken ct = default,
        string? operationId = null,
        int? storeId = null)
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

        var canLog = operationId != null && storeId != null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (canLog)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Найдено {links.Count} связанных товаров для подключения #{connectionId} ({connection.adapterKey})");

        var exportableLinks = links
            .Where(l => l.product?.price != null)
            .ToList();

        // Marketplaces such as Wildberries reject duplicate product IDs in one price upload.
        var uniqueExportableLinks = exportableLinks
            .GroupBy(l => l.externalProductId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Build data for export
        var pricesToExport = uniqueExportableLinks
            .Select(l => new PriceInfo(
                l.externalProductId,
                l.product!.price,
                null, // oldPrice
                l.product.storageQuantity
            ))
            .ToList();

        var skippedNoPrice = links.Count - exportableLinks.Count;
        var skippedDuplicateExternalIds = exportableLinks.Count - uniqueExportableLinks.Count;
        if (canLog && skippedNoPrice > 0)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                $"Пропущено {skippedNoPrice} товаров без цены");
        if (canLog && skippedDuplicateExternalIds > 0)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                $"Пропущено {skippedDuplicateExternalIds} дублей внешних ID в одном запросе");

        if (!pricesToExport.Any())
        {
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                    $"Нет товаров с ценами для экспорта ({sw.ElapsedMilliseconds}мс)");
            return PriceSyncResult.Success(0, links.Count, "Нет товаров с ценами для экспорта");
        }

        if (canLog)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Подготовлено {pricesToExport.Count} цен, отправка на {connection.adapterKey}...");

        try
        {
            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            var result = await adapter.ExportPricesAsync(connection, pricesToExport, ct);
            apiSw.Stop();

            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                    $"Ответ маркетплейса получен за {apiSw.ElapsedMilliseconds}мс");

            if (result.IsSuccess || result.IsPartialSuccess)
            {
                var failedExternalIds = result.Errors?
                    .Where(e => !string.IsNullOrWhiteSpace(e.ExternalId))
                    .Select(e => e.ExternalId!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Update cache
                foreach (var link in exportableLinks.Where(l => !failedExternalIds.Contains(l.externalProductId)))
                {
                    link.externalPrice = link.product!.price;
                    link.externalStock = link.product.storageQuantity;
                    link.lastSyncAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync(ct);
                sw.Stop();

                var msg = $"Экспортировано {result.SuccessCount} цен, ошибок: {result.FailedCount}, пропущено: {skippedNoPrice + skippedDuplicateExternalIds} — {sw.ElapsedMilliseconds}мс";
                if (canLog)
                {
                    await _operationLogger.LogAsync(operationId!, storeId!.Value,
                        result.IsSuccess ? "success" : "warning", msg);
                    if (result.Errors?.Any() == true)
                    {
                        foreach (var error in result.Errors.Take(10))
                        {
                            var externalId = string.IsNullOrWhiteSpace(error.ExternalId) ? "unknown" : error.ExternalId;
                            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                                $"{externalId}: {error.Message}");
                        }
                    }
                }

                return PriceSyncResult.Success(result.SuccessCount, result.FailedCount + skippedNoPrice + skippedDuplicateExternalIds, msg);
            }

            sw.Stop();
            var errorMessages = result.Errors?.Select(e => e.Message).ToList();
            var errMsg = errorMessages?.FirstOrDefault() ?? "Ошибка экспорта цен";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} — {sw.ElapsedMilliseconds}мс");
            return PriceSyncResult.Failure(errMsg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Ошибка экспорта цен: {ex.Message}";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} — {sw.ElapsedMilliseconds}мс");
            return PriceSyncResult.Failure(errMsg);
        }
    }

    /// <summary>
    /// Export stock to the marketplace
    /// </summary>
    public async Task<PriceSyncResult> ExportStocksAsync(
        int connectionId,
        List<int>? linkIds = null,
        CancellationToken ct = default,
        string? operationId = null,
        int? storeId = null)
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
        var stockExportEntries = new List<(ProductMarketplaceLink Link, StockInfo Stock)>();
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
                        stockExportEntries.Add((l, new StockInfo(
                            l.externalProductId,
                            v.quantity ?? l.product!.storageQuantity ?? 0,
                            VariantId: link.externalVariantId,
                            Barcode: v.barcode
                        )));
                    }
                }
            }
            else if (l.product!.storageQuantity != null)
            {
                stockExportEntries.Add((l, new StockInfo(
                    l.externalProductId,
                    l.product.storageQuantity.Value
                )));
            }
        }

        var uniqueStockExportEntries = stockExportEntries
            .GroupBy(e => BuildStockExportKey(e.Stock), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var stocksToExport = uniqueStockExportEntries
            .Select(e => e.Stock)
            .ToList();
        var skippedDuplicateStockTargets = stockExportEntries.Count - uniqueStockExportEntries.Count;

        if (!stocksToExport.Any())
            return PriceSyncResult.Success(0, links.Count, "Нет товаров с остатками для экспорта");

        var canLog = operationId != null && storeId != null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (canLog)
        {
            var variantCount = stocksToExport.Count(s => s.VariantId != null);
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Подготовлено {stocksToExport.Count} остатков ({variantCount} вариантов), отправка на {connection.adapterKey}...");
            if (skippedDuplicateStockTargets > 0)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                    $"Пропущено {skippedDuplicateStockTargets} дублей внешних остатков в одном запросе");
        }

        try
        {
            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            var result = await adapter.ExportStocksAsync(connection, stocksToExport, ct);
            apiSw.Stop();

            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                    $"Ответ маркетплейса получен за {apiSw.ElapsedMilliseconds}мс");

            if (result.IsSuccess || result.IsPartialSuccess)
            {
                var failedExternalIds = result.Errors?
                    .Where(e => !string.IsNullOrWhiteSpace(e.ExternalId))
                    .Select(e => e.ExternalId!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Update cache
                foreach (var link in uniqueStockExportEntries
                    .Where(e => !failedExternalIds.Contains(e.Stock.ExternalProductId))
                    .Select(e => e.Link)
                    .DistinctBy(l => l.Id))
                {
                    link.externalStock = link.product!.storageQuantity;
                    link.lastSyncAt = DateTime.UtcNow;
                }
                await _context.SaveChangesAsync(ct);
                sw.Stop();

                var skipped = result.FailedCount + skippedDuplicateStockTargets;
                var msg = $"Экспортировано {result.SuccessCount} остатков, ошибок: {result.FailedCount}, пропущено: {skippedDuplicateStockTargets} — {sw.ElapsedMilliseconds}мс";
                if (canLog)
                {
                    await _operationLogger.LogAsync(operationId!, storeId!.Value,
                        result.IsSuccess ? "success" : "warning", msg);
                    if (result.Errors?.Any() == true)
                    {
                        foreach (var error in result.Errors.Take(10))
                        {
                            var externalId = string.IsNullOrWhiteSpace(error.ExternalId) ? "unknown" : error.ExternalId;
                            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                                $"{externalId}: {error.Message}");
                        }
                    }
                }

                return PriceSyncResult.Success(result.SuccessCount, skipped, msg);
            }

            sw.Stop();
            var errorMessages = result.Errors?.Select(e => e.Message).ToList();
            var errMsg = errorMessages?.FirstOrDefault() ?? "Ошибка экспорта остатков";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} — {sw.ElapsedMilliseconds}мс");
            return PriceSyncResult.Failure(errMsg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Ошибка экспорта остатков: {ex.Message}";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} — {sw.ElapsedMilliseconds}мс");
            return PriceSyncResult.Failure(errMsg);
        }
    }

    private static string BuildStockExportKey(StockInfo stock)
    {
        return string.Join(
            "|",
            stock.ExternalProductId,
            stock.VariantId ?? "",
            stock.WarehouseId ?? "");
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
