using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for synchronizing prices and stock with marketplaces
/// </summary>
public class PriceSyncService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly MappingService _mappingService;
    private readonly IOperationLogger _operationLogger;
    private readonly CurrencyService _currencyService;

    public PriceSyncService(
        DataContext context,
        IMarketplaceAdapterRegistry adapterRegistry,
        MappingService mappingService,
        IOperationLogger operationLogger,
        CurrencyService currencyService)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _mappingService = mappingService;
        _operationLogger = operationLogger;
        _currencyService = currencyService;
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
            return PriceComparisonResult.Failure("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceComparisonResult.Failure($"Adapter '{connection.adapterKey}' not found");

        // Get all product links
        var links = await _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId)
            .ToListAsync(ct);

        if (!links.Any())
            return PriceComparisonResult.Success(new List<PriceComparisonItem>(), "No linked products");

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
            $"Comparison for {comparisons.Count} products"
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
            return PriceSyncResult.Failure("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceSyncResult.Failure($"Adapter '{connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportPrices))
            return PriceSyncResult.Failure("Adapter does not support price import");

        // Get links with mapping for reverse price import
        var linksQuery = _context.productMarketplaceLinks
            .Include(l => l.product)
                .ThenInclude(p => p!.attributeValues)
                    .ThenInclude(av => av.attribute)
            .Where(l => l.connectionId == connectionId);

        if (linkIds != null && linkIds.Any())
            linksQuery = linksQuery.Where(l => linkIds.Contains(l.Id));

        var links = await linksQuery.ToListAsync(ct);
        var mappingIds = links.Where(l => l.categoryMappingId != null).Select(l => l.categoryMappingId!.Value).Distinct().ToList();
        var mappings = await _context.categoryMappings
            .Where(m => mappingIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        if (!links.Any())
            return PriceSyncResult.Success(0, 0, "No products to sync");

        var extendedPrice = await _context.stores.AsNoTracking()
            .Where(s => s.Id == connection.storeId)
            .Select(s => s.extendedPriceParameters)
            .FirstOrDefaultAsync(ct);

        var canLog = operationId != null && storeId != null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (canLog)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Found {links.Count} linked products, requesting prices from {connection.adapterKey}...");

        try
        {
            // Get prices from the marketplace
            var externalIds = links.Select(l => l.externalProductId).ToList();
            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            var externalPrices = await adapter.ImportPricesAsync(connection, externalIds, ct);
            apiSw.Stop();

            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                    $"Received {externalPrices.Count} prices from the marketplace in {apiSw.ElapsedMilliseconds}ms");

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
                if (link.product != null)
                {
                    ApplyImportedPriceToProduct(
                        link.product,
                        externalPrice,
                        link.categoryMappingId != null && mappings.TryGetValue(link.categoryMappingId.Value, out var mapping)
                            ? mapping
                            : null,
                        extendedPrice);
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

            var msg = $"Updated {updated} products, skipped {skipped} - {sw.ElapsedMilliseconds}ms";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "success", msg);
            return PriceSyncResult.Success(updated, skipped, msg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Price import error: {ex.Message}";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} - {sw.ElapsedMilliseconds}ms");
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
            return PriceSyncResult.Failure("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceSyncResult.Failure($"Adapter '{connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
            return PriceSyncResult.Failure("Adapter does not support price export");

        var linksQuery = _context.productMarketplaceLinks
            .Include(l => l.product)
                .ThenInclude(p => p!.attributeValues)
                    .ThenInclude(av => av.attribute)
            .Where(l => l.connectionId == connectionId);

        if (linkIds != null && linkIds.Any())
            linksQuery = linksQuery.Where(l => linkIds.Contains(l.Id));

        var links = await linksQuery.ToListAsync(ct);

        if (!links.Any())
            return PriceSyncResult.Success(0, 0, "No products to sync");

        var storeSettings = await _context.stores.AsNoTracking()
            .Where(s => s.Id == connection.storeId)
            .Select(s => new { s.extendedPriceParameters, s.baseCurrency })
            .FirstOrDefaultAsync(ct);
        var extendedPrice = storeSettings?.extendedPriceParameters ?? false;
        var storeBaseCurrency = storeSettings?.baseCurrency
            ?? throw new InvalidOperationException($"Store {connection.storeId} has no base currency configured");

        var canLog = operationId != null && storeId != null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (canLog)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Found {links.Count} linked products for connection #{connectionId} ({connection.adapterKey})");

        var exportableLinks = links
            .Where(l => l.product != null)
            .Where(l => !string.IsNullOrWhiteSpace(l.externalProductId)
                     || !string.IsNullOrWhiteSpace(l.externalSku))
            .ToList();

        // Marketplaces such as Wildberries reject duplicate product IDs in one price upload.
        // Use sku:-prefixed key for links that only have externalSku so they aren't collapsed together.
        var uniqueExportableLinks = exportableLinks
            .GroupBy(l => string.IsNullOrWhiteSpace(l.externalProductId)
                ? "sku:" + l.externalSku
                : l.externalProductId,
                StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var categoryIds = uniqueExportableLinks
            .Select(l => l.product?.categoryId)
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var categoryMappings = categoryIds.Count == 0
            ? new List<CategoryMapping>()
            : await _context.categoryMappings
                .Where(cm => cm.connectionId == connectionId && categoryIds.Contains(cm.localCategoryId))
                .ToListAsync(ct);
        var mappingsById = categoryMappings.ToDictionary(m => m.Id);
        var mappingsByCategoryId = categoryMappings
            .GroupBy(m => m.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var pricesToExport = new List<PriceInfo>();
        var mappingNotSelected = 0;
        foreach (var l in uniqueExportableLinks)
        {
            var mapping = ResolvePriceMapping(
                l.categoryMappingId,
                l.product?.categoryId,
                mappingsById,
                mappingsByCategoryId);
            if (mapping == null
                && l.product?.categoryId != null
                && mappingsByCategoryId.TryGetValue(l.product.categoryId.Value, out var options)
                && options.Count > 1)
            {
                mappingNotSelected++;
            }
            else if (mapping != null && l.categoryMappingId != mapping.Id)
            {
                l.categoryMappingId = mapping.Id;
            }

            PriceInfo? fromGraph = null;
            if (mapping != null)
            {
                fromGraph = await _mappingService.BuildPriceInfoFromGraphAsync(
                    l.product!,
                    mapping,
                    l.externalProductId,
                    l.externalSku,
                    l.product!.storageQuantity,
                    ct);
            }

            var price = fromGraph ?? BuildLegacyPriceInfo(l, extendedPrice, storeBaseCurrency);
            if (price.Price != null)
                pricesToExport.Add(price);
        }

        var convertedPrices = new List<PriceInfo>();
        foreach (var price in pricesToExport)
            convertedPrices.Add(await ConvertPriceInfoForExportAsync(price, connection.storeId, ct));
        pricesToExport = convertedPrices;

        var skippedNoPrice = 0;
        foreach (var l in exportableLinks)
        {
            var mapping = ResolvePriceMapping(
                l.categoryMappingId,
                l.product?.categoryId,
                mappingsById,
                mappingsByCategoryId);
            if (mapping != null
                && MappingService.SchemaPriceFields(mapping.graphJson).Count > 0)
            {
                var info = await _mappingService.BuildPriceInfoFromGraphAsync(
                    l.product!, mapping, l.externalProductId, l.externalSku, l.product!.storageQuantity, ct);
                if (info == null)
                    skippedNoPrice++;
            }
            else if (l.product?.price == null)
            {
                skippedNoPrice++;
            }
        }
        var skippedNoIdentifier = links.Count(l =>
            l.product?.price != null
            && string.IsNullOrWhiteSpace(l.externalProductId)
            && string.IsNullOrWhiteSpace(l.externalSku));
        var skippedDuplicateExternalIds = exportableLinks.Count - uniqueExportableLinks.Count;
        if (canLog && skippedNoPrice > 0)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                $"Skipped {skippedNoPrice} products without a price");
        if (canLog && skippedNoIdentifier > 0)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                $"Skipped {skippedNoIdentifier} products without an external identifier");
        if (canLog && skippedDuplicateExternalIds > 0)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                $"Skipped {skippedDuplicateExternalIds} duplicate external IDs in one request");
        if (canLog && mappingNotSelected > 0)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                $"Price mapping was not applied for {mappingNotSelected} products because the category has several mappings and none is selected");

        if (!pricesToExport.Any())
        {
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                    $"No products with prices to export ({sw.ElapsedMilliseconds}ms)");
            return PriceSyncResult.Success(0, links.Count, "No products with prices to export");
        }

        if (canLog)
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Prepared {pricesToExport.Count} prices, sending to {connection.adapterKey}...");

        try
        {
            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            var result = await adapter.ExportPricesAsync(connection, pricesToExport, ct);
            apiSw.Stop();

            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                    $"Marketplace response received in {apiSw.ElapsedMilliseconds}ms");

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

                var msg = $"Exported {result.SuccessCount} prices, errors: {result.FailedCount}, skipped: {skippedNoPrice + skippedDuplicateExternalIds} - {sw.ElapsedMilliseconds}ms";
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
            var errMsg = errorMessages?.FirstOrDefault() ?? "Price export error";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} - {sw.ElapsedMilliseconds}ms");
            return PriceSyncResult.Failure(errMsg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Price export error: {ex.Message}";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} - {sw.ElapsedMilliseconds}ms");
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
            return PriceSyncResult.Failure("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return PriceSyncResult.Failure($"Adapter '{connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks))
            return PriceSyncResult.Failure("Adapter does not support stock export");

        // Get links
        var linksQuery = _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId);

        if (linkIds != null && linkIds.Any())
            linksQuery = linksQuery.Where(l => linkIds.Contains(l.Id));

        var links = await linksQuery.ToListAsync(ct);

        if (!links.Any())
            return PriceSyncResult.Success(0, 0, "No products to sync");

        // Load variants with marketplace links for this connection
        var productIds = links.Select(l => l.productId).ToList();
        var allVariants = await _context.productVariants
            .Where(v => productIds.Contains(v.productId))
            .Include(v => v.marketplaceLinks.Where(l => l.connectionId == connectionId))
            .ToListAsync(ct);
        var variantsByProductId = allVariants
            .GroupBy(v => v.productId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build data for export - per-variant if variants exist, otherwise single stock per link
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
            return PriceSyncResult.Success(0, links.Count, "No products with stock to export");

        var canLog = operationId != null && storeId != null;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (canLog)
        {
            var variantCount = stocksToExport.Count(s => s.VariantId != null);
            await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                $"Prepared {stocksToExport.Count} stock entries ({variantCount} variants), sending to {connection.adapterKey}...");
            if (skippedDuplicateStockTargets > 0)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "warning",
                    $"Skipped {skippedDuplicateStockTargets} duplicate external stock entries in one request");
        }

        try
        {
            var apiSw = System.Diagnostics.Stopwatch.StartNew();
            var result = await adapter.ExportStocksAsync(connection, stocksToExport, ct);
            apiSw.Stop();

            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "info",
                    $"Marketplace response received in {apiSw.ElapsedMilliseconds}ms");

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
                var msg = $"Exported {result.SuccessCount} stock entries, errors: {result.FailedCount}, skipped: {skippedDuplicateStockTargets} - {sw.ElapsedMilliseconds}ms";
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
            var errMsg = errorMessages?.FirstOrDefault() ?? "Stock export error";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} - {sw.ElapsedMilliseconds}ms");
            return PriceSyncResult.Failure(errMsg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var errMsg = $"Stock export error: {ex.Message}";
            if (canLog)
                await _operationLogger.LogAsync(operationId!, storeId!.Value, "error",
                    $"{errMsg} - {sw.ElapsedMilliseconds}ms");
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

    /// <summary>
    /// Saved link choice when it still matches the product category.
    /// One mapping on the category is used when the link has not saved a choice, same as product export.
    /// </summary>
    internal static CategoryMapping? ResolvePriceMapping(
        int? savedMappingId,
        int? productCategoryId,
        IReadOnlyDictionary<int, CategoryMapping> mappingsById,
        IReadOnlyDictionary<int, List<CategoryMapping>> mappingsByCategoryId)
    {
        if (productCategoryId == null)
            return null;

        if (savedMappingId != null
            && mappingsById.TryGetValue(savedMappingId.Value, out var saved)
            && saved.localCategoryId == productCategoryId.Value)
            return saved;

        if (mappingsByCategoryId.TryGetValue(productCategoryId.Value, out var list) && list.Count == 1)
            return list[0];

        return null;
    }

    private static PriceInfo BuildLegacyPriceInfo(ProductMarketplaceLink l, bool extendedPriceParameters, string storeBaseCurrency)
    {
        var p = l.product!;
        if (extendedPriceParameters)
        {
            return new PriceInfo(
                ExternalProductId: l.externalProductId,
                Price: p.price,
                // Marketplaces (e.g. Yandex) reject oldPrice == price as 0% discount
                OldPrice: ProductPriceHelper.SanitizeOldPrice(p.price, p.oldPrice),
                Stock: p.storageQuantity,
                DiscountedPrice: p.discountedPrice,
                MinPrice: p.minPrice,
                ExternalSku: l.externalSku,
                Discount: p.discount,
                NetPrice: p.netPrice,
                Currency: p.currency ?? storeBaseCurrency);
        }

        var avs = p.attributeValues;
        return new PriceInfo(
            ExternalProductId: l.externalProductId,
            Price: p.price,
            OldPrice: ProductPriceHelper.SanitizeOldPrice(p.price, ParsePriceAttr(avs, "old_price")),
            Stock: p.storageQuantity,
            MinPrice: ParsePriceAttr(avs, "min_price"),
            ExternalSku: l.externalSku,
            Discount: ParseIntAttr(avs, "discount")
        );
    }

    private async Task<PriceInfo> ConvertPriceInfoForExportAsync(PriceInfo info, int storeId, CancellationToken ct)
    {
        var baseCurrency = await _currencyService.GetStoreBaseCurrencyAsync(storeId, ct);
        var targetCurrency = CurrencyService.NormalizeCodeOrBase(info.Currency, baseCurrency);
        if (targetCurrency == baseCurrency)
            return info;

        async Task<decimal?> Conv(decimal? v) =>
            v == null ? null : await _currencyService.ConvertFromBaseAsync(v.Value, targetCurrency, storeId, ct);

        return info with
        {
            Price = await Conv(info.Price),
            OldPrice = await Conv(info.OldPrice),
            MinPrice = await Conv(info.MinPrice),
            DiscountedPrice = await Conv(info.DiscountedPrice),
            NetPrice = await Conv(info.NetPrice),
        };
    }

    private static void ApplyImportedPriceToProduct(
        Product product,
        PriceInfo externalPrice,
        CategoryMapping? mapping,
        bool extendedPriceParameters)
    {
        if (externalPrice.Price.HasValue)
            product.price = externalPrice.Price;

        if (extendedPriceParameters)
        {
            if (externalPrice.OldPrice.HasValue) product.oldPrice = externalPrice.OldPrice;
            if (externalPrice.MinPrice.HasValue) product.minPrice = externalPrice.MinPrice;
            if (externalPrice.DiscountedPrice.HasValue) product.discountedPrice = externalPrice.DiscountedPrice;
            if (externalPrice.Discount.HasValue) product.discount = externalPrice.Discount;
            if (externalPrice.NetPrice.HasValue) product.netPrice = externalPrice.NetPrice;
            if (!string.IsNullOrWhiteSpace(externalPrice.Currency))
                product.currency = externalPrice.Currency;
        }

        if (mapping == null || MappingService.SchemaPriceFields(mapping.graphJson).Count == 0)
            return;

        WriteDirectPriceBinding(product, mapping, "price", externalPrice.Price);
        if (!extendedPriceParameters)
        {
            WriteDirectPriceBinding(product, mapping, "oldPrice", externalPrice.OldPrice);
            WriteDirectPriceBinding(product, mapping, "minPrice", externalPrice.MinPrice);
            WriteDirectPriceBinding(product, mapping, "discountedPrice", externalPrice.DiscountedPrice);
            WriteDirectPriceBinding(product, mapping, "discount", externalPrice.Discount);
            WriteDirectPriceBinding(product, mapping, "netPrice", externalPrice.NetPrice);
        }
    }

    private static void WriteDirectPriceBinding(Product product, CategoryMapping mapping, string fieldKey, object? value)
    {
        if (value == null) return;

        var field = MappingService.SchemaPriceFields(mapping.graphJson)
            .FirstOrDefault(f => string.Equals(f.fieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
        var source = field?.source;
        if (source?.type is not ("attribute" or "storeAttribute") || source.attributeId == null)
            return;

        var str = value switch
        {
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? ""
        };

        var av = product.attributeValues.FirstOrDefault(x => x.attributeId == source.attributeId);
        if (av != null)
            av.value = str;
        else
            product.attributeValues.Add(new ProductAttributeValue
            {
                attributeId = source.attributeId.Value,
                value = str,
            });
    }

    private static decimal? ParsePriceAttr(IEnumerable<ProductAttributeValue> avs, string key)
    {
        var av = avs.FirstOrDefault(x => x.attribute?.key == key);
        if (av == null) return null;
        return decimal.TryParse(av.value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v : null;
    }

    private static int? ParseIntAttr(IEnumerable<ProductAttributeValue> avs, string key)
    {
        var av = avs.FirstOrDefault(x => x.attribute?.key == key);
        if (av == null) return null;
        return int.TryParse(av.value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
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
