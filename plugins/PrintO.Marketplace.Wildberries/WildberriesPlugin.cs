using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PrintO.Marketplace.SDK;
using PrintO.Marketplace.SDK.DTOs;
using PrintO.Marketplace.SDK.RateLimit;
using PrintO.Marketplace.Wildberries.Models;

namespace PrintO.Marketplace.Wildberries;

public class WildberriesPlugin : MarketplacePluginBase
{
    #region Directory Cache

    private static readonly TimeSpan DirectoryCacheTtl = TimeSpan.FromMinutes(30);

    // Global directories (colors, kinds, countries, seasons, VAT) — same for all categories
    private static Dictionary<string, List<WbDirectoryItem>>? _globalDirectoryCache;
    private static DateTime _globalDirectoryCacheExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _globalCacheLock = new(1, 1);

    // ТНВЭД — per subjectId
    private static readonly ConcurrentDictionary<int, (List<WbDirectoryItem> Items, DateTime Expiry)> _tnvedCache = new();

    #endregion

    #region Metadata

    public override string Key => "wildberries";
    public override string DisplayName => "Wildberries";
    public override string Version => "1.7.1";
    public override bool IsSystem => true;
    public override string? Description => "Integration with Wildberries API for managing products, prices, and stocks";
    public override string? LogoUrl => "https://upload.wikimedia.org/wikipedia/commons/4/43/Wildberries_Logo.svg";

    public override MarketplaceCapabilities Capabilities =>
        MarketplaceCapabilities.GetCategories |
        MarketplaceCapabilities.GetCategoryFields |
        MarketplaceCapabilities.ImportProduct |
        MarketplaceCapabilities.ImportAllProducts |
        MarketplaceCapabilities.ImportProductBatch |
        MarketplaceCapabilities.ExportProduct |
        MarketplaceCapabilities.ExportProductBulk |
        MarketplaceCapabilities.ArchiveProduct |
        MarketplaceCapabilities.ImportPrices |
        MarketplaceCapabilities.ExportPrices |
        MarketplaceCapabilities.ImportStocks |
        MarketplaceCapabilities.ExportStocks |
        MarketplaceCapabilities.EndpointBasedGrouping |
        MarketplaceCapabilities.ImportGroups |
        MarketplaceCapabilities.ModifyGroupMembership;

    #endregion

    #region Rate Limiting

    public override RateLimitConfig? GetRateLimitConfig()
        => new RateLimitConfig { MaxRequestsPerSecond = 1 };

    public override async Task<List<QuotaInfo>> GetQuotasAsync(ConnectionContext context, CancellationToken ct = default)
    {
        var client = CreateClient(context);
        try
        {
            var limits = await client.GetCardLimitsAsync(ct);
            var data = limits.Data;
            if (data == null) return new List<QuotaInfo>();

            return new List<QuotaInfo>
            {
                new QuotaInfo(
                    "free_limits",
                    "Бесплатные карточки",
                    data.FreeLimits,
                    0,
                    null),
                new QuotaInfo(
                    "paid_limits",
                    "Платные карточки",
                    data.PaidLimits,
                    0,
                    null)
            };
        }
        catch
        {
            return new List<QuotaInfo>();
        }
    }

