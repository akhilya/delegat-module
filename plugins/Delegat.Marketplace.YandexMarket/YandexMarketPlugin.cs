using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Marketplace.YandexMarket.Models;

namespace Delegat.Marketplace.YandexMarket;

public class YandexMarketPlugin : MarketplacePluginBase
{
    private const int BulkBatchSize = 100;

    private static readonly ConcurrentDictionary<string, CachedCategoryTree> CategoryTreeCache = new();
    private static readonly TimeSpan CategoryTreeCacheTtl = TimeSpan.FromHours(24);

    private Dictionary<string, string>? _categoryNameCache;

    #region Metadata

    public override string Key => "yandex";
    public override string DisplayName => "Яндекс Маркет";
    public override string Version => "1.0.0";
    public override bool IsSystem => true;
    public override string? Description => "Интеграция с API Яндекс Маркета для управления товарами, ценами и остатками (FBS)";
    public override string? LogoUrl => "https://upload.wikimedia.org/wikipedia/commons/5/5a/Yandex_Market_logo.svg";
    public override string? ProductEditUrlTemplate => "https://partner.market.yandex.ru/business/{businessId}/assortment?offerId={externalId}";

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
        MarketplaceCapabilities.ImportProductBatch |
        MarketplaceCapabilities.ExportProductBulk |
        MarketplaceCapabilities.ArchiveProduct;

    #endregion

    #region Rate Limiting

    public override RateLimitConfig? GetRateLimitConfig()
        => new RateLimitConfig { MaxRequestsPerSecond = 50 };

    public override Task<List<QuotaInfo>> GetQuotasAsync(ConnectionContext context, CancellationToken ct = default)
        => Task.FromResult(new List<QuotaInfo>());

    public override MarketplaceRateLimiter? GetRateLimiterForConnection(ConnectionContext context)
    {
        var apiKey = context.GetCredential("apiKey");
        return string.IsNullOrEmpty(apiKey) ? null : YandexMarketApiClient.GetRateLimiterForApiKey(apiKey);
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
                    Description = "API-Key токен из кабинета продавца: Настройки → API и модули",
                    Type = "password",
                    IsRequired = true,
                    IsSecret = true,
                    Placeholder = "ACMA:..."
                },
                new()
                {
                    Key = "businessId",
                    DisplayName = "ID кабинета (businessId)",
                    Description = "Опционально. Если не указан — определяется автоматически из списка магазинов",
                    Type = "number",
                    IsRequired = false,
                    IsSecret = false
                },
                new()
                {
                    Key = "campaignId",
                    DisplayName = "ID магазина (campaignId)",
                    Description = "Опционально. FBS-магазин для остатков. Если не указан — выбирается первый FBS",
                    Type = "number",
                    IsRequired = false,
                    IsSecret = false
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
            var client = CreateClient(context);
            var (businessId, campaignId, campaign) = await ResolveIdsAsync(client, context, ct);
            var tokenInfo = await client.GetAuthTokenInfoAsync(ct);

            return new ConnectionTestResult(
                true,
                "Подключение к Яндекс Маркету установлено",
                new Dictionary<string, object>
                {
                    ["businessId"] = businessId,
                    ["campaignId"] = campaignId,
                    ["campaignName"] = campaign.Business?.Name ?? campaign.Domain ?? "",
                    ["placementType"] = campaign.PlacementType ?? "",
                    ["tokenName"] = tokenInfo.Result?.ApiKey?.Name ?? "",
                    ["scopes"] = tokenInfo.Result?.ApiKey?.Scopes ?? new List<string>()
                });
        }
        catch (YandexMarketApiException ex)
        {
            return new ConnectionTestResult(
                false,
                $"Ошибка API Яндекс Маркета: {ex.Message}",
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
        var tree = await GetCachedCategoryTreeAsync(client, context, ct);

        var result = new List<ExternalCategory>();
        FlattenCategories(tree, result, null, "");

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            return result
                .Where(c => c.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (parentId == null)
            return result.Where(c => c.ParentId == null).ToList();

        return result.Where(c => c.ParentId == parentId).ToList();
    }

    public override async Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        if (!long.TryParse(externalCategoryId, out var categoryId) || categoryId <= 0)
            return GetStandardOfferFields();

        var client = CreateClient(context);
        var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);

        var fields = new List<ExternalField>(GetStandardOfferFields());

        try
        {
            var response = await client.GetCategoryParametersAsync(categoryId, businessId, ct);
            if (response.Result?.Parameters == null)
                return fields;

            foreach (var param in response.Result.Parameters)
            {
                fields.Add(MapParameterToField(param));
            }
        }
        catch (YandexMarketApiException ex) when (ex.StatusCode is 400 or 404)
        {
            context.Logger?.LogWarning("Category parameters unavailable for {CategoryId}: {Message}", categoryId, ex.Message);
        }

        return fields;
    }

