using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Storage;
using Delegat.Infrastructure.Security;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Models.Products;
using Delegat.Services.Marketplace.Modifiers;
using Delegat.Services;
using System.Text.Json;
using ExternalMedia = Delegat.Marketplace.SDK.DTOs.ExternalMedia;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Service for synchronizing products with marketplaces (export, import, SKU update).
/// </summary>
public partial class ProductSyncService
{
    private readonly DataContext _context;
    private readonly IMarketplaceAdapterRegistry _adapterRegistry;
    private readonly MappingService _mappingService;
    private readonly IStorageService _storageService;
    private readonly ILogger<ProductSyncService> _logger;
    private readonly IOperationLogger _operationLogger;
    private readonly QuotaService _quotaService;
    private readonly ProductMediaExportGrantService? _mediaExportGrants;

    /// <summary>
    /// Checks whether selected products have the category mapping and required mapped
    /// values needed for marketplace export. Does not send anything to the marketplace.
    /// </summary>
    public async Task<IReadOnlyList<ProductExportReadinessItem>> CheckExportReadinessAsync(
        List<int> productIds,
        int connectionId,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        var ids = productIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        var products = await _context.products
            .AsSplitQuery()
            .Include(p => p.category)
                .ThenInclude(c => c!.attributes)
            .Include(p => p.attributeValues)
                .ThenInclude(av => av.attribute)
            .Include(p => p.storeAttributeValues)
                .ThenInclude(sav => sav.storeAttribute)
            .Include(p => p.files)
            .Include(p => p.images)
                .ThenInclude(i => i.file)
            .Include(p => p.groupMemberships)
                .ThenInclude(gm => gm.group)
            .Where(p => p.storeId == connection.storeId && ids.Contains(p.Id))
            .ToListAsync(ct);

        var productById = products.ToDictionary(p => p.Id);
        var trashedIds = await _context.products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.deletedAt != null)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var trashedIdSet = trashedIds.ToHashSet();
        var links = await _context.productMarketplaceLinks
            .AsNoTracking()
            .Where(l => ids.Contains(l.productId) && l.connectionId == connectionId)
            .ToListAsync(ct);
        var linkByProductId = links.ToDictionary(l => l.productId);

        var categoryIds = products
            .Where(p => p.categoryId.HasValue)
            .Select(p => p.categoryId!.Value)
            .Distinct()
            .ToList();
        var mappings = categoryIds.Count == 0
            ? []
            : await _context.categoryMappings
                .AsNoTracking()
                .Where(m => m.connectionId == connectionId && categoryIds.Contains(m.localCategoryId))
                .ToListAsync(ct);
        var mappingsByCategory = mappings.GroupBy(m => m.localCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var mappingsById = mappings.ToDictionary(m => m.Id);

        var result = new List<ProductExportReadinessItem>(ids.Count);
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        var adapterName = adapter?.DisplayName ?? connection.adapterKey;
        var connectionIssue = !connection.isActive
            ? "Подключение отключено. Включите его перед публикацией товаров."
            : adapter == null
                ? "Не удалось загрузить подключение к площадке. Проверьте настройки подключения."
                : !adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct)
                    ? "Это подключение не поддерживает публикацию товаров."
                : null;
        foreach (var productId in ids)
        {
            if (!productById.TryGetValue(productId, out var product))
            {
                var issue = trashedIdSet.Contains(productId)
                    ? "Товар находится в корзине. Восстановите его перед выкладкой."
                    : "Товар не найден в этом магазине.";
                result.Add(new(productId, $"Product #{productId}", false, [issue]));
                continue;
            }

            var issues = new List<string>();
            if (connectionIssue != null)
            {
                result.Add(new(product.Id, product.name, false, [connectionIssue]));
                continue;
            }

            if (!product.categoryId.HasValue)
            {
                issues.Add("Сначала назначьте товару категорию в каталоге.");
                result.Add(new(product.Id, product.name, false, issues));
                continue;
            }

            CategoryMapping? mapping = null;
            if (linkByProductId.TryGetValue(productId, out var existingLink)
                && existingLink.categoryMappingId.HasValue
                && mappingsById.TryGetValue(existingLink.categoryMappingId.Value, out var selectedMapping)
                && selectedMapping.localCategoryId == product.categoryId.Value)
            {
                mapping = selectedMapping;
            }
            else if (mappingsByCategory.TryGetValue(product.categoryId.Value, out var categoryMappings))
            {
                if (categoryMappings.Count == 1)
                    mapping = categoryMappings[0];
                else if (categoryMappings.Count > 1)
                    issues.Add($"Для этой категории есть несколько разделов {adapterName}. Выберите, в какой раздел опубликовать товар.");
            }

            if (mapping == null && !issues.Any(i => i.StartsWith("Для этой категории есть несколько разделов", StringComparison.Ordinal)))
            {
                issues.Add($"Для категории «{product.category?.name ?? "Без названия"}» не выбран раздел {adapterName}.");
            }
            else if (mapping != null)
            {
                try
                {
                    var exportData = await _mappingService.BuildExportDataAsync(product, mapping, ct);
                    var missingFields = MappingService.SchemaFields(mapping.graphJson)
                        .Where(f => f.isRequired && !string.IsNullOrWhiteSpace(f.fieldKey))
                        .Where(f => !exportData.TryGetValue(f.fieldKey!, out var value)
                            || value == null
                            || (value is string text && string.IsNullOrWhiteSpace(text)))
                        .Select(f => f.label ?? f.fieldKey!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (missingFields.Count > 0)
                        issues.Add($"Заполните обязательные поля: {string.Join(", ", missingFields)}.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not check export readiness for product {ProductId}", productId);
                    issues.Add("Не удалось проверить заполнение полей. Проверьте сопоставление категории.");
                }
            }

            result.Add(new(product.Id, product.name, issues.Count == 0, issues));
        }

        return result;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ProductSyncService"/>.
    /// </summary>
    public ProductSyncService(
        DataContext context,
        IMarketplaceAdapterRegistry adapterRegistry,
        MappingService mappingService,
        IStorageService storageService,
        ILogger<ProductSyncService> logger,
        IOperationLogger operationLogger,
        QuotaService quotaService,
        ProductMediaExportGrantService? mediaExportGrants = null)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _mappingService = mappingService;
        _storageService = storageService;
        _logger = logger;
        _operationLogger = operationLogger;
        _quotaService = quotaService;
        _mediaExportGrants = mediaExportGrants;
    }

