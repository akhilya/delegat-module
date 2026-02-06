using System.Globalization;
using PrintO.Marketplace.SDK;
using PrintO.Marketplace.SDK.DTOs;
using PrintO.Marketplace.Ozon.Models;

namespace PrintO.Marketplace.Ozon;

/// <summary>
/// Ozon Seller API integration plugin for PrintO Marketplace SDK
/// </summary>
public class OzonPlugin : MarketplacePluginBase
{
    #region Metadata

    public override string Key => "ozon";
    public override string DisplayName => "Ozon";
    public override string Version => "1.0.0";
    public override string? Description => "Integration with Ozon Seller API for managing products, prices, and stocks";
    public override string? LogoUrl => "https://upload.wikimedia.org/wikipedia/commons/9/91/Ozon_logo.svg";

    public override MarketplaceCapabilities Capabilities =>
        MarketplaceCapabilities.GetCategories |
        MarketplaceCapabilities.GetCategoryFields |
        MarketplaceCapabilities.ImportProduct |
        MarketplaceCapabilities.ImportAllProducts |
        MarketplaceCapabilities.ExportProduct |
        MarketplaceCapabilities.ImportPrices |
        MarketplaceCapabilities.ExportPrices |
        MarketplaceCapabilities.ImportStocks |
        MarketplaceCapabilities.ExportStocks |
        MarketplaceCapabilities.FieldBasedGrouping |
        MarketplaceCapabilities.ImportProductBatch;

    // Category name cache (categoryId -> fullPath)
    private Dictionary<string, string>? _categoryNameCache;

    #endregion

    #region Credentials

    public override CredentialsSchema GetCredentialsSchema()
    {
        return new CredentialsSchema
        {
            Fields = new List<CredentialField>
            {
                new()
                {
                    Key = "clientId",
                    DisplayName = "Client ID",
                    Description = "Client identifier from the Ozon Seller personal account",
                    Type = "text",
                    IsRequired = true,
                    IsSecret = false,
                    Placeholder = "123456"
                },
                new()
                {
                    Key = "apiKey",
                    DisplayName = "API Key",
                    Description = "API key from the Ozon Seller personal account",
                    Type = "password",
                    IsRequired = true,
                    IsSecret = true,
                    Placeholder = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
                }
            }
        };
    }

    #endregion

    #region Connection