    public override Task<List<ExternalField>> GetPriceFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
        => Task.FromResult(GetStandardPriceFields());

    #endregion

    #region Import

    public override async Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        var products = await ImportProductsBatchAsync(context, new List<string> { externalProductId }, ct);
        return products.FirstOrDefault();
    }

    public override async Task<List<ExternalProduct>> ImportProductsBatchAsync(
        ConnectionContext context,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        if (externalProductIds.Count == 0)
            return new List<ExternalProduct>();

        var client = CreateClient(context);
        var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);
        await EnsureCategoryNameCacheAsync(client, context, ct);

        var response = await client.GetOfferMappingsAsync(
            businessId,
            offerIds: externalProductIds,
            limit: externalProductIds.Count,
            ct: ct);

        return MapOfferMappings(response.Result?.OfferMappings);
    }

    public override async Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);
        await EnsureCategoryNameCacheAsync(client, context, ct);

        List<string>? offerIds = string.IsNullOrWhiteSpace(offerIdFilter) ? null : new List<string> { offerIdFilter };
        var limit = Math.Clamp(pageSize, 1, 100);

        var response = await client.GetOfferMappingsAsync(
            businessId,
            offerIds: offerIds,
            pageToken: cursor,
            limit: limit,
            ct: ct);

        var mappings = response.Result?.OfferMappings ?? new List<GetOfferMappingDto>();
        var products = MapOfferMappings(mappings);
        var nextToken = response.Result?.Paging?.NextPageToken;

        return new PagedResult<ExternalProduct>(
            Items: products,
            HasMore: !string.IsNullOrEmpty(nextToken),
            NextCursor: nextToken,
            TotalCount: null);
    }

    #endregion

    #region Export

    public override async Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(context);
            var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);
            var mapping = BuildUpdateOfferMapping(productData, existingExternalId);

            var response = await client.UpdateOfferMappingsAsync(
                businessId,
                new List<UpdateOfferMappingDto> { mapping },
                ct);

            return BuildExportResult(mapping.Offer.OfferId, response.Result?.OfferMappings);
        }
        catch (YandexMarketApiException ex)
        {
            return new ExportResult(false, ErrorMessage: FormatApiError(ex));
        }
        catch (Exception ex)
        {
            return new ExportResult(false, ErrorMessage: ex.Message);
        }
    }

    public override async Task<BulkExportResult> ExportProductsBulkAsync(
        ConnectionContext context,
        List<BulkExportItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return new BulkExportResult(0, 0, 0, new List<ExportItemResult>());

        var client = CreateClient(context);
        var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);
        var allResults = new List<ExportItemResult>();

        foreach (var batch in items.Chunk(BulkBatchSize))
        {
            var mappings = batch.Select(item =>
                BuildUpdateOfferMapping(item.ProductData, item.ExistingExternalId)).ToList();

            try
            {
                var response = await client.UpdateOfferMappingsAsync(businessId, mappings, ct);
                var errorsByOfferId = (response.Result?.OfferMappings ?? new List<UpdateOfferMappingResultDto>())
                    .Where(r => r.Errors?.Count > 0)
                    .ToDictionary(r => r.OfferId ?? "", r => r.Errors!);

                foreach (var item in batch)
                {
                    var offerId = GetString(item.ProductData, "sku")
                        ?? GetString(item.ProductData, "offerId")
                        ?? item.ExistingExternalId
                        ?? "";

                    if (errorsByOfferId.TryGetValue(offerId, out var errors))
                    {
                        allResults.Add(new ExportItemResult(
                            item.LocalProductId,
                            false,
                            offerId,
                            string.Join("; ", errors.Select(e => e.Message ?? e.Type))));
                    }
                    else
                    {
                        allResults.Add(new ExportItemResult(
                            item.LocalProductId,
                            true,
                            offerId,
                            Details: new Dictionary<string, object> { ["async"] = true }));
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var item in batch)
                {
                    allResults.Add(new ExportItemResult(item.LocalProductId, false, ErrorMessage: ex.Message));
                }
            }
        }

        return new BulkExportResult(
            TotalCount: items.Count,
            SuccessCount: allResults.Count(r => r.Success),
            FailedCount: allResults.Count(r => !r.Success),
            Items: allResults);
    }

    public override async Task<ExportResult> ArchiveProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalProductId))
            return new ExportResult(false, ErrorMessage: "Не указан offerId товара");

        try
        {
            var client = CreateClient(context);
            var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);
            await client.ArchiveOffersAsync(businessId, new List<string> { externalProductId }, ct);
            return new ExportResult(true, ExternalProductId: externalProductId);
        }
        catch (YandexMarketApiException ex)
        {
            return new ExportResult(false, ErrorMessage: FormatApiError(ex));
        }
        catch (Exception ex)
        {
            return new ExportResult(false, ErrorMessage: ex.Message);
        }
    }

    #endregion

    #region Prices

    public override async Task<List<PriceInfo>> ImportPricesAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);

        var offerIds = externalProductIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var response = await client.GetDefaultPricesAsync(businessId, offerIds, ct);

        return (response.Result?.Offers ?? new List<OfferPriceDto>())
            .Select(o => new PriceInfo(
                ExternalProductId: o.OfferId,
                ExternalSku: o.OfferId,
                Price: o.Price?.Value,
                OldPrice: o.Price?.DiscountBase,
                DiscountedPrice: o.Price?.Value,
                Currency: o.Price?.CurrencyId ?? "RUR"))
            .ToList();
    }

    public override async Task<ImportResult> ExportPricesAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var (businessId, _, _) = await ResolveIdsAsync(client, context, ct);

        var offers = prices
            .Select(p =>
            {
                var offerId = !string.IsNullOrWhiteSpace(p.ExternalSku) ? p.ExternalSku! : p.ExternalProductId;
                if (string.IsNullOrWhiteSpace(offerId) || !p.Price.HasValue)
                    return null;

                return new UpdateBusinessPriceDto
                {
                    OfferId = offerId,
                    Price = new PriceWithDiscountDto
                    {
                        Value = p.Price,
                        DiscountBase = p.OldPrice ?? p.Price,
                        CurrencyId = p.Currency ?? "RUR"
                    }
                };
            })
            .Where(o => o != null)
            .Select(o => o!)
            .ToList();

        if (offers.Count == 0)
        {
            return new ImportResult(prices.Count, 0, prices.Count, new List<ImportError>
            {
                new(null, "Нет валидных цен для экспорта")
            });
        }

        try
        {
            await client.UpdateBusinessPricesAsync(businessId, offers, ct);
            return new ImportResult(prices.Count, offers.Count, prices.Count - offers.Count);
        }
        catch (YandexMarketApiException ex)
        {
            return new ImportResult(prices.Count, 0, prices.Count, new List<ImportError>
            {
                new(null, FormatApiError(ex))
            });
        }
    }

    #endregion

    #region Stocks

    public override async Task<List<StockInfo>> ImportStocksAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var (_, campaignId, _) = await ResolveIdsAsync(client, context, ct);

        var offerIds = externalProductIds?.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        var response = await client.GetStocksAsync(campaignId, offerIds, ct);

        var result = new List<StockInfo>();
        foreach (var warehouse in response.Result?.Warehouses ?? new List<WarehouseStockDto>())
        {
            foreach (var offer in warehouse.Offers ?? new List<WarehouseOfferStockDto>())
            {
                var quantity = offer.Stocks?.Sum(s => s.Count) ?? 0;
                result.Add(new StockInfo(
                    ExternalProductId: offer.OfferId,
                    Quantity: quantity,
                    WarehouseId: warehouse.WarehouseId?.ToString()));
            }
        }

        return result;
    }

    public override async Task<ImportResult> ExportStocksAsync(
        ConnectionContext context,
        List<StockInfo> stocks,
        CancellationToken ct = default)
    {
        var client = CreateClient(context);
        var (_, campaignId, _) = await ResolveIdsAsync(client, context, ct);

        var skus = stocks
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalProductId))
            .Select(s => new UpdateStockDto
            {
                Sku = s.ExternalProductId,
                Items = new List<UpdateStockItemDto>
                {
                    new()
                    {
                        Count = Math.Max(0, s.Quantity),
                        UpdatedAt = DateTime.UtcNow.ToString("O")
                    }
                }
            })
            .ToList();

        if (skus.Count == 0)
        {
            return new ImportResult(0, 0, 0);
        }

        try
        {
            foreach (var batch in skus.Chunk(2000))
            {
                await client.UpdateStocksAsync(campaignId, batch.ToList(), ct);
            }

            return new ImportResult(stocks.Count, stocks.Count, 0);
        }
        catch (YandexMarketApiException ex)
        {
            return new ImportResult(stocks.Count, 0, stocks.Count, new List<ImportError>
            {
                new(null, FormatApiError(ex))
            });
        }
    }

    #endregion

    #region Grouping

    public override FieldBasedGroupingConfig? GetFieldBasedGroupingConfig()
        => new FieldBasedGroupingConfig(
            GroupFieldKey: "groupId",
            MarketplaceGeneratesId: false,
            Description: "Объединение вариантов на одной карточке через groupId");

    #endregion

    #region Helpers - Client & IDs

    private static YandexMarketApiClient CreateClient(ConnectionContext context)
    {
        var apiKey = context.GetCredential("apiKey");
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("API-ключ обязателен для подключения к Яндекс Маркету");
        return new YandexMarketApiClient(apiKey);
    }

    private static async Task<(long BusinessId, long CampaignId, CampaignDto Campaign)> ResolveIdsAsync(
        YandexMarketApiClient client,
        ConnectionContext context,
        CancellationToken ct)
    {
        var campaignsResponse = await client.GetCampaignsAsync(ct);
        var campaigns = campaignsResponse.Result?.Campaigns ?? new List<CampaignDto>();

        if (campaigns.Count == 0)
            throw new InvalidOperationException("Не найдено ни одного магазина для данного API-ключа");

        CampaignDto? selected = null;

        if (long.TryParse(context.GetCredential("campaignId"), out var credCampaignId))
            selected = campaigns.FirstOrDefault(c => c.Id == credCampaignId);

        if (selected == null && long.TryParse(context.GetSetting("campaignId"), out var settingCampaignId))
            selected = campaigns.FirstOrDefault(c => c.Id == settingCampaignId);

        if (selected == null)
            selected = campaigns.FirstOrDefault(c => string.Equals(c.PlacementType, "FBS", StringComparison.OrdinalIgnoreCase))
                ?? campaigns.First();

        long businessId;
        if (long.TryParse(context.GetCredential("businessId"), out var credBusinessId) && credBusinessId > 0)
            businessId = credBusinessId;
        else if (selected.Business?.Id > 0)
            businessId = selected.Business.Id;
        else
            throw new InvalidOperationException("Не удалось определить businessId. Укажите его в настройках подключения.");

        return (businessId, selected.Id, selected);
    }

    #endregion

    #region Helpers - Categories

    private async Task<CategoryDto> GetCachedCategoryTreeAsync(
        YandexMarketApiClient client,
        ConnectionContext context,
        CancellationToken ct)
    {
        var apiKey = context.GetCredential("apiKey") ?? "";
        if (CategoryTreeCache.TryGetValue(apiKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Tree;

        var response = await client.GetCategoriesTreeAsync(ct);
        var tree = response.Result ?? new CategoryDto();

        CategoryTreeCache[apiKey] = new CachedCategoryTree(tree, DateTime.UtcNow.Add(CategoryTreeCacheTtl));
        return tree;
    }

    private async Task EnsureCategoryNameCacheAsync(
        YandexMarketApiClient client,
        ConnectionContext context,
        CancellationToken ct)
    {
        if (_categoryNameCache != null) return;

        var tree = await GetCachedCategoryTreeAsync(client, context, ct);
        _categoryNameCache = new Dictionary<string, string>();
        BuildCategoryNameCache(tree, _categoryNameCache, "");
    }

    private static void FlattenCategories(
        CategoryDto node,
        List<ExternalCategory> result,
        string? parentId,
        string pathPrefix)
    {
        var fullPath = string.IsNullOrEmpty(pathPrefix) ? node.Name : $"{pathPrefix} / {node.Name}";
        var id = node.Id.ToString();
        var children = node.Children ?? new List<CategoryDto>();
        var hasChildren = children.Count > 0;

        result.Add(new ExternalCategory(
            Id: id,
            Name: node.Name,
            ParentId: parentId,
            FullPath: fullPath,
            HasChildren: hasChildren,
            ChildrenCount: children.Count));

        foreach (var child in children)
            FlattenCategories(child, result, id, fullPath);
    }

    private static void BuildCategoryNameCache(CategoryDto node, Dictionary<string, string> cache, string pathPrefix)
    {
        var fullPath = string.IsNullOrEmpty(pathPrefix) ? node.Name : $"{pathPrefix} / {node.Name}";
        cache[node.Id.ToString()] = fullPath;

        foreach (var child in node.Children ?? new List<CategoryDto>())
            BuildCategoryNameCache(child, cache, fullPath);
    }

    private static ExternalField MapParameterToField(CategoryParameterDto param)
    {
        var fieldType = MapParameterType(param);
        List<ExternalFieldOption>? options = null;

        if (param.Values is { Count: > 0 })
        {
            options = param.Values
                .Select(v => new ExternalFieldOption(v.Id.ToString(), v.Value))
                .ToList();
        }

        return new ExternalField(
            Key: param.Id.ToString(),
            Name: param.Name,
            IsRequired: param.Required,
            Type: fieldType,
            Options: options,
            Description: param.Description,
            Unit: param.Unit?.Units?.FirstOrDefault()?.Name,
            MaxLength: param.Constraints?.MaxLength,
            MinValue: param.Constraints?.MinValue,
            MaxValue: param.Constraints?.MaxValue,
            Group: "Характеристики категории",
            IsAspect: param.Distinctive);
    }

    private static string MapParameterType(CategoryParameterDto param)
    {
        return param.Type.ToUpperInvariant() switch
        {
            "ENUM" => param.Multivalue ? "multiselect" : "select",
            "BOOLEAN" => "boolean",
            "NUMERIC" => "number",
            _ => "string"
        };
    }

    private static List<ExternalField> GetStandardOfferFields()
    {
        return new List<ExternalField>
        {
            new("name", "Название", true, "string", Group: "Основные поля", SortOrder: -100),
            new("offerId", "SKU (offerId)", true, "string", Description: "Уникальный артикул в вашей системе", Group: "Основные поля", SortOrder: -99),
            new("vendor", "Производитель", true, "string", Group: "Основные поля", SortOrder: -98),
            new("description", "Описание", true, "string", Group: "Основные поля", SortOrder: -97),
            new("pictures", "Изображения", true, "string", Description: "URL изображений", Group: "Основные поля", SortOrder: -96),
            new("barcodes", "Штрихкоды", false, "string", Group: "Основные поля", SortOrder: -95),
            new("groupId", "ID группы (groupId)", false, "string", Description: "Для объединения вариантов на одной карточке", Group: "Основные поля", SortOrder: -94, IsAspect: false),
            new("weight", "Вес (кг)", false, "number", Group: "Габариты", SortOrder: -90),
            new("length", "Длина (см)", false, "number", Group: "Габариты", SortOrder: -89),
            new("width", "Ширина (см)", false, "number", Group: "Габариты", SortOrder: -88),
            new("height", "Высота (см)", false, "number", Group: "Габариты", SortOrder: -87)
        };
    }

    private static List<ExternalField> GetStandardPriceFields()
    {
        return new List<ExternalField>
        {
            new("value", "Цена", true, "number", Group: "Цена", SortOrder: 0),
            new("discountBase", "Зачёркнутая цена", false, "number", Group: "Цена", SortOrder: 1),
            new("currencyId", "Валюта", false, "string", Group: "Цена", SortOrder: 2)
        };
    }

    #endregion

    #region Helpers - Products

    private List<ExternalProduct> MapOfferMappings(List<GetOfferMappingDto>? mappings)
    {
        if (mappings == null || mappings.Count == 0)
            return new List<ExternalProduct>();

        return mappings
            .Where(m => m.Offer != null && !string.IsNullOrWhiteSpace(m.Offer.OfferId))
            .Select(m => MapToExternalProduct(m.Offer!, m.Mapping))
            .ToList();
    }

    private ExternalProduct MapToExternalProduct(GetOfferDto offer, GetMappingDto? mapping)
    {
        var attributes = new Dictionary<string, object>();

        if (offer.ParameterValues != null)
        {
            foreach (var pv in offer.ParameterValues)
            {
                var key = pv.ParameterId.ToString();
                if (pv.ValueId.HasValue)
                    attributes[key] = pv.ValueId.Value;
                else if (!string.IsNullOrEmpty(pv.Value))
                    attributes[key] = pv.Value;
            }
        }

        if (offer.WeightDimensions != null)
        {
            if (offer.WeightDimensions.Weight.HasValue) attributes["weight"] = offer.WeightDimensions.Weight.Value;
            if (offer.WeightDimensions.Length.HasValue) attributes["length"] = offer.WeightDimensions.Length.Value;
            if (offer.WeightDimensions.Width.HasValue) attributes["width"] = offer.WeightDimensions.Width.Value;
            if (offer.WeightDimensions.Height.HasValue) attributes["height"] = offer.WeightDimensions.Height.Value;
        }

        decimal? price = offer.BasicPrice?.Value;
        var categoryId = offer.MarketCategoryId?.ToString();
        string? categoryName = null;
        if (categoryId != null)
            _categoryNameCache?.TryGetValue(categoryId, out categoryName);

        return new ExternalProduct(
            ExternalId: offer.OfferId,
            Sku: offer.OfferId,
            Name: offer.Name ?? offer.OfferId,
            Description: offer.Description,
            Price: price,
            CategoryId: categoryId,
            CategoryName: categoryName,
            Attributes: attributes.Count > 0 ? attributes : null,
            ImageUrls: offer.Pictures,
            Barcode: offer.Barcodes?.FirstOrDefault(),
            Brand: offer.Vendor,
            RawData: new Dictionary<string, object>
            {
                ["marketSku"] = mapping?.MarketSku ?? 0,
                ["marketSkuName"] = mapping?.MarketSkuName ?? ""
            },
            GroupId: offer.GroupId);
    }

    private static UpdateOfferMappingDto BuildUpdateOfferMapping(
        Dictionary<string, object> productData,
        string? existingExternalId)
    {
        var offerId = GetString(productData, "sku")
            ?? GetString(productData, "offerId")
            ?? existingExternalId
            ?? Guid.NewGuid().ToString("N")[..16];

        var offer = new UpdateOfferDto
        {
            OfferId = offerId,
            Name = GetString(productData, "name"),
            Description = GetString(productData, "description"),
            Vendor = GetString(productData, "vendor") ?? GetString(productData, "brand"),
            GroupId = GetString(productData, "groupId"),
            Pictures = GetStringList(productData, "pictures") ?? GetStringList(productData, "images") ?? GetStringList(productData, "_imageUrls"),
            Barcodes = GetBarcodeList(productData)
        };

        var categoryId = GetString(productData, "_categoryId")
            ?? GetString(productData, "categoryId")
            ?? GetString(productData, "marketCategoryId");
        if (long.TryParse(categoryId, out var catId) && catId > 0)
            offer.MarketCategoryId = catId;

        var weight = GetDecimal(productData, "weight");
        var length = GetDecimal(productData, "length");
        var width = GetDecimal(productData, "width");
        var height = GetDecimal(productData, "height");
        if (weight.HasValue || length.HasValue || width.HasValue || height.HasValue)
        {
            offer.WeightDimensions = new WeightDimensionsDto
            {
                Weight = weight,
                Length = length,
                Width = width,
                Height = height
            };
        }

        var price = GetDecimal(productData, "price") ?? GetDecimal(productData, "value");
        var oldPrice = GetDecimal(productData, "oldPrice") ?? GetDecimal(productData, "discountBase");
        if (price.HasValue && price.Value > 0)
        {
            offer.BasicPrice = new PriceWithDiscountDto
            {
                Value = price,
                DiscountBase = oldPrice ?? price,
                CurrencyId = GetString(productData, "currencyId") ?? "RUR"
            };
        }

        Dictionary<string, string>? fieldTypes = null;
        if (productData.TryGetValue("_fieldTypes", out var ftObj) && ftObj is Dictionary<string, string> ft)
        {
            fieldTypes = ft;
            productData.Remove("_fieldTypes");
        }

        var skipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "offerId", "sku", "vendor", "brand", "description", "pictures", "images",
            "_imageUrls", "barcodes", "barcode", "groupId", "price", "value", "oldPrice",
            "discountBase", "currencyId", "weight", "length", "width", "height",
            "_categoryId", "categoryId", "marketCategoryId", "_variants"
        };

        var parameterValues = new List<ParameterValueDto>();
        foreach (var kvp in productData)
        {
            if (skipKeys.Contains(kvp.Key) || !long.TryParse(kvp.Key, out var paramId))
                continue;

            var fieldType = fieldTypes?.GetValueOrDefault(kvp.Key);
            var values = NormalizeValues(kvp.Value);
            if (values.Count == 0) continue;

            if (fieldType is "select" or "multiselect")
            {
                foreach (var val in values)
                {
                    if (TryParseDictionaryValue(val, out var valueId))
                    {
                        parameterValues.Add(new ParameterValueDto { ParameterId = paramId, ValueId = valueId });
                    }
                }
            }
            else
            {
                parameterValues.Add(new ParameterValueDto
                {
                    ParameterId = paramId,
                    Value = values[0]
                });
            }
        }

        if (parameterValues.Count > 0)
            offer.ParameterValues = parameterValues;

        return new UpdateOfferMappingDto { Offer = offer };
    }

    private static ExportResult BuildExportResult(
        string offerId,
        List<UpdateOfferMappingResultDto>? results)
    {
        var itemResult = results?.FirstOrDefault(r => r.OfferId == offerId) ?? results?.FirstOrDefault();
        if (itemResult?.Errors is { Count: > 0 } errors)
        {
            return new ExportResult(
                false,
                offerId,
                string.Join("; ", errors.Select(e => e.Message ?? e.Type)),
                new Dictionary<string, object> { ["async"] = true });
        }

        return new ExportResult(
            true,
            offerId,
            Details: new Dictionary<string, object>
            {
                ["async"] = true,
                ["note"] = "Изменения в каталоге Маркета применяются в течение нескольких минут"
            });
    }

    #endregion

    #region Helpers - Parsing

    private static string? GetString(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return null;
        return value switch
        {
            string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
            _ => value.ToString()
        };
    }

    private static decimal? GetDecimal(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return null;

        return value switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var p2) => p2,
            _ => null
        };
    }

    private static List<string>? GetStringList(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is List<string> list)
            return list.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        if (value is IEnumerable<string> enumerable)
            return enumerable.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        if (value is System.Collections.IList iList)
        {
            return iList.Cast<object>()
                .Select(o => o?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
        }

        if (value is string str)
        {
            if (str.Contains(','))
                return str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return string.IsNullOrWhiteSpace(str) ? null : new List<string> { str.Trim() };
        }

        return null;
    }

    private static List<string>? GetBarcodeList(Dictionary<string, object> data)
    {
        var barcodes = GetStringList(data, "barcodes");
        if (barcodes is { Count: > 0 })
            return barcodes;

        var single = GetString(data, "barcode");
        return string.IsNullOrWhiteSpace(single) ? null : new List<string> { single };
    }

    private static List<string> NormalizeValues(object? value)
    {
        if (value == null) return new List<string>();

        if (value is IEnumerable<string> strings)
            return strings.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        if (value is System.Collections.IList list)
        {
            return list.Cast<object>()
                .Select(o => o?.ToString()?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();
        }

        var str = value.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(str) ? new List<string>() : new List<string> { str };
    }

    private static bool TryParseDictionaryValue(string value, out long valueId)
    {
        valueId = 0;
        if (long.TryParse(value, out valueId) && valueId > 0)
            return true;

        if (value.Contains(':'))
        {
            var idPart = value.Split(':')[0];
            return long.TryParse(idPart, out valueId) && valueId > 0;
        }

        return false;
    }

    private static string FormatApiError(YandexMarketApiException ex)
    {
        if (string.IsNullOrEmpty(ex.ResponseBody))
            return ex.Message;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(ex.ResponseBody);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => !string.IsNullOrEmpty(m));
                var joined = string.Join("; ", messages);
                if (!string.IsNullOrEmpty(joined))
                    return joined;
            }
        }
        catch
        {
            // ignore parse errors
        }

        return ex.Message;
    }

    #endregion

    private sealed record CachedCategoryTree(CategoryDto Tree, DateTime ExpiresAt);
}