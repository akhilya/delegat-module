using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PrintO.Enums;
using PrintO.Infrastructure.Storage;
using PrintO.Models;
using PrintO.Models.Marketplace;
using PrintO.Models.Marketplace.DTOs;
using PrintO.Models.Products;

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

            // Добавляем group_id для field-based группировки
            var fieldGroupingConfig = adapter.GetFieldBasedGroupingConfig();
            if (fieldGroupingConfig != null &&
                adapter.Capabilities.HasFlag(MarketplaceCapabilities.FieldBasedGrouping))
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

            var isNewProduct = existingLink == null;

            // Создаём или обновляем связь
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
    /// </summary>
    public async Task<int> UpdateProductAttributesFromExternalAsync(
        int productId,
        int connectionId,
        int categoryId,
        Dictionary<string, object> externalAttributes,
        CancellationToken ct)
    {
        var categoryMapping = await _context.categoryMappings
            .Include(cm => cm.fieldMappings)
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
            return 0;

        var enabledMappings = categoryMapping.fieldMappings.Where(fm => fm.isEnabled).ToList();

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

        var changedCount = 0;

        foreach (var fieldMapping in enabledMappings)
        {
            if (!externalAttributes.TryGetValue(fieldMapping.externalFieldKey, out var externalValue))
                continue;

            var stringValue = ConvertAttributeValueToString(externalValue);
            if (string.IsNullOrEmpty(stringValue))
                continue;

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    if (!categoryAttributeIds.Contains(fieldMapping.sourceAttributeId.Value))
                        continue;

                    var existing = existingProductAttrs.FirstOrDefault(
                        a => a.attributeId == fieldMapping.sourceAttributeId.Value);

                    if (existing != null)
                    {
                        if (existing.value != stringValue)
                        {
                            existing.value = stringValue;
                            changedCount++;
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
                        changedCount++;
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
                            existing.value = stringValue;
                            changedCount++;
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
                        changedCount++;
                    }
                    break;
                }
            }
        }

        if (changedCount > 0)
            await _context.SaveChangesAsync(ct);

        return changedCount;
    }

    /// <summary>
    /// Read-only version: count how many attribute values would change without saving.
    /// </summary>
    public async Task<int> CountAttributeChangesFromExternalAsync(
        int productId,
        int connectionId,
        int categoryId,
        Dictionary<string, object> externalAttributes,
        CancellationToken ct)
    {
        var categoryMapping = await _context.categoryMappings
            .AsNoTracking()
            .Include(cm => cm.fieldMappings)
            .FirstOrDefaultAsync(cm =>
                cm.connectionId == connectionId &&
                cm.localCategoryId == categoryId, ct);

        if (categoryMapping == null)
            return 0;

        var enabledMappings = categoryMapping.fieldMappings.Where(fm => fm.isEnabled).ToList();

        var existingProductAttrs = await _context.productAttributeValues
            .AsNoTracking()
            .Where(pav => pav.productId == productId)
            .ToListAsync(ct);

        var existingStoreAttrs = await _context.storeProductAttributeValues
            .AsNoTracking()
            .Where(sav => sav.productId == productId)
            .ToListAsync(ct);

        var changedCount = 0;

        foreach (var fieldMapping in enabledMappings)
        {
            if (!externalAttributes.TryGetValue(fieldMapping.externalFieldKey, out var externalValue))
                continue;

            var stringValue = ConvertAttributeValueToString(externalValue);
            if (string.IsNullOrEmpty(stringValue))
                continue;

            switch (fieldMapping.sourceType)
            {
                case FieldMappingSourceType.CategoryAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    var existing = existingProductAttrs.FirstOrDefault(
                        a => a.attributeId == fieldMapping.sourceAttributeId.Value);
                    if (existing == null || existing.value != stringValue)
                        changedCount++;
                    break;
                }

                case FieldMappingSourceType.StoreAttribute when fieldMapping.sourceAttributeId.HasValue:
                {
                    var existing = existingStoreAttrs.FirstOrDefault(
                        a => a.storeAttributeId == fieldMapping.sourceAttributeId.Value);
                    if (existing == null || existing.value != stringValue)
                        changedCount++;
                    break;
                }
            }
        }

        return changedCount;
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

#endregion