    private static void AddMarketplaceUnitIds(
        Dictionary<string, object?> exportData,
        CategoryMapping mapping,
        string adapterKey)
    {
        // Yandex needs marketplace unit IDs on numeric parameters. The saved graph is
        // authoritative; the adapter's runtime metadata cache is only a legacy fallback.
        if (!string.Equals(adapterKey, "yandex", StringComparison.OrdinalIgnoreCase))
            return;

        var unitIds = MappingService.SchemaFields(mapping.graphJson)
            .Where(field => !string.IsNullOrWhiteSpace(field.fieldKey)
                && !string.IsNullOrWhiteSpace(field.unitId))
            .GroupBy(field => field.fieldKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().unitId!, StringComparer.OrdinalIgnoreCase);
        if (unitIds.Count > 0)
            exportData["_fieldUnitIds"] = unitIds;
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

        if (await ProductNotTrashed.IsTrashedAsync(_context, productId, ct))
            return ProductExportResult.Failure(ProductNotTrashed.Message);

        await Log("info", $"Loading {OperationProductRef.Format(productId, null)}");

        // Split query: a category with 100+ attributes cartesian-explodes
        // files/images/memberships into one JOIN (minutes, empty groups).
        var product = await _context.products
            .AsSplitQuery()
            .Include(p => p.category)
                .ThenInclude(c => c!.attributes)
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

        await Log("info", $"Exporting {OperationProductRef.Format(product.Id, product.name)} ({product.sku})");

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

        // A saved mapping counts only when it still belongs to the product's category.
        // Several mappings and no choice is an error — do not pick the first one.
        CategoryMapping? categoryMapping = null;
        if (existingLink?.categoryMappingId != null)
        {
            var saved = await _context.categoryMappings
                .FirstOrDefaultAsync(cm => cm.Id == existingLink.categoryMappingId.Value, ct);
            if (saved != null && saved.localCategoryId == product.categoryId.Value)
                categoryMapping = saved;
        }

        if (categoryMapping == null)
        {
            var matches = await _context.categoryMappings
                .Where(cm =>
                    cm.connectionId == connectionId &&
                    cm.localCategoryId == product.categoryId.Value)
                .ToListAsync(ct);
            if (matches.Count == 1)
                categoryMapping = matches[0];
            else if (matches.Count > 1)
            {
                await Log("error", "Mapping is not selected");
                return ProductExportResult.Failure("Mapping is not selected");
            }
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
            AddMarketplaceUnitIds(exportData, categoryMapping, adapter.Key);

            // Adapter plumbing (not a mapping field): Woo archive import keys attachments by this id.
            exportData["_localProductId"] = product.Id;
            if (!exportData.ContainsKey("delegat_product_id") && !exportData.ContainsKey("delegatProductId"))
                exportData["delegat_product_id"] = product.Id.ToString();

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

            AttachLocalProductGroup(exportData, product, adapter.Key, endpointGroupValue);
            if (exportData.TryGetValue("group_id", out var attachedGroupId) && attachedGroupId != null)
            {
                exportData.TryGetValue("group_name", out var attachedGroupName);
                await Log("info",
                    $"PIM group: {attachedGroupName ?? attachedGroupId} (id={attachedGroupId})");
            }
            else if (string.Equals(adapter.Key, "medusa", StringComparison.OrdinalIgnoreCase))
            {
                await Log("info", "Product has no PIM group — Medusa collection will not be created");
            }

            // Inject variants if product has them - join with links for this connection
            var variants = await _context.productVariants
                .Where(v => v.productId == product.Id)
                .Include(v => v.marketplaceLinks.Where(l => l.connectionId == connectionId))
                .ToListAsync(ct);
            if (variants.Count > 0)
            {
                exportData["_variants"] = variants
                    .Select(v => ProductVariantExport.ToPayload(v, v.marketplaceLinks.FirstOrDefault()))
                    .ToList();
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

            // Export via the adapter
            // "pending:*" placeholders are not real marketplace IDs - treat as null (new product)
            var realExternalId = existingLink?.externalProductId;
            if (realExternalId != null && realExternalId.StartsWith("pending:"))
                realExternalId = null;

            // Already known broken — recreate without calling the marketplace for preflight.
            if (existingLink?.status == ProductMarketplaceStatus.RemoteMissing)
                realExternalId = null;

            // Preflight: if linked remote product disappeared, mark broken and create a new listing.
            // Medusa upsert is idempotent by SKU / external id — skip the extra GET.
            if (realExternalId != null &&
                existingLink != null &&
                !string.Equals(adapter.Key, "medusa", StringComparison.OrdinalIgnoreCase) &&
                adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            {
                try
                {
                    var remote = await adapter.ImportProductAsync(connection, realExternalId, ct);
                    if (remote == null)
                    {
                        await Log("warning",
                            $"Remote product {realExternalId} not found on the marketplace — exporting as new");
                        MarkLinkRemoteMissing(
                            existingLink,
                            $"Product '{realExternalId}' not found on the marketplace");
                        await _context.SaveChangesAsync(ct);
                        realExternalId = null;
                    }
                }
                catch (Exception ex)
                {
                    // Do not block export on transient preflight failures.
                    await Log("warning", $"Could not verify remote product {realExternalId}: {ex.Message}");
                    _logger.LogWarning(ex,
                        "Export preflight failed for product {ProductId} external {ExternalId}",
                        productId, realExternalId);
                }
            }

            var pinnedExternalId = realExternalId;
            var previousLinkExternalId = existingLink?.externalProductId;

            if (realExternalId == null
                && string.Equals(adapter.Key, "wildberries", StringComparison.OrdinalIgnoreCase))
            {
                await Log("info",
                    "Wildberries confirms a new card asynchronously. This export stays running until the article is in the catalog or the draft error list names it (up to 30 minutes).");
            }

            var mediaWarning = await PrepareGalleryVideoExportAsync(
                adapter, connection, product, exportDataNonNull, ct);
            if (mediaWarning != null)
                await Log("warning", mediaWarning);
            await Log("info", "Sending to the marketplace");

            var result = await adapter.ExportProductAsync(
                connection,
                exportDataNonNull,
                realExternalId,
                ct);

            if (!result.Success)
            {
                var level = result.Retryable ? "error" : "warning";
                var safeErrorMessage = MediaExportGrantSecrets.Redact(result.ErrorMessage ?? "unknown");
                await Log(level, $"Marketplace returned an error: {safeErrorMessage}");
                var detailsMessage = MediaExportGrantSecrets.Redact(FormatExportDetails(result.Details));
                if (detailsMessage != null)
                    await Log(level, detailsMessage);

                // Update the link status on error
                if (existingLink != null)
                {
                    existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                    {
                        status = ProductMarketplaceStatus.SyncError,
                        lastErrorMessage = safeErrorMessage,
                        lastSyncAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }

                return ProductExportResult.Failure(safeErrorMessage, result.Retryable);
            }

            var successWarning = MediaExportGrantSecrets.Redact(result.ErrorMessage);
            if (!string.IsNullOrWhiteSpace(successWarning))
                await Log("warning", successWarning);

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

            var resolvedFrom = MarketplaceExportIdentity.TryGetDetailString(result.Details, "resolvedFrom");
            if (!MarketplaceExportIdentity.MayUpdateExternalProductId(
                    previousLinkExternalId,
                    pinnedExternalId,
                    result.ExternalProductId,
                    resolvedFrom))
            {
                var msg =
                    $"Marketplace returned external id '{result.ExternalProductId}' which differs from the linked id '{previousLinkExternalId}'. " +
                    "Link was not overwritten to prevent silent rebind.";
                await Log("error", msg);
                if (existingLink != null)
                {
                    existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                    {
                        status = ProductMarketplaceStatus.SyncError,
                        lastErrorMessage = msg,
                        lastSyncAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }

                return ProductExportResult.Failure(msg);
            }

            var offerIdFromDetails = MarketplaceExportIdentity.TryGetDetailString(result.Details, "offerId");

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
                    externalSku = offerIdFromDetails,
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
                    externalSku = offerIdFromDetails ?? existingLink.externalSku,
                    status = ProductMarketplaceStatus.Synced,
                    lastErrorMessage = null,
                    lastSyncAt = DateTime.UtcNow,
                    categoryMappingId = categoryMapping.Id
                });
            }

            await ApplyExportedVariantIdsAsync(productId, connectionId, result.Details, ct);
            await _context.SaveChangesAsync(ct);

            // Endpoint grouping: join/create the marketplace group for the evaluated value
            if (endpointGroupValue != null)
            {
                await SyncEndpointGroupsByValueAsync(adapter, connection, connectionId,
                    new Dictionary<int, string> { [productId] = endpointGroupValue },
                    new List<BulkProductExportItemResult> { new(productId, true) },
                    ct);
            }

            // Auto-export price after successful product export.
            // Medusa product upsert already writes prices and stock.
            var followUpErrors = new List<string>();
            var skipFollowUp = string.Equals(adapter.Key, "medusa", StringComparison.OrdinalIgnoreCase);
            if (!skipFollowUp && adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportPrices))
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
                        var priceResult = await adapter.ExportPricesAsync(
                            connection, new List<PriceInfo> { priceInfo }, ct);
                        if (priceResult.IsSuccess)
                        {
                            await Log("info", "Price exported");
                        }
                        else
                        {
                            var error = $"Price export failed: {FormatImportErrors(priceResult)}";
                            followUpErrors.Add(error);
                            await Log("warning", error);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        var safeError = MediaExportGrantSecrets.Redact(ex.Message);
                        _logger.LogWarning(ex, "Auto price export failed for product {ProductId}", productId);
                        followUpErrors.Add($"Price export failed: {safeError}");
                        await Log("warning", $"Failed to export price: {safeError}");
                    }
                }
            }

