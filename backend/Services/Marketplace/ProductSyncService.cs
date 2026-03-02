using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrintO.Enums;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Marketplace.DTOs;
using PrintO.Models.Products;
using PrintO.Services.Marketplace.Modifiers;

namespace PrintO.Services.Marketplace;

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

    /// <summary>
    /// Initializes a new instance of <see cref="ProductSyncService"/>.
    /// </summary>
    public ProductSyncService(
        DataContext context,
        IMarketplaceAdapterRegistry adapterRegistry,
        MappingService mappingService,
        IStorageService storageService,
        ILogger<ProductSyncService> logger)
    {
        _context = context;
        _adapterRegistry = adapterRegistry;
        _mappingService = mappingService;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Export a product to a marketplace via the adapter.
    /// </summary>
    public async Task<ProductExportResult> ExportProductAsync(
        int productId,
        int connectionId,
        CancellationToken ct = default)
    {
        // Загружаем товар со всеми связями
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
            return ProductExportResult.Failure($"Товар с ID {productId} не найден");

        // Загружаем подключение
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return ProductExportResult.Failure($"Подключение с ID {connectionId} не найдено");

        if (!connection.isActive)
            return ProductExportResult.Failure("Подключение неактивно");

        // Получаем адаптер
        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return ProductExportResult.Failure($"Адаптер '{connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ExportProduct))
            return ProductExportResult.Failure("Адаптер не поддерживает экспорт товаров");

        // Проверяем наличие категории и маппинга
        if (!product.categoryId.HasValue)
            return ProductExportResult.Failure("Товар не привязан к категории");

        var categoryMapping = await _context.categoryMappings
            .Include(cm => cm.fieldMappings)
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == product.categoryId.Value, ct);

        if (categoryMapping == null)
            return ProductExportResult.Failure(
                $"Не найден маппинг категории для подключения. " +
                $"Создайте маппинг для категории '{product.category?.name}'");

        // Проверяем существующую связь товара
        var existingLink = await _context.productMarketplaceLinks
            .FirstOrDefaultAsync(l =>
                l.productId == productId &&
                l.connectionId == connectionId, ct);

        try
        {
            // Строим данные для экспорта
            var exportData = await _mappingService.BuildExportDataAsync(product, categoryMapping, ct);

            // Добавляем externalCategoryId
            exportData["_categoryId"] = categoryMapping.externalCategoryId;

            // Добавляем group_id для field-based группировки,
            // но только если пользователь НЕ настроил маппинг для этого поля
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
                        "Добавлен group_id={GroupId} для товара {ProductId} (поле: {FieldKey})",
                        groupId, productId, fieldGroupingConfig.GroupFieldKey);
                }
            }

            // Валидация обязательных полей перед отправкой
            var requiredMappings = categoryMapping.fieldMappings
                .Where(fm => fm.isRequired && fm.isEnabled)
                .ToList();

            var missingFields = new List<string>();
            foreach (var mapping in requiredMappings)
            {
                if (!exportData.TryGetValue(mapping.externalFieldKey, out var value) ||
                    value == null ||
                    (value is string strValue && string.IsNullOrWhiteSpace(strValue)))
                {
                    var fieldName = mapping.externalFieldName ?? mapping.externalFieldKey;
                    missingFields.Add(fieldName);
                    _logger.LogWarning(
                        "Обязательное поле '{FieldKey}' ({FieldName}) пустое для товара {ProductId}. " +
                        "SourceType={SourceType}, SourceField={SourceField}",
                        mapping.externalFieldKey, fieldName, productId,
                        mapping.sourceType, mapping.sourceField);
                }
            }

            if (missingFields.Count > 0)
            {
                var fieldsStr = string.Join(", ", missingFields);
                return ProductExportResult.Failure(
                    $"Не заполнены обязательные поля: {fieldsStr}. " +
                    $"Проверьте данные товара и настройки маппинга.");
            }

            // Конвертируем в Dictionary<string, object> для совместимости
            var exportDataNonNull = exportData
                .Where(kv => kv.Value != null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!);

            // DEBUG: Log export data before sending to adapter
            Console.WriteLine($"[ProductSyncService] Export data keys: [{string.Join(", ", exportDataNonNull.Keys)}]");
            foreach (var kvp in exportDataNonNull.Take(15))
            {
                var valueStr = kvp.Value?.ToString();
                if (valueStr?.Length > 50) valueStr = valueStr.Substring(0, 50) + "...";
                Console.WriteLine($"[ProductSyncService]   {kvp.Key} = {valueStr}");
            }

            // Экспортируем через адаптер
            var result = await adapter.ExportProductAsync(
                connection,
                exportDataNonNull,
                existingLink?.externalProductId,
                ct);

            if (!result.Success)
            {
                // Обновляем статус связи при ошибке
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

                return ProductExportResult.Failure(result.ErrorMessage ?? "Ошибка экспорта");
            }

            // Валидация ExternalProductId — плагин мог вернуть Success=true, но с невалидным ID
            if (string.IsNullOrWhiteSpace(result.ExternalProductId) ||
                result.ExternalProductId == "0")
            {
                _logger.LogWarning(
                    "Плагин вернул Success=true, но ExternalProductId недействителен: '{ExternalProductId}' для товара {ProductId}",
                    result.ExternalProductId, productId);

                if (existingLink != null)
                {
                    existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                    {
                        status = ProductMarketplaceStatus.SyncError,
                        lastErrorMessage = $"Маркетплейс не вернул ID товара (получено: '{result.ExternalProductId}')",
                        lastSyncAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(ct);
                }

                return ProductExportResult.Failure(
                    $"Маркетплейс не вернул ID товара. Возможно, товар ещё обрабатывается — попробуйте повторить позже.");
            }

            var isNewProduct = existingLink == null;

            // Создаём или обновляем связь
            if (existingLink == null)
            {
                // Проверяем, нет ли уже связи для этого externalProductId + connectionId
                // (уникальный индекс IX_productMarketplaceLinks_externalProductId_connectionId)
                existingLink = await _context.productMarketplaceLinks
                    .FirstOrDefaultAsync(l =>
                        l.externalProductId == result.ExternalProductId &&
                        l.connectionId == connectionId, ct);

                if (existingLink != null)
                {
                    _logger.LogWarning(
                        "Найдена существующая связь (Id={LinkId}) для externalProductId={ExtId} + connectionId={ConnId}, " +
                        "привязана к productId={OldProductId}. Переназначаю на productId={NewProductId}",
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
                    status = ProductMarketplaceStatus.Synced
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
                    lastSyncAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(ct);

            return ProductExportResult.Success(
                existingLink.Id,
                result.ExternalProductId!,
                isNewProduct,
                isNewProduct ? "Товар создан на маркетплейсе" : "Товар обновлён на маркетплейсе"
            );
        }
        catch (Exception ex)
        {
            return ProductExportResult.Failure($"Ошибка экспорта: {ex.Message}");
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
                productIds.Select(id => new BulkProductExportItemResult(id, false, ErrorMessage: "Подключение не найдено")).ToList());

        if (!connection.isActive)
            return new BulkProductExportResult(0, 0, productIds.Count,
                productIds.Select(id => new BulkProductExportItemResult(id, false, ErrorMessage: "Подключение неактивно")).ToList());

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return new BulkProductExportResult(0, 0, productIds.Count,
                productIds.Select(id => new BulkProductExportItemResult(id, false, ErrorMessage: $"Адаптер '{connection.adapterKey}' не найден")).ToList());

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
            .Include(cm => cm.fieldMappings)
            .Where(cm => cm.connectionId == connectionId && categoryIds.Contains(cm.localCategoryId))
            .ToListAsync(ct);
        var mappingByCategoryId = categoryMappings.ToDictionary(cm => cm.localCategoryId, cm => cm);

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

        // Build BulkExportItem list
        var bulkItems = new List<BulkExportItem>();
        var failedProductIds = new HashSet<int>();

        foreach (var productId in productIds)
        {
            if (!productDict.TryGetValue(productId, out var product))
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: $"Товар с ID {productId} не найден"));
                failedProductIds.Add(productId);
                continue;
            }

            if (!product.categoryId.HasValue)
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: "Товар не привязан к категории"));
                failedProductIds.Add(productId);
                continue;
            }

            if (!mappingByCategoryId.TryGetValue(product.categoryId.Value, out var categoryMapping))
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false,
                    ErrorMessage: $"Не найден маппинг категории для '{product.category?.name}'"));
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

                // Validate required fields
                var requiredMappings = categoryMapping.fieldMappings
                    .Where(fm => fm.isRequired && fm.isEnabled)
                    .ToList();

                var missingFields = new List<string>();
                foreach (var mapping in requiredMappings)
                {
                    if (!exportData.TryGetValue(mapping.externalFieldKey, out var value) ||
                        value == null ||
                        (value is string strValue && string.IsNullOrWhiteSpace(strValue)))
                    {
                        missingFields.Add(mapping.externalFieldName ?? mapping.externalFieldKey);
                    }
                }

                if (missingFields.Count > 0)
                {
                    itemResults.Add(new BulkProductExportItemResult(productId, false,
                        ErrorMessage: $"Не заполнены обязательные поля: {string.Join(", ", missingFields)}"));
                    failedProductIds.Add(productId);
                    continue;
                }

                var exportDataNonNull = exportData
                    .Where(kv => kv.Value != null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value!);

                var existingExternalId = linkByProductId.TryGetValue(productId, out var link)
                    ? link.externalProductId
                    : null;

                bulkItems.Add(new BulkExportItem(productId, exportDataNonNull, existingExternalId));
            }
            catch (Exception ex)
            {
                itemResults.Add(new BulkProductExportItemResult(productId, false, ErrorMessage: $"Ошибка подготовки: {ex.Message}"));
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
                                ErrorMessage: exportItemResult.ErrorMessage ?? "Ошибка экспорта"));
                            continue;
                        }

                        // Validate ExternalProductId
                        if (string.IsNullOrWhiteSpace(exportItemResult.ExternalProductId) ||
                            exportItemResult.ExternalProductId == "0")
                        {
                            itemResults.Add(new BulkProductExportItemResult(
                                exportItemResult.LocalProductId, false,
                                ErrorMessage: "Маркетплейс не вернул ID товара"));
                            continue;
                        }

                        // Create/update ProductMarketplaceLink
                        var pid = exportItemResult.LocalProductId;
                        var isNew = !linkByProductId.ContainsKey(pid);

                        if (linkByProductId.TryGetValue(pid, out var existingLink))
                        {
                            existingLink.UpdateFill(new ProductMarketplaceLink.UpdateForm
                            {
                                externalProductId = exportItemResult.ExternalProductId,
                                status = ProductMarketplaceStatus.Synced,
                                lastErrorMessage = null,
                                lastSyncAt = DateTime.UtcNow
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
                                    lastSyncAt = DateTime.UtcNow
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
                                    status = ProductMarketplaceStatus.Synced
                                });
                                _context.productMarketplaceLinks.Add(newLink);
                                linkByProductId[pid] = newLink;
                            }
                        }

                        itemResults.Add(new BulkProductExportItemResult(
                            pid, true,
                            ExternalProductId: exportItemResult.ExternalProductId,
                            IsNewProduct: isNew));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk export failed for connectionId={ConnectionId}", connectionId);
                foreach (var item in bulkItems)
                {
                    if (!failedProductIds.Contains(item.LocalProductId))
                        itemResults.Add(new BulkProductExportItemResult(item.LocalProductId, false, ErrorMessage: $"Ошибка экспорта: {ex.Message}"));
                }
            }

            // One SaveChangesAsync at the end
            await _context.SaveChangesAsync(ct);
        }

        // Post-export: sync groups for EndpointBasedGrouping (e.g., WB merge groups)
        if (adapter.Capabilities.HasFlag(MarketplaceCapabilities.EndpointBasedGrouping))
        {
            await SyncEndpointGroupsAfterExportAsync(adapter, connection, connectionId, productIds, itemResults, ct);
        }

        var successCount = itemResults.Count(r => r.IsSuccess);
        var failedCount = itemResults.Count(r => !r.IsSuccess);
        return new BulkProductExportResult(successCount, failedCount, productIds.Count, itemResults);
    }

    /// <summary>
    /// After bulk export, sync endpoint-based groups (e.g., WB merge groups).
    /// For each product group that has successfully exported members, create or update the group on the marketplace.
    /// </summary>
    private async Task SyncEndpointGroupsAfterExportAsync(
        IMarketplaceAdapter adapter,
        MarketplaceConnection connection,
        int connectionId,
        List<int> exportedProductIds,
        List<BulkProductExportItemResult> itemResults,
        CancellationToken ct)
    {
        try
        {
            // Find all product groups that contain any of the exported products
            var groupMemberships = await _context.Set<ProductGroupMember>()
                .Include(gm => gm.group)
                    .ThenInclude(g => g.members)
                .Where(gm => exportedProductIds.Contains(gm.productId))
                .ToListAsync(ct);

            var groupIds = groupMemberships
                .Select(gm => gm.groupId)
                .Distinct()
                .ToList();

            if (groupIds.Count == 0) return;

            // Load existing group marketplace links for this connection
            var existingGroupLinks = await _context.productGroupMarketplaceLinks
                .Where(gl => gl.connectionId == connectionId && groupIds.Contains(gl.productGroupId))
                .ToDictionaryAsync(gl => gl.productGroupId, ct);

            // Load all product marketplace links for members of these groups
            var allGroupMemberProductIds = groupMemberships
                .SelectMany(gm => gm.group.members.Select(m => m.productId))
                .Distinct()
                .ToList();

            var productLinks = await _context.productMarketplaceLinks
                .Where(l => l.connectionId == connectionId && allGroupMemberProductIds.Contains(l.productId))
                .ToDictionaryAsync(l => l.productId, ct);

            // Process each group
            foreach (var groupId in groupIds)
            {
                var membership = groupMemberships.First(gm => gm.groupId == groupId);
                var group = membership.group;

                // Collect external IDs for all group members that have been exported
                var memberExternalIds = group.members
                    .OrderBy(m => m.sortOrder)
                    .Where(m => productLinks.ContainsKey(m.productId))
                    .Select(m => productLinks[m.productId].externalProductId)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                if (memberExternalIds.Count < 2)
                    continue; // Need at least 2 products for a group

                existingGroupLinks.TryGetValue(groupId, out var groupLink);

                if (groupLink != null && !string.IsNullOrEmpty(groupLink.externalGroupId))
                {
                    // Group already exists — add any new members
                    // The WB API is idempotent for adding existing members, so we can safely add all
                    try
                    {
                        var addResult = await adapter.AddProductsToGroupAsync(
                            connection, groupLink.externalGroupId, memberExternalIds, ct);

                        groupLink.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
                        {
                            status = addResult.Success
                                ? ProductMarketplaceStatus.Synced
                                : ProductMarketplaceStatus.SyncError,
                            lastErrorMessage = addResult.ErrorMessage,
                            lastSyncAt = DateTime.UtcNow
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to update group {GroupId} membership on connection {ConnectionId}",
                            groupId, connectionId);
                    }
                }
                else
                {
                    // Create new group
                    try
                    {
                        var createResult = await adapter.CreateGroupAsync(
                            connection, memberExternalIds, group.name, ct);

                        if (createResult.Success && !string.IsNullOrEmpty(createResult.ExternalGroupId))
                        {
                            if (groupLink == null)
                            {
                                groupLink = new ProductGroupMarketplaceLink();
                                groupLink.AddFill(new ProductGroupMarketplaceLink.AddForm
                                {
                                    productGroupId = groupId,
                                    connectionId = connectionId,
                                    externalGroupId = createResult.ExternalGroupId,
                                    externalGroupName = group.name,
                                    status = ProductMarketplaceStatus.Synced
                                });
                                _context.productGroupMarketplaceLinks.Add(groupLink);
                            }
                            else
                            {
                                groupLink.UpdateFill(new ProductGroupMarketplaceLink.UpdateForm
                                {
                                    externalGroupId = createResult.ExternalGroupId,
                                    externalGroupName = group.name,
                                    status = ProductMarketplaceStatus.Synced,
                                    lastSyncAt = DateTime.UtcNow
                                });
                            }

                            _logger.LogInformation(
                                "Created group {GroupId} on connection {ConnectionId}, externalGroupId={ExternalGroupId}",
                                groupId, connectionId, createResult.ExternalGroupId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Failed to create group {GroupId} on connection {ConnectionId}: {Error}",
                                groupId, connectionId, createResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to create group {GroupId} on connection {ConnectionId}",
                            groupId, connectionId);
                    }
                }
            }

            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Post-export group sync failed for connection {ConnectionId}. Products were exported successfully.",
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
        CancellationToken ct = default)
    {
        var connection = await _context.marketplaceConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, ct);

        if (connection == null)
            return ProductImportResult.Failure("Подключение не найдено");

        var adapter = _adapterRegistry.GetByKey(connection.adapterKey);
        if (adapter == null)
            return ProductImportResult.Failure($"Адаптер '{connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.ImportProduct))
            return ProductImportResult.Failure("Адаптер не поддерживает импорт товаров");

        try
        {
            // Получаем товар с маркетплейса
            var externalProduct = await adapter.ImportProductAsync(connection, externalProductId, ct);
            if (externalProduct == null)
                return ProductImportResult.Failure($"Товар с ID '{externalProductId}' не найден на маркетплейсе");

            _logger.LogInformation(
                "Импортируем товар {ExternalId}: Name={Name}, Description={Description}, Price={Price}, Quantity={Quantity}, Attributes={AttrCount}",
                externalProductId,
                externalProduct.Name,
                externalProduct.Description?.Substring(0, Math.Min(50, externalProduct.Description?.Length ?? 0)) ?? "null",
                externalProduct.Price,
                externalProduct.Quantity,
                externalProduct.Attributes?.Count ?? 0);

            // Проверяем, нет ли уже связи
            var existingLink = await _context.productMarketplaceLinks
                .Include(l => l.product)
                .FirstOrDefaultAsync(l =>
                    l.connectionId == connectionId &&
                    l.externalProductId == externalProductId, ct);

            if (existingLink != null)
            {
                return ProductImportResult.AlreadyExists(
                    existingLink.productId,
                    existingLink.product?.name ?? "Unknown"
                );
            }

            // Создаём локальный товар
            var product = new Product();
            product.AddFill(new Product.AddForm(
                new Product.UserAddForm
                {
                    sku = externalProduct.Sku ?? $"MP-{externalProductId}",
                    name = externalProduct.Name ?? $"Product {externalProductId}",
                    description = externalProduct.Description ?? "",
                    price = externalProduct.Price,
                    storageQuantity = externalProduct.Quantity,
                    categoryId = targetCategoryId
                },
                connection.storeId,
                targetCategoryId
            ));

            _context.products.Add(product);
            await _context.SaveChangesAsync(ct);

            // Импортируем атрибуты на основе маппинга категории
            if (targetCategoryId.HasValue && externalProduct.Attributes?.Count > 0)
            {
                await ImportProductAttributesAsync(
                    product.Id,
                    connectionId,
                    targetCategoryId.Value,
                    externalProduct.Attributes,
                    ct);
            }

            // Импортируем изображения
            if (externalProduct.ImageUrls?.Any() == true && importByUserId.HasValue)
            {
                await ImportProductImagesAsync(
                    product.Id,
                    connection.storeId,
                    importByUserId.Value,
                    externalProduct.ImageUrls,
                    ct);
            }

            // Создаём связь
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

            // Добавляем товар в группу, если GroupId указан
            if (!string.IsNullOrEmpty(externalProduct.GroupId))
            {
                await AddProductToGroupAsync(product.Id, connectionId, connection.storeId, externalProduct.GroupId, externalProduct.GroupName, ct);
            }

            return ProductImportResult.Success(
                product.Id,
                link.Id,
                product.name,
                "Товар успешно импортирован"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка импорта товара {ExternalId}", externalProductId);
            return ProductImportResult.Failure($"Ошибка импорта: {ex.Message}");
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
        // Находим CategoryMapping для этой категории и подключения
        var categoryMapping = await _context.categoryMappings
            .Include(cm => cm.fieldMappings)
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
            return;

        var enabledMappings = categoryMapping.fieldMappings.Where(fm => fm.isEnabled).ToList();

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
            // Проверяем, есть ли значение в external атрибутах
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
            .Include(cm => cm.fieldMappings)
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
        {
            _logger.LogWarning(
                "Нет маппинга категории для connectionId={ConnectionId}, categoryId={CategoryId} — атрибуты не будут обновлены",
                connectionId, categoryId);
            return changes;
        }

        var enabledMappings = categoryMapping.fieldMappings.Where(fm => fm.isEnabled).ToList();

        if (enabledMappings.Count == 0)
        {
            _logger.LogWarning(
                "Нет включённых маппингов полей для categoryMappingId={MappingId} — атрибуты не будут обновлены",
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
            "Обновление атрибутов: productId={ProductId}, enabledMappings={Count}, externalKeys=[{Keys}]",
            productId, enabledMappings.Count,
            string.Join(", ", externalAttributes.Keys.Take(20)));

        foreach (var fieldMapping in enabledMappings)
        {
            if (!externalAttributes.TryGetValue(fieldMapping.externalFieldKey, out var externalValue))
            {
                _logger.LogDebug(
                    "  Маппинг '{FieldName}' (key={Key}): ключ не найден в external атрибутах, sourceType={SourceType}",
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
                            "  Маппинг '{FieldName}': sourceAttributeId={AttrId} не найден в атрибутах категории {CategoryId}",
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
                        "  Маппинг '{FieldName}' (key={Key}): sourceType={SourceType} — пропущен при импорте атрибутов",
                        fieldName, fieldMapping.externalFieldKey, fieldMapping.sourceType);
                    break;
            }
        }

        if (changes.Count > 0)
            await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Обновление атрибутов для productId={ProductId}: {ChangedCount} изменений из {TotalMappings} маппингов",
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
            .Include(cm => cm.fieldMappings)
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
            return result;

        var enabledMappings = categoryMapping.fieldMappings.Where(fm => fm.isEnabled).ToList();

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

        // Если это список, берём первое значение или объединяем через запятую
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
            return ProductExportResult.Failure("Связь товара не найдена");

        var adapter = _adapterRegistry.GetByKey(link.connection.adapterKey);
        if (adapter == null)
            return ProductExportResult.Failure($"Адаптер '{link.connection.adapterKey}' не найден");

        if (!adapter.Capabilities.HasFlag(MarketplaceCapabilities.UpdateSku))
            return ProductExportResult.Failure("Маркетплейс не поддерживает обновление SKU");

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

                return ProductExportResult.Success(link.Id, link.externalProductId, false, "SKU успешно обновлён");
            }

            return ProductExportResult.Failure(result.ErrorMessage ?? "Ошибка обновления SKU");
        }
        catch (NotSupportedException)
        {
            return ProductExportResult.Failure("Маркетплейс не поддерживает обновление SKU");
        }
        catch (Exception ex)
        {
            return ProductExportResult.Failure($"Ошибка: {ex.Message}");
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
        CancellationToken ct = default)
    {
        var imageIndex = 0;

        foreach (var imageUrl in imageUrls)
        {
            // Проверяем, не добавлено ли уже это изображение в очередь
            var exists = await _context.pendingImageDownloads
                .AnyAsync(p => p.productId == productId && p.imageUrl == imageUrl, ct);

            if (exists)
            {
                imageIndex++;
                continue;
            }

            // Добавляем в очередь на фоновую загрузку
            var pending = new PendingImageDownload
            {
                productId = productId,
                storeId = storeId,
                authorUserId = authorUserId,
                imageUrl = imageUrl,
                imageIndex = imageIndex,
                status = ImageDownloadStatus.Pending,
                createdAt = DateTime.UtcNow
            };

            _context.pendingImageDownloads.Add(pending);
            imageIndex++;
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Добавлено {Count} изображений в очередь загрузки для товара {ProductId}",
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
        // Ищем группу, в которой состоит товар, и которая уже связана с маркетплейсом
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
            // Ищем существующую связь группы с маркетплейсом
            var groupLink = await _context.productGroupMarketplaceLinks
                .Include(gl => gl.productGroup)
                    .ThenInclude(g => g.members)
                .FirstOrDefaultAsync(gl =>
                    gl.connectionId == connectionId &&
                    gl.externalGroupId == externalGroupId, ct);

            if (groupLink == null)
            {
                // Название группы: используем externalGroupName если есть, иначе fallback
                var groupName = !string.IsNullOrEmpty(externalGroupName)
                    ? externalGroupName
                    : $"Группа {externalGroupId}";

                var newGroup = new ProductGroup();
                newGroup.AddFill(new ProductGroup.AddForm
                {
                    name = groupName,
                    description = $"Автоматически создана при импорте (externalGroupId: {externalGroupId})",
                    storeId = storeId
                });

                _context.Set<ProductGroup>().Add(newGroup);
                await _context.SaveChangesAsync(ct);

                // Создаём связь группы с маркетплейсом
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
                    "Создана новая группа {GroupId} для externalGroupId={ExternalGroupId}",
                    newGroup.Id, externalGroupId);
            }

            // Проверяем, не добавлен ли товар уже в эту группу
            var isAlreadyMember = groupLink.productGroup.members.Any(m => m.productId == productId);
            if (isAlreadyMember)
            {
                _logger.LogDebug(
                    "Товар {ProductId} уже состоит в группе {GroupId}",
                    productId, groupLink.productGroupId);
                return;
            }

            // Добавляем товар в группу
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
                "Товар {ProductId} добавлен в группу {GroupId} (externalGroupId={ExternalGroupId})",
                productId, groupLink.productGroupId, externalGroupId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Ошибка при добавлении товара {ProductId} в группу с externalGroupId={ExternalGroupId}",
                productId, externalGroupId);
            // Не прерываем импорт из-за ошибки добавления в группу
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
                // Валидируем расширение
                if (extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif")
                    return extension;
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга URL
        }

        return null;
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
            ErrorMessage = $"Товар уже связан с локальным товаром '{productName}'"
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
    bool IsNewProduct = false
);

#endregion