    public override async Task<ConnectionTestResult> TestConnectionAsync(
        ConnectionContext context,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var warehouses = await client.GetWarehousesAsync(ct);

            return new ConnectionTestResult(
                true,
                "Connection established successfully",
                new Dictionary<string, object>
                {
                    ["warehouseCount"] = warehouses.Result.Count,
                    ["warehouses"] = warehouses.Result.Select(w => w.Name).ToList()
                }
            );
        }
        catch (OzonApiException ex)
        {
            return new ConnectionTestResult(
                false,
                $"Ozon API connection error: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["statusCode"] = ex.StatusCode,
                    ["response"] = ex.ResponseBody ?? ""
                }
            );
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Error: {ex.Message}");
        }
    }

    #endregion

    #region Categories

    public override async Task<List<ExternalCategory>> GetCategoriesAsync(
        ConnectionContext context,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);
        var response = await client.GetCategoryTreeAsync(ct);

        var result = new List<ExternalCategory>();
        FlattenCategories(response.Result, result, null, "");

        // Filter by search query
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            result = result
                .Where(c => c.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        // Filter by parentId
        else if (parentId == null)
        {
            // Return root categories only
            result = result.Where(c => c.ParentId == null).ToList();
        }
        else
        {
            // Return children of the specified parent
            result = result.Where(c => c.ParentId == parentId).ToList();
        }

        return result;
    }

    /// <summary>
    /// Flatten hierarchical category tree into a flat list
    /// </summary>
    private void FlattenCategories(
        List<OzonCategoryNode> nodes,
        List<ExternalCategory> result,
        string? parentId,
        string pathPrefix,
        long parentDescriptionCategoryId = 0)
    {
        foreach (var node in nodes.Where(n => !n.Disabled))
        {
            // If this is a type node (leaf), create combined ID
            if (node.IsTypeNode)
            {
                // Use the node's descriptionCategoryId, or inherit from parent if 0
                var descCatId = node.DescriptionCategoryId > 0
                    ? node.DescriptionCategoryId
                    : parentDescriptionCategoryId;

                var id = $"{descCatId}:{node.TypeId}";
                var fullPath = string.IsNullOrEmpty(pathPrefix)
                    ? node.DisplayName
                    : $"{pathPrefix} / {node.DisplayName}";

                result.Add(new ExternalCategory(
                    Id: id,
                    Name: node.DisplayName,
                    ParentId: parentId,
                    FullPath: fullPath,
                    HasChildren: false,
                    ChildrenCount: 0
                ));
            }
            else
            {
                // This is a category node
                var descCatId = node.DescriptionCategoryId;
                var id = descCatId.ToString();
                var fullPath = string.IsNullOrEmpty(pathPrefix)
                    ? node.DisplayName
                    : $"{pathPrefix} / {node.DisplayName}";

                var hasChildren = node.Children.Any(c => !c.Disabled);

                result.Add(new ExternalCategory(
                    Id: id,
                    Name: node.DisplayName,
                    ParentId: parentId,
                    FullPath: fullPath,
                    HasChildren: hasChildren,
                    ChildrenCount: node.Children.Count(c => !c.Disabled)
                ));

                // Recursively process children, передаём descriptionCategoryId для наследования
                if (node.Children.Any())
                {
                    FlattenCategories(node.Children, result, id, fullPath, descCatId);
                }
            }
        }
    }

    /// <summary>
    /// Загрузить кэш названий категорий для маппинга
    /// </summary>
    private async Task EnsureCategoryNameCacheAsync(OzonApiClient client, CancellationToken ct)
    {
        if (_categoryNameCache != null) return;

        var treeResponse = await client.GetCategoryTreeAsync(ct);
        _categoryNameCache = new Dictionary<string, string>();
        BuildCategoryNameCache(treeResponse.Result, _categoryNameCache, "");
    }

    /// <summary>
    /// Рекурсивно строит кэш: categoryId -> fullPath
    /// </summary>
    private void BuildCategoryNameCache(List<OzonCategoryNode> nodes, Dictionary<string, string> cache, string pathPrefix, long parentDescriptionCategoryId = 0)
    {
        foreach (var node in nodes.Where(n => !n.Disabled))
        {
            var fullPath = string.IsNullOrEmpty(pathPrefix) ? node.DisplayName : $"{pathPrefix} / {node.DisplayName}";

            if (node.IsTypeNode)
            {
                // Используем descriptionCategoryId узла, или наследуем от родителя если 0
                var descCatId = node.DescriptionCategoryId > 0
                    ? node.DescriptionCategoryId
                    : parentDescriptionCategoryId;
                var id = $"{descCatId}:{node.TypeId}";
                cache[id] = fullPath;
            }
            else
            {
                var descCatId = node.DescriptionCategoryId;
                cache[descCatId.ToString()] = fullPath;

                if (node.Children.Any())
                {
                    BuildCategoryNameCache(node.Children, cache, fullPath, descCatId);
                }
            }
        }
    }

    /// <summary>
    /// Поиск descriptionCategoryId по typeId в дереве категорий
    /// </summary>
    private long? FindDescriptionCategoryIdByTypeId(List<OzonCategoryNode> nodes, long typeId)
    {
        foreach (var node in nodes)
        {
            if (node.IsTypeNode && node.TypeId == typeId)
            {
                return node.DescriptionCategoryId;
            }

            if (node.Children.Any())
            {
                var found = FindDescriptionCategoryIdByTypeId(node.Children, typeId);
                if (found.HasValue)
                    return found;
            }
        }

        return null;
    }

    public override async Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[OzonPlugin] GetCategoryFieldsAsync called with externalCategoryId: {externalCategoryId}");

        // Parse combined category ID (format: "descriptionCategoryId:typeId")
        var parts = externalCategoryId.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], out var descriptionCategoryId) ||
            !long.TryParse(parts[1], out var typeId))
        {
            // Category ID without type - cannot fetch attributes from Ozon
            // Return empty list to allow category creation without marketplace attributes
            Console.WriteLine($"[OzonPlugin] Invalid category ID format: {externalCategoryId}, parts count: {parts.Length}");
            return new List<ExternalField>();
        }

        // typeId must be > 0 for Ozon API
        if (typeId <= 0)
        {
            Console.WriteLine($"[OzonPlugin] TypeId is <= 0: {typeId}");
            return new List<ExternalField>();
        }

        using var client = CreateClient(context);

        // Если descriptionCategoryId = 0, пытаемся найти его по typeId в дереве категорий
        if (descriptionCategoryId <= 0)
        {
            Console.WriteLine($"[OzonPlugin] descriptionCategoryId is 0, searching in category tree by typeId={typeId}");
            try
            {
                var treeResponse = await client.GetCategoryTreeAsync(ct);
                var foundDescCatId = FindDescriptionCategoryIdByTypeId(treeResponse.Result, typeId);
                if (foundDescCatId.HasValue)
                {
                    descriptionCategoryId = foundDescCatId.Value;
                    Console.WriteLine($"[OzonPlugin] Found descriptionCategoryId={descriptionCategoryId} for typeId={typeId}");
                }
                else
                {
                    Console.WriteLine($"[OzonPlugin] Could not find descriptionCategoryId for typeId={typeId} in category tree");
                    return new List<ExternalField>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OzonPlugin] Error searching category tree: {ex.Message}");
                return new List<ExternalField>();
            }
        }

        Console.WriteLine($"[OzonPlugin] Fetching attributes for descriptionCategoryId={descriptionCategoryId}, typeId={typeId}");

        CategoryAttributeResponse response;
        try
        {
            response = await client.GetCategoryAttributesAsync(descriptionCategoryId, typeId, ct);
            Console.WriteLine($"[OzonPlugin] Got {response.Result?.Count ?? 0} attributes from Ozon API");
        }
        catch (OzonApiException ex) when (ex.StatusCode == 400)
        {
            // BadRequest - категория не поддерживает атрибуты или невалидный ID
            // Возвращаем пустой список чтобы категория могла быть создана без атрибутов
            Console.WriteLine($"[OzonPlugin] Ozon API returned BadRequest: {ex.Message}");
            return new List<ExternalField>();
        }

        var fields = new List<ExternalField>();

        // Add standard Ozon fields that are NOT returned by the attributes API
        // These are top-level fields required for product import
        fields.AddRange(GetStandardOzonFields());

        if (response.Result == null)
        {
            Console.WriteLine("[OzonPlugin] response.Result is null, returning only standard fields");
            return fields;
        }

        foreach (var attr in response.Result)
        {
            Console.WriteLine($"[OzonPlugin] Attribute: id={attr.Id}, name={attr.Name}, type={attr.Type}, isCollection={attr.IsCollection}, maxValueCount={attr.MaxValueCount}, dictionaryId={attr.DictionaryId}");

            var fieldType = MapOzonTypeToFieldType(attr.Type);
            List<ExternalFieldOption>? options = null;

            // Load dictionary values for select fields
            if (attr.DictionaryId > 0)
            {
                try
                {
                    var valuesResponse = await client.GetAttributeValuesAsync(
                        descriptionCategoryId,
                        typeId,
                        attr.Id,
                        0,
                        5000,
                        ct);

                    options = valuesResponse.Result
                        .Select(v => new ExternalFieldOption(v.Id.ToString(), v.Value))
                        .ToList();

                    // Determine select vs multiselect:
                    // isCollection=False → SELECT (single value)
                    // isCollection=True AND maxValueCount=1 → SELECT (collection limited to 1)
                    // isCollection=True AND maxValueCount!=1 → MULTISELECT
                    fieldType = attr.IsCollection && attr.MaxValueCount != 1 ? "multiselect" : "select";
                }
                catch
                {
                    // If loading dictionary fails, keep as string
                }
            }

            fields.Add(new ExternalField(
                Key: attr.Id.ToString(),
                Name: attr.Name,
                IsRequired: attr.IsRequired,
                Type: fieldType,
                Options: options,
                Description: attr.Description,
                Group: attr.GroupName,
                SortOrder: (int)attr.GroupId
            ));
        }

        // Summary log for select/multiselect attributes (with DictionaryId > 0)
        var selectAttrs = response.Result.Where(a => a.DictionaryId > 0).ToList();
        Console.WriteLine($"[OzonPlugin] Select/multiselect summary: {selectAttrs.Count} attrs with DictionaryId > 0");
        foreach (var attr in selectAttrs.Take(10))
        {
            Console.WriteLine($"[OzonPlugin]   - {attr.Name}: maxValueCount={attr.MaxValueCount}, isCollection={attr.IsCollection}");
        }
        if (selectAttrs.Count > 10)
        {
            Console.WriteLine($"[OzonPlugin]   ... and {selectAttrs.Count - 10} more");
        }

        return fields;
    }

    private static string MapOzonTypeToFieldType(string ozonType)
    {
        return ozonType.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" => "number",
            "decimal" => "number",
            "boolean" => "boolean",
            "url" => "string",
            _ => "string"
        };
    }

    /// <summary>
    /// Get standard Ozon fields that are NOT returned by the category attributes API.
    /// These are top-level fields required for product import.
    /// </summary>
    private static List<ExternalField> GetStandardOzonFields()
    {
        return new List<ExternalField>
        {
            new ExternalField(
                Key: "name",
                Name: "Название товара",
                IsRequired: true,
                Type: "string",
                Description: "Название товара, отображаемое на сайте (до 500 символов)",
                Group: "Основные поля",
                SortOrder: -100
            ),
            new ExternalField(
                Key: "sku",
                Name: "Артикул (offer_id)",
                IsRequired: true,
                Type: "string",
                Description: "Артикул продавца (уникальный идентификатор товара в вашей системе)",
                Group: "Основные поля",
                SortOrder: -99
            ),
            new ExternalField(
                Key: "price",
                Name: "Цена",
                IsRequired: true,
                Type: "number",
                Description: "Цена товара в рублях",
                Group: "Основные поля",
                SortOrder: -98
            ),
            new ExternalField(
                Key: "oldPrice",
                Name: "Старая цена",
                IsRequired: false,
                Type: "number",
                Description: "Старая цена до скидки (для отображения зачёркнутой цены)",
                Group: "Основные поля",
                SortOrder: -97
            ),
            new ExternalField(
                Key: "weight",
                Name: "Вес (г)",
                IsRequired: true,
                Type: "number",
                Description: "Вес товара в граммах (с упаковкой)",
                Group: "Размеры и вес",
                SortOrder: -90
            ),
            new ExternalField(
                Key: "height",
                Name: "Высота (мм)",
                IsRequired: true,
                Type: "number",
                Description: "Высота товара в миллиметрах (с упаковкой)",
                Group: "Размеры и вес",
                SortOrder: -89
            ),
            new ExternalField(
                Key: "width",
                Name: "Ширина (мм)",
                IsRequired: true,
                Type: "number",
                Description: "Ширина товара в миллиметрах (с упаковкой)",
                Group: "Размеры и вес",
                SortOrder: -88
            ),
            new ExternalField(
                Key: "depth",
                Name: "Глубина (мм)",
                IsRequired: true,
                Type: "number",
                Description: "Глубина (длина) товара в миллиметрах (с упаковкой)",
                Group: "Размеры и вес",
                SortOrder: -87
            ),
            new ExternalField(
                Key: "barcode",
                Name: "Штрихкод (EAN)",
                IsRequired: false,
                Type: "string",
                Description: "Штрихкод товара (EAN-13 или EAN-8)",
                Group: "Основные поля",
                SortOrder: -80
            ),
            new ExternalField(
                Key: "images",
                Name: "Изображения",
                IsRequired: false,
                Type: "string",
                Description: "URL изображений товара (через запятую или список)",
                Group: "Медиа",
                SortOrder: -70
            )
        };
    }

    #endregion

    #region Product Import

    public override async Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        if (!long.TryParse(externalProductId, out var productId))
        {
            return null;
        }

        using var client = CreateClient(context);

        // Get product info
        var infoResponse = await client.GetProductInfoAsync(
            productIds: new List<string> { productId.ToString() },
            ct: ct);

        if (infoResponse.Items.Count == 0)
        {
            return null;
        }

        var item = infoResponse.Items[0];

        // Get product attributes
        var attrsResponse = await client.GetProductAttributesAsync(
            productIds: new List<string> { productId.ToString() },
            ct: ct);

        var attrs = attrsResponse.Result.FirstOrDefault();

        var product = MapToExternalProduct(item, attrs);

        // Get detailed stocks (product info endpoint returns 0 for FBS sellers)
        try
        {
            var stocksResponse = await client.GetStocksAsync(productIds: new List<long> { productId }, limit: 1, ct: ct);
            var stockItem = stocksResponse.Items.FirstOrDefault();
            if (stockItem != null)
            {
                var totalStock = stockItem.Stocks.Sum(s => s.Present);
                product = product with { Quantity = totalStock };
            }
        }
        catch
        {
            // Fall back to product info stocks
        }

        return product;
    }

    public override async Task<List<ExternalProduct>> ImportProductsBatchAsync(
        ConnectionContext context,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        if (externalProductIds.Count == 0) return [];

        using var client = CreateClient(context);
        await EnsureCategoryNameCacheAsync(client, ct);

        // 1. Batch: product info (1 API call)
        var productIdsLong = externalProductIds
            .Where(id => long.TryParse(id, out _))
            .Select(long.Parse).ToList();
        var infoResponse = await client.GetProductInfoAsync(
            productIds: externalProductIds, ct: ct);

        // 2. Batch: product attributes (1 API call)
        var attrsResponse = await client.GetProductAttributesAsync(
            productIds: externalProductIds, limit: externalProductIds.Count, ct: ct);
        var attrsDict = attrsResponse.Result.ToDictionary(a => a.Id);

        // 3. Batch: stocks (1 API call)
        var stocksDict = new Dictionary<long, int>();
        try
        {
            var stocksResponse = await client.GetStocksAsync(
                productIds: productIdsLong,
                limit: productIdsLong.Count, ct: ct);
            foreach (var si in stocksResponse.Items)
                stocksDict[si.ProductId] = si.Stocks.Sum(s => s.Present);
        }
        catch { /* fallback to info stocks */ }

        // 4. Map all
        return infoResponse.Items.Select(item =>
        {
            var product = MapToExternalProduct(item, attrsDict.GetValueOrDefault(item.Id), _categoryNameCache);
            if (stocksDict.TryGetValue(item.Id, out var stock))
                product = product with { Quantity = stock };
            return product;
        }).ToList();
    }

    public override async Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);

        // Загружаем кэш категорий для получения названий
        await EnsureCategoryNameCacheAsync(client, ct);

        // Если задан фильтр по SKU, используем его
        List<string>? offerIds = null;
        if (!string.IsNullOrWhiteSpace(offerIdFilter))
        {
            offerIds = new List<string> { offerIdFilter };
        }

        // Get product list
        var listResponse = await client.GetProductListAsync(
            lastId: cursor ?? "",
            limit: pageSize,
            offerIds: offerIds,
            ct: ct);

        if (listResponse.Result.Items.Count == 0)
        {
            return new PagedResult<ExternalProduct>(
                Items: new List<ExternalProduct>(),
                HasMore: false,
                NextCursor: null,
                TotalCount: listResponse.Result.Total
            );
        }

        var productIdsLong = listResponse.Result.Items.Select(i => i.ProductId).ToList();
        var productIds = productIdsLong.Select(id => id.ToString()).ToList();

        // Get product info
        var infoResponse = await client.GetProductInfoAsync(productIds: productIds, ct: ct);

        // Get product attributes
        var attrsResponse = await client.GetProductAttributesAsync(productIds: productIds, ct: ct);

        // Get detailed stocks (the product info endpoint returns 0 for FBS sellers)
        Dictionary<long, int> stocksDict = new();
        try
        {
            var stocksResponse = await client.GetStocksAsync(productIds: productIdsLong, limit: productIdsLong.Count, ct: ct);
            foreach (var stockItem in stocksResponse.Items)
            {
                var totalStock = stockItem.Stocks.Sum(s => s.Present);
                stocksDict[stockItem.ProductId] = totalStock;
            }
        }
        catch
        {
            // If stocks fetch fails, fall back to product info stocks
        }

        var attrsDict = attrsResponse.Result.ToDictionary(a => a.Id, a => a);

        var products = infoResponse.Items
            .Select(item =>
            {
                var product = MapToExternalProduct(item, attrsDict.GetValueOrDefault(item.Id), _categoryNameCache);
                // Override quantity with detailed stocks if available
                if (stocksDict.TryGetValue(item.Id, out var detailedStock))
                {
                    product = product with { Quantity = detailedStock };
                }
                return product;
            })
            .ToList();

        var hasMore = !string.IsNullOrEmpty(listResponse.Result.LastId) &&
                      listResponse.Result.Items.Count >= pageSize;

        return new PagedResult<ExternalProduct>(
            Items: products,
            HasMore: hasMore,
            NextCursor: hasMore ? listResponse.Result.LastId : null,
            TotalCount: listResponse.Result.Total
        );
    }

    private static ExternalProduct MapToExternalProduct(
        ProductInfoItem item,
        ProductAttributesItem? attrs,
        Dictionary<string, string>? categoryNameCache = null)
    {
        var attributes = new Dictionary<string, object>();
        string? description = null;
        string? brand = null;
        string? groupId = null;
        string? groupName = null;

        // Ozon attribute IDs for common fields
        const long DescriptionAttributeId = 4191; // Аннотация
        const long BrandAttributeId = 85; // Бренд
        const long GroupAttributeId = 9048; // Объединить на одной карточке
        const long GroupNameAttributeId = 22390; // Название группы

        if (attrs != null)
        {
            Console.WriteLine($"[OzonPlugin] attrs.Attributes.Count={attrs.Attributes.Count}");
            foreach (var attr in attrs.Attributes)
            {
                var values = attr.Values.Select(v =>
                    v.DictionaryValueId > 0 ? v.DictionaryValueId.ToString() : v.Value).ToList();

                Console.WriteLine($"[OzonPlugin]   attr: id={attr.AttributeId}, complexId={attr.ComplexId}, values=[{string.Join(", ", values.Take(3))}]");

                attributes[attr.AttributeId.ToString()] = values.Count == 1 ? values[0] : values;

                // Extract description from attribute 4191
                if (attr.AttributeId == DescriptionAttributeId && values.Count > 0)
                {
                    description = values[0];
                }

                // Extract brand from attribute 85
                if (attr.AttributeId == BrandAttributeId && values.Count > 0)
                {
                    brand = values[0];
                }

                // Extract group_id from attribute 9048
                if (attr.AttributeId == GroupAttributeId && values.Count > 0)
                {
                    groupId = values[0];
                }

                // Extract group name from attribute 22390
                if (attr.AttributeId == GroupNameAttributeId && values.Count > 0)
                {
                    groupName = values[0];
                }
            }

            if (attrs.Weight > 0) attributes["weight"] = attrs.Weight;
            if (attrs.Height > 0) attributes["height"] = attrs.Height;
            if (attrs.Width > 0) attributes["width"] = attrs.Width;
            if (attrs.Depth > 0) attributes["depth"] = attrs.Depth;
        }

        decimal? price = null;
        if (decimal.TryParse(item.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPrice))
        {
            price = parsedPrice;
        }

        int? quantity = null;
        if (item.Stocks != null)
        {
            quantity = item.Stocks.Present;
        }

        // Формируем ID категории
        // Приоритет: description_category_id:type_id, затем просто description_category_id, затем category_id (старый формат)
        string categoryId;
        if (item.DescriptionCategoryId > 0 && item.TypeId > 0)
        {
            categoryId = $"{item.DescriptionCategoryId}:{item.TypeId}";
        }
        else if (item.DescriptionCategoryId > 0)
        {
            categoryId = item.DescriptionCategoryId.ToString();
        }
        else if (item.CategoryId > 0 && item.TypeId > 0)
        {
            // Fallback: category_id с type_id
            categoryId = $"{item.CategoryId}:{item.TypeId}";
        }
        else if (item.CategoryId > 0)
        {
            // Старый формат - только category_id
            categoryId = item.CategoryId.ToString();
        }
        else
        {
            // Крайний случай - используем type_id как есть (без description_category_id)
            // Это не идеально, но позволяет хотя бы увидеть товар
            categoryId = item.TypeId > 0 ? $"0:{item.TypeId}" : "0";
        }

        // Получаем название категории из кэша
        string? categoryName = null;
        categoryNameCache?.TryGetValue(categoryId, out categoryName);

        // Build image list with PrimaryImage first
        // Note: PrimaryImage is List<string>?, not string?
        var imageUrls = new List<string>();
        if (item.PrimaryImage?.Count > 0)
        {
            imageUrls.Add(item.PrimaryImage[0]);
        }
        if (item.Images != null)
        {
            foreach (var img in item.Images)
            {
                // Avoid duplicates if PrimaryImage is already in Images
                if (!imageUrls.Contains(img))
                {
                    imageUrls.Add(img);
                }
            }
        }

        // Logging for debugging import issues
        var descPreview = description?.Length > 30 ? description.Substring(0, 30) + "..." : description;
        Console.WriteLine($"[OzonPlugin] MapToExternalProduct: id={item.Id}, sku={item.OfferId}");
        Console.WriteLine($"[OzonPlugin]   price={price} (raw: {item.Price}), quantity={quantity} (stocks: {item.Stocks?.Present})");
        Console.WriteLine($"[OzonPlugin]   description={descPreview}, attrs={attributes.Count}");

        return new ExternalProduct(
            ExternalId: item.Id.ToString(),
            Sku: item.OfferId,
            Name: item.Name,
            Description: description,
            Price: price,
            Quantity: quantity,
            CategoryId: categoryId,
            CategoryName: categoryName,
            Attributes: attributes,
            ImageUrls: imageUrls.Count > 0 ? imageUrls : null,
            Barcode: item.Barcode,
            Brand: brand,
            RawData: new Dictionary<string, object>
            {
                ["fbo_sku"] = item.FboSku,
                ["fbs_sku"] = item.FbsSku,
                ["visible"] = item.Visible,
                ["is_archived"] = item.IsArchived
            },
            GroupId: groupId,
            GroupName: groupName
        );
    }

    #endregion

    #region Product Export

    public override async Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);

            Console.WriteLine($"[OzonPlugin.Export] Building import item from {productData.Count} keys");
            var importItem = BuildImportItem(productData, existingExternalId);

            // Log the built item for debugging
            Console.WriteLine($"[OzonPlugin.Export] Built item: Name={importItem.Name?.Substring(0, Math.Min(50, importItem.Name?.Length ?? 0))}..., OfferId={importItem.OfferId}");
            Console.WriteLine($"[OzonPlugin.Export]   CategoryId={importItem.DescriptionCategoryId}, TypeId={importItem.TypeId}");
            Console.WriteLine($"[OzonPlugin.Export]   Price={importItem.Price}, Weight={importItem.Weight}, Dimensions={importItem.Height}x{importItem.Width}x{importItem.Depth}");
            Console.WriteLine($"[OzonPlugin.Export]   Images: {importItem.Images?.Count ?? 0}, PrimaryImage={importItem.PrimaryImage?.Substring(0, Math.Min(50, importItem.PrimaryImage?.Length ?? 0))}");
            Console.WriteLine($"[OzonPlugin.Export]   Attributes count: {importItem.Attributes.Count}");
            foreach (var attr in importItem.Attributes)
            {
                var valueDesc = attr.Values.FirstOrDefault()?.DictionaryValueId > 0
                    ? $"DictId={attr.Values.First().DictionaryValueId}"
                    : $"Value={attr.Values.FirstOrDefault()?.Value?.Substring(0, Math.Min(30, attr.Values.FirstOrDefault()?.Value?.Length ?? 0))}";
                Console.WriteLine($"[OzonPlugin.Export]   - Attr {attr.Id}: {valueDesc} (total values: {attr.Values.Count})");
            }

            // Submit import request
            var importResponse = await client.ImportProductsAsync(
                new List<ProductImportItem> { importItem },
                ct);

            var taskId = importResponse.Result.TaskId;

            // Poll for completion (timeout 2 minutes, poll every 3 seconds)
            var statusResponse = await client.PollImportStatusAsync(
                taskId,
                TimeSpan.FromMinutes(2),
                TimeSpan.FromSeconds(3),
                ct);

            var resultItem = statusResponse.Result.Items.FirstOrDefault();

            if (resultItem == null)
            {
                return new ExportResult(
                    Success: false,
                    ErrorMessage: "Не получен результат импорта"
                );
            }

            if (resultItem.Status == "failed" || resultItem.Errors?.Any() == true)
            {
                Console.WriteLine($"[OzonPlugin.Export] FAILED! Status={resultItem.Status}, Errors count={resultItem.Errors?.Count ?? 0}");

                var errors = new List<string>();
                if (resultItem.Errors != null)
                {
                    foreach (var e in resultItem.Errors)
                    {
                        var errorDetail = $"{e.Field}: {e.Message}";

                        // Try to find which attribute this error refers to
                        if (!string.IsNullOrEmpty(e.Field) && long.TryParse(e.Field, out var attrId))
                        {
                            // Find the attribute in our sent data
                            var sentAttr = importItem.Attributes.FirstOrDefault(a => a.Id == attrId);
                            if (sentAttr != null)
                            {
                                var sentValue = sentAttr.Values.FirstOrDefault();
                                var valueInfo = sentValue?.DictionaryValueId > 0
                                    ? $"DictionaryId={sentValue.DictionaryValueId}"
                                    : $"Value='{sentValue?.Value}'";
                                errorDetail = $"Атрибут {attrId}: {e.Message} (отправлено: {valueInfo})";
                            }
                        }

                        Console.WriteLine($"[OzonPlugin.Export]   Error: {errorDetail}");
                        errors.Add(errorDetail);
                    }
                }

                // Also log what we sent for debugging
                Console.WriteLine($"[OzonPlugin.Export] Sent attributes for failed item:");
                foreach (var attr in importItem.Attributes.Take(20))
                {
                    var v = attr.Values.FirstOrDefault();
                    Console.WriteLine($"[OzonPlugin.Export]   {attr.Id} = {(v?.DictionaryValueId > 0 ? $"Dict:{v.DictionaryValueId}" : v?.Value)}");
                }

                return new ExportResult(
                    Success: false,
                    ErrorMessage: string.Join("; ", errors),
                    Details: new Dictionary<string, object>
                    {
                        ["taskId"] = taskId,
                        ["errors"] = resultItem.Errors ?? new List<ImportItemError>(),
                        ["sentAttributes"] = importItem.Attributes.Select(a => new { a.Id, Value = a.Values.FirstOrDefault()?.Value, DictId = a.Values.FirstOrDefault()?.DictionaryValueId }).ToList()
                    }
                );
            }

            return new ExportResult(
                Success: true,
                ExternalProductId: resultItem.ProductId.ToString(),
                Details: new Dictionary<string, object>
                {
                    ["taskId"] = taskId,
                    ["offerId"] = resultItem.OfferId,
                    ["status"] = resultItem.Status
                }
            );
        }
        catch (OzonApiException ex)
        {
            // Parse Ozon error response for more details
            var errorMessage = $"Ошибка Ozon API ({ex.StatusCode})";
            if (!string.IsNullOrEmpty(ex.ResponseBody))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(ex.ResponseBody);
                    if (doc.RootElement.TryGetProperty("message", out var msgEl))
                    {
                        errorMessage = msgEl.GetString() ?? errorMessage;
                    }
                    else if (doc.RootElement.TryGetProperty("error", out var errEl))
                    {
                        errorMessage = errEl.GetString() ?? errorMessage;
                    }
                }
                catch
                {
                    // If JSON parsing fails, use first 200 chars of response
                    errorMessage = ex.ResponseBody.Length > 200
                        ? ex.ResponseBody.Substring(0, 200) + "..."
                        : ex.ResponseBody;
                }
            }

            Console.WriteLine($"[OzonPlugin.Export] OzonApiException: {ex.StatusCode}, Response: {ex.ResponseBody}");

            return new ExportResult(
                Success: false,
                ErrorMessage: errorMessage,
                Details: new Dictionary<string, object>
                {
                    ["statusCode"] = ex.StatusCode,
                    ["response"] = ex.ResponseBody ?? ""
                }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OzonPlugin.Export] Exception: {ex.Message}");
            return new ExportResult(
                Success: false,
                ErrorMessage: ex.Message
            );
        }
    }

    // Known Ozon attribute IDs for standard fields
    private const string OzonAttrName = "4180";           // Название товара
    private const string OzonAttrDescription = "4191";    // Аннотация
    private const string OzonAttrBrand = "85";            // Бренд
    private const string OzonAttrGroupName = "9048";      // Название для объединения
    private const string OzonAttrSku = "9024";            // Артикул (если используется как атрибут)
    private const string OzonAttrTypeId = "8229";         // ID типа товара
    private const string OzonAttrHashtags = "23171";      // Хештеги

    // Known Ozon attribute IDs for dimensions (these are top-level fields, not attributes)
    private const string OzonAttrWeight = "4496";         // Вес товара
    private const string OzonAttrHeight = "4498";         // Высота товара
    private const string OzonAttrWidth = "4499";          // Ширина товара
    private const string OzonAttrDepth = "4497";          // Глубина товара

    private static ProductImportItem BuildImportItem(Dictionary<string, object> productData, string? existingExternalId)
    {
        var item = new ProductImportItem();

        // Required fields - check both literal keys and Ozon attribute IDs
        item.Name = GetString(productData, "name")
            ?? GetString(productData, OzonAttrName)
            ?? throw new ArgumentException($"name is required (key 'name' or '{OzonAttrName}')");

        item.OfferId = GetString(productData, "sku")
            ?? GetString(productData, "offer_id")
            ?? GetString(productData, OzonAttrSku)
            ?? Guid.NewGuid().ToString("N")[..16];

        // Category - check _categoryId, categoryId, category_id
        var categoryId = GetString(productData, "_categoryId")
            ?? GetString(productData, "categoryId")
            ?? GetString(productData, "category_id");
        if (!string.IsNullOrEmpty(categoryId) && categoryId.Contains(':'))
        {
            var parts = categoryId.Split(':');
            if (long.TryParse(parts[0], out var descCatId) && long.TryParse(parts[1], out var typeId))
            {
                item.DescriptionCategoryId = descCatId;
                item.TypeId = typeId;
            }
        }

        // Price - check multiple possible keys
        var price = GetDecimal(productData, "price")
            ?? GetDecimal(productData, "4389");  // Try known price attribute
        if (price.HasValue && price.Value > 0)
        {
            item.Price = price.Value.ToString("F0");
        }

        var oldPrice = GetDecimal(productData, "oldPrice") ?? GetDecimal(productData, "old_price");
        if (oldPrice.HasValue && oldPrice.Value > 0)
        {
            item.OldPrice = oldPrice.Value.ToString("F0");
        }

        // Dimensions - check literal keys AND Ozon attribute IDs
        item.Weight = GetInt(productData, "weight") ?? GetInt(productData, OzonAttrWeight);
        item.Height = GetInt(productData, "height") ?? GetInt(productData, OzonAttrHeight);
        item.Width = GetInt(productData, "width") ?? GetInt(productData, OzonAttrWidth);
        item.Depth = GetInt(productData, "depth") ?? GetInt(productData, OzonAttrDepth);

        // Images - handle multiple input types
        if (productData.TryGetValue("images", out var imagesObj) && imagesObj != null)
        {
            var images = new List<string>();

            if (imagesObj is List<string> stringList)
            {
                images = stringList;
            }
            else if (imagesObj is IEnumerable<string> stringEnum)
            {
                images = stringEnum.ToList();
            }
            else if (imagesObj is System.Collections.IList list)
            {
                foreach (var item2 in list)
                {
                    if (item2 != null)
                        images.Add(item2.ToString()!);
                }
            }
            else if (imagesObj is string singleImage && !string.IsNullOrEmpty(singleImage))
            {
                // Single image URL or comma-separated list
                if (singleImage.Contains(','))
                {
                    images = singleImage.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
                else
                {
                    images.Add(singleImage);
                }
            }

            Console.WriteLine($"[OzonPlugin.BuildItem] Images: type={imagesObj.GetType().Name}, count={images.Count}");
            if (images.Count > 0)
            {
                foreach (var img in images.Take(3))
                {
                    Console.WriteLine($"[OzonPlugin.BuildItem]   - {img.Substring(0, Math.Min(80, img.Length))}...");
                }

                item.Images = images;
                item.PrimaryImage = images[0];
            }
        }
        else
        {
            Console.WriteLine($"[OzonPlugin.BuildItem] Images: not found or null");
        }

        // Barcode
        item.Barcode = GetString(productData, "barcode");

        // Extract field type metadata (injected by MappingService)
        Dictionary<string, string>? fieldTypes = null;
        if (productData.TryGetValue("_fieldTypes", out var ftObj) && ftObj is Dictionary<string, string> ft)
        {
            fieldTypes = ft;
            productData.Remove("_fieldTypes");
        }

        // Attributes that should NOT be sent as attributes (they are top-level or problematic)
        var skipAttributes = new HashSet<string>
        {
            OzonAttrName,      // Used for Name
            OzonAttrWeight,    // Top-level field
            OzonAttrHeight,    // Top-level field
            OzonAttrWidth,     // Top-level field
            OzonAttrDepth      // Top-level field
        };

        // All numeric keys are Ozon attributes - add them directly
        foreach (var kvp in productData)
        {
            // Skip non-numeric keys (they are standard fields, not attributes)
            if (!long.TryParse(kvp.Key, out var attrId)) continue;

            // Skip attributes that are handled as top-level fields
            if (skipAttributes.Contains(kvp.Key))
            {
                Console.WriteLine($"[OzonPlugin.BuildItem] SKIP attr {kvp.Key}: handled as top-level field");
                continue;
            }

            var values = new List<AttributeValue>();

            // Look up the field type from metadata
            var fieldType = fieldTypes?.GetValueOrDefault(kvp.Key);

            if (kvp.Value is string strValue && !string.IsNullOrEmpty(strValue))
            {
                // Special handling for hashtags - clean invalid characters
                if (kvp.Key == OzonAttrHashtags)
                {
                    strValue = CleanHashtags(strValue);
                    if (string.IsNullOrEmpty(strValue)) continue; // Skip if empty after cleaning
                }

                if (fieldType == "number")
                {
                    // Number attributes: always send as plain value, never as dictionary ID
                    values.Add(new AttributeValue { Value = strValue.Trim() });
                }
                else if (strValue.Contains(':'))
                {
                    // Format: "dictionaryValueId:displayText" - use dictionary ID
                    var colonIndex = strValue.IndexOf(':');
                    var idPart = strValue.Substring(0, colonIndex);
                    if (long.TryParse(idPart, out var dictValueId) && dictValueId > 0)
                    {
                        values.Add(new AttributeValue { DictionaryValueId = dictValueId, Value = "" });
                    }
                    else
                    {
                        // ID is 0 or invalid - SKIP this attribute entirely
                        // Ozon requires valid dictionary IDs for dictionary-type attributes
                        Console.WriteLine($"[OzonPlugin.BuildItem] SKIP attr {kvp.Key}: dictionary ID is 0 or invalid (value: '{strValue}')");
                        continue;
                    }
                }
                else if (long.TryParse(strValue, out var dictValueId) && dictValueId > 0)
                {
                    // Pure numeric - dictionary value ID
                    values.Add(new AttributeValue { DictionaryValueId = dictValueId, Value = "" });
                }
                else
                {
                    // Plain text value - skip if it's just whitespace
                    var trimmed = strValue.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        values.Add(new AttributeValue { Value = trimmed });
                    }
                }
            }
            else if (kvp.Value is List<string> listValue)
            {
                foreach (var v in listValue)
                {
                    if (string.IsNullOrEmpty(v)) continue;

                    if (fieldType == "number")
                    {
                        // Number attributes: always send as plain value
                        var trimmed = v.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            values.Add(new AttributeValue { Value = trimmed });
                    }
                    else if (v.Contains(':'))
                    {
                        var colonIndex = v.IndexOf(':');
                        var idPart = v.Substring(0, colonIndex);
                        if (long.TryParse(idPart, out var dictValueId) && dictValueId > 0)
                        {
                            values.Add(new AttributeValue { DictionaryValueId = dictValueId, Value = "" });
                        }
                        else
                        {
                            var textPart = v.Substring(colonIndex + 1);
                            values.Add(new AttributeValue { Value = textPart });
                        }
                    }
                    else if (long.TryParse(v, out var dictValueId) && dictValueId > 0)
                    {
                        values.Add(new AttributeValue { DictionaryValueId = dictValueId, Value = "" });
                    }
                    else
                    {
                        var trimmed = v.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            values.Add(new AttributeValue { Value = trimmed });
                        }
                    }
                }
            }
            else if (kvp.Value is bool boolValue)
            {
                // Boolean attributes - Ozon expects string "true"/"false" or specific dictionary values
                values.Add(new AttributeValue { Value = boolValue.ToString().ToLowerInvariant() });
            }
            else if (kvp.Value != null)
            {
                // Other types - convert to string, skip if empty
                var strVal = kvp.Value.ToString()?.Trim();
                if (!string.IsNullOrEmpty(strVal))
                {
                    values.Add(new AttributeValue { Value = strVal });
                }
            }

            if (values.Any())
            {
                item.Attributes.Add(new ProductAttribute
                {
                    Id = attrId,
                    Values = values
                });
            }
            else
            {
                Console.WriteLine($"[OzonPlugin.BuildItem] SKIP attr {kvp.Key}: no valid values (original: '{kvp.Value}')");
            }
        }

        return item;
    }

    #endregion

    #region Prices

    public override async Task<List<PriceInfo>> ImportPricesAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);

        List<long>? productIds = null;
        if (externalProductIds != null && externalProductIds.Count > 0)
        {
            productIds = externalProductIds
                .Where(id => long.TryParse(id, out _))
                .Select(long.Parse)
                .ToList();
        }

        var result = new List<PriceInfo>();
        var cursor = "";

        do
        {
            var response = await client.GetPricesAsync(
                cursor: cursor,
                limit: 1000,
                productIds: productIds,
                ct: ct);

            foreach (var item in response.Items)
            {
                decimal? price = null;
                decimal? oldPrice = null;

                if (decimal.TryParse(item.Price.CurrentPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) price = p;
                if (decimal.TryParse(item.Price.OldPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var op) && op > 0) oldPrice = op;

                result.Add(new PriceInfo(
                    ExternalProductId: item.ProductId.ToString(),
                    Price: price,
                    OldPrice: oldPrice,
                    Currency: item.Price.CurrencyCode
                ));
            }

            cursor = response.Cursor;

            // If specific IDs were requested, no need to paginate
            if (productIds != null) break;

        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    public override async Task<ImportResult> ExportPricesAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);

        var items = prices.Select(p => new PriceImportItem
        {
            ProductId = long.TryParse(p.ExternalProductId, out var id) ? id : null,
            Price = p.Price?.ToString("F0") ?? "0",
            OldPrice = p.OldPrice?.ToString("F0"),
            CurrencyCode = p.Currency
        }).ToList();

        var response = await client.UpdatePricesAsync(items, ct);

        var successCount = response.Result.Count(r => r.Updated);
        var failedCount = response.Result.Count(r => !r.Updated);

        var errors = response.Result
            .Where(r => !r.Updated && r.Errors?.Any() == true)
            .SelectMany(r => r.Errors!.Select(e => new ImportError(
                r.ProductId.ToString(),
                e.Message,
                e.Code
            )))
            .ToList();

        return new ImportResult(
            TotalCount: prices.Count,
            SuccessCount: successCount,
            FailedCount: failedCount,
            Errors: errors.Any() ? errors : null
        );
    }

    #endregion

    #region Stocks

    public override async Task<List<StockInfo>> ImportStocksAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);

        List<long>? productIds = null;
        if (externalProductIds != null && externalProductIds.Count > 0)
        {
            productIds = externalProductIds
                .Where(id => long.TryParse(id, out _))
                .Select(long.Parse)
                .ToList();
        }

        var result = new List<StockInfo>();
        var cursor = "";

        do
        {
            var response = await client.GetStocksAsync(
                cursor: cursor,
                limit: 1000,
                productIds: productIds,
                ct: ct);

            foreach (var item in response.Items)
            {
                // Aggregate stocks from all warehouses/types
                var totalStock = item.Stocks.Sum(s => s.Present);

                // Get first warehouse info if available
                var firstStock = item.Stocks.FirstOrDefault();

                result.Add(new StockInfo(
                    ExternalProductId: item.ProductId.ToString(),
                    Quantity: totalStock,
                    WarehouseId: null,
                    WarehouseName: firstStock?.WarehouseName
                ));
            }

            cursor = response.Cursor;

            // If specific IDs were requested, no need to paginate
            if (productIds != null) break;

        } while (!string.IsNullOrEmpty(cursor));

        return result;
    }

    public override async Task<ImportResult> ExportStocksAsync(
        ConnectionContext context,
        List<StockInfo> stocks,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);

        // Get warehouse list to find default warehouse
        var warehouses = await client.GetWarehousesAsync(ct);
        var defaultWarehouse = warehouses.Result.FirstOrDefault(w => w.Status == "working");

        if (defaultWarehouse == null)
        {
            return new ImportResult(
                TotalCount: stocks.Count,
                SuccessCount: 0,
                FailedCount: stocks.Count,
                Errors: new List<ImportError>
                {
                    new ImportError(null, "Не найден активный склад для обновления остатков")
                }
            );
        }

        var items = stocks.Select(s => new StockUpdateItem
        {
            ProductId = long.TryParse(s.ExternalProductId, out var id) ? id : null,
            Stock = s.Quantity,
            WarehouseId = !string.IsNullOrEmpty(s.WarehouseId) && long.TryParse(s.WarehouseId, out var whId)
                ? whId
                : defaultWarehouse.WarehouseId
        }).ToList();

        var response = await client.UpdateStocksAsync(items, ct);

        var successCount = response.Result.Count(r => r.Updated);
        var failedCount = response.Result.Count(r => !r.Updated);

        var errors = response.Result
            .Where(r => !r.Updated && r.Errors?.Any() == true)
            .SelectMany(r => r.Errors!.Select(e => new ImportError(
                r.ProductId.ToString(),
                e.Message,
                e.Code
            )))
            .ToList();

        return new ImportResult(
            TotalCount: stocks.Count,
            SuccessCount: successCount,
            FailedCount: failedCount,
            Errors: errors.Any() ? errors : null
        );
    }

    #endregion

    #region Grouping

    /// <summary>
    /// Ozon uses attribute 9048 for product grouping (card consolidation)
    /// </summary>
    public override FieldBasedGroupingConfig? GetFieldBasedGroupingConfig()
    {
        return new FieldBasedGroupingConfig(
            GroupFieldKey: "9048",
            MarketplaceGeneratesId: false,
            Description: "Объединение товаров в одну карточку через атрибут 'Название для объединения в одну карточку'"
        );
    }

    #endregion

    #region Helper Methods

    private static OzonApiClient CreateClient(ConnectionContext context)
    {
        var clientId = context.GetCredential("clientId");
        var apiKey = context.GetCredential("apiKey");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Client ID и API Key обязательны для подключения к Ozon API");
        }

        return new OzonApiClient(clientId, apiKey);
    }

    private static string? GetString(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }

    private static decimal? GetDecimal(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is decimal d) return d;
            if (value is double dbl) return (decimal)dbl;
            if (value is int i) return i;
            if (value is long l) return l;
            if (decimal.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static int? GetInt(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    /// <summary>
    /// Clean hashtags to only allow letters, numbers, # and underscore.
    /// Ozon rules: only letters, numbers, #, and underscore. Separate with spaces.
    /// </summary>
    private static string CleanHashtags(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        foreach (char c in input)
        {
            // Allow: letters (any language), digits, #, _, space
            if (char.IsLetterOrDigit(c) || c == '#' || c == '_' || c == ' ')
            {
                result.Append(c);
            }
            // Skip invalid characters like apostrophes, special symbols
        }
        return result.ToString().Trim();
    }

    #endregion
}
