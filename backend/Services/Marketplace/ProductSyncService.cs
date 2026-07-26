using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Delegat.Enums;
using Delegat.Infrastructure.Storage;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services.Marketplace.Modifiers;
using System.Text.Json;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for synchronizing products with marketplaces (export, import, SKU update).
/// </summary>
public class ProductSyncService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly MappingService _mappingService;
    private readonly IStorageService _storageService;
    private readonly ILogger<ProductSyncService> _logger;
    private readonly IOperationLogger _operationLogger;

    /// <summary>
    /// Initializes a new instance of <see cref="ProductSyncService"/>.
    /// </summary>
    public ProductSyncService(
        DataContext context,
        IMarketplaceAdapterRegistry adapterRegistry,
        MappingService mappingService,
        IStorageService storageService,
        ILogger<ProductSyncService> logger,
        IOperationLogger operationLogger)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _mappingService = mappingService;
        _storageService = storageService;
        _logger = logger;
        _operationLogger = operationLogger;
    }

    /// <summary>
    /// Export a product to a marketplace via the adapter.
    /// </summary>
    public async Task<ProductExportResult> ExportProductAsync(
        int productId,
        int connectionId,
        CancellationToken ct = default,
        string? operationId = null,
        int? operationStoreId = null)
    {
        // Helper: log to operations panel (no-op when called without an operation context)
        async Task Log(string level, string message)
        {
            if (operationId != null && operationStoreId != null)
                await _operationLogger.LogAsync(operationId, operationStoreId.Value, level, message);
        }

        // Load the product with all relations
        var product = await _context.products
            .Include(p => p.category)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
            .Include(p => p.images)
                .ThenInclude(i => i.file)
            .Include(p => p.groupMemberships)
                .ThenInclude(gm => gm.group)
            .FirstOrDefaultAsync(p => p.Id == productId, ct);

        if (product == null)
            return ProductExportResult.Failure($"Product with ID {productId} not found");

        await Log("info", $"Exporting product: {product.name} ({product.sku})");

        // Load the connection
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
        {
            await Log("error", $"Connection #{connectionId} not found");
            return ProductExportResult.Failure($"Connection with ID {connectionId} not found");
        }

        if (!connection.isActive)
        {
            await Log("error", "Connection is inactive");
            return ProductExportResult.Failure("Connection is inactive");
        }

        // Get the adapter
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
        {
            await Log("error", $"Adapter '{connection.adapterKey}' not found");
            return ProductExportResult.Failure($"Adapter '{connection.adapterKey}' not found");
        }

        await Log("info", $"Connection: {connection.name ?? connection.adapterKey}");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct))
        {
            await Log("error", "Adapter does not support product export");
            return ProductExportResult.Failure("Adapter does not support product export");
        }

        // Check that a category and mapping exist
        if (!product.categoryId.HasValue)
        {
            await Log("error", "Product is not assigned to a category");
            return ProductExportResult.Failure("Product is not assigned to a category");
        }

        // Check the product's existing link
        var existingLink = await _context.productMarketplaceLinks
            .FirstOrDefaultAsync(l =>
                l.productId == productId &&
                l.connectionId == connectionId, ct);

        // If the product has a selected mapping - use it, otherwise fall back to the first one for the category
        CategoryMapping? categoryMapping;
        if (existingLink?.categoryMappingId != null)
        {
            categoryMapping = await _context.categoryMappings
                .FirstOrDefaultAsync(cm => cm.Id == existingLink.categoryMappingId.Value, ct);
        }
        else
        {
            categoryMapping = await _context.categoryMappings
                .FirstOrDefaultAsync(cm =>
                    cm.connectionId == connectionId &&
                    cm.localCategoryId == product.categoryId.Value, ct);
        }

        if (categoryMapping == null)
        {
            await Log("error", $"No mapping found for category '{product.category?.name}'");
            return ProductExportResult.Failure(
                $"No category mapping found for the connection. " +
                $"Create a mapping for category '{product.category?.name}'");
        }

        await Log("info", $"Category mapping: {product.category?.name} -> {categoryMapping.externalCategoryName ?? categoryMapping.externalCategoryId}");

        try
        {
            // Build the export data
            await Log("info", "Preparing export data");
            var exportData = await _mappingService.BuildExportDataAsync(product, categoryMapping, ct);

            // Add externalCategoryId
            exportData["_categoryId"] = categoryMapping.externalCategoryId;

            // Add group_id for field-based grouping,
            // but only if the user has NOT configured a mapping for this field
            var fieldGroupingConfig = adapter.GetFieldBasedGroupingConfig();
            if (fieldGroupingConfig != null &&
                adapter.Capabilities.HasFlag(MarketplaceCapabilities.FieldBasedGrouping) &&
                !exportData.ContainsKey(fieldGroupingConfig.GroupFieldKey))
            {
                var groupId = await GetProductGroupIdAsync(productId, connectionId, ct);
                if (groupId != null)
                {
                    exportData[fieldGroupingConfig.GroupFieldKey] = groupId;
                    _logger.LogDebug(
                        "Added group_id={GroupId} for product {ProductId} (field: {FieldKey})",
                        groupId, productId, fieldGroupingConfig.GroupFieldKey);
                }
            }

            // Endpoint grouping: the synthetic field's evaluated value drives grouping -
            // strip it from the payload, remember for the post-export group sync
            string? endpointGroupValue = null;
            if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping) &&
                exportData.TryGetValue(SyntheticGroupFieldKey, out var rawGroupValue))
            {
                exportData.Remove(SyntheticGroupFieldKey);
                endpointGroupValue = rawGroupValue?.ToString()?.Trim();
                if (endpointGroupValue is { Length: 0 }) endpointGroupValue = null;
            }

            // Inject variants if product has them - join with links for this connection
            var variants = await _context.productVariants
                .Where(v => v.productId == product.Id)
                .Include(v => v.marketplaceLinks.Where(l => l.connectionId == connectionId))
                .ToListAsync(ct);
            if (variants.Count > 0)
            {
                exportData["_variants"] = variants.Select(v =>
                {
                    var link = v.marketplaceLinks.FirstOrDefault();
                    return new Dictionary<string, object?>
                    {
                        ["variantId"] = link?.externalVariantId,
                        ["techSize"] = v.size,
                        ["marketplaceSize"] = link?.marketplaceSize,
                        ["barcode"] = v.barcode,
                        ["price"] = v.price,
                        ["quantity"] = v.quantity
                    };
                }).ToList();
            }

            // Validate required fields before sending (schema = graph adapter fields)
            var requiredMappings = MappingService.SchemaFields(categoryMapping.graphJson)
                .Where(f => f.isRequired && !string.IsNullOrEmpty(f.fieldKey))
                .ToList();

            var missingFields = new List<string>();
            var missingDetails = new List<string>();
            foreach (var mapping in requiredMappings)
            {
                if (!exportData.TryGetValue(mapping.fieldKey!, out var value) ||
                    value == null ||
                    (value is string strValue && string.IsNullOrWhiteSpace(strValue)))
                {
                    var fieldName = mapping.label ?? mapping.fieldKey!;
                    missingFields.Add(fieldName);
                    missingDetails.Add($"{fieldName} [key={mapping.fieldKey}]");
                    _logger.LogWarning(
                        "Required field '{FieldKey}' ({FieldName}) is empty for product {ProductId}.",
                        mapping.fieldKey, fieldName, productId);
                }
            }

            if (missingFields.Count > 0)
            {
                var fieldsStr = string.Join(", ", missingFields);
                var detailsStr = string.Join(", ", missingDetails);
                var mappingHint =
                    $"mapping #{categoryMapping.Id}" +
                    (string.IsNullOrWhiteSpace(categoryMapping.name)
                        ? ""
                        : $" «{categoryMapping.name}»");
                await Log(
                    "error",
                    $"Required fields are not filled ({mappingHint}): {detailsStr}. " +
                    "Export was blocked before calling the adapter (no HTTP request). " +
                    "Open field mapping and turn off Required for unused attributes, or fill values on the product. " +
                    "Attributes like clothing size/gender often come from another marketplace template.");
                return ProductExportResult.Failure(
                    $"Required fields are not filled: {fieldsStr}. " +
                    $"Check the product data and mapping settings ({mappingHint}). " +
                    "Disable Required on unused mapping fields or recreate the WooCommerce category mapping.");
            }

            await Log("info", $"Prepared fields: {exportData.Count(kv => kv.Value != null)}, required: {requiredMappings.Count}");

            // Convert to Dictionary<string, object> for compatibility
            var exportDataNonNull = exportData
                .Where(kv => kv.Value != null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!);

            // Log formatted JSON payload to operations panel (visible to user)
            try
            {
                // Filter out internal keys (_fieldTypes, _categoryId, _variants) for a cleaner view,
                // but keep them in the actual payload
                var userVisibleData = exportDataNonNull
                    .Where(kv => !kv.Key.StartsWith("_"))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(userVisibleData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                // Truncate to 3000 chars to avoid flooding the log
                if (jsonPayload.Length > 3000)
                    jsonPayload = jsonPayload[..2997] + "...";
                await Log("info", $"Payload to send:\n{jsonPayload}");
            }
            catch { /* serialization failure shouldn't block export */ }

            await Log("info", "Sending to the marketplace");

            // Export via the adapter
            // "pending:*" placeholders are not real marketplace IDs - treat as null (new product)
            var realExternalId = existingLink?.externalProductId;
            if (realExternalId != null && realExternalId.StartsWith("pending:"))
                realExternalId = null;

            var result = await adapter.ExportProductAsync(
                connection,
                exportDataNonNull,
                realExternalId,
                ct);

            if (!result.Success)
            {
                await Log("error", $"Marketplace returned an error: {result.ErrorMessage ?? "unknown"}");
                var detailsMessage = FormatExportDetails(result.Details);
                if (detailsMessage != null)
                    await Log("error", detailsMessage);

                // Update the link status on error
                if (existingLink != null)
                {
                    existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                    {
                        status = ProductMarketplaceStatus.SyncError,
                        lastErrorMessage = result.ErrorMessage,
                        lastSyncAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }

                return ProductExportResult.Failure(result.ErrorMessage ?? "Export error");
            }

            await Log("success", $"Marketplace responded: ExternalID = {result.ExternalProductId}");
            var successDetailsMessage = FormatExportDetails(result.Details);
            if (successDetailsMessage != null)
                await Log("info", successDetailsMessage);

            // Validate ExternalProductId - the plugin may return Success=true but with an invalid ID
            if (string.IsNullOrWhiteSpace(result.ExternalProductId) ||
                result.ExternalProductId == "0")
            {
                await Log("warning", $"ExternalProductId is invalid: '{result.ExternalProductId}'");
                _logger.LogWarning(
                    "Plugin returned Success=true but ExternalProductId is invalid: '{ExternalProductId}' for product {ProductId}",
                    result.ExternalProductId, productId);

                if (existingLink != null)
                {
                    existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                    {
                        status = ProductMarketplaceStatus.SyncError,
                        lastErrorMessage = $"Marketplace did not return a product ID (received: '{result.ExternalProductId}')",
                        lastSyncAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }

                return ProductExportResult.Failure(
                    $"Marketplace did not return a product ID. The product may still be processing - try again later.");
            }

            var isNewProduct = existingLink == null;

            // Create or update the link
            if (existingLink == null)
            {
                // Check whether a link already exists for this externalProductId + connectionId
                // (unique index IX_productMarketplaceLinks_externalProductId_connectionId)
                existingLink = await _context.productMarketplaceLinks
                    .FirstOrDefaultAsync(l =>
                        l.externalProductId == result.ExternalProductId &&
                        l.connectionId == connectionId, ct);

                if (existingLink != null)
                {
                    _logger.LogWarning(
                        "Found an existing link (Id={LinkId}) for externalProductId={ExtId} + connectionId={ConnId}, " +
                        "bound to productId={OldProductId}. Reassigning to productId={NewProductId}",
                        existingLink.Id, result.ExternalProductId, connectionId,
                        existingLink.productId, productId);
                    existingLink.productId = productId;
                    isNewProduct = false;
                }
            }

            if (existingLink == null)
            {
                existingLink = new ProductMarketplaceLink();
                existingLink.AddFill(new ProductMarketplaceLink.AddForm
                {
                    productId = productId,
                    connectionId = connectionId,
                    externalProductId = result.ExternalProductId!,
                    status = ProductMarketplaceStatus.Synced,
                    categoryMappingId = categoryMapping.Id
                });
                _context.productMarketplaceLinks.Add(existingLink);
            }
            else
            {
                existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                {
                    externalProductId = result.ExternalProductId,
                    status = ProductMarketplaceStatus.Synced,
                    lastErrorMessage = null,
                    lastSyncAt = DateTime.UtcNow,
                    categoryMappingId = categoryMapping.Id
                });
            }

            await _context.SaveChangesAsync(ct);

            // Endpoint grouping: join/create the marketplace group for the evaluated value
            if (endpointGroupValue != null)
            {
                await SyncEndpointGroupsByValueAsync(adapter, connection, connectionId,
                    new Dictionary<int, string> { [productId] = endpointGroupValue },
                    new List<BulkProductExportItemResult> { new(productId, true) },
                    ct);
            }

            // Auto-export price after successful product export
            if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
            {
                var priceInfo = await _mappingService.BuildPriceInfoFromGraphAsync(
                    product,
                    categoryMapping,
                    existingLink.externalProductId,
                    existingLink.externalSku ?? product.sku,
                    product.storageQuantity,
                    ct)
                    ?? (product.price.HasValue
                        ? new PriceInfo(existingLink.externalProductId, product.price, Stock: product.storageQuantity)
                        : null);

                if (priceInfo != null)
                {
                    await Log("info", $"Automatic price export: {priceInfo.Price}");
                    try
                    {
                        await adapter.ExportPricesAsync(connection, new List<PriceInfo> { priceInfo }, ct);
                        await Log("info", "Price exported");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Auto price export failed for product {ProductId}", productId);
                        await Log("warning", $"Failed to export price: {ex.Message}");
                    }
                }
            }

            // Auto-export stock after successful product export
            if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks))
            {
                await Log("info", $"Automatic stock export: {product.storageQuantity ?? 0}");
                try
                {
                    var stockInfo = new StockInfo(
                        existingLink.externalProductId,
                        product.storageQuantity ?? 0
                    );
                    await adapter.ExportStocksAsync(connection, new List<StockInfo> { stockInfo }, ct);
                    await Log("info", "Stock exported");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Auto stock export failed for product {ProductId}", productId);
                    await Log("warning", $"Failed to export stock: {ex.Message}");
                }
            }

            await Log("success", isNewProduct ? "Product created on the marketplace" : "Product updated on the marketplace");

            return ProductExportResult.Success(
                existingLink.Id,
                result.ExternalProductId!,
                isNewProduct,
                isNewProduct ? "Product created on the marketplace" : "Product updated on the marketplace"
            );
        }
        catch (Exception ex)
        {
            await Log("error", $"Export error: {ex.Message}");
            return ProductExportResult.Failure($"Export error: {ex.Message}");
        }
    }

    /// <summary>
    /// Export multiple products to a marketplace in a single bulk operation.
    /// Uses batched DB queries and a single adapter call for efficiency.
    /// </summary>
    public async Task<BulkProductExportResult> ExportProductsBulkAsync(
        List<int> productIds,
        int connectionId,
        CancellationToken ct = default)
    {
        var itemResults = new List<BulkProductExportItemResult>();

        // Load connection
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return new BulkProductExportResult(0, 0, productIds.Count,
                productIds.Select(id => new BulkProductExportItemResult(id, false, ErrorMessage: "Connection not found")).ToList());

        if (!connection.isActive)
            return new BulkProductExportResult(0, 0, productIds.Count,
                productIds.Select(id => new BulkProductExportItemResult(id, false, ErrorMessage: "Connection is inactive")).ToList());

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return new BulkProductExportResult(0, 0, productIds.Count,
                productIds.Select(id => new BulkProductExportItemResult(id, false, ErrorMessage: $"Adapter '{connection.adapterKey}' not found")).ToList());

        // One query: load all products with includes
        var products = await _context.products
            .Include(p => p.category)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
            .Include(p => p.images)
                .ThenInclude(i => i.file)
            .Include(p => p.groupMemberships)
                .ThenInclude(gm => gm.group)
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(ct);

        var productDict = products.ToDictionary(p => p.Id);

        // One query: load all existing links
        var existingLinks = await _context.productMarketplaceLinks
            .Where(l => productIds.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByProductId = existingLinks.ToDictionary(l => l.productId, l => l);

        // One query: load all category mappings
        var categoryIds = products
            .Where(p => p.categoryId.HasValue)
            .Select(p => p.categoryId!.Value)
            .Distinct()
            .ToList();

        var categoryMappings = await _context.categoryMappings
            .Where(cm => cm.connectionId == connectionId && categoryIds.Contains(cm.localCategoryId))
            .ToListAsync(ct);

        // Group by category (there can be several mappings per category)
        var mappingsByCategoryId = categoryMappings
            .GroupBy(cm => cm.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var mappingsById = categoryMappings.ToDictionary(cm => cm.Id, cm => cm);

        // One query: load all group links (for field-based grouping)
        var fieldGroupingConfig = adapter.GetFieldBasedGroupingConfig();
        Dictionary<int, string?> groupIdByProductId = new();

        if (fieldGroupingConfig != null && adapter.Capabilities.HasFlag(MarketplaceCapabilities.FieldBasedGrouping))
        {
            var groupLinks = await _context.productGroupMarketplaceLinks
                .Include(gl => gl.productGroup)
                    .ThenInclude(g => g.members)
                .Where(gl => gl.connectionId == connectionId)
                .ToListAsync(ct);

            foreach (var gl in groupLinks)
            {
                foreach (var member in gl.productGroup.members)
                {
                    if (productIds.Contains(member.productId))
                        groupIdByProductId[member.productId] = gl.externalGroupId;
                }
            }
        }

        // One query: load all product variants with marketplace links for this connection
        var allVariants = await _context.productVariants
            .Where(v => productIds.Contains(v.productId))
            .Include(v => v.marketplaceLinks.Where(l => l.connectionId == connectionId))
            .ToListAsync(ct);
        var variantsByProductId = allVariants
            .GroupBy(v => v.productId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Build BulkExportItem list
        var bulkItems = new List<BulkExportItem>();
        var categoryMappingIdByProductId = new Dictionary<int, int>();
        var failedProductIds = new HashSet<int>();
        // Endpoint-based grouping: the synthetic field's evaluated value drives which
        // marketplace card each product joins (products with equal values merge)
        var endpointGrouping = adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping);
        var groupValueByProductId = new Dictionary<int, string>();

        foreach (var productId in productIds)
        {
            if (!productDict.TryGetValue(productId, out var product))
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: $"Product with ID {productId} not found"));
                failedProductIds.Add(productId);
                continue;
            }

            if (!product.categoryId.HasValue)
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: "Product is not assigned to a category"));
                failedProductIds.Add(productId);
                continue;
            }

            // Resolve category mapping: prefer product's selected mapping, fallback to first for category
            CategoryMapping? categoryMapping = null;

            if (linkByProductId.TryGetValue(productId, out var existingLink)
                && existingLink.categoryMappingId != null
                && mappingsById.TryGetValue(existingLink.categoryMappingId.Value, out var overrideMapping))
            {
                categoryMapping = overrideMapping;
            }
            else if (mappingsByCategoryId.TryGetValue(product.categoryId.Value, out var catMappings))
            {
                categoryMapping = catMappings.First();
            }

            if (categoryMapping == null)
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false,
                    ErrorMessage: $"No category mapping found for '{product.category?.name}'"));
                failedProductIds.Add(productId);
                continue;
            }

            try
            {
                var exportData = await _mappingService.BuildExportDataAsync(product, categoryMapping, ct);
                exportData["_categoryId"] = categoryMapping.externalCategoryId;

                // Add group_id for field-based grouping
                if (fieldGroupingConfig != null && !exportData.ContainsKey(fieldGroupingConfig.GroupFieldKey))
                {
                    if (groupIdByProductId.TryGetValue(productId, out var groupId) && groupId != null)
                        exportData[fieldGroupingConfig.GroupFieldKey] = groupId;
                }

                // Endpoint grouping: pull the synthetic field's value out - it's consumed by the
                // post-export group sync, the adapter must not receive it as a product attribute
                if (endpointGrouping && exportData.TryGetValue(SyntheticGroupFieldKey, out var rawGroupValue))
                {
                    exportData.Remove(SyntheticGroupFieldKey);
                    var groupValue = rawGroupValue?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(groupValue))
                        groupValueByProductId[productId] = groupValue;
                }

                // Inject variants if present
                if (variantsByProductId.TryGetValue(productId, out var prodVariants) && prodVariants.Count > 0)
                {
                    exportData["_variants"] = prodVariants.Select(v =>
                    {
                        var link = v.marketplaceLinks.FirstOrDefault();
                        return new Dictionary<string, object?>
                        {
                            ["variantId"] = link?.externalVariantId,
                            ["techSize"] = v.size,
                            ["marketplaceSize"] = link?.marketplaceSize,
                            ["barcode"] = v.barcode,
                            ["price"] = v.price,
                            ["quantity"] = v.quantity
                        };
                    }).ToList();
                }

                // Validate required fields (schema = graph adapter fields)
                var requiredMappings = MappingService.SchemaFields(categoryMapping.graphJson)
                    .Where(f => f.isRequired && !string.IsNullOrEmpty(f.fieldKey))
                    .ToList();

                var missingFields = new List<string>();
                var missingDetails = new List<string>();
                foreach (var mapping in requiredMappings)
                {
                    if (!exportData.TryGetValue(mapping.fieldKey!, out var value) ||
                        value == null ||
                        (value is string strValue && string.IsNullOrWhiteSpace(strValue)))
                    {
                        var fieldName = mapping.label ?? mapping.fieldKey!;
                        missingFields.Add(fieldName);
                        missingDetails.Add($"{fieldName} [key={mapping.fieldKey}]");
                    }
                }

                if (missingFields.Count > 0)
                {
                    var mappingHint =
                        $"mapping #{categoryMapping.Id}" +
                        (string.IsNullOrWhiteSpace(categoryMapping.name)
                            ? ""
                            : $" «{categoryMapping.name}»");
                    itemResults.Add(new BulkProductExportItemResult(productId, false,
                        ErrorMessage:
                            $"Required fields are not filled ({mappingHint}): {string.Join(", ", missingDetails)}. " +
                            "Blocked before adapter HTTP call. Disable Required on unused mapping fields or recreate WooCommerce mapping."));
                    failedProductIds.Add(productId);
                    continue;
                }

                var exportDataNonNull = exportData
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!);

                var existingExternalId = linkByProductId.TryGetValue(productId, out var link)
                    ? link.externalProductId
                    : null;
                // "pending:*" placeholders are not real marketplace IDs - treat as null (new product)
                if (existingExternalId != null && existingExternalId.StartsWith("pending:"))
                    existingExternalId = null;

                bulkItems.Add(new BulkExportItem(productId, exportDataNonNull, existingExternalId));
                categoryMappingIdByProductId[productId] = categoryMapping.Id;
            }
            catch (Exception ex)
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: $"Preparation error: {ex.Message}"));
                failedProductIds.Add(productId);
            }
        }

        // Call adapter bulk export
        if (bulkItems.Count > 0)
        {
            try
            {
                var bulkResult = await adapter.ExportProductsBulkAsync(connection, bulkItems, ct);

                // Pre-load existing links by externalProductId to avoid N+1 queries
                Dictionary<string, ProductMarketplaceLink> dupLinksByExternalId = new();
                if (bulkResult.Items != null)
                {
                    var allExternalIds = bulkResult.Items
                        .Where(i => i.Success && !string.IsNullOrEmpty(i.ExternalProductId))
                        .Select(i => i.ExternalProductId!)
                        .Distinct()
                        .ToList();
                    if (allExternalIds.Count > 0)
                    {
                        dupLinksByExternalId = await _context.productMarketplaceLinks
                            .Where(l => l.connectionId == connectionId && allExternalIds.Contains(l.externalProductId))
                            .ToDictionaryAsync(l => l.externalProductId, ct);
                    }
                }

                if (bulkResult.Items != null)
                {
                    foreach (var exportItemResult in bulkResult.Items)
                    {
                        if (!exportItemResult.Success)
                        {
                            itemResults.Add(new BulkProductExportItemResult(
                                exportItemResult.LocalProductId, false,
                                ErrorMessage: exportItemResult.ErrorMessage ?? "Export error",
                                Details: exportItemResult.Details));
                            continue;
                        }

                        // Validate ExternalProductId
                        if (string.IsNullOrWhiteSpace(exportItemResult.ExternalProductId) ||
                            exportItemResult.ExternalProductId == "0")
                        {
                            itemResults.Add(new BulkProductExportItemResult(
                                exportItemResult.LocalProductId, false,
                                ErrorMessage: "Marketplace did not return a product ID"));
                            continue;
                        }

                        // Create/update ProductMarketplaceLink
                        var pid = exportItemResult.LocalProductId;
                        var isNew = !linkByProductId.ContainsKey(pid);
                        int? usedCategoryMappingId = categoryMappingIdByProductId.TryGetValue(pid, out var categoryMappingId)
                            ? categoryMappingId
                            : null;

                        if (linkByProductId.TryGetValue(pid, out var existingLink))
                        {
                            existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                            {
                                externalProductId = exportItemResult.ExternalProductId,
                                status = ProductMarketplaceStatus.Synced,
                                lastErrorMessage = null,
                                lastSyncAt = DateTime.UtcNow,
                                categoryMappingId = usedCategoryMappingId
                            });
                        }
                        else
                        {
                            // Check for existing link by externalProductId (pre-loaded)
                            dupLinksByExternalId.TryGetValue(exportItemResult.ExternalProductId!, out var dupLink);

                            if (dupLink != null)
                            {
                                dupLink.productId = pid;
                                dupLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                                {
                                    externalProductId = exportItemResult.ExternalProductId,
                                    status = ProductMarketplaceStatus.Synced,
                                    lastErrorMessage = null,
                                    lastSyncAt = DateTime.UtcNow,
                                    categoryMappingId = usedCategoryMappingId
                                });
                                linkByProductId[pid] = dupLink;
                                isNew = false;
                            }
                            else
                            {
                                var newLink = new ProductMarketplaceLink();
                                newLink.AddFill(new ProductMarketplaceLink.AddForm
                                {
                                    productId = pid,
                                    connectionId = connectionId,
                                    externalProductId = exportItemResult.ExternalProductId!,
                                    status = ProductMarketplaceStatus.Synced,
                                    categoryMappingId = usedCategoryMappingId
                                });
                                _context.productMarketplaceLinks.Add(newLink);
                                linkByProductId[pid] = newLink;
                            }
                        }

                        itemResults.Add(new BulkProductExportItemResult(
                            pid, true,
                            ExternalProductId: exportItemResult.ExternalProductId,
                            IsNewProduct: isNew,
                            Details: exportItemResult.Details));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk export failed for connectionId={ConnectionId}", connectionId);
                foreach (var item in bulkItems)
                {
                    if (!failedProductIds.Contains(item.LocalProductId))
                        itemResults.Add(new BulkProductExportItemResult(item.LocalProductId, false, ErrorMessage: $"Export error: {ex.Message}"));
                }
            }

            // One SaveChangesAsync at the end
            await _context.SaveChangesAsync(ct);
        }

        // Post-export: sync groups for EndpointBasedGrouping (e.g., WB merge groups)
        // by the EVALUATED grouping value - not by local group membership
        if (endpointGrouping && groupValueByProductId.Count > 0)
        {
            await SyncEndpointGroupsByValueAsync(adapter, connection, connectionId, groupValueByProductId, itemResults, ct);
        }

        var successCount = itemResults.Count(r => r.IsSuccess);
        var failedCount = itemResults.Count(r => !r.IsSuccess);
        return new BulkProductExportResult(successCount, failedCount, productIds.Count, itemResults);
    }

    /// <summary>
    /// Synthetic mapping field key for endpoint-based grouping adapters (WB-style).
    /// Its evaluated value is stripped from export data and drives post-export group sync.
    /// </summary>
    public const string SyntheticGroupFieldKey = "__group__";

    /// <summary>
    /// The mapping field that controls card grouping for this adapter:
    /// field-based (Ozon) → the adapter's real field; endpoint-based (WB) → synthetic key; else null.
    /// </summary>
    public static string? ResolveGroupingFieldKey(IMarketplaceAdapter adapter)
        => adapter.GetFieldBasedGroupingConfig()?.GroupFieldKey
           ?? (adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping)
               ? SyntheticGroupFieldKey
               : null);

    /// <summary>
    /// After export, sync endpoint-based groups (e.g., WB merge groups) by the EVALUATED grouping
    /// value: products whose mapping produced the same value join one marketplace card. The mapping
    /// acts as a translation layer - local groups are not consulted directly (their names flow in
    /// via the default groupName binding, optionally transformed by modifiers).
    /// </summary>
    private async Task SyncEndpointGroupsByValueAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        int connectionId,
        Dictionary<int, string> groupValueByProductId,
        List<BulkProductExportItemResult> itemResults,
        CancellationToken ct)
    {
        try
        {
            var successProductIds = itemResults
                .Where(r => r.IsSuccess)
                .Select(r => r.ProductId)
                .ToHashSet();

            var byValue = groupValueByProductId
                .Where(kv => successProductIds.Contains(kv.Key))
                .GroupBy(kv => kv.Value)
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToList());

            if (byValue.Count == 0)
            {
                _logger.LogDebug("[GroupSync] No exported products with a grouping value, skipping");
                return;
            }

            var allProductIds = byValue.Values.SelectMany(x => x).Distinct().ToList();
            var productLinks = (await _context.productMarketplaceLinks
                    .Where(l => l.connectionId == connectionId && allProductIds.Contains(l.productId))
                    .ToListAsync(ct))
                .GroupBy(l => l.productId)
                .ToDictionary(g => g.Key, g => g.First());

            var values = byValue.Keys.ToList();
            var linkByValue = (await _context.groupValueMarketplaceLinks
                    .Where(l => l.connectionId == connectionId && values.Contains(l.groupValue))
                    .ToListAsync(ct))
                .GroupBy(l => l.groupValue)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var (groupValue, productIds) in byValue)
            {
                var externalIds = productIds
                    .Where(id => productLinks.ContainsKey(id))
                    .Select(id => productLinks[id].externalProductId)
                    .Where(id => !string.IsNullOrEmpty(id) && !id.StartsWith("pending:"))
                    .ToList();

                if (externalIds.Count == 0) continue;

                linkByValue.TryGetValue(groupValue, out var link);

                if (link != null && !string.IsNullOrEmpty(link.externalGroupId))
                {
                    // Marketplace group already exists for this value - add the exported products
                    _logger.LogInformation(
                        "[GroupSync] Adding {Count} products to group '{Value}' (externalGroupId={ExternalGroupId})",
                        externalIds.Count, groupValue, link.externalGroupId);
                    try
                    {
                        var addResult = await adapter.AddProductsToGroupAsync(
                            connection, link.externalGroupId, externalIds, ct);

                        if (!addResult.Success)
                            _logger.LogWarning(
                                "[GroupSync] AddProductsToGroupAsync failed for value '{Value}': {Error} (marketplace may limit group size, e.g. WB max 30 cards)",
                                groupValue, addResult.ErrorMessage);

                        link.status = addResult.Success ? ProductMarketplaceStatus.Synced : ProductMarketplaceStatus.SyncError;
                        link.lastErrorMessage = addResult.ErrorMessage;
                        link.lastSyncAt = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[GroupSync] Exception adding products to group '{Value}' on connection {ConnectionId}",
                            groupValue, connectionId);
                    }
                }
                else
                {
                    if (externalIds.Count < 2)
                    {
                        _logger.LogDebug(
                            "[GroupSync] Value '{Value}' has only {Count} exported product(s) and no existing group, skipping (need >= 2)",
                            groupValue, externalIds.Count);
                        continue;
                    }

                    _logger.LogInformation(
                        "[GroupSync] Creating group for value '{Value}' with {Count} members: [{Members}]",
                        groupValue, externalIds.Count, string.Join(", ", externalIds));
                    try
                    {
                        var createResult = await adapter.CreateGroupAsync(connection, externalIds, groupValue, ct);

                        if (createResult.Success && !string.IsNullOrEmpty(createResult.ExternalGroupId))
                        {
                            if (link == null)
                            {
                                link = new GroupValueMarketplaceLink
                                {
                                    connectionId = connectionId,
                                    groupValue = groupValue,
                                    externalGroupId = createResult.ExternalGroupId,
                                    status = ProductMarketplaceStatus.Synced,
                                    lastSyncAt = DateTime.UtcNow,
                                };
                                _context.groupValueMarketplaceLinks.Add(link);
                            }
                            else
                            {
                                link.externalGroupId = createResult.ExternalGroupId;
                                link.status = ProductMarketplaceStatus.Synced;
                                link.lastSyncAt = DateTime.UtcNow;
                            }

                            _logger.LogInformation(
                                "[GroupSync] Created group for value '{Value}' on connection {ConnectionId}, externalGroupId={ExternalGroupId}",
                                groupValue, connectionId, createResult.ExternalGroupId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[GroupSync] CreateGroupAsync failed for value '{Value}' with {MemberCount} members: {Error}",
                                groupValue, externalIds.Count, createResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[GroupSync] Exception creating group for value '{Value}' on connection {ConnectionId}",
                            groupValue, connectionId);
                    }
                }
            }

            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[GroupSync] Post-export group sync failed for connection {ConnectionId}. Products were exported successfully.",
                connectionId);
        }
    }

    /// <summary>
    /// Import a product from a marketplace and create a local product with a link.
    /// </summary>
    public async Task<ProductImportResult> ImportProductAsync(
        int connectionId,
        string externalProductId,
        int? targetCategoryId = null,
        int? importByUserId = null,
        CancellationToken ct = default,
        string? batchId = null,
        int? batchStoreId = null,
        string? imageBatchId = null)
    {
        // Helper for operation-level logging (visible in UI)
        async Task Log(string level, string message)
        {
            if (batchId != null && batchStoreId != null)
                await _operationLogger.LogAsync(batchId, batchStoreId.Value, level, message);
        }

        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return ProductImportResult.Failure("Connection not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return ProductImportResult.Failure($"Adapter '{connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            return ProductImportResult.Failure("Adapter does not support product import");

        try
        {
            // Get the product from the marketplace
            await Log("info", $"Loading product {externalProductId} from the marketplace...");
            var externalProduct = await adapter.ImportProductAsync(connection, externalProductId, ct);
            if (externalProduct == null)
            {
                await Log("error", $"Product {externalProductId} not found on the marketplace");
                return ProductImportResult.Failure($"Product with ID '{externalProductId}' not found on the marketplace");
            }

            var productLabel = externalProduct.Name ?? externalProductId;
            await Log("info", $"Received: {productLabel} | SKU: {externalProduct.Sku ?? "-"} | Price: {externalProduct.Price} | Stock: {externalProduct.Quantity} | Attributes: {externalProduct.Attributes?.Count ?? 0} | Photos: {externalProduct.ImageUrls?.Count ?? 0} | Variants: {externalProduct.Variants?.Count ?? 0}");

            _logger.LogInformation(
                "Importing product {ExternalId}: Name={Name}, Description={Description}, Price={Price}, Quantity={Quantity}, Attributes={AttrCount}",
                externalProductId,
                externalProduct.Name,
                externalProduct.Description?.Substring(0, Math.Min(50, externalProduct.Description?.Length ?? 0)) ?? "null",
                externalProduct.Price,
                externalProduct.Quantity,
                externalProduct.Attributes?.Count ?? 0);

            // Check whether a link already exists
            var existingLink = await _context.productMarketplaceLinks
                .Include(l => l.product)
                .FirstOrDefaultAsync(l =>
                    l.connectionId == connectionId &&
                    l.externalProductId == externalProductId, ct);

            if (existingLink != null)
            {
                await Log("warning", $"{productLabel} - already imported (ID: {existingLink.productId})");
                return ProductImportResult.AlreadyExists(
                    existingLink.productId,
                    existingLink.product?.name ?? "Unknown"
                );
            }

            // Create a local product
            await Log("info", $"Creating product in the database: {productLabel}...");
            var product = new Product();
            product.AddFill(new Product.AddForm(
                new Product.UserAddForm
                {
                    sku = externalProduct.Sku ?? $"MP-{externalProductId}",
                    name = externalProduct.Name ?? $"Product {externalProductId}",
                    description = Delegat.Marketplace.SDK.DescriptionFormatting.FromMarketplaceHtml(externalProduct.Description) ?? "",
                    price = externalProduct.Price,
                    storageQuantity = externalProduct.Quantity,
                    categoryId = targetCategoryId
                },
                connection.storeId,
                targetCategoryId
            ));

            _context.products.Add(product);
            await _context.SaveChangesAsync(ct);
            await Log("info", $"Product created (ID: {product.Id})");

            // Import attributes based on the category mapping
            if (targetCategoryId.HasValue && externalProduct.Attributes?.Count > 0)
            {
                await Log("info", $"Importing {externalProduct.Attributes.Count} attributes...");
                await ImportProductAttributesAsync(
                    product.Id,
                    connectionId,
                    targetCategoryId.Value,
                    externalProduct.Attributes,
                    ct);
                await Log("info", $"Attributes imported");
            }

            // Import images
            if (externalProduct.ImageUrls?.Any() == true && importByUserId.HasValue)
            {
                await Log("info", $"Queuing {externalProduct.ImageUrls.Count} photos for download...");
                await ImportProductImagesAsync(
                    product.Id,
                    connection.storeId,
                    importByUserId.Value,
                    externalProduct.ImageUrls,
                    ct,
                    imageBatchId: imageBatchId);
                await Log("info", $"Photos queued");
            }

            // Create the link
            var link = new ProductMarketplaceLink();
            link.AddFill(new ProductMarketplaceLink.AddForm
            {
                productId = product.Id,
                connectionId = connectionId,
                externalProductId = externalProductId,
                externalSku = externalProduct.Sku,
                status = ProductMarketplaceStatus.Synced
            });
            link.externalPrice = externalProduct.Price;
            link.externalStock = externalProduct.Quantity;
            link.lastSyncAt = DateTime.UtcNow;

            _context.productMarketplaceLinks.Add(link);
            await _context.SaveChangesAsync(ct);

            // Sync variants (sizes) if present
            if (externalProduct.Variants != null && externalProduct.Variants.Count > 0)
            {
                await Log("info", $"Syncing {externalProduct.Variants.Count} variants (sizes)...");
                await SyncVariantsAsync(product.Id, connectionId, externalProduct.Variants, ct);
                await Log("info", $"Variants synced");
            }

            // Add the product to a group if GroupId is provided
            if (!string.IsNullOrEmpty(externalProduct.GroupId))
            {
                await Log("info", $"Adding to group: {externalProduct.GroupName ?? externalProduct.GroupId}...");
                await AddProductToGroupAsync(product.Id, connectionId, connection.storeId, externalProduct.GroupId, externalProduct.GroupName, ct);
                await Log("info", $"Product added to the group");
            }

            return ProductImportResult.Success(
                product.Id,
                link.Id,
                product.name,
                "Product imported successfully"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product import error {ExternalId}", externalProductId);
            await Log("error", $"Import error {externalProductId}: {ex.Message}");
            return ProductImportResult.Failure($"Import error: {ex.Message}");
        }
    }

    /// <summary>
    /// Import product attributes based on category field mappings.
    /// </summary>
    private async Task ImportProductAttributesAsync(
        int productId,
        int connectionId,
        int categoryId,
        Dictionary<string, object> externalAttributes,
        CancellationToken ct)
    {
        // Check whether the product has a selected mapping
        var existingLink = await _context.productMarketplaceLinks
            .FirstOrDefaultAsync(l => l.productId == productId && l.connectionId == connectionId, ct);

        CategoryMapping? categoryMapping;
        if (existingLink?.categoryMappingId != null)
        {
            categoryMapping = await _context.categoryMappings
                .FirstOrDefaultAsync(cm => cm.Id == existingLink.categoryMappingId.Value, ct);
        }
        else
        {
            categoryMapping = await _context.categoryMappings
                .FirstOrDefaultAsync(cm =>
                    cm.connectionId == connectionId &&
                    cm.localCategoryId == categoryId, ct);
        }

        if (categoryMapping == null)
            return;

        // Import is graph-native: project the node graph to per-field import descriptors.
        var enabledMappings = MappingService.ProjectGraphForImport(categoryMapping.graphJson);

        // Pre-load valid CategoryAttribute IDs to avoid FK constraint failures
        var categoryAttributeIdsList = await _context.categoryAttributes
            .Where(ca => ca.categoryId == categoryId)
            .Select(ca => ca.Id)
            .ToListAsync(ct);
        var categoryAttributeIds = new HashSet<int>(categoryAttributeIdsList);

        // Pre-load valid StoreAttribute IDs
        var storeAttributeIdsList = await _context.storeAttributes
            .Select(sa => sa.Id)
            .ToListAsync(ct);
        var storeAttributeIds = new HashSet<int>(storeAttributeIdsList);

        var importedCount = 0;

        foreach (var fieldMapping in enabledMappings)
        {
            // Check whether the external attributes contain a value
            if (!externalAttributes.TryGetValue(fieldMapping.externalFieldKey, out var externalValue))
            {
                // Log: no match for this external field key
                continue;
            }

            var stringValue = ConvertAttributeValueToString(externalValue);
            if (string.IsNullOrEmpty(stringValue))
                continue;

            // Reverse-apply modifiers to convert marketplace values back to local format
            stringValue = ModifierPipeline.ApplyModifiersReverse(stringValue, fieldMapping.modifiersJson)?.ToString()
                          ?? stringValue;

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                    if (!categoryAttributeIds.Contains(fieldMapping.sourceAttributeId.Value))
                        continue;

                    var productAttrValue = new ProductAttributeValue();
                    productAttrValue.AddFill(new ProductAttributeValue.AddForm
                    {
                        productId = productId,
                        attributeId = fieldMapping.sourceAttributeId.Value,
                        value = stringValue
                    });
                    _context.productAttributeValues.Add(productAttrValue);
                    importedCount++;
                    break;

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                    if (!storeAttributeIds.Contains(fieldMapping.sourceAttributeId.Value))
                        continue;

                    var storeAttrValue = new StoreProductAttributeValue();
                    storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                    {
                        productId = productId,
                        storeAttributeId = fieldMapping.sourceAttributeId.Value,
                        value = stringValue
                    });
                    _context.storeProductAttributeValues.Add(storeAttrValue);
                    importedCount++;
                    break;
            }
        }

        if (importedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Update or create product attribute values from external marketplace data.
    /// Unlike ImportProductAttributesAsync (which only creates), this method
    /// also updates existing attribute values when they differ.
    /// Returns detailed list of changes made.
    /// </summary>
    public async Task<List<AttributeChangeDetail>> UpdateProductAttributesFromExternalAsync(
        int productId,
        int connectionId,
        int categoryId,
        Dictionary<string, object> externalAttributes,
        CancellationToken ct,
        HashSet<string>? excludeFieldNames = null)
    {
        var changes = new List<AttributeChangeDetail>();

        var categoryMapping = await _context.categoryMappings
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
        {
            _logger.LogWarning(
                "No category mapping for connectionId={ConnectionId}, categoryId={CategoryId} - attributes will not be updated",
                connectionId, categoryId);
            return changes;
        }

        // Import is graph-native: project the node graph to per-field import descriptors.
        var enabledMappings = MappingService.ProjectGraphForImport(categoryMapping.graphJson);

        if (enabledMappings.Count == 0)
        {
            _logger.LogWarning(
                "No enabled field mappings for categoryMappingId={MappingId} - attributes will not be updated",
                categoryMapping.Id);
        }

        // Pre-load existing attribute values for this product
        var existingProductAttrs = await _context.productAttributeValues
            .Where(pav => pav.productId == productId)
            .ToListAsync(ct);

        var existingStoreAttrs = await _context.storeProductAttributeValues
            .Where(sav => sav.productId == productId)
            .ToListAsync(ct);

        // Pre-load valid IDs
        var categoryAttributeIds = (await _context.categoryAttributes
            .Where(ca => ca.categoryId == categoryId)
            .Select(ca => ca.Id)
            .ToListAsync(ct)).ToHashSet();

        var storeAttributeIds = (await _context.storeAttributes
            .Select(sa => sa.Id)
            .ToListAsync(ct)).ToHashSet();

        _logger.LogDebug(
            "Updating attributes: productId={ProductId}, enabledMappings={Count}, externalKeys=[{Keys}]",
            productId, enabledMappings.Count,
            string.Join(", ", externalAttributes.Keys.Take(20)));

        foreach (var fieldMapping in enabledMappings)
        {
            if (!externalAttributes.TryGetValue(fieldMapping.externalFieldKey, out var externalValue))
            {
                _logger.LogDebug(
                    "  Mapping '{FieldName}' (key={Key}): key not found in external attributes, sourceType={SourceType}",
                    fieldMapping.externalFieldName, fieldMapping.externalFieldKey, fieldMapping.sourceType);
                continue;
            }

            var stringValue = ConvertAttributeValueToString(externalValue);
            if (string.IsNullOrEmpty(stringValue))
                continue;

            // Reverse-apply modifiers to convert marketplace values back to local format
            stringValue = ModifierPipeline.ApplyModifiersReverse(stringValue, fieldMapping.modifiersJson)?.ToString()
                          ?? stringValue;

            var fieldName = fieldMapping.externalFieldName ?? fieldMapping.externalFieldKey;

            if (excludeFieldNames?.Contains(fieldName) == true)
                continue;

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!categoryAttributeIds.Contains(fieldMapping.sourceAttributeId.Value))
                    {
                        _logger.LogWarning(
                            "  Mapping '{FieldName}': sourceAttributeId={AttrId} not found in attributes of category {CategoryId}",
                            fieldName, fieldMapping.sourceAttributeId.Value, categoryId);
                        continue;
                    }

                    var existing = existingProductAttrs.FirstOrDefault(
                        a => a.attributeId == fieldMapping.sourceAttributeId.Value);

                    if (existing != null)
                    {
                        if (existing.value != stringValue)
                        {
                            changes.Add(new AttributeChangeDetail(fieldName, existing.value, stringValue));
                            existing.value = stringValue;
                        }
                    }
                    else
                    {
                        var productAttrValue = new ProductAttributeValue();
                        productAttrValue.AddFill(new ProductAttributeValue.AddForm
                        {
                            productId = productId,
                            attributeId = fieldMapping.sourceAttributeId.Value,
                            value = stringValue
                        });
                        _context.productAttributeValues.Add(productAttrValue);
                        changes.Add(new AttributeChangeDetail(fieldName, null, stringValue));
                    }
                    break;
                }

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!storeAttributeIds.Contains(fieldMapping.sourceAttributeId.Value))
                        continue;

                    var existing = existingStoreAttrs.FirstOrDefault(
                        a => a.storeAttributeId == fieldMapping.sourceAttributeId.Value);

                    if (existing != null)
                    {
                        if (existing.value != stringValue)
                        {
                            changes.Add(new AttributeChangeDetail(fieldName, existing.value, stringValue));
                            existing.value = stringValue;
                        }
                    }
                    else
                    {
                        var storeAttrValue = new StoreProductAttributeValue();
                        storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                        {
                            productId = productId,
                            storeAttributeId = fieldMapping.sourceAttributeId.Value,
                            value = stringValue
                        });
                        _context.storeProductAttributeValues.Add(storeAttrValue);
                        changes.Add(new AttributeChangeDetail(fieldName, null, stringValue));
                    }
                    break;
                }

                default:
                    _logger.LogDebug(
                        "  Mapping '{FieldName}' (key={Key}): sourceType={SourceType} - skipped during attribute import",
                        fieldName, fieldMapping.externalFieldKey, fieldMapping.sourceType);
                    break;
            }
        }

        if (changes.Count > 0)
            await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updating attributes for productId={ProductId}: {ChangedCount} changes out of {TotalMappings} mappings",
            productId, changes.Count, enabledMappings.Count);

        return changes;
    }

    /// <summary>
    /// Read-only: get detailed list of attribute changes (field name, old value, new value).
    /// </summary>
    public async Task<List<AttributeChangeDetail>> GetAttributeChangesDetailedAsync(
        int productId,
        int connectionId,
        int categoryId,
        Dictionary<string, object> externalAttributes,
        CancellationToken ct)
    {
        var result = new List<AttributeChangeDetail>();

        var categoryMapping = await _context.categoryMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
            return result;

        // Import is graph-native: project the node graph to per-field import descriptors.
        var enabledMappings = MappingService.ProjectGraphForImport(categoryMapping.graphJson);

        var existingProductAttrs = await _context.productAttributeValues
            .AsNoTracking()
            .Where(pav => pav.productId == productId)
            .ToListAsync(ct);

        var existingStoreAttrs = await _context.storeProductAttributeValues
            .AsNoTracking()
            .Where(sav => sav.productId == productId)
            .ToListAsync(ct);

        foreach (var fieldMapping in enabledMappings)
        {
            if (!externalAttributes.TryGetValue(fieldMapping.externalFieldKey, out var externalValue))
                continue;

            var stringValue = ConvertAttributeValueToString(externalValue);
            if (string.IsNullOrEmpty(stringValue))
                continue;

            // Reverse-apply modifiers to convert marketplace values back to local format
            stringValue = ModifierPipeline.ApplyModifiersReverse(stringValue, fieldMapping.modifiersJson)?.ToString()
                          ?? stringValue;

            var fieldName = fieldMapping.externalFieldName ?? fieldMapping.externalFieldKey;
            var hasIrreversible = ModifierPipeline.HasIrreversibleModifiers(fieldMapping.modifiersJson);

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    var existing = existingProductAttrs.FirstOrDefault(
                        a => a.attributeId == fieldMapping.sourceAttributeId.Value);
                    if (existing == null || existing.value != stringValue)
                        result.Add(new AttributeChangeDetail(fieldName, existing?.value, stringValue, hasIrreversible));
                    break;
                }

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    var existing = existingStoreAttrs.FirstOrDefault(
                        a => a.storeAttributeId == fieldMapping.sourceAttributeId.Value);
                    if (existing == null || existing.value != stringValue)
                        result.Add(new AttributeChangeDetail(fieldName, existing?.value, stringValue, hasIrreversible));
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Convert an attribute value to its string representation.
    /// </summary>
    private static string? ConvertAttributeValueToString(object value)
    {
        if (value == null)
            return null;

        // If it is a list, take the first value or join them with a comma
        if (value is System.Collections.IList list)
        {
            if (list.Count == 0)
                return null;
            if (list.Count == 1)
                return list[0]?.ToString();
            return string.Join(", ", list.Cast<object>().Select(v => v?.ToString()));
        }

        // JsonElement
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString(),
                System.Text.Json.JsonValueKind.Number => jsonElement.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.Array => string.Join(", ", jsonElement.EnumerateArray().Select(e => e.ToString())),
                _ => jsonElement.ToString()
            };
        }

        return value.ToString();
    }

    /// <summary>
    /// Update a product SKU on the marketplace.
    /// </summary>
    public async Task<ProductExportResult> UpdateSkuAsync(
        int linkId,
        string newSku,
        CancellationToken ct = default)
    {
        var link = await _context.productMarketplaceLinks
            .Include(l => l.connection)
            .FirstOrDefaultAsync(l => l.Id == linkId, ct);

        if (link == null)
            return ProductExportResult.Failure("Product link not found");

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            return ProductExportResult.Failure($"Adapter '{link.connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.UpdateSku))
            return ProductExportResult.Failure("Marketplace does not support SKU updates");

        try
        {
            var result = await adapter.UpdateSkuAsync(
                link.connection,
                link.externalProductId,
                newSku,
                link.externalSku,
                ct);

            if (result.Success)
            {
                link.UpdateFill(new ProductMarketplaceLink.UpdateForm
                {
                    externalSku = newSku,
                    lastSyncAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);

                return ProductExportResult.Success(link.Id, link.externalProductId, false, "SKU updated successfully");
            }

            return ProductExportResult.Failure(result.ErrorMessage ?? "SKU update error");
        }
        catch (NotSupportedException)
        {
            return ProductExportResult.Failure("Marketplace does not support SKU updates");
        }
        catch (Exception ex)
        {
            return ProductExportResult.Failure($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the synchronization status for products linked to a marketplace connection.
    /// </summary>
    public async Task<List<ProductSyncStatus>> GetProductSyncStatusAsync(
        int connectionId,
        List<int>? productIds = null,
        CancellationToken ct = default)
    {
        var query = _context.productMarketplaceLinks
            .Include(l => l.product)
            .Where(l => l.connectionId == connectionId);

        if (productIds != null && productIds.Any())
            query = query.Where(l => productIds.Contains(l.productId));

        var links = await query.ToListAsync(ct);

        return links.Select(l => new ProductSyncStatus
        {
            ProductId = l.productId,
            ProductName = l.product?.name,
            ProductSku = l.product?.sku,
            LinkId = l.Id,
            ExternalProductId = l.externalProductId,
            ExternalSku = l.externalSku,
            Status = l.status,
            LastSyncAt = l.lastSyncAt,
            LastErrorMessage = l.lastErrorMessage,
            LocalPrice = l.product?.price,
            ExternalPrice = l.externalPrice,
            LocalStock = l.product?.storageQuantity,
            ExternalStock = l.externalStock
        }).ToList();
    }

    /// <summary>
    /// Enqueue product images for background download by ImageDownloadBackgroundService.
    /// </summary>
    private async Task ImportProductImagesAsync(
        int productId,
        int storeId,
        int authorUserId,
        List<string> imageUrls,
        CancellationToken ct = default,
        string? imageBatchId = null)
    {
        var imageIndex = 0;

        foreach (var imageUrl in imageUrls)
        {
            // Check whether this image is already queued
            var exists = await _context.pendingImageDownloads
                .AnyAsync(p => p.productId == productId && p.imageUrl == imageUrl, ct);

            if (exists)
            {
                imageIndex++;
                continue;
            }

            // Add to the background download queue
            var pending = new PendingImageDownload
            {
                productId = productId,
                storeId = storeId,
                authorUserId = authorUserId,
                imageUrl = imageUrl,
                imageIndex = imageIndex,
                status = ImageDownloadStatus.Pending,
                createdAt = DateTime.UtcNow,
                batchId = imageBatchId
            };

            _context.pendingImageDownloads.Add(pending);
            imageIndex++;
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Queued {Count} images for download for product {ProductId}",
            imageUrls.Count, productId);
    }

    /// <summary>
    /// Get the external group ID for a product (used in field-based grouping).
    /// </summary>
    private async Task<string?> GetProductGroupIdAsync(
        int productId,
        int connectionId,
        CancellationToken ct = default)
    {
        // Find a group the product belongs to that is already linked to the marketplace
        var groupLink = await _context.productGroupMarketplaceLinks
            .Include(gl => gl.productGroup)
                .ThenInclude(g => g.members)
            .Where(gl => gl.connectionId == connectionId)
            .Where(gl => gl.productGroup.members.Any(m => m.productId == productId))
            .FirstOrDefaultAsync(ct);

        return groupLink?.externalGroupId;
    }

    /// <summary>
    /// Add a product to a group during import (creates the group if it does not exist).
    /// </summary>
    private async Task AddProductToGroupAsync(
        int productId,
        int connectionId,
        int storeId,
        string externalGroupId,
        string? externalGroupName = null,
        CancellationToken ct = default)
    {
        try
        {
            // Find an existing group link with the marketplace
            var groupLink = await _context.productGroupMarketplaceLinks
                .Include(gl => gl.productGroup)
                    .ThenInclude(g => g.members)
                .FirstOrDefaultAsync(gl =>
                    gl.connectionId == connectionId &&
                    gl.externalGroupId == externalGroupId, ct);

            if (groupLink == null)
            {
                // Group name: use externalGroupName if present, otherwise fall back
                var groupName = !string.IsNullOrEmpty(externalGroupName)
                    ? externalGroupName
                    : $"Group {externalGroupId}";

                var newGroup = new ProductGroup();
                newGroup.AddFill(new ProductGroup.AddForm
                {
                    name = groupName,
                    description = $"Automatically created during import (externalGroupId: {externalGroupId})",
                    storeId = storeId
                });

                _context.Set<ProductGroup>().Add(newGroup);
                await _context.SaveChangesAsync(ct);

                // Create the group link with the marketplace
                groupLink = new ProductGroupMarketplaceLink();
                groupLink.AddFill(new ProductGroupMarketplaceLink.AddForm
                {
                    productGroupId = newGroup.Id,
                    connectionId = connectionId,
                    externalGroupId = externalGroupId,
                    externalGroupName = externalGroupName,
                    status = Enums.ProductMarketplaceStatus.Synced
                });

                _context.productGroupMarketplaceLinks.Add(groupLink);
                await _context.SaveChangesAsync(ct);

                // Reload group with members collection
                groupLink.productGroup = newGroup;
                groupLink.productGroup.members = new List<ProductGroupMember>();

                _logger.LogInformation(
                    "Created a new group {GroupId} for externalGroupId={ExternalGroupId}",
                    newGroup.Id, externalGroupId);
            }

            // Check whether the product is already in this group
            var isAlreadyMember = groupLink.productGroup.members.Any(m => m.productId == productId);
            if (isAlreadyMember)
            {
                _logger.LogDebug(
                    "Product {ProductId} is already a member of group {GroupId}",
                    productId, groupLink.productGroupId);
                return;
            }

            // Add the product to the group
            var maxSortOrder = groupLink.productGroup.members.Any()
                ? groupLink.productGroup.members.Max(m => m.sortOrder)
                : 0;

            var member = new ProductGroupMember();
            member.AddFill(new ProductGroupMember.AddForm
            {
                groupId = groupLink.productGroupId,
                productId = productId,
                sortOrder = maxSortOrder + 1
            });

            _context.Set<ProductGroupMember>().Add(member);
            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Product {ProductId} added to group {GroupId} (externalGroupId={ExternalGroupId})",
                productId, groupLink.productGroupId, externalGroupId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error adding product {ProductId} to group with externalGroupId={ExternalGroupId}",
                productId, externalGroupId);
            // Do not abort the import because of a group-add error
        }
    }

    /// <summary>
    /// Extract a file extension from a URL, returning null if unrecognized.
    /// </summary>
    private static string? GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var lastDot = path.LastIndexOf('.');

            if (lastDot > 0 && lastDot < path.Length - 1)
            {
                var extension = path.Substring(lastDot).ToLowerInvariant();
                // Validate the extension
                if (extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif")
                    return extension;
            }
        }
        catch
        {
            // Ignore URL parsing errors
        }

        return null;
    }

    /// <summary>
    /// Sync product variants (sizes) from an external product into the local database.
    /// Additive: matches existing variants by barcode or size, creates new ones if not found.
    /// Creates/updates ProductVariantLink for marketplace-specific IDs.
    /// </summary>
    public async Task SyncVariantsAsync(
        int productId,
        int connectionId,
        List<ExternalProductVariant> variants,
        CancellationToken ct = default)
    {
        // Load existing local variants for this product
        var existingVariants = await _context.productVariants
            .Where(v => v.productId == productId)
            .ToListAsync(ct);

        // Load existing links for this connection
        var existingLinks = await _context.productVariantLinks
            .Where(l => l.variant.productId == productId && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByVariantId = existingLinks.ToDictionary(l => l.variantId, l => l);

        int nextSortOrder = existingVariants.Count > 0
            ? existingVariants.Max(v => v.sortOrder) + 1
            : 0;

        foreach (var v in variants)
        {
            var firstBarcode = v.Barcodes?.FirstOrDefault();

            // Try to find existing local variant by barcode or size
            var localVariant = existingVariants.FirstOrDefault(ev =>
                (!string.IsNullOrEmpty(firstBarcode) && ev.barcode == firstBarcode) ||
                (!string.IsNullOrEmpty(v.TechSize) && ev.size == v.TechSize));

            if (localVariant == null)
            {
                // Create new local variant
                localVariant = new ProductVariant();
                localVariant.AddFill(new ProductVariant.AddForm
                {
                    productId = productId,
                    barcode = firstBarcode,
                    size = v.TechSize,
                    price = v.Price,
                    quantity = v.Quantity,
                    sortOrder = nextSortOrder++
                });
                _context.productVariants.Add(localVariant);
                await _context.SaveChangesAsync(ct); // need ID for link
                existingVariants.Add(localVariant);
            }
            else
            {
                // Update existing variant data from marketplace
                if (v.Price.HasValue)
                    localVariant.price = v.Price;
                if (v.Quantity.HasValue)
                    localVariant.quantity = v.Quantity;
                if (!string.IsNullOrEmpty(firstBarcode) && string.IsNullOrEmpty(localVariant.barcode))
                    localVariant.barcode = firstBarcode;
                localVariant.updatedAt = DateTime.UtcNow;
            }

            // Create or update marketplace link
            if (linkByVariantId.TryGetValue(localVariant.Id, out var existingLink))
            {
                existingLink.externalVariantId = v.VariantId;
                existingLink.marketplaceSize = v.MarketplaceSize;
                existingLink.rawData = v.Extra != null
                    ? System.Text.Json.JsonSerializer.Serialize(v.Extra)
                    : null;
            }
            else
            {
                var link = new ProductVariantLink
                {
                    variantId = localVariant.Id,
                    connectionId = connectionId,
                    externalVariantId = v.VariantId,
                    marketplaceSize = v.MarketplaceSize,
                    rawData = v.Extra != null
                        ? System.Text.Json.JsonSerializer.Serialize(v.Extra)
                        : null
                };
                _context.productVariantLinks.Add(link);
            }
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Synced {Count} variants for product {ProductId} on connection {ConnectionId}",
            variants.Count, productId, connectionId);
    }

    private static string? FormatExportDetails(Dictionary<string, object>? details)
    {
        if (details == null || details.Count == 0)
            return null;

        try
        {
            if (details.TryGetValue("images", out var images))
            {
                var json = JsonSerializer.Serialize(images);
                return $"Image export details: {json}";
            }

            return $"Export details: {JsonSerializer.Serialize(details)}";
        }
        catch
        {
            return "Export details received, but could not be serialized for the log";
        }
    }
}

#region Result DTOs

/// <summary>
/// Result of a product export operation.
/// </summary>
public class ProductExportResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? LinkId { get; private set; }
    public string? ExternalProductId { get; private set; }
    public bool IsNewProduct { get; private set; }
    public string? Message { get; private set; }

    /// <summary>
    /// Create a successful export result.
    /// </summary>
    public static ProductExportResult Success(int linkId, string externalProductId, bool isNew, string message)
    {
        return new ProductExportResult
        {
            IsSuccess = true,
            LinkId = linkId,
            ExternalProductId = externalProductId,
            IsNewProduct = isNew,
            Message = message
        };
    }

    /// <summary>
    /// Create a failure export result.
    /// </summary>
    public static ProductExportResult Failure(string error)
    {
        return new ProductExportResult
        {
            IsSuccess = false,
            ErrorMessage = error
        };
    }
}

/// <summary>
/// Result of a product import operation.
/// </summary>
public class ProductImportResult
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? ProductId { get; private set; }
    public int? LinkId { get; private set; }
    public string? ProductName { get; private set; }
    public string? Message { get; private set; }
    public bool AlreadyLinked { get; private set; }

    /// <summary>
    /// Create a successful import result.
    /// </summary>
    public static ProductImportResult Success(int productId, int linkId, string productName, string message)
    {
        return new ProductImportResult
        {
            IsSuccess = true,
            ProductId = productId,
            LinkId = linkId,
            ProductName = productName,
            Message = message
        };
    }

    /// <summary>
    /// Create a result indicating the product is already linked.
    /// </summary>
    public static ProductImportResult AlreadyExists(int productId, string productName)
    {
        return new ProductImportResult
        {
            IsSuccess = false,
            AlreadyLinked = true,
            ProductId = productId,
            ProductName = productName,
            ErrorMessage = $"Product is already linked to local product '{productName}'"
        };
    }

    /// <summary>
    /// Create a failure import result.
    /// </summary>
    public static ProductImportResult Failure(string error)
    {
        return new ProductImportResult
        {
            IsSuccess = false,
            ErrorMessage = error
        };
    }
}

/// <summary>
/// DTO representing the synchronization status of a product on a marketplace.
/// </summary>
public class ProductSyncStatus
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? ProductSku { get; set; }
    public int LinkId { get; set; }
    public string ExternalProductId { get; set; } = null!;
    public string? ExternalSku { get; set; }
    public ProductMarketplaceStatus Status { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public decimal? LocalPrice { get; set; }
    public decimal? ExternalPrice { get; set; }
    public int? LocalStock { get; set; }
    public int? ExternalStock { get; set; }
}

/// <summary>
/// Detail of a single attribute change (for import preview).
/// </summary>
public record AttributeChangeDetail(string FieldName, string? OldValue, string? NewValue, bool HasIrreversibleModifiers = false);

/// <summary>
/// Result of a bulk product export operation.
/// </summary>
public class BulkProductExportResult
{
    public int SuccessCount { get; }
    public int FailedCount { get; }
    public int TotalCount { get; }
    public List<BulkProductExportItemResult> Items { get; }

    public BulkProductExportResult(int successCount, int failedCount, int totalCount, List<BulkProductExportItemResult> items)
    {
        SuccessCount = successCount;
        FailedCount = failedCount;
        TotalCount = totalCount;
        Items = items;
    }
}

/// <summary>
/// Per-product result in a bulk export.
/// </summary>
public record BulkProductExportItemResult(
    int ProductId,
    bool IsSuccess,
    string? ExternalProductId = null,
    string? ErrorMessage = null,
    bool IsNewProduct = false,
    Dictionary<string, object>? Details = null
);

#endregion