            // Auto-export stock after successful product export
            if (!skipFollowUp && adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportStocks))
            {
                await Log("info", $"Automatic stock export: {product.storageQuantity ?? 0}");
                try
                {
                    var stockInfo = new StockInfo(
                        existingLink.externalProductId,
                        product.storageQuantity ?? 0
                    );
                    var stockResult = await adapter.ExportStocksAsync(
                        connection, new List<StockInfo> { stockInfo }, ct);
                    if (stockResult.IsSuccess)
                    {
                        await Log("info", "Stock exported");
                    }
                    else
                    {
                        var error = $"Stock export failed: {FormatImportErrors(stockResult)}";
                        followUpErrors.Add(error);
                        await Log("warning", error);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var safeError = MediaExportGrantSecrets.Redact(ex.Message);
                    _logger.LogWarning(ex, "Auto stock export failed for product {ProductId}", productId);
                    followUpErrors.Add($"Stock export failed: {safeError}");
                    await Log("warning", $"Failed to export stock: {safeError}");
                }
            }

            if (followUpErrors.Count > 0)
            {
                var error = string.Join("; ", followUpErrors);
                await Log("warning", $"Card exported, but the product is not fully synced: {error}");
                return ProductExportResult.Failure(error);
            }

            await Log("success", isNewProduct ? "Product created on the marketplace" : "Product updated on the marketplace");

            return ProductExportResult.Success(
                existingLink.Id,
                result.ExternalProductId!,
                isNewProduct,
                isNewProduct ? "Product created on the marketplace" : "Product updated on the marketplace"
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex)
        {
            var message = $"{ex.Code ?? "media_export_failed"}: {ex.Message}";
            await Log("error", message);
            return ProductExportResult.Failure(message, retryable: false);
        }
        catch (Exception ex)
        {
            var safeError = MediaExportGrantSecrets.Redact(ex.Message);
            await Log("error", $"Export error: {safeError}");
            return ProductExportResult.Failure($"Export error: {safeError}");
        }
    }

    /// <summary>
    /// Export multiple products to a marketplace in a single bulk operation.
    /// Uses batched DB queries and a single adapter call for efficiency.
    /// </summary>
    public async Task<BulkProductExportResult> ExportProductsBulkAsync(
        List<int> productIds,
        int connectionId,
        CancellationToken ct = default,
        Func<string, CancellationToken, Task>? progress = null)
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

        // One query: load all products with includes (split to avoid cartesian explosion)
        var products = await _context.products
            .AsSplitQuery()
            .Include(p => p.category)
                .ThenInclude(c => c!.attributes)
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
                var trashMessage = await ProductNotTrashed.IsTrashedAsync(_context, productId, ct)
                    ? ProductNotTrashed.Message
                    : $"Product with ID {productId} not found";
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: trashMessage));
                failedProductIds.Add(productId);
                continue;
            }

            if (!product.categoryId.HasValue)
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: "Product is not assigned to a category"));
                failedProductIds.Add(productId);
                continue;
            }

            // Resolve category mapping: a saved choice only if it still matches this category.
            // Several mappings and no choice is an error — do not pick the first one.
            CategoryMapping? categoryMapping = null;

            if (linkByProductId.TryGetValue(productId, out var existingLink)
                && existingLink.categoryMappingId != null
                && mappingsById.TryGetValue(existingLink.categoryMappingId.Value, out var overrideMapping)
                && overrideMapping.localCategoryId == product.categoryId.Value)
            {
                categoryMapping = overrideMapping;
            }
            else if (mappingsByCategoryId.TryGetValue(product.categoryId.Value, out var catMappings))
            {
                if (catMappings.Count == 1)
                    categoryMapping = catMappings[0];
                else if (catMappings.Count > 1)
                {
                    itemResults.Add(new BulkProductExportItemResult(productId, false,
                        ErrorMessage: "Mapping is not selected"));
                    failedProductIds.Add(productId);
                    continue;
                }
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
                AddMarketplaceUnitIds(exportData, categoryMapping, adapter.Key);
                exportData["_localProductId"] = product.Id;
                if (!exportData.ContainsKey("delegat_product_id") && !exportData.ContainsKey("delegatProductId"))
                    exportData["delegat_product_id"] = product.Id.ToString();
                exportData["_categoryId"] = categoryMapping.externalCategoryId;

                // Add group_id for field-based grouping
                if (fieldGroupingConfig != null && !exportData.ContainsKey(fieldGroupingConfig.GroupFieldKey))
                {
                    if (groupIdByProductId.TryGetValue(productId, out var groupId) && groupId != null)
                        exportData[fieldGroupingConfig.GroupFieldKey] = groupId;
                }

                // Endpoint grouping: pull the synthetic field's value out - it's consumed by the
                // post-export group sync, the adapter must not receive it as a product attribute
                string? groupValue = null;
                if (endpointGrouping && exportData.TryGetValue(SyntheticGroupFieldKey, out var rawGroupValue))
                {
                    exportData.Remove(SyntheticGroupFieldKey);
                    groupValue = rawGroupValue?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(groupValue))
                        groupValue = null;
                    else
                        groupValueByProductId[productId] = groupValue;
                }

                AttachLocalProductGroup(exportData, product, adapter.Key, groupValue);

                // Inject variants if present
                if (variantsByProductId.TryGetValue(productId, out var prodVariants) && prodVariants.Count > 0)
                {
                    exportData["_variants"] = prodVariants
                        .Select(v => ProductVariantExport.ToPayload(v, v.marketplaceLinks.FirstOrDefault()))
                        .ToList();
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
                // Broken remote links must recreate, not update a missing ID
                if (link != null && link.status == ProductMarketplaceStatus.RemoteMissing)
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

        // Grants are created only after every item has passed local validation and
        // immediately before the real bulk adapter call.
        foreach (var item in bulkItems.ToList())
        {
            if (!productDict.TryGetValue(item.LocalProductId, out var itemProduct))
                continue;

            try
            {
                var warning = await PrepareGalleryVideoExportAsync(
                    adapter, connection, itemProduct, item.ProductData, ct);
                if (warning != null && progress != null)
                    await progress($"Product {item.LocalProductId}: {warning}", ct);
            }
            catch (ApiException ex)
            {
                bulkItems.Remove(item);
                failedProductIds.Add(item.LocalProductId);
                itemResults.Add(new BulkProductExportItemResult(
                    item.LocalProductId,
                    false,
                    ErrorMessage: $"{ex.Code ?? "media_export_failed"}: {ex.Message}",
                    Retryable: false));
            }
        }

        // Call adapter bulk export
        if (bulkItems.Count > 0)
        {
            try
            {
                var bulkResult = progress != null && adapter is IBulkExportProgressAdapter progressAdapter
                    ? await progressAdapter.ExportProductsBulkAsync(connection, bulkItems, progress, ct)
                    : await adapter.ExportProductsBulkAsync(connection, bulkItems, ct);

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
                        // Group in memory: EF Core cannot translate GroupBy without an aggregate,
                        // and duplicate external IDs must not throw and wipe the whole bulk result
                        var matchingLinks = await _context.productMarketplaceLinks
                            .Where(l => l.connectionId == connectionId && allExternalIds.Contains(l.externalProductId))
                            .ToListAsync(ct);
                        dupLinksByExternalId = matchingLinks
                            .GroupBy(l => l.externalProductId)
                            .ToDictionary(g => g.Key, g => g.First());
                    }
                }

                var adapterProductIds = bulkItems.Select(i => i.LocalProductId).ToHashSet();
                var resultGroups = (bulkResult.Items ?? [])
                    .GroupBy(i => i.LocalProductId)
                    .ToDictionary(g => g.Key, g => g.ToList());
                var foreignProductIds = resultGroups.Keys
                    .Where(id => !adapterProductIds.Contains(id))
                    .ToList();

                if (foreignProductIds.Count > 0)
                {
                    var protocolError =
                        $"Marketplace returned results for unrequested products ({string.Join(", ", foreignProductIds)}); " +
                        "the bulk response is not authoritative and requires reconciliation.";
                    _logger.LogError(
                        "Bulk export protocol fault for connectionId={ConnectionId}: foreign product ids [{ProductIds}]",
                        connectionId, string.Join(", ", foreignProductIds));
                    foreach (var item in bulkItems)
                    {
                        itemResults.Add(new BulkProductExportItemResult(
                            item.LocalProductId,
                            false,
                            ErrorMessage: protocolError,
                            Retryable: false));
                    }
                }
                else
                {
                    foreach (var resultGroup in resultGroups)
                    {
                        if (resultGroup.Value.Count != 1)
                        {
                            itemResults.Add(new BulkProductExportItemResult(
                                resultGroup.Key,
                                false,
                                ErrorMessage:
                                    $"Marketplace returned {resultGroup.Value.Count} conflicting results for this product; outcome requires reconciliation.",
                                Retryable: false));
                            continue;
                        }

                        var exportItemResult = resultGroup.Value[0];
                        if (!exportItemResult.Success)
                        {
                            itemResults.Add(new BulkProductExportItemResult(
                                exportItemResult.LocalProductId, false,
                                ErrorMessage: MediaExportGrantSecrets.Redact(exportItemResult.ErrorMessage ?? "Export error"),
                                Details: MediaExportGrantSecrets.RedactDetails(exportItemResult.Details),
                                Retryable: exportItemResult.Retryable));
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
                            var previousId = existingLink.externalProductId;
                            // What we passed to the adapter (null means create-as-new even if link row exists)
                            string? pinnedId = null;
                            foreach (var bi in bulkItems)
                            {
                                if (bi.LocalProductId == pid)
                                {
                                    pinnedId = bi.ExistingExternalId;
                                    break;
                                }
                            }

                            var resolvedFrom = MarketplaceExportIdentity.TryGetDetailString(
                                exportItemResult.Details, "resolvedFrom");
                            if (!MarketplaceExportIdentity.MayUpdateExternalProductId(
                                    previousId,
                                    pinnedId,
                                    exportItemResult.ExternalProductId,
                                    resolvedFrom))
                            {
                                var msg =
                                    $"Marketplace returned external id '{exportItemResult.ExternalProductId}' which differs from the linked id '{previousId}'. Link was not overwritten.";
                                existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                                {
                                    status = ProductMarketplaceStatus.SyncError,
                                    lastErrorMessage = msg,
                                    lastSyncAt = DateTime.UtcNow
                                });
                                itemResults.Add(new BulkProductExportItemResult(
                                    pid, false, ErrorMessage: msg, Details: exportItemResult.Details));
                                continue;
                            }

                            var offerIdFromDetails = MarketplaceExportIdentity.TryGetDetailString(
                                exportItemResult.Details, "offerId");

                            existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                            {
                                externalProductId = exportItemResult.ExternalProductId,
                                externalSku = offerIdFromDetails ?? existingLink.externalSku,
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
                                var offerIdFromDetails = MarketplaceExportIdentity.TryGetDetailString(
                                    exportItemResult.Details, "offerId");
                                var newLink = new ProductMarketplaceLink();
                                newLink.AddFill(new ProductMarketplaceLink.AddForm
                                {
                                    productId = pid,
                                    connectionId = connectionId,
                                    externalProductId = exportItemResult.ExternalProductId!,
                                    externalSku = offerIdFromDetails,
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
                            ErrorMessage: MediaExportGrantSecrets.Redact(exportItemResult.ErrorMessage),
                            IsNewProduct: isNew,
                            Details: MediaExportGrantSecrets.RedactDetails(exportItemResult.Details)));
                        await ApplyExportedVariantIdsAsync(
                            pid, connectionId, exportItemResult.Details, ct);
                    }

                    foreach (var item in bulkItems.Where(i => !resultGroups.ContainsKey(i.LocalProductId)))
                    {
                        itemResults.Add(new BulkProductExportItemResult(
                            item.LocalProductId,
                            false,
                            ErrorMessage:
                                "Marketplace did not return a result for this product; outcome requires reconciliation.",
                            Retryable: false));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var safeError = MediaExportGrantSecrets.Redact(ex.ToString());
                _logger.LogError("Bulk export failed for connectionId={ConnectionId}: {Error}", connectionId, safeError);
                // Do not overwrite items already recorded as successful — WP may have created them
                var reportedIds = itemResults.Select(r => r.ProductId).ToHashSet();
                foreach (var item in bulkItems)
                {
                    if (reportedIds.Contains(item.LocalProductId) || failedProductIds.Contains(item.LocalProductId))
                        continue;
                    itemResults.Add(new BulkProductExportItemResult(
                        item.LocalProductId, false, ErrorMessage: $"Export error: {MediaExportGrantSecrets.Redact(ex.Message)}"));
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

    private async Task<string?> PrepareGalleryVideoExportAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        Product product,
        Dictionary<string, object> exportData,
        CancellationToken ct)
    {
        var videoFileIds = GetGalleryVideoFileIds(product);
        var mediaCapabilities = adapter is Delegat.Marketplace.SDK.IMarketplaceMediaCapabilities mediaAdapter
            ? mediaAdapter.GetMediaCapabilities()
            : new Delegat.Marketplace.SDK.MediaCapabilities();

        // Video URLs are not an established export field for legacy adapters. Strip graph
        // values for every adapter unless its optional capability explicitly permits safe,
        // permanent-URL export; only then replace them with host-issued gallery grants.
        var hasUntrustedVideoMapping = exportData.ContainsKey("videos")
            || exportData.ContainsKey("_videoUrls")
            || exportData.ContainsKey("videoNames")
            || exportData.ContainsKey("_videoNames");
        exportData.Remove("videos");
        exportData.Remove("_videoUrls");
        exportData.Remove("videoNames");
        exportData.Remove("_videoNames");

        if (!mediaCapabilities.CanExportVideos || !mediaCapabilities.UsesPermanentUrls)
            return videoFileIds.Length == 0
                ? null
                : $"{adapter.DisplayName} does not support video export with permanent URLs; gallery videos were not sent.";

        // Yandex accepts permanent URLs only. Arbitrary graph URLs are not equivalent to
        // a host-issued grant, so the built-in video field is populated from owned gallery files.
        if (videoFileIds.Length == 0)
            return hasUntrustedVideoMapping
                ? "Mapped video URLs were ignored; add downloadable video files to the product gallery to export them safely."
                : null;

        if (mediaCapabilities.MaxVideos is int maxVideos && videoFileIds.Length > maxVideos)
        {
            throw new ApiException(
                $"{adapter.DisplayName} accepts at most {maxVideos} videos; this product has {videoFileIds.Length}.",
                StatusCodes.Status400BadRequest,
                code: "media_export_video_limit_exceeded");
        }
        if (_mediaExportGrants == null)
            throw ApiException.InternalServerError(code: "media_export_grants_unavailable");

        exportData["videos"] = (await _mediaExportGrants.GetOrCreateUrlsAsync(
            connection.Id,
            product.Id,
            videoFileIds,
            ct)).ToList();
        if (string.Equals(adapter.Key, "ozon", StringComparison.OrdinalIgnoreCase))
            exportData["videoNames"] = GetGalleryVideoFiles(product)
                .Select((file, index) => string.IsNullOrWhiteSpace(file.displayName)
                    ? $"video_{index + 1}"
                    : file.displayName.Trim())
                .ToList();
        return null;
    }

    private static Delegat.Models.File[] GetGalleryVideoFiles(Product product)
    {
        if (product.images == null || product.images.Count == 0)
            return [];

        return product.images
            .OrderBy(image => image.index)
            .Select(image => image.file ?? product.files?.FirstOrDefault(file => file.Id == image.fileId))
            .Where(file => file != null
                && !string.IsNullOrWhiteSpace(file.contentType)
                && file.contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .Select(file => file!)
            .DistinctBy(file => file.Id)
            .ToArray();
    }

    private static int[] GetGalleryVideoFileIds(Product product)
        => GetGalleryVideoFiles(product).Select(file => file.Id).ToArray();

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
    /// Medusa collections are assigned at product upsert from the local PIM group.
    /// Endpoint grouping otherwise only runs after the batch, and a stale
    /// marketplace group id (after a catalog wipe) never recreates the collection.
    /// </summary>
    internal static void AttachLocalProductGroup(
        Dictionary<string, object?> exportData,
        Product product,
        string adapterKey,
        string? mappingGroupValue = null)
    {
        if (!string.Equals(adapterKey, "medusa", StringComparison.OrdinalIgnoreCase))
            return;

        var group = product.groupMemberships?
            .Where(m => m.group != null)
            .OrderBy(m => m.sortOrder)
            .Select(m => m.group!)
            .FirstOrDefault();
        if (group != null)
        {
            exportData["group_id"] = group.Id.ToString();
            if (!HasNonEmpty(exportData, "group_name") && !HasNonEmpty(exportData, "groupName"))
            {
                exportData["group_name"] = !string.IsNullOrWhiteSpace(mappingGroupValue)
                    ? mappingGroupValue.Trim()
                    : group.name;
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(mappingGroupValue))
            return;
        if (!HasNonEmpty(exportData, "group_name") && !HasNonEmpty(exportData, "groupName"))
            exportData["group_name"] = mappingGroupValue.Trim();
    }

    private static bool HasNonEmpty(Dictionary<string, object?> data, string key)
        => data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value?.ToString());

    private static async Task<bool> RecreateEndpointGroupAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        GroupValueMarketplaceLink link,
        List<string> externalIds,
        string groupValue,
        CancellationToken ct)
    {
        var createResult = await adapter.CreateGroupAsync(connection, externalIds, groupValue, ct);
        if (!createResult.Success || string.IsNullOrEmpty(createResult.ExternalGroupId))
            return false;

        link.externalGroupId = createResult.ExternalGroupId;
        link.status = ProductMarketplaceStatus.Synced;
        link.lastErrorMessage = null;
        link.lastSyncAt = DateTime.UtcNow;
        return true;
    }

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
                        {
                            _logger.LogWarning(
                                "[GroupSync] AddProductsToGroupAsync failed for value '{Value}': {Error}. Recreating group.",
                                groupValue, addResult.ErrorMessage);
                            var recreated = await RecreateEndpointGroupAsync(
                                adapter, connection, link, externalIds, groupValue, ct);
                            if (!recreated)
                            {
                                link.status = ProductMarketplaceStatus.SyncError;
                                link.lastErrorMessage = addResult.ErrorMessage;
                            }
                        }
                        else
                        {
                            link.status = ProductMarketplaceStatus.Synced;
                            link.lastErrorMessage = null;
                        }

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
                    if (externalIds.Count < 2 &&
                        !string.Equals(adapter.Key, "medusa", StringComparison.OrdinalIgnoreCase))
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

        var existingLink = await _context.productMarketplaceLinks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(l => l.product)
            .FirstOrDefaultAsync(l =>
                l.connectionId == connectionId &&
                l.externalProductId == externalProductId, ct);

        if (existingLink != null)
        {
            var existingName = existingLink.product?.name ?? "Unknown";
            await Log("warning", $"{existingName} - already imported (ID: {existingLink.productId})");
            return ProductImportResult.AlreadyExists(
                existingLink.productId,
                existingName
            );
        }

        try
        {
            await _quotaService.EnsureProductQuotaAsync(connection.storeId);
        }
        catch (ApiException ex) when (ex.Code == "product_limit_reached")
        {
            await Log("error", ex.Message);
            return ProductImportResult.Failure(ex.Message, ex.Code, ex.Data);
        }

        try
        {
            // Get the product from the marketplace
            await Log("info", $"Loading product {externalProductId} from the marketplace...");
            var externalProduct = await adapter.ImportProductAsync(connection, externalProductId, ct);
            if (externalProduct == null)
            {
                await Log("error", $"Product {externalProductId} not found on the marketplace");
                await MarkLinksRemoteMissingByExternalIdAsync(
                    connectionId,
                    externalProductId,
                    $"Product '{externalProductId}' not found on the marketplace",
                    ct);
                return ProductImportResult.Failure($"Product with ID '{externalProductId}' not found on the marketplace");
            }

            externalProduct = await WithImportedPriceAsync(adapter, connection, externalProduct, ct);

            var productLabel = externalProduct.Name ?? externalProductId;
            var importBarcode = MarketplaceImportBarcode.Resolve(externalProduct);
            await Log("info", $"Received: {productLabel} | SKU: {externalProduct.Sku ?? "-"} | Barcode: {importBarcode ?? "-"} | Price: {externalProduct.Price} | Stock: {externalProduct.Quantity} | Attributes: {externalProduct.Attributes?.Count ?? 0} | Photos: {externalProduct.ImageUrls?.Count ?? 0} | Variants: {externalProduct.Variants?.Count ?? 0}");

            _logger.LogInformation(
                "Importing product {ExternalId}: Name={Name}, Description={Description}, Price={Price}, Quantity={Quantity}, Barcode={Barcode}, Attributes={AttrCount}",
                externalProductId,
                externalProduct.Name,
                externalProduct.Description?.Substring(0, Math.Min(50, externalProduct.Description?.Length ?? 0)) ?? "null",
                externalProduct.Price,
                externalProduct.Quantity,
                importBarcode ?? "null",
                externalProduct.Attributes?.Count ?? 0);

            await Log("info", $"Preparing atomic local import: {productLabel}...");
            return await CommitNewImportAsync(connection, externalProduct, externalProductId,
                targetCategoryId, importByUserId, imageBatchId, linked: true, receiptKey: null, ct);
        }
        catch (ApiException ex) when (ex.Code == "product_limit_reached")
        {
            await Log("error", ex.Message);
            return ProductImportResult.Failure(ex.Message, ex.Code, ex.Data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product import error {ExternalId}", externalProductId);
            await Log("error", $"Import error {externalProductId}: {ex.Message}");
            return ProductImportResult.Failure($"Import error: {ex.Message}");
        }
    }

    /// <summary>
    /// Create a local product from an already-fetched public card. Does not
    /// insert a marketplace link (export later as a new listing).
    /// </summary>
    public async Task<ProductImportResult> ImportUnlinkedExternalProductAsync(
        MarketplaceConnection connection,
        ExternalProduct externalProduct,
        int? targetCategoryId,
        int userId,
        CancellationToken ct,
        string? operationId = null)
    {
        async Task Log(string level, string message)
        {
            if (operationId != null)
                await _operationLogger.LogAsync(operationId, connection.storeId, level, message);
        }

        var publicId = (externalProduct.ExternalId ?? "").Trim();
        if (publicId.Length == 0)
            return ProductImportResult.Failure("Public product id is required");

        var receiptKey = PublicImportReceiptKey(connection, publicId, operationId);
        var previous = await FindCommittedImportAsync(connection, publicId, linked: false, receiptKey, ct);
        if (previous != null)
            return previous;

        try
        {
            await _quotaService.EnsureProductQuotaAsync(connection.storeId);
        }
        catch (ApiException ex) when (ex.Code == "product_limit_reached")
        {
            await Log("error", ex.Message);
            return ProductImportResult.Failure(ex.Message, ex.Code, ex.Data);
        }

        try
        {
            var productLabel = externalProduct.Name ?? publicId;
            await Log("info", $"Preparing atomic public-card copy: {productLabel} | public id: {publicId}");
            return await CommitNewImportAsync(connection, externalProduct, publicId,
                targetCategoryId, userId > 0 ? userId : null, operationId, linked: false, receiptKey, ct);
        }
        catch (ApiException ex) when (ex.Code == "product_limit_reached")
        {
            await Log("error", ex.Message);
            return ProductImportResult.Failure(ex.Message, ex.Code, ex.Data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Public catalog import error {PublicId}", publicId);
            await Log("error", $"Import error {publicId}: {ex.Message}");
            return ProductImportResult.Failure($"Import error: {ex.Message}");
        }
    }

    internal async Task<string> ResolveUniqueSkuAsync(
        int storeId,
        string? vendor,
        string publicId,
        string? adapterKey,
        CancellationToken ct)
    {
        var preferred = StringClip.ClipRequired(
            vendor, Product.PRODUCT_SKU_MAX_LENGTH, $"MP-{publicId}");
        if (!await SkuTakenAsync(storeId, preferred, ct))
            return preferred;

        var tag = string.IsNullOrWhiteSpace(adapterKey) ? "mp" : adapterKey.Trim().ToLowerInvariant();
        if (tag.Length > 2)
            tag = tag[..2];
        var tagged = StringClip.ClipRequired(
            $"{preferred}-{tag}-{publicId}", Product.PRODUCT_SKU_MAX_LENGTH, $"MP-{publicId}");
        if (tagged != preferred && !await SkuTakenAsync(storeId, tagged, ct))
            return tagged;

        var mp = StringClip.ClipRequired($"MP-{publicId}", Product.PRODUCT_SKU_MAX_LENGTH, publicId);
        if (!await SkuTakenAsync(storeId, mp, ct))
            return mp;

        for (var n = 2; n < 1000; n++)
        {
            var suffix = $"-{n}";
            var maxStem = Product.PRODUCT_SKU_MAX_LENGTH - suffix.Length;
            if (maxStem < 1)
                break;
            var stem = mp.Length <= maxStem ? mp : mp[..maxStem];
            var candidate = stem + suffix;
            if (!await SkuTakenAsync(storeId, candidate, ct))
                return candidate;
        }

        return mp;
    }

    private Task<bool> SkuTakenAsync(int storeId, string sku, CancellationToken ct)
        => _context.products.AnyAsync(
            p => p.storeId == storeId && p.sku == sku && p.deletedAt == null, ct);

    /// <summary>
    /// When the product card has no selling price, load it from the marketplace prices API.
    /// </summary>
    internal async Task<ExternalProduct> WithImportedPriceAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        ExternalProduct product,
        CancellationToken ct)
    {
        var filled = await WithImportedPricesAsync(adapter, connection, [product], ct);
        return filled[0];
    }

    internal async Task<List<ExternalProduct>> WithImportedPricesAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        IReadOnlyList<ExternalProduct> products,
        CancellationToken ct)
    {
        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportPrices))
            return products as List<ExternalProduct> ?? products.ToList();

        return await MarketplaceImportPrice.FillMissingAsync(
            products,
            (ids, token) => adapter.ImportPricesAsync(connection, ids, token),
            ct);
    }

    /// <summary>
    /// Import product attributes based on category field mappings.
    /// </summary>
    internal async Task<int> ImportProductAttributesAsync(
        int productId,
        int connectionId,
        int categoryId,
        Dictionary<string, object> externalAttributes,
        CancellationToken ct,
        int? authorUserId = null,
        string? mediaBatchId = null,
        IReadOnlyList<ExternalMedia>? externalMedia = null)
    {
        // Check whether the product has a selected mapping
        var existingLink = await _context.productMarketplaceLinks
            .FirstOrDefaultAsync(l => l.productId == productId && l.connectionId == connectionId, ct);

        CategoryMapping? categoryMapping = null;
        if (existingLink?.categoryMappingId != null)
        {
            var saved = await _context.categoryMappings
                .FirstOrDefaultAsync(cm => cm.Id == existingLink.categoryMappingId.Value, ct);
            if (saved != null && saved.localCategoryId == categoryId)
                categoryMapping = saved;
        }

        if (categoryMapping == null)
        {
            var matches = await _context.categoryMappings
                .Where(cm =>
                    cm.connectionId == connectionId &&
                    cm.localCategoryId == categoryId)
                .ToListAsync(ct);
            if (matches.Count == 1)
                categoryMapping = matches[0];
        }

        if (categoryMapping == null)
            return 0;

        // Import is graph-native: project the node graph to per-field import descriptors.
        var enabledMappings = await _mappingService.ProjectGraphForImportAsync(categoryMapping.graphJson, ct);
        var byKey = new Dictionary<string, object>(externalAttributes, StringComparer.OrdinalIgnoreCase);

        var (categoryAttributeSpecs, storeAttributeSpecs) =
            await LoadAttributeSpecsAsync(categoryId, noTracking: true, ct);

        var existingProductAttrs = await _context.productAttributeValues
            .Include(value => value.files)
            .Where(value => value.productId == productId)
            .ToListAsync(ct);
        var existingStoreAttrs = await _context.storeProductAttributeValues
            .Include(value => value.files)
            .Where(value => value.productId == productId)
            .ToListAsync(ct);
        var pendingRows = authorUserId.HasValue
            ? await _context.pendingImageDownloads
                .Where(row => row.productId == productId && row.status == ImageDownloadStatus.Pending)
                .ToListAsync(ct)
            : [];
        var productStoreId = authorUserId.HasValue
            ? await _context.products.Where(product => product.Id == productId)
                .Select(product => product.storeId).FirstOrDefaultAsync(ct)
            : 0;
        var importedCount = 0;
        var dirty = false;

        foreach (var fieldMapping in enabledMappings)
        {
            if (!ImportedAttributeValue.TryGet(
                    byKey, fieldMapping.externalFieldKey, fieldMapping.externalFieldName, out var externalValue))
                continue;

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                    if (!categoryAttributeSpecs.TryGetValue(fieldMapping.sourceAttributeId.Value, out var categorySpec))
                        continue;

                    var existing = existingProductAttrs.FirstOrDefault(
                        value => value.attributeId == fieldMapping.sourceAttributeId.Value);
                    if (categorySpec.Type == AttributeType.File)
                    {
                        if (existing?.files.Count > 0)
                            continue;
                        if (existing != null && existing.value.Length > 0)
                        {
                            existing.value = string.Empty;
                            dirty = true;
                        }
                        if (authorUserId.HasValue && QueueFileAttributeUrls(
                                productId,
                                productStoreId,
                                authorUserId.Value,
                                mediaBatchId,
                                categoryAttributeId: fieldMapping.sourceAttributeId.Value,
                                storeAttributeId: null,
                                categorySpec,
                                externalValue,
                                externalMedia,
                                pendingRows))
                        {
                            importedCount++;
                            dirty = true;
                        }
                        continue;
                    }

                    var productValue = FormatImportedAttribute(
                        externalValue, categorySpec, fieldMapping.modifiersJson);
                    if (string.IsNullOrEmpty(productValue))
                        continue;

                    var productAttrValue = new ProductAttributeValue();
                    productAttrValue.AddFill(new ProductAttributeValue.AddForm
                    {
                        productId = productId,
                        attributeId = fieldMapping.sourceAttributeId.Value,
                        value = productValue
                    });
                    _context.productAttributeValues.Add(productAttrValue);
                    importedCount++;
                    dirty = true;
                    break;

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                    if (!storeAttributeSpecs.TryGetValue(fieldMapping.sourceAttributeId.Value, out var storeSpec))
                        continue;

                    var existingStore = existingStoreAttrs.FirstOrDefault(
                        value => value.storeAttributeId == fieldMapping.sourceAttributeId.Value);
                    if (storeSpec.Type == AttributeType.File)
                    {
                        if (existingStore?.files.Count > 0)
                            continue;
                        if (existingStore != null && existingStore.value.Length > 0)
                        {
                            existingStore.value = string.Empty;
                            dirty = true;
                        }
                        if (authorUserId.HasValue && QueueFileAttributeUrls(
                                productId,
                                productStoreId,
                                authorUserId.Value,
                                mediaBatchId,
                                categoryAttributeId: null,
                                storeAttributeId: fieldMapping.sourceAttributeId.Value,
                                storeSpec,
                                externalValue,
                                externalMedia,
                                pendingRows))
                        {
                            importedCount++;
                            dirty = true;
                        }
                        continue;
                    }

                    var storeValue = FormatImportedAttribute(
                        externalValue, storeSpec, fieldMapping.modifiersJson);
                    if (string.IsNullOrEmpty(storeValue))
                        continue;

                    var storeAttrValue = new StoreProductAttributeValue();
                    storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                    {
                        productId = productId,
                        storeAttributeId = fieldMapping.sourceAttributeId.Value,
                        value = storeValue
                    });
                    _context.storeProductAttributeValues.Add(storeAttrValue);
                    importedCount++;
                    dirty = true;
                    break;
            }
        }

        if (dirty)
            await _context.SaveChangesAsync(ct);

        return importedCount;
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
        HashSet<string>? excludeFieldNames = null,
        int? authorUserId = null,
        string? mediaBatchId = null,
        IReadOnlyList<ExternalMedia>? externalMedia = null)
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
        var enabledMappings = await _mappingService.ProjectGraphForImportAsync(categoryMapping.graphJson, ct);

        if (enabledMappings.Count == 0)
        {
            _logger.LogWarning(
                "No enabled field mappings for categoryMappingId={MappingId} - attributes will not be updated",
                categoryMapping.Id);
        }

        // Pre-load existing attribute values for this product
        var existingProductAttrs = await _context.productAttributeValues
            .Include(value => value.files)
            .Where(pav => pav.productId == productId)
            .ToListAsync(ct);

        var existingStoreAttrs = await _context.storeProductAttributeValues
            .Include(value => value.files)
            .Where(sav => sav.productId == productId)
            .ToListAsync(ct);

        var (categoryAttributeSpecs, storeAttributeSpecs) =
            await LoadAttributeSpecsAsync(categoryId, noTracking: false, ct);

        var byKey = new Dictionary<string, object>(externalAttributes, StringComparer.OrdinalIgnoreCase);
        var pendingRows = authorUserId.HasValue
            ? await _context.pendingImageDownloads
                .Where(row => row.productId == productId && row.status == ImageDownloadStatus.Pending)
                .ToListAsync(ct)
            : [];
        var productStoreId = authorUserId.HasValue
            ? await _context.products.Where(item => item.Id == productId)
                .Select(item => item.storeId).FirstOrDefaultAsync(ct)
            : 0;

        _logger.LogDebug(
            "Updating attributes: productId={ProductId}, enabledMappings={Count}, externalKeys=[{Keys}]",
            productId, enabledMappings.Count,
            string.Join(", ", byKey.Keys.Take(20)));

        var dirty = false;

        foreach (var fieldMapping in enabledMappings)
        {
            if (!ImportedAttributeValue.TryGet(
                    byKey, fieldMapping.externalFieldKey, fieldMapping.externalFieldName, out var externalValue))
            {
                _logger.LogDebug(
                    "  Mapping '{FieldName}' (key={Key}): key not found in external attributes, sourceType={SourceType}",
                    fieldMapping.externalFieldName, fieldMapping.externalFieldKey, fieldMapping.sourceType);
                continue;
            }

            var fieldName = fieldMapping.externalFieldName ?? fieldMapping.externalFieldKey;

            if (excludeFieldNames?.Contains(fieldName) == true)
                continue;

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!categoryAttributeSpecs.TryGetValue(fieldMapping.sourceAttributeId.Value, out var categorySpec))
                    {
                        _logger.LogWarning(
                            "  Mapping '{FieldName}': sourceAttributeId={AttrId} not found in attributes of category {CategoryId}",
                            fieldName, fieldMapping.sourceAttributeId.Value, categoryId);
                        continue;
                    }

                    var existing = existingProductAttrs.FirstOrDefault(
                        a => a.attributeId == fieldMapping.sourceAttributeId.Value);
                    if (categorySpec.Type == AttributeType.File)
                    {
                        if (existing?.files.Count > 0)
                            break;
                        if (existing != null && existing.value.Length > 0)
                        {
                            existing.value = string.Empty;
                            dirty = true;
                        }
                        if (authorUserId.HasValue && QueueFileAttributeUrls(
                                productId,
                                productStoreId,
                                authorUserId.Value,
                                mediaBatchId,
                                categoryAttributeId: fieldMapping.sourceAttributeId.Value,
                                storeAttributeId: null,
                                categorySpec,
                                externalValue,
                                externalMedia,
                                pendingRows))
                            dirty = true;
                        break;
                    }

                    var stringValue = FormatImportedAttribute(
                        externalValue, categorySpec, fieldMapping.modifiersJson);
                    if (string.IsNullOrEmpty(stringValue))
                        continue;

                    if (existing != null)
                    {
                        if (!ImportedAttributeValue.AreEquivalent(
                                existing.value, stringValue, categorySpec.Type, categorySpec.Options))
                        {
                            changes.Add(PreviewChange(fieldName, existing.value, stringValue, categorySpec));
                        }

                        if (existing.value != stringValue)
                        {
                            existing.value = stringValue;
                            dirty = true;
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
                        changes.Add(PreviewChange(fieldName, null, stringValue, categorySpec));
                        dirty = true;
                    }
                    break;
                }

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!storeAttributeSpecs.TryGetValue(fieldMapping.sourceAttributeId.Value, out var storeSpec))
                        continue;

                    var existing = existingStoreAttrs.FirstOrDefault(
                        a => a.storeAttributeId == fieldMapping.sourceAttributeId.Value);
                    if (storeSpec.Type == AttributeType.File)
                    {
                        if (existing?.files.Count > 0)
                            break;
                        if (existing != null && existing.value.Length > 0)
                        {
                            existing.value = string.Empty;
                            dirty = true;
                        }
                        if (authorUserId.HasValue && QueueFileAttributeUrls(
                                productId,
                                productStoreId,
                                authorUserId.Value,
                                mediaBatchId,
                                categoryAttributeId: null,
                                storeAttributeId: fieldMapping.sourceAttributeId.Value,
                                storeSpec,
                                externalValue,
                                externalMedia,
                                pendingRows))
                            dirty = true;
                        break;
                    }

                    var stringValue = FormatImportedAttribute(
                        externalValue, storeSpec, fieldMapping.modifiersJson);
                    if (string.IsNullOrEmpty(stringValue))
                        continue;

                    if (existing != null)
                    {
                        if (!ImportedAttributeValue.AreEquivalent(
                                existing.value, stringValue, storeSpec.Type, storeSpec.Options))
                        {
                            changes.Add(PreviewChange(fieldName, existing.value, stringValue, storeSpec));
                        }

                        if (existing.value != stringValue)
                        {
                            existing.value = stringValue;
                            dirty = true;
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
                        changes.Add(PreviewChange(fieldName, null, stringValue, storeSpec));
                        dirty = true;
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

        if (dirty)
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
        var enabledMappings = await _mappingService.ProjectGraphForImportAsync(categoryMapping.graphJson, ct);

        var existingProductAttrs = await _context.productAttributeValues
            .AsNoTracking()
            .Include(pav => pav.files)
            .Where(pav => pav.productId == productId)
            .ToListAsync(ct);

        var existingStoreAttrs = await _context.storeProductAttributeValues
            .AsNoTracking()
            .Include(sav => sav.files)
            .Where(sav => sav.productId == productId)
            .ToListAsync(ct);

        var (categoryAttributeSpecs, storeAttributeSpecs) =
            await LoadAttributeSpecsAsync(categoryId, noTracking: true, ct);

        var byKey = new Dictionary<string, object>(externalAttributes, StringComparer.OrdinalIgnoreCase);

        foreach (var fieldMapping in enabledMappings)
        {
            if (!ImportedAttributeValue.TryGet(
                    byKey, fieldMapping.externalFieldKey, fieldMapping.externalFieldName, out var externalValue))
                continue;

            var fieldName = fieldMapping.externalFieldName ?? fieldMapping.externalFieldKey;
            var hasIrreversible = ModifierPipeline.HasIrreversibleModifiers(fieldMapping.modifiersJson);

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!categoryAttributeSpecs.TryGetValue(fieldMapping.sourceAttributeId.Value, out var categorySpec))
                        continue;
                    if (categorySpec.Type == AttributeType.File)
                    {
                        var existingFileValue = existingProductAttrs.FirstOrDefault(
                            a => a.attributeId == fieldMapping.sourceAttributeId.Value);
                        if (existingFileValue?.files.Count > 0)
                            break;
                        var urls = GetImportedFileUrls(externalValue, categorySpec);
                        if (urls.Count > 0)
                        {
                            result.Add(new AttributeChangeDetail(
                                fieldName, null, string.Join("\n", urls)));
                        }
                        continue;
                    }
                    var stringValue = FormatImportedAttribute(
                        externalValue, categorySpec, fieldMapping.modifiersJson);
                    if (string.IsNullOrEmpty(stringValue))
                        continue;
                    var existing = existingProductAttrs.FirstOrDefault(
                        a => a.attributeId == fieldMapping.sourceAttributeId.Value);
                    if (existing == null ||
                        !ImportedAttributeValue.AreEquivalent(
                            existing.value, stringValue, categorySpec.Type, categorySpec.Options))
                    {
                        result.Add(PreviewChange(
                            fieldName, existing?.value, stringValue, categorySpec, hasIrreversible));
                    }
                    break;
                }

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!storeAttributeSpecs.TryGetValue(fieldMapping.sourceAttributeId.Value, out var storeSpec))
                        continue;
                    if (storeSpec.Type == AttributeType.File)
                    {
                        var existingFileValue = existingStoreAttrs.FirstOrDefault(
                            a => a.storeAttributeId == fieldMapping.sourceAttributeId.Value);
                        if (existingFileValue?.files.Count > 0)
                            break;
                        var urls = GetImportedFileUrls(externalValue, storeSpec);
                        if (urls.Count > 0)
                        {
                            result.Add(new AttributeChangeDetail(
                                fieldName, null, string.Join("\n", urls)));
                        }
                        continue;
                    }
                    var stringValue = FormatImportedAttribute(
                        externalValue, storeSpec, fieldMapping.modifiersJson);
                    if (string.IsNullOrEmpty(stringValue))
                        continue;
                    var existing = existingStoreAttrs.FirstOrDefault(
                        a => a.storeAttributeId == fieldMapping.sourceAttributeId.Value);
                    if (existing == null ||
                        !ImportedAttributeValue.AreEquivalent(
                            existing.value, stringValue, storeSpec.Type, storeSpec.Options))
                    {
                        result.Add(PreviewChange(
                            fieldName, existing?.value, stringValue, storeSpec, hasIrreversible));
                    }
                    break;
                }
            }
        }

        return result;
    }

    private readonly record struct AttrSpec(
        AttributeType Type,
        string? Options,
        bool AllowMultipleFiles);

    private static readonly HashSet<string> ImportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".webm", ".m4v", ".mkv", ".avi"
    };

    private static List<string> GetImportedFileUrls(object? value, AttrSpec spec)
    {
        var urls = ReadStringValues(value)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Where(item => UrlSafety.TryValidateHttpUrl(item, out _, out _))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return urls.Take(spec.AllowMultipleFiles ? 20 : 1).ToList();
    }

    private static IEnumerable<string> ReadStringValues(object? value)
    {
        if (value is string text)
        {
            yield return text;
            yield break;
        }

        if (value is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String && json.GetString() is string jsonText)
                yield return jsonText;
            else if (json.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in json.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is string itemText)
                        yield return itemText;
                }
            }
            yield break;
        }

        if (value is System.Collections.IEnumerable values)
        {
            foreach (var item in values)
            {
                foreach (var textItem in ReadStringValues(item))
                    yield return textItem;
            }
        }
    }

    private static (PendingMediaKind Kind, string? FileName) GetImportedMediaMetadata(
        string url,
        IReadOnlyList<ExternalMedia>? externalMedia)
    {
        var typedVideo = externalMedia?.FirstOrDefault(media =>
            string.Equals(media.Kind, "video", StringComparison.OrdinalIgnoreCase)
            && ImageSourceUrl.EqualsNormalized(media.Url, url));
        var isVideoExtension = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && ImportedVideoExtensions.Contains(Path.GetExtension(uri.AbsolutePath));
        return (isVideoExtension || typedVideo != null ? PendingMediaKind.Video : PendingMediaKind.Image,
            SafeMediaFileName(typedVideo?.FileName));
    }

    private bool QueueFileAttributeUrls(
        int productId,
        int storeId,
        int authorUserId,
        string? mediaBatchId,
        int? categoryAttributeId,
        int? storeAttributeId,
        AttrSpec spec,
        object externalValue,
        IReadOnlyList<ExternalMedia>? externalMedia,
        List<PendingImageDownload> pendingRows)
    {
        if (storeId <= 0 || (categoryAttributeId.HasValue == storeAttributeId.HasValue))
            return false;

        var changed = false;
        foreach (var url in GetImportedFileUrls(externalValue, spec))
        {
            var matchingRows = pendingRows
                .Where(row => row.status == ImageDownloadStatus.Pending
                    && string.Equals(row.imageUrl.Trim(), url.Trim(), StringComparison.Ordinal))
                .ToList();
            if (matchingRows.Any(row =>
                    row.targetAttributeId == categoryAttributeId
                    && row.targetStoreAttributeId == storeAttributeId))
                continue;

            var unassigned = matchingRows.FirstOrDefault(row =>
                !row.targetAttributeId.HasValue && !row.targetStoreAttributeId.HasValue);
            if (unassigned != null)
            {
                unassigned.targetAttributeId = categoryAttributeId;
                unassigned.targetStoreAttributeId = storeAttributeId;
                changed = true;
                continue;
            }

            var (kind, fileName) = GetImportedMediaMetadata(url, externalMedia);
            var nextIndex = pendingRows.Count == 0 ? 0 : pendingRows.Max(row => row.imageIndex) + 1;
            var pending = new PendingImageDownload
            {
                productId = productId,
                storeId = storeId,
                authorUserId = authorUserId,
                imageUrl = url,
                mediaKind = kind,
                mediaFileName = fileName,
                imageIndex = nextIndex,
                targetAttributeId = categoryAttributeId,
                targetStoreAttributeId = storeAttributeId,
                skipGallery = true,
                batchId = mediaBatchId
            };
            _context.pendingImageDownloads.Add(pending);
            pendingRows.Add(pending);
            changed = true;
        }

        return changed;
    }

    private async Task<(Dictionary<int, AttrSpec> Category, Dictionary<int, AttrSpec> Store)> LoadAttributeSpecsAsync(
        int categoryId,
        bool noTracking,
        CancellationToken ct)
    {
        IQueryable<CategoryAttribute> categoryQuery = _context.categoryAttributes
            .Where(ca => ca.categoryId == categoryId);
        IQueryable<StoreAttribute> storeQuery = _context.storeAttributes;
        if (noTracking)
        {
            categoryQuery = categoryQuery.AsNoTracking();
            storeQuery = storeQuery.AsNoTracking();
        }

        var category = await categoryQuery.ToDictionaryAsync(
            ca => ca.Id, ca => new AttrSpec(ca.type, ca.options, ca.allowMultipleFiles), ct);
        var store = await storeQuery.ToDictionaryAsync(
            sa => sa.Id, sa => new AttrSpec(sa.type, sa.options, sa.allowMultipleFiles), ct);
        return (category, store);
    }

    private static AttributeChangeDetail PreviewChange(
        string fieldName,
        string? oldStored,
        string newStored,
        AttrSpec spec,
        bool hasIrreversible = false)
    {
        return new AttributeChangeDetail(
            fieldName,
            ImportedAttributeValue.ToDisplayString(oldStored, spec.Type, spec.Options),
            ImportedAttributeValue.ToDisplayString(newStored, spec.Type, spec.Options),
            hasIrreversible);
    }

    /// <summary>
    /// Convert a marketplace attribute value to the invariant string stored on the product.
    /// </summary>
    private static string? FormatImportedAttribute(
        object? value,
        AttrSpec spec,
        string? modifiersJson)
        => FormatImportedAttribute(value, spec.Type, modifiersJson, spec.Options);

    private static string? FormatImportedAttribute(
        object? value,
        AttributeType type,
        string? modifiersJson,
        string? optionsJson = null)
    {
        var stored = ImportedAttributeValue.ToStoredString(value, type, optionsJson);
        if (string.IsNullOrEmpty(stored))
            return null;

        if (string.IsNullOrWhiteSpace(modifiersJson))
            return stored;

        var reversed = ModifierPipeline.ApplyModifiersReverse(stored, modifiersJson);
        if (reversed == null)
            return stored;

        if (reversed is string reversedText && reversedText == stored)
            return stored;

        return ImportedAttributeValue.ToStoredString(reversed, type, optionsJson)
               ?? reversed.ToString();
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
    /// Verify that linked remote products still exist. Marks missing ones as <see cref="ProductMarketplaceStatus.RemoteMissing"/>.
    /// </summary>
    public async Task<LinkVerifyResult> VerifyLinksAsync(
        int connectionId,
        int? linkId = null,
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (connection == null)
            throw new InvalidOperationException($"Connection with ID {connectionId} not found");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw new InvalidOperationException($"Adapter '{connection.adapterKey}' not found");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            throw new InvalidOperationException(
                "Adapter does not support product import — cannot verify remote product existence");

        var query = _context.productMarketplaceLinks
            .Where(l => l.connectionId == connectionId);

        if (linkId.HasValue)
            query = query.Where(l => l.Id == linkId.Value);

        var links = await query.ToListAsync(ct);
        var checkable = links
            .Where(l => !string.IsNullOrWhiteSpace(l.externalProductId) &&
                        !l.externalProductId.StartsWith("pending:", StringComparison.Ordinal))
            .ToList();

        if (checkable.Count == 0)
        {
            return new LinkVerifyResult(0, 0, 0, 0);
        }

        // A whole connection is one catalog list compared with the saved external ids.
        // A single link stays a direct lookup so one row does not download the shop.
        HashSet<string> foundIds;
        if (!linkId.HasValue &&
            adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportAllProducts))
        {
            var listed = await ListRemoteExternalIdsAsync(adapter, connection, ct);
            foundIds = listed.Ids;
            if (!listed.Complete)
            {
                var remaining = checkable
                    .Where(l => !foundIds.Contains(l.externalProductId))
                    .ToList();
                if (remaining.Count > 0)
                {
                    foreach (var id in await LookupExternalIdsAsync(adapter, connection, remaining, ct))
                        foundIds.Add(id);
                }
            }
        }
        else
        {
            foundIds = await LookupExternalIdsAsync(adapter, connection, checkable, ct);
        }

        var confirmed = 0;
        var broken = 0;
        var recovered = 0;
        var now = DateTime.UtcNow;

        foreach (var link in checkable)
        {
            if (foundIds.Contains(link.externalProductId))
            {
                confirmed++;
                if (link.status == ProductMarketplaceStatus.RemoteMissing)
                {
                    link.status = ProductMarketplaceStatus.Synced;
                    link.lastErrorMessage = null;
                    link.lastSyncAt = now;
                    recovered++;
                }
            }
            else
            {
                broken++;
                MarkLinkRemoteMissing(
                    link,
                    $"Product '{link.externalProductId}' not found on the marketplace");
            }
        }

        await _context.SaveChangesAsync(ct);

        return new LinkVerifyResult(checkable.Count, confirmed, broken, recovered);
    }

    private static async Task<HashSet<string>> LookupExternalIdsAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        List<ProductMarketplaceLink> links,
        CancellationToken ct)
    {
        var foundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int batchSize = 50;
        for (var i = 0; i < links.Count; i += batchSize)
        {
            var chunk = links.Skip(i).Take(batchSize).Select(l => l.externalProductId).Distinct().ToList();
            var found = await adapter.ImportProductsBatchAsync(connection, chunk, ct);
            foreach (var product in found)
            {
                if (!string.IsNullOrWhiteSpace(product.ExternalId))
                    foundIds.Add(product.ExternalId);
            }
        }

        return foundIds;
    }

    /// <summary>
    /// One pass over the remote catalog. An incomplete list must fail the check:
    /// a missing page would mark live links as gone.
    /// </summary>
    private async Task<(HashSet<string> Ids, bool Complete)> ListRemoteExternalIdsAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        CancellationToken ct)
    {
        var foundIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        const int pageSize = 1000;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            PagedResult<ExternalProduct> page;
            try
            {
                page = await adapter.ImportAllProductsAsync(connection, pageSize, cursor, null, ct);
            }
            catch (Exception ex) when (IsCatalogListingFault(ex))
            {
                // A later page can 500 (Yandex Market does this on one page token)
                // after earlier pages succeeded. Keep those ids and check the
                // saved links that were not in them with a direct lookup.
                _logger.LogWarning(
                    ex,
                    "Connection {ConnectionId} catalog listing stopped early after {RemoteCount} ids",
                    connection.Id, foundIds.Count);
                return (foundIds, false);
            }
            foreach (var product in page.Items)
            {
                if (!string.IsNullOrWhiteSpace(product.ExternalId))
                    foundIds.Add(product.ExternalId);
            }

            if (page.Items.Count == 0)
            {
                if (page.HasMore)
                    throw new InvalidOperationException(
                        "Marketplace catalog listing returned an empty page with more results");
                break;
            }

            if (!page.HasMore || string.IsNullOrEmpty(page.NextCursor))
                break;

            if (!seenCursors.Add(page.NextCursor))
                throw new InvalidOperationException("Marketplace catalog listing repeated a page cursor");

            cursor = page.NextCursor;
        }

        _logger.LogInformation(
            "Connection {ConnectionId} link check compared {RemoteCount} remote product ids",
            connection.Id, foundIds.Count);

        return (foundIds, true);
    }

    private static bool IsCatalogListingFault(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? "";
            if (message.Contains("InternalServerError", StringComparison.OrdinalIgnoreCase)
                || message.Contains("INTERNAL_ERROR", StringComparison.OrdinalIgnoreCase)
                || message.Contains("BadGateway", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase)
                || message.Contains("GatewayTimeout", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task MarkLinksRemoteMissingByExternalIdAsync(
        int connectionId,
        string externalProductId,
        string errorMessage,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(externalProductId) ||
            externalProductId.StartsWith("pending:", StringComparison.Ordinal))
            return;

        var links = await _context.productMarketplaceLinks
            .Where(l => l.connectionId == connectionId && l.externalProductId == externalProductId)
            .ToListAsync(ct);

        if (links.Count == 0)
            return;

        foreach (var link in links)
            MarkLinkRemoteMissing(link, errorMessage);

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mark a single link as remote-missing (e.g. after import-from-link returns not found).
    /// </summary>
    public async Task MarkLinkRemoteMissingAsync(
        int linkId,
        string errorMessage,
        CancellationToken ct = default)
    {
        var link = await _context.productMarketplaceLinks.FirstOrDefaultAsync(l => l.Id == linkId, ct);
        if (link == null)
            return;

        MarkLinkRemoteMissing(link, errorMessage);
        await _context.SaveChangesAsync(ct);
    }

    private static void MarkLinkRemoteMissing(ProductMarketplaceLink link, string errorMessage)
    {
        link.UpdateFill(new ProductMarketplaceLink.UpdateForm
        {
            status = ProductMarketplaceStatus.RemoteMissing,
            lastErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage,
            lastSyncAt = DateTime.UtcNow
        });
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
        await QueueImportImagesAsync(
            productId, storeId, authorUserId, imageUrls, imageUrls, imageBatchId, ct);
    }

    private async Task QueueImportedMediaAsync(
        int productId,
        int storeId,
        int authorUserId,
        ExternalProduct externalProduct,
        CancellationToken ct,
        string? batchId,
        Func<string, string, Task> log)
    {
        var imageUrls = new List<string>();
        var seenImages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in (externalProduct.ImageUrls ?? [])
            .Concat((externalProduct.Media ?? [])
                .Where(m => string.Equals(m.Kind, "image", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Url)))
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            var key = ImageSourceUrl.Normalize(url) ?? url.Trim();
            if (seenImages.Add(key)) imageUrls.Add(url.Trim());
        }

        if (imageUrls.Count > 0)
        {
            await log("info", $"Queuing {imageUrls.Count} photos for download...");
            await ImportProductImagesAsync(productId, storeId, authorUserId, imageUrls, ct, batchId);
            await log("info", "Photos queued");
        }

        var videos = new List<Delegat.Marketplace.SDK.DTOs.ExternalMedia>();
        var seenVideos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var media in externalProduct.Media ?? [])
        {
            if (!string.Equals(media.Kind, "video", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(media.Url))
                continue;
            if (!UrlSafety.TryValidateHttpUrl(media.Url, out _, out var error))
            {
                await log("warning", $"Skipped a marketplace video with an unsafe or invalid URL ({error}).");
                continue;
            }
            var key = ImageSourceUrl.Normalize(media.Url) ?? media.Url.Trim();
            if (seenVideos.Add(key)) videos.Add(media);
        }

        if (videos.Count == 0) return;
        if (videos.Count > 100)
        {
            await log("warning", "The marketplace product exposed more than 100 videos; only the first 100 are queued.");
            videos = videos.Take(100).ToList();
        }

        var state = await LoadImageImportStateAsync(productId, ct);
        var nextIndex = await NextGalleryIndexAsync(productId, ct);
        var queued = 0;
        foreach (var media in videos)
        {
            var key = ImageSourceUrl.Normalize(media.Url);
            if (key != null && state.QueuedKeys.Contains(key)) continue;

            _context.pendingImageDownloads.Add(new PendingImageDownload
            {
                productId = productId,
                storeId = storeId,
                authorUserId = authorUserId,
                imageUrl = media.Url,
                imageIndex = nextIndex++,
                mediaKind = PendingMediaKind.Video,
                mediaFileName = SafeMediaFileName(media.FileName),
                status = ImageDownloadStatus.Pending,
                createdAt = DateTime.UtcNow,
                batchId = batchId
            });
            if (key != null) state.QueuedKeys.Add(key);
            queued++;
        }

        if (queued > 0)
        {
            await _context.SaveChangesAsync(ct);
            await log("info", $"Queued {queued} directly hosted videos for download.");
        }
    }

    private static string? SafeMediaFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var name = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(name)) return null;
        return name.Length <= 255 ? name : name[..255];
    }

    /// <summary>
    /// Preview marketplace photo URLs against the local product (no downloads).
    /// </summary>
    public async Task<ImportImagesPreview> PreviewImportImagesAsync(
        int productId,
        IReadOnlyList<string>? remoteUrls,
        CancellationToken ct = default,
        IReadOnlyList<ExternalMedia>? remoteMedia = null)
    {
        var galleryCount = await _context.images.CountAsync(i => i.productId == productId, ct);
        var state = await LoadImageImportStateAsync(productId, ct);
        var items = new List<ImportImagePreviewItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var media in CollectImportMedia(remoteUrls, remoteMedia))
        {
            var raw = media.Url;
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var trimmed = raw.Trim();
            var key = ImageSourceUrl.Normalize(trimmed) ?? trimmed;
            if (!seen.Add(key))
                continue;

            items.Add(new ImportImagePreviewItem(trimmed, ClassifyImportImage(state, trimmed), media.Kind, media.FileName));
        }

        return new ImportImagesPreview(items, galleryCount);
    }

    /// <summary>
    /// Queue selected marketplace photo URLs for append-only download.
    /// Selected URLs must match the adapter allowlist (normalized). URLs already
    /// in flight are skipped; already-imported URLs may be queued again as extra
    /// copies. Never replaces existing gallery slots.
    /// </summary>
    public async Task<ImportImagesQueueResult> QueueImportImagesAsync(
        int productId,
        int storeId,
        int authorUserId,
        IReadOnlyList<string>? selectedUrls,
        IReadOnlyList<string>? allowlist,
        string? batchId = null,
        CancellationToken ct = default,
        IReadOnlyList<ExternalMedia>? remoteMedia = null)
    {
        var selected = (selectedUrls ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToList();

        var allowed = CollectImportMedia(allowlist, remoteMedia);

        var canonical = new List<ExternalMedia>();
        foreach (var url in selected)
        {
            var match = allowed.FirstOrDefault(a => ImageSourceUrl.EqualsNormalized(a.Url, url));
            if (match == null)
            {
                throw ApiException.BadRequest(
                    "Media URL is not from the marketplace product",
                    code: "image_url_not_from_adapter");
            }

            if (!canonical.Any(c => ImageSourceUrl.EqualsNormalized(c.Url, match.Url)))
                canonical.Add(match);
        }

        var state = await LoadImageImportStateAsync(productId, ct);
        var nextIndex = await NextGalleryIndexAsync(productId, ct);
        var queued = 0;
        var skipped = 0;

        foreach (var media in canonical)
        {
            var url = media.Url;
            if (ClassifyImportImage(state, url) == ImportImageStatuses.Queued)
            {
                skipped++;
                continue;
            }

            _context.pendingImageDownloads.Add(new PendingImageDownload
            {
                productId = productId,
                storeId = storeId,
                authorUserId = authorUserId,
                imageUrl = url,
                imageIndex = nextIndex++,
                mediaKind = media.Kind == "video" ? PendingMediaKind.Video : PendingMediaKind.Image,
                mediaFileName = SafeMediaFileName(media.FileName),
                status = ImageDownloadStatus.Pending,
                createdAt = DateTime.UtcNow,
                batchId = batchId
            });

            var key = ImageSourceUrl.Normalize(url);
            if (key != null)
                state.QueuedKeys.Add(key);
            queued++;
        }

        if (queued > 0)
            await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Queued {Count} media files for download for product {ProductId} (skipped {Skipped})",
            queued, productId, skipped);

        return new ImportImagesQueueResult(queued, skipped);
    }

    private static List<ExternalMedia> CollectImportMedia(
        IReadOnlyList<string>? imageUrls, IReadOnlyList<ExternalMedia>? media)
    {
        var result = new List<ExternalMedia>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Prefer the adapter's explicit type when a URL appears in both projections.
        foreach (var item in (media ?? []).Concat((imageUrls ?? [])
            .Select((url, index) => new ExternalMedia("image", url, index))))
        {
            var kind = item.Kind?.ToLowerInvariant();
            if (kind is not ("image" or "video") || string.IsNullOrWhiteSpace(item.Url)) continue;
            var url = item.Url.Trim();
            if (seen.Add(ImageSourceUrl.Normalize(url) ?? url))
                result.Add(item with { Kind = kind, Url = url });
        }
        return result;
    }

    private async Task<ImageImportState> LoadImageImportStateAsync(int productId, CancellationToken ct)
    {
        var state = new ImageImportState();

        var sourceUrls = await _context.files.AsNoTracking()
            .Where(f => f.productId == productId && f.sourceUrl != null)
            .Select(f => f.sourceUrl!)
            .ToListAsync(ct);

        foreach (var sourceUrl in sourceUrls)
        {
            var key = ImageSourceUrl.Normalize(sourceUrl);
            if (key != null)
                state.ImportedKeys.Add(key);
        }

        var pending = await _context.pendingImageDownloads.AsNoTracking()
            .Where(p => p.productId == productId)
            .Select(p => new { p.imageUrl, p.status, p.retryCount })
            .ToListAsync(ct);

        foreach (var row in pending)
        {
            var key = ImageSourceUrl.Normalize(row.imageUrl);
            if (key == null)
                continue;

            if (row.status == ImageDownloadStatus.Completed)
                state.ImportedKeys.Add(key);
            else if (IsActiveImageDownload(row.status, row.retryCount))
                state.QueuedKeys.Add(key);
        }

        return state;
    }

    private async Task<int> NextGalleryIndexAsync(int productId, CancellationToken ct)
    {
        var maxGallery = await _context.images
            .Where(i => i.productId == productId)
            .Select(i => (int?)i.index)
            .MaxAsync(ct) ?? -1;

        var maxPending = await _context.pendingImageDownloads
            .Where(p => p.productId == productId && (
                p.status == ImageDownloadStatus.Pending
                || p.status == ImageDownloadStatus.InProgress
                || (p.status == ImageDownloadStatus.Failed
                    && p.retryCount < PendingImageDownload.MAX_RETRIES)))
            .Select(p => (int?)p.imageIndex)
            .MaxAsync(ct) ?? -1;

        return Math.Max(maxGallery, maxPending) + 1;
    }

    private static string ClassifyImportImage(ImageImportState state, string url)
    {
        var key = ImageSourceUrl.Normalize(url);
        if (key != null && state.QueuedKeys.Contains(key))
            return ImportImageStatuses.Queued;
        if (key != null && state.ImportedKeys.Contains(key))
            return ImportImageStatuses.AlreadyImported;
        return ImportImageStatuses.New;
    }

    internal static bool IsActiveImageDownload(ImageDownloadStatus status, int retryCount) =>
        status is ImageDownloadStatus.Pending or ImageDownloadStatus.InProgress
        || (status == ImageDownloadStatus.Failed && retryCount < PendingImageDownload.MAX_RETRIES);

    private sealed class ImageImportState
    {
        public HashSet<string> ImportedKeys { get; } = new(StringComparer.Ordinal);
        public HashSet<string> QueuedKeys { get; } = new(StringComparer.Ordinal);
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
    /// After a successful export, store marketplace variant ids returned in adapter details.
    /// </summary>
    private async Task ApplyExportedVariantIdsAsync(
        int productId,
        int connectionId,
        Dictionary<string, object>? details,
        CancellationToken ct)
    {
        var exported = ProductVariantExport.ReadExportedVariantIds(details);
        if (exported.Count == 0)
            return;

        var localIds = exported.Keys.ToList();
        var allowed = await _context.productVariants
            .Where(v => v.productId == productId && localIds.Contains(v.Id))
            .Select(v => v.Id)
            .ToListAsync(ct);
        if (allowed.Count == 0)
            return;

        var allowedSet = allowed.ToHashSet();
        var existing = await _context.productVariantLinks
            .Where(l => l.connectionId == connectionId && allowedSet.Contains(l.variantId))
            .ToListAsync(ct);
        var byVariant = existing.ToDictionary(l => l.variantId);

        foreach (var (localId, externalId) in exported)
        {
            if (!allowedSet.Contains(localId))
                continue;

            if (byVariant.TryGetValue(localId, out var link))
            {
                link.externalVariantId = externalId;
                continue;
            }

            _context.productVariantLinks.Add(new ProductVariantLink
            {
                variantId = localId,
                connectionId = connectionId,
                externalVariantId = externalId,
                createdAt = DateTime.UtcNow
            });
        }
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
        CancellationToken ct = default,
        bool createMarketplaceLinks = true)
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

            if (createMarketplaceLinks)
            {
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
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Synced {Count} variants for product {ProductId} on connection {ConnectionId}",
            variants.Count, productId, connectionId);
    }

    private static readonly JsonSerializerOptions ExportDetailsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string FormatImportErrors(ImportResult result)
    {
        var messages = result.Errors?
            .Select(e => string.IsNullOrWhiteSpace(e.ExternalId)
                ? e.Message
                : $"{e.ExternalId}: {e.Message}")
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        return messages is { Count: > 0 }
            ? string.Join("; ", messages)
            : $"{result.FailedCount} item(s) failed";
    }

    private static string? FormatExportDetails(Dictionary<string, object>? details)
    {
        if (details == null || details.Count == 0)
            return null;

        try
        {
            if (details.TryGetValue("images", out var images))
            {
                var json = JsonSerializer.Serialize(images, ExportDetailsJsonOptions);
                return $"Image export details: {json}";
            }

            return $"Export details: {JsonSerializer.Serialize(details, ExportDetailsJsonOptions)}";
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
    public bool Retryable { get; private set; } = true;

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
    public static ProductExportResult Failure(string error, bool retryable = true)
    {
        return new ProductExportResult
        {
            IsSuccess = false,
            ErrorMessage = error,
            Retryable = retryable
        };
    }
}

/// <summary>
/// Status strings for marketplace photo re-import preview.
/// </summary>
public static class ImportImageStatuses
{
    public const string New = "new";
    public const string AlreadyImported = "alreadyImported";
    public const string Queued = "queued";
}

/// <summary>
/// One marketplace photo in a re-import preview.
/// </summary>
public sealed record ImportImagePreviewItem(string Url, string Status, string Kind = "image", string? FileName = null);

/// <summary>
/// Preview of marketplace photos vs the local product gallery.
/// </summary>
public sealed record ImportImagesPreview(
    IReadOnlyList<ImportImagePreviewItem> Images,
    int LocalGalleryCount);

/// <summary>
/// Result of queueing selected marketplace photos for download.
/// </summary>
public sealed record ImportImagesQueueResult(int QueuedCount, int SkippedCount);

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
    public string? ErrorCode { get; private set; }
    public object? ErrorData { get; private set; }

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
    /// Successful copy of a public card with no marketplace link.
    /// </summary>
    public static ProductImportResult SuccessUnlinked(int productId, string productName, string message)
    {
        return new ProductImportResult
        {
            IsSuccess = true,
            ProductId = productId,
            LinkId = null,
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
    public static ProductImportResult Failure(string error, string? code = null, object? data = null)
    {
        return new ProductImportResult
        {
            IsSuccess = false,
            ErrorMessage = error,
            ErrorCode = code,
            ErrorData = data
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
/// Result of verifying marketplace product links against the remote catalog.
/// </summary>
public record LinkVerifyResult(int Checked, int Confirmed, int Broken, int Recovered);

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

/// <summary>Readiness of one catalog product for export to a marketplace.</summary>
public record ProductExportReadinessItem(
    int ProductId,
    string? ProductName,
    bool IsReady,
    IReadOnlyList<string> Issues);

/// <summary>
/// Per-product result in a bulk export.
/// </summary>
public record BulkProductExportItemResult(
    int ProductId,
    bool IsSuccess,
    string? ExternalProductId = null,
    string? ErrorMessage = null,
    bool IsNewProduct = false,
    Dictionary<string, object>? Details = null,
    bool Retryable = true
);

#endregion