    public override MarketplaceRateLimiter? GetRateLimiterForConnection(ConnectionContext context)
    {
        var apiKey = context.GetCredential("apiKey");
        if (string.IsNullOrEmpty(apiKey)) return null;
        return WildberriesApiClient.GetRateLimiterForApiKey(apiKey);
    }

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
                    Key = "apiKey",
                    DisplayName = "API-ключ",
                    Description = "API-ключ из личного кабинета Wildberries",
                    Type = "password",
                    IsRequired = true,
                    IsSecret = true,
                    Placeholder = "eyJhbGciOiJFUzI1NiIs..."
                }
            }
        };
    }

    #endregion

    #region Connection

    public override async Task<ConnectionTestResult> TestConnectionAsync(
        ConnectionContext context, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(context);
            var warehouses = await client.GetWarehousesAsync(ct);

            return new ConnectionTestResult(
                true,
                "Соединение установлено",
                new Dictionary<string, object>
                {
                    ["warehouseCount"] = warehouses.Count,
                    ["warehouses"] = warehouses.Select(w => w.Name).ToList()
                });
        }
        catch (WildberriesApiException ex)
        {
            return new ConnectionTestResult(
                false,
                $"Ошибка API Wildberries: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["statusCode"] = ex.StatusCode,
                    ["response"] = ex.ResponseBody ?? ""
                });
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Ошибка: {ex.Message}");
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
        var client = CreateClient(context);

        // Search by name
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var subjects = await client.GetSubjectsAsync(name: searchQuery, ct: ct);
            return (subjects.Data ?? new())
                .Select(s => new ExternalCategory(
                    Id: s.SubjectId.ToString(),
                    Name: s.SubjectName,
                    ParentId: $"parent:{s.ParentId}",
                    ParentName: s.ParentName,
                    FullPath: $"{s.ParentName} / {s.SubjectName}",
                    HasChildren: false))
                .ToList();
        }

        // Root level: return parent categories
        if (parentId == null)
        {
            var parents = await client.GetParentCategoriesAsync(ct);
            return (parents.Data ?? new())
                .Where(p => p.IsVisible)
                .Select(p => new ExternalCategory(
                    Id: $"parent:{p.Id}",
                    Name: p.Name,
                    HasChildren: true))
                .ToList();
        }

        // Children of a parent category
        if (parentId.StartsWith("parent:") && int.TryParse(parentId[7..], out var pid))
        {
            var subjects = await client.GetSubjectsAsync(parentId: pid, ct: ct);
            return (subjects.Data ?? new())
                .Select(s => new ExternalCategory(
                    Id: s.SubjectId.ToString(),
                    Name: s.SubjectName,
                    ParentId: parentId,
                    ParentName: s.ParentName,
                    FullPath: $"{s.ParentName} / {s.SubjectName}",
                    HasChildren: false))
                .ToList();
        }

        return new List<ExternalCategory>();
    }

    public override async Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        // Parent categories have no fields
        if (externalCategoryId.StartsWith("parent:"))
            return new List<ExternalField>();

        if (!int.TryParse(externalCategoryId, out var subjectId))
            return new List<ExternalField>();

        var client = CreateClient(context);
        var response = await client.GetCharacteristicsAsync(subjectId, ct);

        var fields = new List<ExternalField>();

        // Standard fields
        fields.Add(new ExternalField("sku", "Артикул продавца (vendorCode)", true, "string",
            Description: "Уникальный артикул товара в вашей системе (до 72 символов)", SortOrder: 0, MaxLength: 72));
        fields.Add(new ExternalField("name", "Наименование", true, "string",
            Description: "Название товара, отображаемое на сайте (до 60 символов)", SortOrder: 1, MaxLength: 60));
        fields.Add(new ExternalField("description", "Описание", true, "string",
            Description: "Подробное описание товара (до 5000 символов)", SortOrder: 2, MaxLength: 5000));
        fields.Add(new ExternalField("brand", "Бренд", false, "string",
            Description: "Название бренда товара. Должно точно совпадать с названием бренда в системе WB", SortOrder: 3));
        fields.Add(new ExternalField("price", "Цена (руб)", true, "number",
            Description: "Цена товара в рублях до скидки", SortOrder: 4));
        fields.Add(new ExternalField("length", "Длина (см)", true, "number",
            Description: "Длина упаковки в сантиметрах", SortOrder: 5, Group: "Габариты"));
        fields.Add(new ExternalField("width", "Ширина (см)", true, "number",
            Description: "Ширина упаковки в сантиметрах", SortOrder: 6, Group: "Габариты"));
        fields.Add(new ExternalField("height", "Высота (см)", true, "number",
            Description: "Высота упаковки в сантиметрах", SortOrder: 7, Group: "Габариты"));
        fields.Add(new ExternalField("weight", "Вес брутто (кг)", true, "number",
            Description: "Вес товара с упаковкой в килограммах", SortOrder: 8, Group: "Габариты"));
        fields.Add(new ExternalField("images", "Изображения (URL через запятую)", true, "string",
            Description: "Ссылки на изображения товара (через запятую). Минимум 1, максимум 30", SortOrder: 9));

        fields.Add(new ExternalField("barcode", "Баркод (EAN/GTIN)", false, "string",
            Description: "Штрих-код товара (EAN-13, GTIN). Если не указан, WB сгенерирует автоматически", SortOrder: 10));

        if (response.Data == null) return fields;

        // Known directory characteristics that need options loaded
        var directoryCharcNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Цвет", "Пол", "Страна производства", "Сезон", "НДС", "Ставка НДС", "ТНВЭД"
        };

        // Preload directories for known chars
        Dictionary<string, List<WbDirectoryItem>>? directoryCache = null;

        foreach (var charc in response.Data)
        {
            string fieldType;
            List<ExternalFieldOption>? options = null;

            if (charc.CharcType == 0)
            {
                // Boolean characteristics (e.g. "Для взрослых" / 18+)
                fieldType = "select";
                options = new List<ExternalFieldOption>
                {
                    new("Да", "Да"),
                    new("Нет", "Нет"),
                };
            }
            else if (charc.CharcType == 4)
            {
                fieldType = "number";
            }
            else if (charc.CharcType == 1)
            {
                fieldType = charc.MaxCount > 1 || charc.MaxCount == 0 ? "multiselect" : "string";

                // Load directory values for known characteristics
                if (directoryCharcNames.Contains(charc.Name))
                {
                    directoryCache ??= await LoadDirectoryCacheAsync(client, subjectId, ct);

                    if (directoryCache.TryGetValue(charc.Name, out var items))
                    {
                        options = items.Select(i => new ExternalFieldOption(i.Name, i.Name)).ToList();
                        fieldType = charc.MaxCount > 1 || charc.MaxCount == 0 ? "multiselect" : "select";
                    }
                }
            }
            else
            {
                continue;
            }

            // Build description from available metadata
            var descParts = new List<string>();
            if (!string.IsNullOrEmpty(charc.UnitName))
                descParts.Add($"Единица измерения: {charc.UnitName}");
            if (charc.MaxCount > 1)
                descParts.Add($"Макс. значений: {charc.MaxCount}");
            else if (charc.MaxCount == 0)
                descParts.Add("Без ограничения количества значений");
            var description = descParts.Count > 0 ? string.Join(". ", descParts) : null;

            fields.Add(new ExternalField(
                Key: $"charc:{charc.CharcId}",
                Name: charc.Name,
                IsRequired: charc.Required,
                Type: fieldType,
                Options: options,
                Description: description,
                Unit: charc.UnitName,
                Group: "Характеристики",
                SortOrder: charc.Popular ? 100 : 200
            ));
        }

        return fields;
    }

    private static async Task<Dictionary<string, List<WbDirectoryItem>>> LoadDirectoryCacheAsync(
        WildberriesApiClient client, int subjectId, CancellationToken ct)
    {
        var result = new Dictionary<string, List<WbDirectoryItem>>(StringComparer.OrdinalIgnoreCase);

        // Load global directories (cached across all categories)
        var global = await LoadGlobalDirectoriesAsync(client, ct);
        foreach (var kv in global)
            result[kv.Key] = kv.Value;

        // ТНВЭД — per subjectId, cached separately
        if (_tnvedCache.TryGetValue(subjectId, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            result["ТНВЭД"] = cached.Items;
        }
        else
        {
            try
            {
                var tnved = await client.GetTnvedAsync(subjectId, ct);
                if (tnved.Data != null)
                {
                    var items = tnved.Data
                        .Select(t => new WbDirectoryItem { Name = t.Tnved }).ToList();
                    _tnvedCache[subjectId] = (items, DateTime.UtcNow + DirectoryCacheTtl);
                    result["ТНВЭД"] = items;
                }
            }
            catch { /* non-critical */ }
        }

        return result;
    }

    private static async Task<Dictionary<string, List<WbDirectoryItem>>> LoadGlobalDirectoriesAsync(
        WildberriesApiClient client, CancellationToken ct)
    {
        if (_globalDirectoryCache != null && _globalDirectoryCacheExpiry > DateTime.UtcNow)
            return _globalDirectoryCache;

        await _globalCacheLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_globalDirectoryCache != null && _globalDirectoryCacheExpiry > DateTime.UtcNow)
                return _globalDirectoryCache;

            var cache = new Dictionary<string, List<WbDirectoryItem>>(StringComparer.OrdinalIgnoreCase);

            // Colors — {name, parentName}
            try
            {
                var colors = await client.GetColorsAsync(ct);
                if (colors.Data != null) cache["Цвет"] = colors.Data;
            }
            catch { /* non-critical */ }

            // Kinds — array of strings
            try
            {
                var kinds = await client.GetKindsAsync(ct);
                if (kinds.Data != null)
                    cache["Пол"] = kinds.Data.Select(s => new WbDirectoryItem { Name = s }).ToList();
            }
            catch { /* non-critical */ }

            // Countries — {id, name, fullName}
            try
            {
                var countries = await client.GetCountriesAsync(ct);
                if (countries.Data != null)
                    cache["Страна производства"] = countries.Data
                        .Select(c => new WbDirectoryItem { Name = c.Name }).ToList();
            }
            catch { /* non-critical */ }

            // Seasons — array of strings
            try
            {
                var seasons = await client.GetSeasonsAsync(ct);
                if (seasons.Data != null)
                    cache["Сезон"] = seasons.Data.Select(s => new WbDirectoryItem { Name = s }).ToList();
            }
            catch { /* non-critical */ }

            // VAT rates — array of strings
            try
            {
                var vat = await client.GetVatRatesAsync(ct);
                if (vat.Data != null)
                {
                    var items = vat.Data.Select(s => new WbDirectoryItem { Name = s }).ToList();
                    cache["НДС"] = items;
                    cache["Ставка НДС"] = items;
                }
            }
            catch { /* non-critical */ }

            _globalDirectoryCache = cache;
            _globalDirectoryCacheExpiry = DateTime.UtcNow + DirectoryCacheTtl;
            return cache;
        }
        finally
        {
            _globalCacheLock.Release();
        }
    }

    #endregion

    #region Product Import

    public override async Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context, string externalProductId, CancellationToken ct = default)
    {
        var client = CreateClient(context);

        var request = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Filter = new WbCardsListFilter { TextSearch = externalProductId, WithPhoto = -1 },
                Cursor = new WbCardsListCursor { Limit = 100 }
            }
        };

        var response = await client.GetCardsListAsync(request, ct);

        if (response.Cards == null || response.Cards.Count == 0) return null;

        // Find by nmID
        if (int.TryParse(externalProductId, out var nmId))
        {
            var card = response.Cards.FirstOrDefault(c => c.NmId == nmId);
            if (card != null) return MapCardToProduct(card);
        }

        // Fallback: return first card
        return MapCardToProduct(response.Cards[0]);
    }

    public override async Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context, int pageSize = 100, string? cursor = null,
        string? offerIdFilter = null, CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var limit = Math.Min(pageSize, 100);

        string? updatedAt = null;
        int? nmId = null;

        // Parse cursor: "updatedAt|nmID"
        if (!string.IsNullOrEmpty(cursor))
        {
            var parts = cursor.Split('|');
            if (parts.Length == 2)
            {
                updatedAt = parts[0];
                if (int.TryParse(parts[1], out var parsedNmId))
                    nmId = parsedNmId;
            }
        }

        var request = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Sort = new WbCardsListSort { Ascending = false },
                Filter = new WbCardsListFilter
                {
                    WithPhoto = -1,
                    TextSearch = offerIdFilter
                },
                Cursor = new WbCardsListCursor
                {
                    Limit = limit,
                    UpdatedAt = updatedAt,
                    NmId = nmId
                }
            }
        };

        var response = await client.GetCardsListAsync(request, ct);
        var products = (response.Cards ?? new()).Select(MapCardToProduct).ToList();

        string? nextCursor = null;
        var hasMore = false;

        if (response.Cursor != null && products.Count == limit)
        {
            hasMore = true;
            nextCursor = $"{response.Cursor.UpdatedAt}|{response.Cursor.NmId}";
        }

        return new PagedResult<ExternalProduct>(
            products, hasMore, nextCursor, response.Cursor?.Total);
    }

    private static ExternalProduct MapCardToProduct(WbCard card)
    {
        // Extract price from first size
        decimal? price = null;
        string? barcode = null;
        if (card.Sizes?.Count > 0)
        {
            var firstSize = card.Sizes[0];
            if (firstSize.Price.HasValue)
                price = firstSize.Price.Value;
            barcode = firstSize.Skus?.FirstOrDefault();
        }

        // Map characteristics to attributes
        var attributes = new Dictionary<string, object>();
        if (card.Characteristics != null)
        {
            foreach (var charc in card.Characteristics)
            {
                var key = $"charc:{charc.Id}";
                if (charc.Value.HasValue)
                {
                    attributes[key] = ExtractCharacteristicValue(charc.Value.Value);
                }
            }
        }

        // Image URLs
        var imageUrls = card.Photos?
            .Where(p => !string.IsNullOrEmpty(p.Big))
            .Select(p => p.Big!)
            .ToList();

        // Raw data for round-trip
        var rawData = new Dictionary<string, object>
        {
            ["imtID"] = card.ImtId,
            ["nmUUID"] = card.NmUuid ?? "",
            ["subjectID"] = card.SubjectId,
        };
        if (card.Sizes != null)
        {
            rawData["sizes"] = JsonSerializer.Serialize(card.Sizes);
        }
        if (card.Dimensions != null)
        {
            rawData["dimensions"] = JsonSerializer.Serialize(card.Dimensions);
        }

        // Build variants for multi-size cards (more than 1 size)
        List<ExternalProductVariant>? variants = null;
        if (card.Sizes?.Count > 1)
        {
            variants = card.Sizes.Select(s => new ExternalProductVariant(
                VariantId: s.ChrtId.ToString(),
                TechSize: s.TechSize,
                MarketplaceSize: s.WbSize,
                Barcodes: s.Skus,
                Price: s.Price
            )).ToList();
        }

        return new ExternalProduct(
            ExternalId: card.NmId.ToString(),
            Sku: card.VendorCode,
            Name: card.Title ?? card.VendorCode,
            Description: card.Description,
            Price: price,
            Quantity: null,
            CategoryId: card.SubjectId.ToString(),
            CategoryName: card.SubjectName,
            Attributes: attributes.Count > 0 ? attributes : null,
            ImageUrls: imageUrls?.Count > 0 ? imageUrls : null,
            Barcode: barcode,
            Brand: card.Brand,
            RawData: rawData,
            GroupId: card.ImtId.ToString(),
            Variants: variants
        );
    }

    private static object ExtractCharacteristicValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String
                    ? (object)(e.GetString() ?? "")
                    : e.ValueKind == JsonValueKind.Number
                        ? e.TryGetInt64(out var n) ? n : e.GetDouble()
                        : e.ToString())
                .ToList(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.ToString()
        };
    }

    #endregion

    #region Product Export

    public override async Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        var client = CreateClient(context);

        if (existingExternalId != null)
            return await UpdateProductAsync(client, productData, existingExternalId, context.Logger, ct);

        return await CreateProductAsync(client, productData, context.Logger, ct);
    }

    private async Task<ExportResult> CreateProductAsync(
        WildberriesApiClient client,
        Dictionary<string, object> data,
        ILogger? logger,
        CancellationToken ct)
    {
        var subjectId = GetIntValue(data, "_categoryId");
        if (subjectId == 0)
            return new ExportResult(false, ErrorMessage: "_categoryId is required");

        var vendorCode = GetStringValue(data, "sku");
        if (string.IsNullOrEmpty(vendorCode))
            return new ExportResult(false, ErrorMessage: "sku (vendorCode) is required");

        // Check if a card with this vendorCode already exists on WB
        var searchRequest = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Filter = new WbCardsListFilter { TextSearch = vendorCode, WithPhoto = -1 },
                Cursor = new WbCardsListCursor { Limit = 10 }
            }
        };
        var searchResponse = await client.GetCardsListAsync(searchRequest, ct);
        var existingCard = searchResponse.Cards?.FirstOrDefault(c =>
            c.VendorCode.Equals(vendorCode, StringComparison.OrdinalIgnoreCase));

        if (existingCard != null)
        {
            // Card already exists on WB — update instead of creating a duplicate
            return await UpdateProductAsync(client, data, existingCard.NmId.ToString(), logger, ct);
        }

        var variant = BuildVariant(data, vendorCode);

        var group = new WbCardCreateGroup
        {
            SubjectId = subjectId,
            Variants = new List<WbCardVariant> { variant }
        };

        try
        {
            var response = await client.CreateCardsAsync(new List<WbCardCreateGroup> { group }, ct);

            if (response.Error)
            {
                return new ExportResult(false, ErrorMessage: response.ErrorText ?? "Create failed");
            }

            // Poll for the created card by vendorCode
            var nmId = await PollForCreatedCardAsync(client, vendorCode, ct);
            if (nmId == null)
            {
                // Check error list
                var errors = await CheckCardErrorsAsync(client, vendorCode, ct);
                if (!string.IsNullOrEmpty(errors))
                    return new ExportResult(false, ErrorMessage: $"Card creation failed: {errors}");

                return new ExportResult(false, ErrorMessage: "Card created but could not be found. It may still be processing.");
            }

            // Upload images
            var imageUrls = GetImageUrls(data);
            if (imageUrls.Count > 0)
            {
                try
                {
                    await client.SaveMediaByUrlAsync(new WbMediaSaveRequest
                    {
                        NmId = nmId.Value,
                        Data = imageUrls
                    }, ct);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[WbPlugin] Image upload failed for nmId={NmId}", nmId);
                }
            }

            return new ExportResult(true, ExternalProductId: nmId.Value.ToString());
        }
        catch (WildberriesApiException ex)
        {
            return new ExportResult(false, ErrorMessage: $"WB API error: {ex.Message}");
        }
    }

    private async Task<ExportResult> UpdateProductAsync(
        WildberriesApiClient client,
        Dictionary<string, object> data,
        string existingExternalId,
        ILogger? logger,
        CancellationToken ct)
    {
        if (!int.TryParse(existingExternalId, out var nmId))
            return new ExportResult(false, ErrorMessage: $"Invalid nmID: {existingExternalId}");

        // Fetch existing card (WB overwrites entire card on update)
        var fetchRequest = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Filter = new WbCardsListFilter { TextSearch = existingExternalId, WithPhoto = -1 },
                Cursor = new WbCardsListCursor { Limit = 100 }
            }
        };

        var fetchResponse = await client.GetCardsListAsync(fetchRequest, ct);
        var existingCard = fetchResponse.Cards?.FirstOrDefault(c => c.NmId == nmId);
        if (existingCard == null)
            return new ExportResult(false, ErrorMessage: $"Card with nmID={nmId} not found");

        // Build update item, merging with existing data
        var updateItem = new WbCardUpdateItem
        {
            NmId = nmId,
            VendorCode = existingCard.VendorCode, // immutable
            Brand = GetStringValue(data, "brand") ?? existingCard.Brand,
            Title = GetStringValue(data, "name") ?? existingCard.Title,
            Description = GetStringValue(data, "description") ?? existingCard.Description,
            Dimensions = BuildDimensions(data) ?? existingCard.Dimensions,
            Characteristics = BuildCharacteristics(data)
                ?? existingCard.Characteristics?.Select(c => new WbCardCreateCharacteristic
                {
                    Id = c.Id,
                    Value = c.Value.HasValue ? ExtractCharacteristicValue(c.Value.Value) : null
                }).ToList(),
            Sizes = BuildUpdateSizes(existingCard.Sizes, data)
        };

        try
        {
            var response = await client.UpdateCardsAsync(new List<WbCardUpdateItem> { updateItem }, ct);

            if (response.Error)
            {
                return new ExportResult(false, existingExternalId, response.ErrorText ?? "Update failed");
            }

            // Upload images if changed
            var imageUrls = GetImageUrls(data);
            if (imageUrls.Count > 0)
            {
                try
                {
                    await client.SaveMediaByUrlAsync(new WbMediaSaveRequest
                    {
                        NmId = nmId,
                        Data = imageUrls
                    }, ct);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "[WbPlugin] Image upload failed for nmId={NmId}", nmId);
                }
            }

            return new ExportResult(true, existingExternalId);
        }
        catch (WildberriesApiException ex)
        {
            return new ExportResult(false, existingExternalId, $"WB API error: {ex.Message}");
        }
    }

    private async Task<int?> PollForCreatedCardAsync(
        WildberriesApiClient client, string vendorCode, CancellationToken ct)
    {
        // Wait initial 5s for WB processing
        await Task.Delay(5000, ct);

        for (int i = 0; i < 5; i++)
        {
            var request = new WbCardsListRequest
            {
                Settings = new WbCardsListSettings
                {
                    Filter = new WbCardsListFilter { TextSearch = vendorCode, WithPhoto = -1 },
                    Cursor = new WbCardsListCursor { Limit = 10 }
                }
            };

            var response = await client.GetCardsListAsync(request, ct);
            var card = response.Cards?.FirstOrDefault(c =>
                c.VendorCode.Equals(vendorCode, StringComparison.OrdinalIgnoreCase));

            if (card != null)
                return card.NmId;

            await Task.Delay(5000, ct);
        }

        return null;
    }

    private static async Task<string?> CheckCardErrorsAsync(
        WildberriesApiClient client, string vendorCode, CancellationToken ct)
    {
        try
        {
            var errorRequest = new WbCardErrorListRequest
            {
                Cursor = new WbCardErrorCursor { Limit = 100 },
                Order = new WbCardErrorOrder { Ascending = false }
            };
            var errorResponse = await client.GetCardErrorsAsync(errorRequest, ct);

            if (errorResponse.Data?.Items != null)
            {
                foreach (var batch in errorResponse.Data.Items)
                {
                    if (batch.VendorCodes?.Contains(vendorCode) == true && batch.Errors != null)
                    {
                        if (batch.Errors.TryGetValue(vendorCode, out var errors))
                            return string.Join("; ", errors);
                    }
                }
            }
        }
        catch { /* non-critical */ }

        return null;
    }

    public override async Task<ExportResult> ArchiveProductAsync(
        ConnectionContext context, string externalProductId, CancellationToken ct = default)
    {
        if (!int.TryParse(externalProductId, out var nmId))
            return new ExportResult(false, ErrorMessage: $"Invalid nmID: {externalProductId}");

        var client = CreateClient(context);
        try
        {
            var response = await client.MoveToTrashAsync(new List<int> { nmId }, ct);
            if (response.Error)
                return new ExportResult(false, externalProductId, response.ErrorText);

            return new ExportResult(true, externalProductId);
        }
        catch (WildberriesApiException ex)
        {
            return new ExportResult(false, externalProductId, ex.Message);
        }
    }

    #region Bulk Export

    public override async Task<BulkExportResult> ExportProductsBulkAsync(
        ConnectionContext context,
        List<BulkExportItem> items,
        CancellationToken ct = default)
    {
        context.Logger?.LogInformation("[WB Plugin v{Version}] ExportProductsBulkAsync called with {ItemCount} items", Version, items.Count);

        var client = CreateClient(context);
        var results = new List<ExportItemResult>();
        int successCount = 0, failedCount = 0;

        // Phase 1: Validation — fail invalid items immediately
        var validItems = new List<BulkExportItem>();
        foreach (var item in items)
        {
            var sku = GetStringValue(item.ProductData, "sku");
            var categoryId = GetIntValue(item.ProductData, "_categoryId");

            if (string.IsNullOrEmpty(sku))
            {
                results.Add(new ExportItemResult(item.LocalProductId, false, ErrorMessage: "sku (vendorCode) is required"));
                failedCount++;
                continue;
            }

            if (categoryId == 0 && item.ExistingExternalId == null)
            {
                results.Add(new ExportItemResult(item.LocalProductId, false, ErrorMessage: "_categoryId is required for new products"));
                failedCount++;
                continue;
            }

            validItems.Add(item);
        }

        // Phase 2: Partition into creates (no ExternalId) and updates (have ExternalId)
        var creates = validItems.Where(i => i.ExistingExternalId == null).ToList();
        var updates = validItems.Where(i => i.ExistingExternalId != null).ToList();

        // Phase 3: Pre-check — find which "creates" already exist on WB by vendorCode
        if (creates.Count > 0)
        {
            var createVendorCodes = creates
                .Select(i => GetStringValue(i.ProductData, "sku")!)
                .ToList();

            var existingCards = await FetchExistingCardsMapAsync(client, createVendorCodes, ct);

            if (existingCards.Count > 0)
            {
                var movedToUpdate = new List<BulkExportItem>();
                var remainingCreates = new List<BulkExportItem>();

                foreach (var item in creates)
                {
                    var vc = GetStringValue(item.ProductData, "sku")!;
                    if (existingCards.TryGetValue(vc, out var card))
                    {
                        // Move to updates with the existing nmId
                        updates.Add(new BulkExportItem(
                            item.LocalProductId,
                            item.ProductData,
                            card.NmId.ToString()));
                    }
                    else
                    {
                        remainingCreates.Add(item);
                    }
                }

                creates = remainingCreates;
            }
        }

        // Phase 4: Batch create
        if (creates.Count > 0)
        {
            // Group by subjectId for the WB API
            var groups = creates
                .GroupBy(i => GetIntValue(i.ProductData, "_categoryId"))
                .Select(g => new WbCardCreateGroup
                {
                    SubjectId = g.Key,
                    Variants = g.Select(i =>
                        BuildVariant(i.ProductData, GetStringValue(i.ProductData, "sku")!)).ToList()
                })
                .ToList();

            // WB API limit: max 30 items (variants) per request
            var chunks = ChunkCardGroups(groups, 30);
            var failedChunkItems = new List<BulkExportItem>();

            foreach (var chunk in chunks)
            {
                try
                {
                    var response = await client.CreateCardsAsync(chunk, ct);

                    if (response.Error)
                    {
                        // This chunk failed — collect items for fallback
                        context.Logger?.LogWarning("[WbPlugin] Batch create chunk failed: {ErrorText}. Will fallback to individual creates", response.ErrorText);
                        var chunkVendorCodes = chunk.SelectMany(g => g.Variants).Select(v => v.VendorCode).ToHashSet();
                        failedChunkItems.AddRange(creates.Where(i => chunkVendorCodes.Contains(GetStringValue(i.ProductData, "sku")!)));
                    }
                    else
                    {
                        // Collect vendor codes for this chunk only
                        var chunkVendorCodes = chunk
                            .SelectMany(g => g.Variants)
                            .Select(v => v.VendorCode)
                            .ToList();

                        var chunkItems = creates
                            .Where(i => chunkVendorCodes.Contains(GetStringValue(i.ProductData, "sku")!))
                            .ToList();

                        // Phase 5: Batch poll for created cards
                        var foundCards = await BatchPollForCreatedCardsAsync(client, chunkVendorCodes, ct);

                        // Check errors for cards that weren't found
                        var missingVendorCodes = chunkVendorCodes
                            .Where(vc => !foundCards.ContainsKey(vc))
                            .ToList();

                        Dictionary<string, string> cardErrors = new();
                        if (missingVendorCodes.Count > 0)
                        {
                            cardErrors = await BatchCheckCardErrorsAsync(client, missingVendorCodes, ct);
                        }

                        // Build results for each created item + collect image uploads
                        var imageUploadsForCreates = new List<(int NmId, List<string> Urls, int LocalProductId)>();
                        foreach (var item in chunkItems)
                        {
                            var vc = GetStringValue(item.ProductData, "sku")!;

                            if (foundCards.TryGetValue(vc, out var nmId))
                            {
                                var imageUrls = GetImageUrls(item.ProductData);
                                if (imageUrls.Count > 0)
                                    imageUploadsForCreates.Add((nmId, imageUrls, item.LocalProductId));

                                results.Add(new ExportItemResult(item.LocalProductId, true,
                                    nmId.ToString()));
                                successCount++;
                            }
                            else if (cardErrors.TryGetValue(vc, out var errorMsg))
                            {
                                results.Add(new ExportItemResult(item.LocalProductId, false,
                                    ErrorMessage: $"Card creation failed: {errorMsg}"));
                                failedCount++;
                            }
                            else
                            {
                                results.Add(new ExportItemResult(item.LocalProductId, false,
                                    ErrorMessage: "Card created but could not be found. It may still be processing."));
                                failedCount++;
                            }
                        }

                        // Parallel image upload for created cards
                        if (imageUploadsForCreates.Count > 0)
                        {
                            var createImageErrors = new ConcurrentDictionary<int, string>();
                            var semaphore = new SemaphoreSlim(5);
                            var imageTasks = imageUploadsForCreates.Select(async iu =>
                            {
                                await semaphore.WaitAsync(ct);
                                try
                                {
                                    var (ok, err) = await UploadImagesWithRetryAsync(client, iu.NmId, iu.Urls, 2, ct);
                                    if (!ok)
                                        createImageErrors[iu.NmId] = err ?? "Image upload failed";
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            });
                            await Task.WhenAll(imageTasks);

                            // Update results with image warnings
                            foreach (var (nmId, _, localProductId) in imageUploadsForCreates)
                            {
                                if (createImageErrors.TryGetValue(nmId, out var imgErr))
                                {
                                    var idx = results.FindIndex(r =>
                                        r.LocalProductId == localProductId && r.Success);
                                    if (idx >= 0)
                                    {
                                        results[idx] = new ExportItemResult(localProductId, true,
                                            nmId.ToString(),
                                            $"Card created but image upload failed: {imgErr}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (WildberriesApiException ex)
                {
                    // This chunk threw — collect items for fallback
                    context.Logger?.LogWarning(ex, "[WbPlugin] Batch create chunk threw. Will fallback to individual creates");
                    var chunkVendorCodes = chunk.SelectMany(g => g.Variants).Select(v => v.VendorCode).ToHashSet();
                    failedChunkItems.AddRange(creates.Where(i => chunkVendorCodes.Contains(GetStringValue(i.ProductData, "sku")!)));
                }
            }

            // Fallback: individually create items from failed chunks
            if (failedChunkItems.Count > 0)
            {
                context.Logger?.LogWarning("[WbPlugin] Falling back to individual creates for {Count} items from failed chunks", failedChunkItems.Count);
                var fallbackResults = await FallbackIndividualCreatesAsync(client, failedChunkItems, context.Logger, ct);
                foreach (var r in fallbackResults)
                {
                    results.Add(r);
                    if (r.Success) successCount++; else failedCount++;
                }
            }
        }

        // Phase 6: Batch updates
        if (updates.Count > 0)
        {
            var updateResults = await BatchUpdateProductsAsync(client, updates, ct);
            foreach (var r in updateResults)
            {
                results.Add(r);
                if (r.Success) successCount++; else failedCount++;
            }
        }

        return new BulkExportResult(items.Count, successCount, failedCount, results);
    }

    private async Task<List<ExportItemResult>> BatchUpdateProductsAsync(
        WildberriesApiClient client,
        List<BulkExportItem> updates,
        CancellationToken ct)
    {
        var results = new List<ExportItemResult>();

        // 6a: Batch fetch all existing cards by nmId
        var nmIdMap = new Dictionary<int, BulkExportItem>();
        foreach (var item in updates)
        {
            if (int.TryParse(item.ExistingExternalId!, out var nmId))
                nmIdMap[nmId] = item;
            else
                results.Add(new ExportItemResult(item.LocalProductId, false,
                    ErrorMessage: $"Invalid nmID: {item.ExistingExternalId}"));
        }

        if (nmIdMap.Count == 0)
            return results;

        var cardsById = await FetchCardsByNmIdsAsync(client, nmIdMap.Keys.ToList(), ct);

        // 6b: Build update items by merging with existing cards
        var allUpdateItems = new List<WbCardUpdateItem>();
        var imageUploads = new List<(int NmId, List<string> Urls, int LocalProductId)>();
        var updateItemToProductId = new Dictionary<int, int>(); // nmId -> localProductId

        foreach (var (nmId, item) in nmIdMap)
        {
            if (!cardsById.TryGetValue(nmId, out var existingCard))
            {
                results.Add(new ExportItemResult(item.LocalProductId, false,
                    ErrorMessage: $"Card with nmID={nmId} not found"));
                continue;
            }

            var updateItem = BuildUpdateItem(existingCard, item.ProductData);
            allUpdateItems.Add(updateItem);
            updateItemToProductId[nmId] = item.LocalProductId;

            var imageUrls = GetImageUrls(item.ProductData);
            if (imageUrls.Count > 0)
                imageUploads.Add((nmId, imageUrls, item.LocalProductId));
        }

        if (allUpdateItems.Count == 0)
            return results;

        // 6c: Batch update — chunk by 1000 (WB limit: 3000 nmID per request, use 1000 for safety)
        var successNmIds = new HashSet<int>();

        foreach (var chunk in allUpdateItems
            .Select((item, i) => new { item, i })
            .GroupBy(x => x.i / 1000)
            .Select(g => g.Select(x => x.item).ToList()))
        {
            try
            {
                var response = await client.UpdateCardsAsync(chunk, ct);
                if (response.Error)
                {
                    // Entire chunk failed — mark all as failed
                    foreach (var ui in chunk)
                    {
                        results.Add(new ExportItemResult(
                            updateItemToProductId[ui.NmId], false,
                            ui.NmId.ToString(),
                            response.ErrorText ?? "Batch update failed"));
                    }
                }
                else
                {
                    foreach (var ui in chunk)
                        successNmIds.Add(ui.NmId);
                }
            }
            catch (WildberriesApiException ex)
            {
                foreach (var ui in chunk)
                {
                    results.Add(new ExportItemResult(
                        updateItemToProductId[ui.NmId], false,
                        ui.NmId.ToString(),
                        $"WB API error: {ex.Message}"));
                }
            }
        }

        // 6d: Parallel image upload for successful updates
        var imageErrors = new ConcurrentDictionary<int, string>(); // nmId -> error
        if (imageUploads.Count > 0)
        {
            var semaphore = new SemaphoreSlim(5);
            var imageTasks = imageUploads
                .Where(iu => successNmIds.Contains(iu.NmId))
                .Select(async iu =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var (ok, err) = await UploadImagesWithRetryAsync(client, iu.NmId, iu.Urls, 2, ct);
                        if (!ok)
                            imageErrors[iu.NmId] = err ?? "Image upload failed";
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

            await Task.WhenAll(imageTasks);
        }

        // Build success results
        foreach (var nmId in successNmIds)
        {
            var localProductId = updateItemToProductId[nmId];
            imageErrors.TryGetValue(nmId, out var imgWarning);
            results.Add(new ExportItemResult(localProductId, true, nmId.ToString(),
                imgWarning != null ? $"Updated but image upload failed: {imgWarning}" : null));
        }

        return results;
    }

    private static WbCardUpdateItem BuildUpdateItem(WbCard existingCard, Dictionary<string, object> data)
    {
        return new WbCardUpdateItem
        {
            NmId = existingCard.NmId,
            VendorCode = existingCard.VendorCode, // immutable
            Brand = GetStringValue(data, "brand") ?? existingCard.Brand,
            Title = GetStringValue(data, "name") ?? existingCard.Title,
            Description = GetStringValue(data, "description") ?? existingCard.Description,
            Dimensions = BuildDimensions(data) ?? existingCard.Dimensions,
            Characteristics = BuildCharacteristics(data)
                ?? existingCard.Characteristics?.Select(c => new WbCardCreateCharacteristic
                {
                    Id = c.Id,
                    Value = c.Value.HasValue ? ExtractCharacteristicValue(c.Value.Value) : null
                }).ToList(),
            Sizes = BuildUpdateSizes(existingCard.Sizes, data)
        };
    }

    /// <summary>
    /// Paginate through all seller cards (100/page) and return them.
    /// Replaces N individual TextSearch calls with ~2-5 paginated requests.
    /// </summary>
    private static async Task<List<WbCard>> FetchAllCardsAsync(
        WildberriesApiClient client, CancellationToken ct)
    {
        var allCards = new List<WbCard>();

        var request = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Sort = new WbCardsListSort { Ascending = false },
                Filter = new WbCardsListFilter { WithPhoto = -1 },
                Cursor = new WbCardsListCursor { Limit = 100 }
            }
        };

        while (true)
        {
            var response = await client.GetCardsListAsync(request, ct);
            if (response.Cards == null || response.Cards.Count == 0) break;
            allCards.AddRange(response.Cards);

            if (response.Cursor == null || response.Cards.Count < 100) break;

            request.Settings.Cursor = new WbCardsListCursor
            {
                Limit = 100,
                UpdatedAt = response.Cursor.UpdatedAt,
                NmId = response.Cursor.NmId
            };
        }

        return allCards;
    }

    private static async Task<Dictionary<int, WbCard>> FetchCardsByNmIdsAsync(
        WildberriesApiClient client,
        List<int> nmIds,
        CancellationToken ct)
    {
        var nmIdSet = new HashSet<int>(nmIds);
        var allCards = await FetchAllCardsAsync(client, ct);
        return allCards
            .Where(c => nmIdSet.Contains(c.NmId))
            .ToDictionary(c => c.NmId, c => c);
    }

    private async Task<List<ExportItemResult>> FallbackIndividualCreatesAsync(
        WildberriesApiClient client,
        List<BulkExportItem> creates,
        ILogger? logger,
        CancellationToken ct)
    {
        var results = new List<ExportItemResult>();
        foreach (var item in creates)
        {
            try
            {
                var result = await CreateProductAsync(client, item.ProductData, logger, ct);
                results.Add(new ExportItemResult(
                    item.LocalProductId, result.Success, result.ExternalProductId, result.ErrorMessage));
            }
            catch (Exception ex)
            {
                results.Add(new ExportItemResult(item.LocalProductId, false, ErrorMessage: ex.Message));
            }
        }
        return results;
    }

    private static async Task<Dictionary<string, WbCard>> FetchExistingCardsMapAsync(
        WildberriesApiClient client,
        List<string> vendorCodes,
        CancellationToken ct)
    {
        var vendorCodeSet = new HashSet<string>(vendorCodes, StringComparer.OrdinalIgnoreCase);
        var allCards = await FetchAllCardsAsync(client, ct);
        return allCards
            .Where(c => vendorCodeSet.Contains(c.VendorCode))
            .ToDictionary(c => c.VendorCode, c => c, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, int>> BatchPollForCreatedCardsAsync(
        WildberriesApiClient client,
        List<string> vendorCodes,
        CancellationToken ct)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(vendorCodes, StringComparer.OrdinalIgnoreCase);

        // Initial wait — WB needs time to index newly created cards
        await Task.Delay(5_000, ct);

        for (int attempt = 0; attempt < 5 && remaining.Count > 0; attempt++)
        {
            if (attempt > 0) await Task.Delay(5_000, ct);

            var allCards = await FetchAllCardsAsync(client, ct);
            foreach (var card in allCards)
            {
                if (remaining.Contains(card.VendorCode))
                {
                    result[card.VendorCode] = card.NmId;
                    remaining.Remove(card.VendorCode);
                }
            }
        }

        return result;
    }

    private static async Task<Dictionary<string, string>> BatchCheckCardErrorsAsync(
        WildberriesApiClient client,
        List<string> vendorCodes,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var remaining = new HashSet<string>(vendorCodes, StringComparer.OrdinalIgnoreCase);

        try
        {
            var errorRequest = new WbCardErrorListRequest
            {
                Cursor = new WbCardErrorCursor { Limit = 100 },
                Order = new WbCardErrorOrder { Ascending = false }
            };
            var errorResponse = await client.GetCardErrorsAsync(errorRequest, ct);

            if (errorResponse.Data?.Items != null)
            {
                foreach (var batch in errorResponse.Data.Items)
                {
                    if (batch.Errors == null) continue;
                    foreach (var vc in remaining.ToList())
                    {
                        if (batch.Errors.TryGetValue(vc, out var errors))
                        {
                            result[vc] = string.Join("; ", errors);
                            remaining.Remove(vc);
                        }
                    }
                    if (remaining.Count == 0) break;
                }
            }
        }
        catch { /* non-critical */ }

        return result;
    }

    private static async Task<(bool Success, string? Error)> UploadImagesWithRetryAsync(
        WildberriesApiClient client,
        int nmId,
        List<string> imageUrls,
        int maxRetries,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var response = await client.SaveMediaByUrlAsync(new WbMediaSaveRequest
                {
                    NmId = nmId,
                    Data = imageUrls
                }, ct);

                if (response.Error)
                {
                    var errorMsg = response.ErrorText ?? "Unknown image upload error";
                    if (attempt < maxRetries)
                    {
                        var delay = (int)Math.Pow(2, attempt + 1) * 1000; // 2s, 4s
                        await Task.Delay(delay, ct);
                        continue;
                    }
                    return (false, errorMsg);
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                if (attempt < maxRetries)
                {
                    var delay = (int)Math.Pow(2, attempt + 1) * 1000;
                    await Task.Delay(delay, ct);
                    continue;
                }
                return (false, ex.Message);
            }
        }

        return (false, "Max retries exceeded");
    }

    #endregion

    #endregion

    #region Prices

    public override async Task<List<PriceInfo>> ImportPricesAsync(
        ConnectionContext context, List<string>? externalProductIds = null, CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var result = new List<PriceInfo>();

        if (externalProductIds != null && externalProductIds.Count > 0)
        {
            // Fetch by specific IDs in batches of 1000
            var nmIds = externalProductIds
                .Where(id => int.TryParse(id, out _))
                .Select(id => int.Parse(id))
                .ToList();

            for (int i = 0; i < nmIds.Count; i += 1000)
            {
                var batch = nmIds.Skip(i).Take(1000).ToList();
                var response = await client.GetGoodsWithPricesByNmIdsAsync(batch, ct);
                MapPriceResponse(response, result);
            }
        }
        else
        {
            // Fetch all with pagination
            var offset = 0;
            const int limit = 1000;
            while (true)
            {
                var response = await client.GetGoodsWithPricesAsync(limit, offset, ct);
                var count = MapPriceResponse(response, result);
                if (count < limit) break;
                offset += limit;
            }
        }

        return result;
    }

    private static int MapPriceResponse(WbGoodsListResponse response, List<PriceInfo> result)
    {
        var goods = response.Data?.ListGoods;
        if (goods == null) return 0;

        foreach (var item in goods)
        {
            decimal? price = null;
            decimal? oldPrice = null;
            string currency = item.CurrencyCode ?? "RUB";

            if (item.Sizes?.Count > 0)
            {
                var firstSize = item.Sizes[0];
                price = firstSize.DiscountedPrice;
                oldPrice = firstSize.Price;
            }

            // Product-level price from first size (WB goods API uses sizeID, not chrtID,
            // so per-variant prices are imported via MapCardToProduct instead)
            result.Add(new PriceInfo(
                ExternalProductId: item.NmId.ToString(),
                Price: price,
                OldPrice: oldPrice,
                Currency: currency));
        }

        return goods.Count;
    }

    public override async Task<ImportResult> ExportPricesAsync(
        ConnectionContext context, List<PriceInfo> prices, CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var errors = new List<ImportError>();
        var successCount = 0;

        // Process in batches of 1000
        for (int i = 0; i < prices.Count; i += 1000)
        {
            var batch = prices.Skip(i).Take(1000).ToList();
            var items = new List<WbPriceItem>();

            foreach (var p in batch)
            {
                if (!int.TryParse(p.ExternalProductId, out var nmId))
                {
                    errors.Add(new ImportError(p.ExternalProductId, "Invalid nmID"));
                    continue;
                }

                // WB uses base price + discount%. Reverse-calculate from absolute prices.
                int basePrice;
                int discount;

                if (p.OldPrice.HasValue && p.OldPrice.Value > 0 && p.Price.HasValue)
                {
                    basePrice = (int)Math.Round(p.OldPrice.Value);
                    discount = (int)Math.Round((1 - p.Price.Value / p.OldPrice.Value) * 100);
                    if (discount < 0) discount = 0;
                    if (discount > 99) discount = 99;
                }
                else if (p.Price.HasValue)
                {
                    basePrice = (int)Math.Round(p.Price.Value);
                    discount = 0;
                }
                else
                {
                    errors.Add(new ImportError(p.ExternalProductId, "Price is required"));
                    continue;
                }

                items.Add(new WbPriceItem { NmId = nmId, Price = basePrice, Discount = discount });
            }

            if (items.Count == 0) continue;

            try
            {
                var response = await client.SetPricesAndDiscountsAsync(items, ct);
                if (response.Error)
                {
                    foreach (var item in items)
                        errors.Add(new ImportError(item.NmId.ToString(), response.ErrorText ?? "Price update failed"));
                }
                else
                {
                    successCount += items.Count;
                }
            }
            catch (WildberriesApiException ex)
            {
                foreach (var item in items)
                    errors.Add(new ImportError(item.NmId.ToString(), ex.Message));
            }
        }

        return new ImportResult(prices.Count, successCount, errors.Count, errors.Count > 0 ? errors : null);
    }

    #endregion

    #region Stocks

    public override async Task<List<StockInfo>> ImportStocksAsync(
        ConnectionContext context, List<string>? externalProductIds = null, CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var result = new List<StockInfo>();

        // Get warehouses
        var warehouses = await client.GetWarehousesAsync(ct);
        if (warehouses.Count == 0) return result;

        // Get all cards to build nmID -> chrtIDs mapping
        var chrtIdMap = await BuildChrtIdMapAsync(client, ct);
        if (chrtIdMap.Count == 0) return result;

        // Filter by requested IDs
        var nmIdsToQuery = externalProductIds != null
            ? chrtIdMap.Where(kv => externalProductIds.Contains(kv.Key.ToString()))
                .ToDictionary(kv => kv.Key, kv => kv.Value)
            : chrtIdMap;

        // Aggregate stocks across warehouses
        var stocksByNmId = new Dictionary<int, int>();

        foreach (var warehouse in warehouses)
        {
            var allChrtIds = nmIdsToQuery.Values.SelectMany(ids => ids).Distinct().ToList();

            // Query in batches of 1000
            for (int i = 0; i < allChrtIds.Count; i += 1000)
            {
                var batch = allChrtIds.Skip(i).Take(1000).ToList();
                try
                {
                    var levels = await client.GetStockLevelsAsync(warehouse.Id, batch, ct);
                    if (levels.Stocks == null) continue;

                    foreach (var level in levels.Stocks)
                    {
                        // Find which nmID this chrtId belongs to
                        foreach (var (nmId, chrtIds) in nmIdsToQuery)
                        {
                            if (chrtIds.Contains(level.ChrtId))
                            {
                                stocksByNmId.TryGetValue(nmId, out var current);
                                stocksByNmId[nmId] = current + level.Amount;
                            }
                        }
                    }
                }
                catch (WildberriesApiException ex)
                {
                    context.Logger?.LogWarning(ex, "[WbPlugin] GetStockLevels failed for warehouse {WarehouseId}", warehouse.Id);
                }
            }
        }

        foreach (var (nmId, quantity) in stocksByNmId)
        {
            result.Add(new StockInfo(
                ExternalProductId: nmId.ToString(),
                Quantity: quantity));
        }

        return result;
    }

    public override async Task<ImportResult> ExportStocksAsync(
        ConnectionContext context, List<StockInfo> stocks, CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var errors = new List<ImportError>();
        var successCount = 0;

        // Get warehouses — use first one
        var warehouses = await client.GetWarehousesAsync(ct);
        if (warehouses.Count == 0)
            return new ImportResult(stocks.Count, 0, stocks.Count,
                new List<ImportError> { new ImportError(null, "No warehouses found") });

        var warehouseId = warehouses[0].Id;

        // Get cards to resolve nmID -> first chrtID
        var chrtIdMap = await BuildChrtIdMapAsync(client, ct);

        // Build stock items
        var stockItems = new List<WbStockItem>();
        foreach (var stock in stocks)
        {
            if (!int.TryParse(stock.ExternalProductId, out var nmId))
            {
                errors.Add(new ImportError(stock.ExternalProductId, "Invalid nmID"));
                continue;
            }

            // If VariantId is provided, use it directly as chrtId (multi-size support)
            if (!string.IsNullOrEmpty(stock.VariantId) && int.TryParse(stock.VariantId, out var directChrtId))
            {
                stockItems.Add(new WbStockItem { ChrtId = directChrtId, Amount = stock.Quantity });
                continue;
            }

            if (!chrtIdMap.TryGetValue(nmId, out var chrtIds) || chrtIds.Count == 0)
            {
                errors.Add(new ImportError(stock.ExternalProductId, "No sizes found for product"));
                continue;
            }

            // Fallback: apply stock to first chrtID only
            stockItems.Add(new WbStockItem { ChrtId = chrtIds[0], Amount = stock.Quantity });
        }

        // Update in batches of 1000
        for (int i = 0; i < stockItems.Count; i += 1000)
        {
            var batch = stockItems.Skip(i).Take(1000).ToList();
            try
            {
                await client.UpdateStocksAsync(warehouseId, batch, ct);
                successCount += batch.Count;
            }
            catch (WildberriesApiException ex)
            {
                foreach (var item in batch)
                    errors.Add(new ImportError(item.ChrtId.ToString(), ex.Message));
            }
        }

        return new ImportResult(stocks.Count, successCount, errors.Count, errors.Count > 0 ? errors : null);
    }

    /// <summary>
    /// Build mapping: nmID -> list of chrtIDs (from all card sizes)
    /// </summary>
    private static async Task<Dictionary<int, List<int>>> BuildChrtIdMapAsync(
        WildberriesApiClient client, CancellationToken ct)
    {
        var allCards = await FetchAllCardsAsync(client, ct);
        var map = new Dictionary<int, List<int>>();

        foreach (var card in allCards)
        {
            if (card.Sizes != null)
            {
                map[card.NmId] = card.Sizes
                    .Where(s => s.ChrtId > 0)
                    .Select(s => s.ChrtId)
                    .ToList();
            }
        }

        return map;
    }

    #endregion

    #region Product Grouping

    public override async Task<PagedResult<ExternalGroup>> ImportGroupsAsync(
        ConnectionContext context, int pageSize = 100, string? cursor = null, CancellationToken ct = default)
    {
        var client = CreateClient(context);

        // Fetch all cards and group by imtID
        var allCards = new List<WbCard>();
        var request = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Sort = new WbCardsListSort { Ascending = false },
                Filter = new WbCardsListFilter { WithPhoto = -1 },
                Cursor = new WbCardsListCursor { Limit = 100 }
            }
        };

        while (true)
        {
            var response = await client.GetCardsListAsync(request, ct);
            if (response.Cards == null || response.Cards.Count == 0) break;
            allCards.AddRange(response.Cards);

            if (response.Cursor == null || response.Cards.Count < 100) break;

            request.Settings.Cursor = new WbCardsListCursor
            {
                Limit = 100,
                UpdatedAt = response.Cursor.UpdatedAt,
                NmId = response.Cursor.NmId
            };
        }

        var groups = allCards
            .GroupBy(c => c.ImtId)
            .Where(g => g.Count() >= 2) // Only groups with 2+ cards
            .Select(g => new ExternalGroup(
                ExternalId: g.Key.ToString(),
                Name: g.First().Title ?? g.First().VendorCode,
                ProductExternalIds: g.Select(c => c.NmId.ToString()).ToList()))
            .ToList();

        return new PagedResult<ExternalGroup>(groups, false);
    }

    private const int WbMaxGroupSize = 30;

    public override async Task<CreateGroupResult> CreateGroupAsync(
        ConnectionContext context, List<string> productExternalIds, string? groupName = null,
        CancellationToken ct = default)
    {
        if (productExternalIds.Count < 2)
            return new CreateGroupResult(false, ErrorMessage: "Need at least 2 products to create a group");

        // WB limits merge groups to 30 cards — split into chunks if needed
        if (productExternalIds.Count <= WbMaxGroupSize)
            return await CreateSingleGroupAsync(context, productExternalIds, ct);

        // Split into chunks of WbMaxGroupSize, each chunk becomes its own merge group
        var chunks = productExternalIds
            .Select((id, i) => new { id, i })
            .GroupBy(x => x.i / WbMaxGroupSize)
            .Select(g => g.Select(x => x.id).ToList())
            .ToList();

        var groupIds = new List<string>();
        var errors = new List<string>();

        foreach (var chunk in chunks)
        {
            if (chunk.Count < 2)
            {
                // Single-product chunk can't form a group — skip
                continue;
            }

            var result = await CreateSingleGroupAsync(context, chunk, ct);
            if (result.Success && result.ExternalGroupId != null)
                groupIds.Add(result.ExternalGroupId);
            else
                errors.Add(result.ErrorMessage ?? "Unknown error");
        }

        if (groupIds.Count == 0)
            return new CreateGroupResult(false, ErrorMessage: $"Failed to create any groups: {string.Join("; ", errors)}");

        var details = new Dictionary<string, object>();
        if (groupIds.Count > 1)
            details["additionalGroupIds"] = groupIds.Skip(1).ToList();
        if (errors.Count > 0)
            details["errors"] = errors;

        return new CreateGroupResult(true, ExternalGroupId: groupIds[0], Details: details.Count > 0 ? details : null);
    }

    private async Task<CreateGroupResult> CreateSingleGroupAsync(
        ConnectionContext context, List<string> productExternalIds, CancellationToken ct)
    {
        // Fetch first card to get its imtID
        var firstId = productExternalIds[0];
        var client = CreateClient(context);
        var fetchRequest = new WbCardsListRequest
        {
            Settings = new WbCardsListSettings
            {
                Filter = new WbCardsListFilter { TextSearch = firstId, WithPhoto = -1 },
                Cursor = new WbCardsListCursor { Limit = 10 }
            }
        };
        var fetchResponse = await client.GetCardsListAsync(fetchRequest, ct);
        var firstCard = fetchResponse.Cards?.FirstOrDefault(c => c.NmId.ToString() == firstId);
        if (firstCard == null)
            return new CreateGroupResult(false, ErrorMessage: $"Product {firstId} not found");

        if (firstCard.ImtId == 0)
            return new CreateGroupResult(false, ErrorMessage: $"Product {firstId} has no imtID");

        var imtId = firstCard.ImtId;

        // Merge remaining products via moveNm (1 API call instead of 2N)
        var remaining = productExternalIds.Skip(1).ToList();
        var addResult = await AddProductsToGroupAsync(context, imtId.ToString(), remaining, ct);

        if (addResult.Success)
            return new CreateGroupResult(true, ExternalGroupId: imtId.ToString());

        return new CreateGroupResult(false, ErrorMessage: addResult.ErrorMessage);
    }

    public override async Task<GroupMembershipResult> AddProductsToGroupAsync(
        ConnectionContext context, string externalGroupId, List<string> productExternalIds,
        CancellationToken ct = default)
    {
        if (!int.TryParse(externalGroupId, out var imtId))
            return new GroupMembershipResult(false, ErrorMessage: $"Invalid imtID: {externalGroupId}");

        var client = CreateClient(context);
        var errors = new List<GroupMembershipError>();

        // Parse nmIDs, skip invalid ones
        var nmIds = new List<int>();
        foreach (var productId in productExternalIds)
        {
            if (int.TryParse(productId, out var nmId))
                nmIds.Add(nmId);
            else
                errors.Add(new GroupMembershipError(productId, "Invalid nmID"));
        }

        if (nmIds.Count == 0)
            return new GroupMembershipResult(false, FailedCount: errors.Count, Errors: errors,
                ErrorMessage: "No valid nmIDs to add");

        try
        {
            // Pre-check: query current group size to avoid wasting a rate-limited API call
            var currentGroupSize = await GetGroupMemberCountAsync(client, imtId, ct);
            if (currentGroupSize >= 0 && currentGroupSize + nmIds.Count > WbMaxGroupSize)
            {
                var msg = $"Group {imtId} already has {currentGroupSize} cards; adding {nmIds.Count} would exceed WB limit of {WbMaxGroupSize}";
                context.Logger?.LogWarning("[WbPlugin] {Message}", msg);
                return new GroupMembershipResult(false, FailedCount: nmIds.Count, ErrorMessage: msg);
            }

            // moveNm API has a limit of ~30 nmIds per call — chunk to stay within it
            const int moveNmChunkSize = 29;
            var chunks = nmIds.Chunk(moveNmChunkSize).ToList();

            foreach (var chunk in chunks)
            {
                var request = new WbMoveNmRequest
                {
                    TargetImtId = imtId,
                    NmIds = chunk.ToList()
                };
                var response = await client.MoveNmAsync(request, ct);

                if (response.Error)
                {
                    return new GroupMembershipResult(false, FailedCount: nmIds.Count,
                        ErrorMessage: response.ErrorText);
                }
            }
        }
        catch (WildberriesApiException ex)
        {
            return new GroupMembershipResult(false, ErrorMessage: ex.Message);
        }

        return new GroupMembershipResult(true, AddedCount: nmIds.Count, FailedCount: errors.Count,
            Errors: errors.Count > 0 ? errors : null);
    }

    // WB API does not support removing products from merge groups or deleting groups
    // The base class throws NotSupportedException by default, which is correct.

    #endregion

    #region Helpers

    /// <summary>
    /// Splits card-create groups into chunks where total variant count per chunk &lt;= maxItems.
    /// If a single group has more variants than maxItems, it is split into sub-groups.
    /// </summary>
    private static List<List<WbCardCreateGroup>> ChunkCardGroups(
        List<WbCardCreateGroup> groups, int maxItems)
    {
        var chunks = new List<List<WbCardCreateGroup>>();
        var currentChunk = new List<WbCardCreateGroup>();
        var currentCount = 0;

        foreach (var group in groups)
        {
            if (group.Variants.Count <= maxItems)
            {
                if (currentCount + group.Variants.Count > maxItems && currentChunk.Count > 0)
                {
                    chunks.Add(currentChunk);
                    currentChunk = new List<WbCardCreateGroup>();
                    currentCount = 0;
                }

                currentChunk.Add(group);
                currentCount += group.Variants.Count;
            }
            else
            {
                // Flush current chunk first
                if (currentChunk.Count > 0)
                {
                    chunks.Add(currentChunk);
                    currentChunk = new List<WbCardCreateGroup>();
                    currentCount = 0;
                }

                // Split oversized group into sub-groups of maxItems variants each
                for (var i = 0; i < group.Variants.Count; i += maxItems)
                {
                    var subVariants = group.Variants.GetRange(i,
                        Math.Min(maxItems, group.Variants.Count - i));
                    chunks.Add(new List<WbCardCreateGroup>
                    {
                        new() { SubjectId = group.SubjectId, Variants = subVariants }
                    });
                }
            }
        }

        if (currentChunk.Count > 0)
            chunks.Add(currentChunk);

        return chunks;
    }

    /// <summary>
    /// Query WB for the number of cards currently in a merge group (by imtId).
    /// Returns -1 if the count could not be determined (caller should skip the pre-check).
    /// </summary>
    private static async Task<int> GetGroupMemberCountAsync(
        WildberriesApiClient client, int imtId, CancellationToken ct)
    {
        try
        {
            var request = new WbCardsListRequest
            {
                Settings = new WbCardsListSettings
                {
                    Filter = new WbCardsListFilter { ImtId = imtId, WithPhoto = -1 },
                    Cursor = new WbCardsListCursor { Limit = 100 }
                }
            };
            var response = await client.GetCardsListAsync(request, ct);
            return response.Cards?.Count ?? 0;
        }
        catch
        {
            // If the pre-check fails, don't block — let moveNm proceed and report its own error
            return -1;
        }
    }

    private static WbCardVariant BuildVariant(Dictionary<string, object> data, string vendorCode)
    {
        // Build sizes from _variants if present (multi-size), fallback to single techSize "0"
        List<WbCardCreateSize> sizes;
        if (data.TryGetValue("_variants", out var variantsObj) && variantsObj is IEnumerable<object> variantsList)
        {
            sizes = new List<WbCardCreateSize>();
            foreach (var v in variantsList)
            {
                if (v is Dictionary<string, object?> vDict)
                {
                    var techSize = vDict.TryGetValue("techSize", out var ts) ? ts?.ToString() : "0";
                    var barcode = vDict.TryGetValue("barcode", out var bc) ? bc?.ToString() : null;
                    int? price = null;
                    if (vDict.TryGetValue("price", out var p) && p != null)
                    {
                        if (p is decimal d) price = (int)d;
                        else if (int.TryParse(p.ToString(), out var pi)) price = pi;
                    }
                    sizes.Add(new WbCardCreateSize
                    {
                        TechSize = techSize ?? "0",
                        Price = price ?? 0,
                        Skus = !string.IsNullOrEmpty(barcode) ? new List<string> { barcode } : null
                    });
                }
            }
            // Fallback if parsing yielded no sizes
            if (sizes.Count == 0)
            {
                sizes.Add(new WbCardCreateSize
                {
                    TechSize = "0",
                    Price = GetIntValue(data, "price"),
                    Skus = !string.IsNullOrEmpty(GetStringValue(data, "barcode"))
                        ? new List<string> { GetStringValue(data, "barcode")! }
                        : null
                });
            }
        }
        else
        {
            sizes = new List<WbCardCreateSize>
            {
                new WbCardCreateSize
                {
                    TechSize = "0",
                    Price = GetIntValue(data, "price"),
                    Skus = !string.IsNullOrEmpty(GetStringValue(data, "barcode"))
                        ? new List<string> { GetStringValue(data, "barcode")! }
                        : null
                }
            };
        }

        return new WbCardVariant
        {
            VendorCode = vendorCode,
            Title = GetStringValue(data, "name"),
            Description = GetStringValue(data, "description"),
            Brand = GetStringValue(data, "brand"),
            Dimensions = BuildDimensions(data),
            Characteristics = BuildCharacteristics(data),
            Sizes = sizes
        };
    }

    /// <summary>
    /// Build update sizes from existing card sizes, optionally merging variant prices from _variants.
    /// </summary>
    private static List<WbCardUpdateSize>? BuildUpdateSizes(List<WbCardSize>? existingSizes, Dictionary<string, object> data)
    {
        if (existingSizes == null) return null;

        // Build a lookup of variant prices by chrtId from _variants
        Dictionary<string, decimal?>? variantPrices = null;
        if (data.TryGetValue("_variants", out var variantsObj) && variantsObj is IEnumerable<object> variantsList)
        {
            variantPrices = new Dictionary<string, decimal?>();
            foreach (var v in variantsList)
            {
                if (v is Dictionary<string, object?> vDict
                    && vDict.TryGetValue("variantId", out var vid)
                    && vid != null)
                {
                    decimal? price = null;
                    if (vDict.TryGetValue("price", out var p) && p != null)
                    {
                        if (p is decimal d) price = d;
                        else if (decimal.TryParse(p.ToString(), out var dp)) price = dp;
                    }
                    variantPrices[vid.ToString()!] = price;
                }
            }
        }

        return existingSizes.Select(s =>
        {
            var updateSize = new WbCardUpdateSize
            {
                ChrtId = s.ChrtId,
                TechSize = s.TechSize,
                WbSize = s.WbSize,
                Skus = s.Skus
            };
            return updateSize;
        }).ToList();
    }

    private static WildberriesApiClient CreateClient(ConnectionContext context)
    {
        var apiKey = context.GetCredential("apiKey")
            ?? throw new InvalidOperationException("API key is required");
        return new WildberriesApiClient(apiKey, context.Logger);
    }

    private static string? GetStringValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var val)) return null;
        if (val is JsonElement je) return je.GetString();
        return val?.ToString();
    }

    private static int GetIntValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var val)) return 0;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetInt32();
            if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var parsed)) return parsed;
        }
        if (val is int i) return i;
        if (val is long l) return (int)l;
        if (val is double d) return (int)d;
        if (val is decimal m) return (int)m;
        if (val is string s && int.TryParse(s, out var r)) return r;
        return 0;
    }

    private static WbDimensions? BuildDimensions(Dictionary<string, object> data)
    {
        var l = GetIntValue(data, "length");
        var w = GetIntValue(data, "width");
        var h = GetIntValue(data, "height");
        var weight = GetDoubleValue(data, "weight");

        if (l == 0 && w == 0 && h == 0 && weight == 0) return null;

        return new WbDimensions
        {
            Length = l,
            Width = w,
            Height = h,
            WeightBrutto = weight
        };
    }

    private static double GetDoubleValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var val)) return 0;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number) return je.GetDouble();
            if (je.ValueKind == JsonValueKind.String && double.TryParse(je.GetString(), CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        if (val is double d) return d;
        if (val is float f) return f;
        if (val is int i) return i;
        if (val is decimal m) return (double)m;
        if (val is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var r)) return r;
        return 0;
    }

    private static List<WbCardCreateCharacteristic>? BuildCharacteristics(Dictionary<string, object> data)
    {
        // Extract field type metadata (set by MappingService.CoerceToFieldType)
        Dictionary<string, string>? fieldTypes = null;
        if (data.TryGetValue("_fieldTypes", out var ftObj) && ftObj is Dictionary<string, string> ft)
            fieldTypes = ft;

        var chars = new List<WbCardCreateCharacteristic>();

        foreach (var (key, value) in data)
        {
            if (!key.StartsWith("charc:")) continue;
            if (!int.TryParse(key[6..], out var charcId)) continue;

            // If MappingService marked this field as string/text, always send as string list
            if (fieldTypes != null && fieldTypes.TryGetValue(key, out var ftype)
                && (ftype == "string" || ftype == "text"))
            {
                chars.Add(new WbCardCreateCharacteristic
                {
                    Id = charcId,
                    Value = new List<string> { value?.ToString() ?? "" }
                });
                continue;
            }

            object? parsedValue = null;

            if (value is JsonElement je)
            {
                parsedValue = je.ValueKind switch
                {
                    JsonValueKind.String => ParseStringCharacteristic(je.GetString()!),
                    JsonValueKind.Number => je.TryGetInt64(out var n) ? (object)n : je.GetDouble(),
                    JsonValueKind.Array => je.EnumerateArray()
                        .Select(e => e.GetString() ?? e.ToString())
                        .ToList(),
                    _ => je.ToString()
                };
            }
            else if (value is string s)
            {
                parsedValue = ParseStringCharacteristic(s);
            }
            else if (value is IEnumerable<object> list)
            {
                parsedValue = list.Select(x => x.ToString()!).ToList();
            }
            else
            {
                parsedValue = value;
            }

            chars.Add(new WbCardCreateCharacteristic { Id = charcId, Value = parsedValue });
        }

        // Log built characteristics for debugging
        foreach (var c in chars)
        {
            var valStr = c.Value switch
            {
                IEnumerable<string> list => $"[{string.Join(", ", list)}]",
                _ => c.Value?.ToString() ?? "null"
            };
            Console.WriteLine($"[WbPlugin] Characteristic {c.Id} = {valStr}");
        }

        return chars.Count > 0 ? chars : null;
    }

    /// <summary>
    /// If the string is a valid number, return it as long/double so WB receives a numeric JSON value.
    /// Otherwise wrap it in a list (WB expects string characteristics as string arrays).
    /// </summary>
    private static object ParseStringCharacteristic(string s)
    {
        if (long.TryParse(s, out var l)) return l;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        return new List<string> { s };
    }

    private static List<string> GetImageUrls(Dictionary<string, object> data)
    {
        if (!data.TryGetValue("images", out var val)) return new List<string>();

        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
                return je.EnumerateArray().Select(e => e.GetString()!).Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (je.ValueKind == JsonValueKind.String)
            {
                var str = je.GetString();
                if (!string.IsNullOrEmpty(str))
                    return str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }
        else if (val is string s && !string.IsNullOrEmpty(s))
        {
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        else if (val is List<string> list)
        {
            return list;
        }

        return new List<string>();
    }

    #endregion
}
