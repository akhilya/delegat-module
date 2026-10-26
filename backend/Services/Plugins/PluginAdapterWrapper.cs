using System.Text.Json;
using Microsoft.Extensions.Logging;
using Delegat.Infrastructure.Security;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;

namespace Delegat.Services.Plugins;

/// <summary>
/// Wraps an IMarketplacePlugin to implement IMarketplaceAdapter.
/// Handles conversion between MarketplaceConnection and ConnectionContext.
/// </summary>
public class PluginAdapterWrapper : IMarketplaceAdapter, IBulkExportProgressAdapter
{
    private readonly IMarketplacePlugin _plugin;
    private readonly ILogger _logger;
    private readonly EncryptionService? _encryptionService;
    private readonly IAdapterHttpLogger? _httpLogger;
    private readonly IMarketplaceMetadataCache? _metadataCache;
    private readonly IMediaBlobReader? _mediaBlobReader;
    private readonly IStorefrontPageLoader? _pageLoader;

    public PluginAdapterWrapper(
        IMarketplacePlugin plugin,
        ILoggerFactory loggerFactory,
        EncryptionService? encryptionService = null,
        IAdapterHttpLogger? httpLogger = null,
        IMarketplaceMetadataCache? metadataCache = null,
        IMediaBlobReader? mediaBlobReader = null,
        IStorefrontPageLoader? pageLoader = null)
    {
        _plugin = plugin;
        _logger = loggerFactory.CreateLogger($"Plugin.{plugin.Key}");
        _encryptionService = encryptionService;
        _httpLogger = httpLogger;
        _metadataCache = metadataCache;
        _mediaBlobReader = mediaBlobReader;
        _pageLoader = pageLoader;
    }

    #region Metadata

    public string Key => _plugin.Key;
    public string DisplayName => _plugin.DisplayName;
    public string? Description => _plugin.Description;
    public string? LogoUrl => _plugin.LogoUrl;
    public string? ProductEditUrlTemplate => _plugin.ProductEditUrlTemplate;

    public Marketplace.MarketplaceCapabilities Capabilities =>
        (Marketplace.MarketplaceCapabilities)(int)_plugin.Capabilities;

    public Marketplace.CredentialsSchema GetCredentialsSchema()
    {
        var schema = _plugin.GetCredentialsSchema();
        return new Marketplace.CredentialsSchema
        {
            Fields = schema.Fields.Select(f => new Marketplace.CredentialField
            {
                Key = f.Key,
                DisplayName = f.DisplayName,
                Description = f.Description,
                Type = f.Type,
                IsRequired = f.IsRequired,
                IsSecret = f.IsSecret,
                DefaultValue = f.DefaultValue,
                Placeholder = f.Placeholder,
                Options = f.Options?.Select(o => new Marketplace.SelectOption
                {
                    Value = o.Value,
                    Label = o.Label
                }).ToList()
            }).ToList()
        };
    }

    public RateLimitConfig? GetRateLimitConfig() => _plugin.GetRateLimitConfig();

    public async Task<List<QuotaInfo>> GetQuotasAsync(
        MarketplaceConnection connection, CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        return await _plugin.GetQuotasAsync(context, ct);
    }

    public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection)
    {
        var context = CreateContext(connection);
        return _plugin.GetRateLimiterForConnection(context);
    }

    public int ClearMetadataCaches(MarketplaceConnection connection)
    {
        var removed = 0;
        if (_metadataCache != null)
        {
            // Host-side caches (category fields controller + limits quotas)
            removed += _metadataCache.RemoveByPrefix(
                $"category-fields:{connection.adapterKey}:{connection.Id}:");
            _metadataCache.Remove($"limits-quotas:{connection.Id}");
            removed++;

            // Adapter metadata prefixes used by plugins
            removed += connection.adapterKey switch
            {
                "wildberries" => _metadataCache.RemoveByPrefix("wb:"),
                "ozon" => _metadataCache.RemoveByPrefix("ozon:"),
                "yandex" => _metadataCache.RemoveByPrefix("yandex:"),
                "opencart" =>
                    _metadataCache.RemoveByPrefix($"opencart:cats:{connection.Id}:")
                    + _metadataCache.RemoveByPrefix($"opencart:fields:{connection.Id}:"),
                "woocommerce" =>
                    _metadataCache.RemoveByPrefix($"woocommerce:"),
                _ => _metadataCache.RemoveByPrefix($"{connection.adapterKey}:")
            };
        }

        var context = CreateContext(connection);
        _plugin.ClearLocalMetadataCaches(context);
        return removed;
    }

    public IReadOnlyList<AdapterToolDescriptor> GetTools() => _plugin.GetTools();

    public IReadOnlyDictionary<string, IReadOnlyList<OrderActionField>> GetOrderActionFields()
        => _plugin.GetOrderActionFields();

    public async Task<AdapterToolRunResult> RunToolAsync(
        MarketplaceConnection? connection,
        string toolId,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken ct = default)
    {
        ConnectionContext? context = connection == null ? null : CreateContext(connection);
        return await _plugin.RunToolAsync(context, toolId, parameters, ct);
    }

    #endregion

    #region Connection

    public async Task<Models.Marketplace.DTOs.ConnectionTestResult> TestConnectionAsync(
        MarketplaceConnection connection, CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.TestConnectionAsync(context, ct);
        return new Models.Marketplace.DTOs.ConnectionTestResult(
            result.Success,
            result.Message,
            result.Details
        );
    }

    #endregion

    #region Categories

    public async Task<List<Models.Marketplace.DTOs.ExternalCategory>> GetCategoriesAsync(
        MarketplaceConnection connection,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var categories = await _plugin.GetCategoriesAsync(context, parentId, searchQuery, ct);

        return categories.Select(c => new Models.Marketplace.DTOs.ExternalCategory(
            c.Id,
            c.Name,
            c.ParentId,
            c.ParentName,
            c.FullPath,
            c.HasChildren,
            c.ChildrenCount
        )).ToList();
    }

    public async Task<List<Models.Marketplace.DTOs.ExternalField>> GetCategoryFieldsAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var fields = await _plugin.GetCategoryFieldsAsync(context, externalCategoryId, ct);

        return fields.Select(f => new Models.Marketplace.DTOs.ExternalField(
            f.Key,
            f.Name,
            f.IsRequired,
            f.Type,
            f.Options?.Select(o => new Models.Marketplace.DTOs.ExternalFieldOption(o.Value, o.Label)).ToList(),
            f.Description,
            f.Unit,
            f.MaxLength,
            f.MinValue,
            f.MaxValue,
            f.ValidationPattern,
            f.Group,
            f.SortOrder,
            f.IsAspect,
            f.AllowCustomValues,
            f.MaxValues
        )).ToList();
    }

    public async Task<List<Models.Marketplace.DTOs.ExternalField>> GetPriceFieldsAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var fields = await _plugin.GetPriceFieldsAsync(context, externalCategoryId, ct);

        return fields.Select(f => new Models.Marketplace.DTOs.ExternalField(
            f.Key,
            f.Name,
            f.IsRequired,
            f.Type,
            f.Options?.Select(o => new Models.Marketplace.DTOs.ExternalFieldOption(o.Value, o.Label)).ToList(),
            f.Description,
            f.Unit,
            f.MaxLength,
            f.MinValue,
            f.MaxValue,
            f.ValidationPattern,
            f.Group,
            f.SortOrder
        )).ToList();
    }

    public async Task<Models.Marketplace.DTOs.CreateCategoryResult> CreateCategoryAsync(
        MarketplaceConnection connection,
        Models.Marketplace.DTOs.CreateCategoryRequest request,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var sdkRequest = new Delegat.Marketplace.SDK.DTOs.CreateCategoryRequest
        {
            Name = request.Name,
            Description = request.Description,
            ParentId = request.ParentId,
            StoreId = request.StoreId,
            LanguageId = request.LanguageId,
            SortOrder = request.SortOrder,
            Fields = request.Fields.Select(f => new Delegat.Marketplace.SDK.DTOs.CreateCategoryField
            {
                Key = f.Key,
                Name = f.Name,
                Type = f.Type,
                IsRequired = f.IsRequired,
                Description = f.Description,
                Group = f.Group,
                SortOrder = f.SortOrder,
                Options = f.Options?.Select(o => new Delegat.Marketplace.SDK.DTOs.ExternalFieldOption(o.Value, o.Label)).ToList()
            }).ToList()
        };

        var result = await _plugin.CreateCategoryAsync(context, sdkRequest, ct);
        return new Models.Marketplace.DTOs.CreateCategoryResult(
            result.Success,
            result.ExternalCategoryId,
            result.ExternalCategoryName,
            result.Fields?.Select(f => new Models.Marketplace.DTOs.ExternalField(
                f.Key,
                f.Name,
                f.IsRequired,
                f.Type,
                f.Options?.Select(o => new Models.Marketplace.DTOs.ExternalFieldOption(o.Value, o.Label)).ToList(),
                f.Description,
                f.Unit,
                f.MaxLength,
                f.MinValue,
                f.MaxValue,
                f.ValidationPattern,
                f.Group,
                f.SortOrder,
                f.IsAspect,
                f.AllowCustomValues,
                f.MaxValues
            )).ToList(),
            result.ErrorMessage,
            result.Details);
    }

    #endregion

    #region Products

    public async Task<Models.Marketplace.DTOs.ExternalProduct?> ImportProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var product = await _plugin.ImportProductAsync(context, externalProductId, ct);

        if (product == null) return null;

        var mapped = MapImportedProduct(product);
        var filled = await AttachMissingPricesAsync(connection, [mapped], ct);
        return filled[0];
    }

    public async Task<List<Models.Marketplace.DTOs.ExternalProduct>> ImportProductsBatchAsync(
        MarketplaceConnection connection,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var products = await _plugin.ImportProductsBatchAsync(context, externalProductIds, ct);

        return await AttachMissingPricesAsync(
            connection,
            products.Select(MapImportedProduct).ToList(),
            ct);
    }

    public async Task<Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct>> ImportAllProductsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.ImportAllProductsAsync(context, pageSize, cursor, offerIdFilter, ct);

        var items = await AttachMissingPricesAsync(
            connection,
            result.Items.Select(MapImportedProduct).ToList(),
            ct);

        return new Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct>(
            items,
            result.HasMore,
            result.NextCursor,
            result.TotalCount
        );
    }

    public async Task<Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct>> SearchPublicCatalogAsync(
        MarketplaceConnection connection,
        string query,
        int pageSize = 50,
        string? cursor = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await MarketplaceStorefrontError.GuardAsync(
            () => _plugin.SearchPublicCatalogAsync(context, query, pageSize, cursor, ct));
        return new Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct>(
            result.Items.Select(MapImportedProduct).ToList(),
            result.HasMore,
            result.NextCursor,
            result.TotalCount
        );
    }

    public async Task<Models.Marketplace.DTOs.ExternalProduct?> GetPublicProductAsync(
        MarketplaceConnection connection,
        string publicId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var product = await MarketplaceStorefrontError.GuardAsync(
            () => _plugin.GetPublicProductAsync(context, publicId, ct));
        return product == null ? null : MapImportedProduct(product);
    }

    private static Models.Marketplace.DTOs.ExternalProduct MapImportedProduct(
        Delegat.Marketplace.SDK.DTOs.ExternalProduct p)
    {
        return new Models.Marketplace.DTOs.ExternalProduct(
            p.ExternalId,
            p.Sku,
            p.Name,
            p.Description,
            p.Price,
            p.Quantity,
            p.CategoryId,
            p.CategoryName,
            p.Attributes,
            p.ImageUrls,
            p.Barcode,
            p.Brand,
            p.RawData,
            p.GroupId,
            p.GroupName,
            p.Variants?.Select(v => new Models.Marketplace.DTOs.ExternalProductVariant(
                v.VariantId,
                v.TechSize,
                v.MarketplaceSize,
                v.Barcodes,
                v.Price,
                v.Quantity,
                v.Extra)).ToList()
        );
    }

    private async Task<List<Models.Marketplace.DTOs.ExternalProduct>> AttachMissingPricesAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.ExternalProduct> products,
        CancellationToken ct)
    {
        if (!Capabilities.HasFlag(Marketplace.MarketplaceCapabilities.ImportPrices))
            return products;

        return await MarketplaceImportPrice.FillMissingAsync(
            products,
            (ids, token) => ImportPricesAsync(connection, ids, token),
            ct);
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> ExportProductAsync(
        MarketplaceConnection connection,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("ExportProductAsync called with {DataKeyCount} data keys", productData.Count);
        var context = CreateContext(connection);
        var result = await _plugin.ExportProductAsync(context, productData, existingExternalId, ct);
        _logger.LogDebug("ExportProductAsync returned: Success={Success}, Error={Error}", result.Success, result.ErrorMessage);

        return new Models.Marketplace.DTOs.ExportResult(
            result.Success,
            result.ExternalProductId,
            result.ErrorMessage,
            result.Details,
            result.Retryable
        );
    }

    public async Task<Models.Marketplace.DTOs.BulkExportResult> ExportProductsBulkAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.BulkExportItem> items,
        CancellationToken ct = default)
        => await ExportProductsBulkCoreAsync(connection, items, progress: null, ct);

    public async Task<Models.Marketplace.DTOs.BulkExportResult> ExportProductsBulkAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.BulkExportItem> items,
        Func<string, CancellationToken, Task> progress,
        CancellationToken ct = default)
        => await ExportProductsBulkCoreAsync(connection, items, progress, ct);

    private async Task<Models.Marketplace.DTOs.BulkExportResult> ExportProductsBulkCoreAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.BulkExportItem> items,
        Func<string, CancellationToken, Task>? progress,
        CancellationToken ct)
    {
        var context = CreateContext(connection);
        context.Progress = progress;

        // Convert backend BulkExportItem → SDK BulkExportItem
        var sdkItems = items.Select(i => new Delegat.Marketplace.SDK.DTOs.BulkExportItem(
            i.LocalProductId,
            i.ProductData,
            i.ExistingExternalId
        )).ToList();

        var result = await _plugin.ExportProductsBulkAsync(context, sdkItems, ct);

        // Convert SDK BulkExportResult → backend BulkExportResult
        return new Models.Marketplace.DTOs.BulkExportResult(
            result.TotalCount,
            result.SuccessCount,
            result.FailedCount,
            result.Items?.Select(i => new Models.Marketplace.DTOs.ExportItemResult(
                i.LocalProductId,
                i.Success,
                i.ExternalProductId,
                i.ErrorMessage,
                i.Details,
                i.Retryable
            )).ToList()
        );
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> UpdateSkuAsync(
        MarketplaceConnection connection,
        string externalProductId,
        string newSku,
        string? currentSku = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.UpdateSkuAsync(context, externalProductId, newSku, currentSku, ct);

        return new Models.Marketplace.DTOs.ExportResult(
            result.Success,
            result.ExternalProductId,
            result.ErrorMessage,
            result.Details
        );
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> ArchiveProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.ArchiveProductAsync(context, externalProductId, ct);
        return new Models.Marketplace.DTOs.ExportResult(
            result.Success, result.ExternalProductId, result.ErrorMessage, result.Details);
    }

    #endregion

    #region Prices & Stocks

    public async Task<List<Models.Marketplace.DTOs.PriceInfo>> ImportPricesAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var prices = await _plugin.ImportPricesAsync(context, externalProductIds, ct);

        return prices.Select(p => new Models.Marketplace.DTOs.PriceInfo(
            p.ExternalProductId,
            p.Price,
            p.OldPrice,
            p.Stock,
            p.DiscountedPrice,
            p.Currency ?? "RUB",
            p.VariantId,
            p.MinPrice,
            p.ExternalSku,
            p.Discount,
            p.NetPrice,
            p.AutoActionEnabled,
            p.PriceStrategyEnabled,
            p.MinPriceForAutoActionsEnabled
        )).ToList();
    }

    public async Task<Models.Marketplace.DTOs.ImportResult> ExportPricesAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.PriceInfo> prices,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var sdkPrices = prices.Select(p => new PriceInfo(
            p.ExternalProductId,
            p.Price,
            p.OldPrice,
            p.Stock,
            p.DiscountedPrice,
            p.Currency ?? "RUB",
            p.VariantId,
            p.MinPrice,
            p.ExternalSku,
            p.Discount,
            p.NetPrice,
            p.AutoActionEnabled,
            p.PriceStrategyEnabled,
            p.MinPriceForAutoActionsEnabled
        )).ToList();

        var result = await _plugin.ExportPricesAsync(context, sdkPrices, ct);

        return new Models.Marketplace.DTOs.ImportResult(
            result.TotalCount,
            result.SuccessCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.ImportError(
                e.ExternalId,
                e.Message,
                e.Details
            )).ToList()
        );
    }

    public async Task<List<Models.Marketplace.DTOs.StockInfo>> ImportStocksAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var stocks = await _plugin.ImportStocksAsync(context, externalProductIds, ct);

        return stocks.Select(s => new Models.Marketplace.DTOs.StockInfo(
            s.ExternalProductId,
            s.Quantity,
            s.WarehouseId,
            s.WarehouseName
        )).ToList();
    }

    public async Task<Models.Marketplace.DTOs.ImportResult> ExportStocksAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.StockInfo> stocks,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var sdkStocks = stocks.Select(s => new StockInfo(
            s.ExternalProductId,
            s.Quantity,
            s.WarehouseId,
            s.WarehouseName
        )).ToList();

        var result = await _plugin.ExportStocksAsync(context, sdkStocks, ct);

        return new Models.Marketplace.DTOs.ImportResult(
            result.TotalCount,
            result.SuccessCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.ImportError(
                e.ExternalId,
                e.Message,
                e.Details
            )).ToList()
        );
    }

    public async Task<Models.Marketplace.DTOs.PagedResult<ExternalOrder>> GetOrdersAsync(
        MarketplaceConnection connection,
        OrderQuery? query = null,
        CancellationToken ct = default)
    {
        var result = await _plugin.GetOrdersAsync(CreateContext(connection), query, ct);
        return new Models.Marketplace.DTOs.PagedResult<ExternalOrder>(
            result.Items ?? [],
            result.HasMore,
            result.NextCursor,
            result.TotalCount);
    }

    public Task<ExternalOrder?> GetOrderAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        CancellationToken ct = default)
        => _plugin.GetOrderAsync(CreateContext(connection), externalOrderId, ct);

    public Task<OrderStatusUpdateResult> UpdateOrderStatusAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        OrderStatusUpdate update,
        CancellationToken ct = default)
        => _plugin.UpdateOrderStatusAsync(CreateContext(connection), externalOrderId, update, ct);

    public Task<OrderLabelResult> GetOrderLabelAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        CancellationToken ct = default)
        => _plugin.GetOrderLabelAsync(CreateContext(connection), externalOrderId, ct);

    public Task<List<ExternalFulfillmentLocation>> GetFulfillmentLocationsAsync(
        MarketplaceConnection connection,
        FulfillmentLocationQuery? query = null,
        CancellationToken ct = default)
        => _plugin.GetFulfillmentLocationsAsync(CreateContext(connection), query, ct);

    public async Task<Models.Marketplace.DTOs.PagedResult<ExternalShipment>> GetShipmentsAsync(
        MarketplaceConnection connection,
        ShipmentQuery? query = null,
        CancellationToken ct = default)
    {
        var result = await _plugin.GetShipmentsAsync(CreateContext(connection), query, ct);
        return new Models.Marketplace.DTOs.PagedResult<ExternalShipment>(
            result.Items ?? [],
            result.HasMore,
            result.NextCursor,
            result.TotalCount);
    }

    public Task<ExternalShipment?> GetShipmentAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => _plugin.GetShipmentAsync(CreateContext(connection), externalShipmentId, ct);

    public Task<ShipmentMutationResult> CreateShipmentAsync(
        MarketplaceConnection connection,
        CreateShipmentRequest request,
        CancellationToken ct = default)
        => _plugin.CreateShipmentAsync(CreateContext(connection), request, ct);

    public Task<ShipmentMutationResult> AddOrdersToShipmentAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        ShipmentOrdersUpdate update,
        CancellationToken ct = default)
        => _plugin.AddOrdersToShipmentAsync(CreateContext(connection), externalShipmentId, update, ct);

    public Task<CreateShipmentBoxesResult> CreateShipmentBoxesAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CreateShipmentBoxesRequest request,
        CancellationToken ct = default)
        => _plugin.CreateShipmentBoxesAsync(CreateContext(connection), externalShipmentId, request, ct);

    public Task<List<ExternalShipmentBox>> GetShipmentBoxesAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => _plugin.GetShipmentBoxesAsync(CreateContext(connection), externalShipmentId, ct);

    public Task<ShipmentMutationResult> AssignOrdersToShipmentBoxAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        string externalBoxId,
        ShipmentOrdersUpdate update,
        CancellationToken ct = default)
        => _plugin.AssignOrdersToShipmentBoxAsync(
            CreateContext(connection), externalShipmentId, externalBoxId, update, ct);

    public Task<OrderLabelResult> GetShipmentBoxLabelAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        string externalBoxId,
        CancellationToken ct = default)
        => _plugin.GetShipmentBoxLabelAsync(
            CreateContext(connection), externalShipmentId, externalBoxId, ct);

    public Task<OrderLabelResult> GetShipmentLabelAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => _plugin.GetShipmentLabelAsync(CreateContext(connection), externalShipmentId, ct);

    public Task<ShipmentMutationResult> DeliverShipmentAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => _plugin.DeliverShipmentAsync(CreateContext(connection), externalShipmentId, ct);

    public async Task<Models.Marketplace.DTOs.PagedResult<ExternalPromotion>> GetPromotionsAsync(
        MarketplaceConnection connection,
        PromotionQuery? query = null,
        CancellationToken ct = default)
    {
        var result = await _plugin.GetPromotionsAsync(CreateContext(connection), query, ct);
        return new Models.Marketplace.DTOs.PagedResult<ExternalPromotion>(
            result.Items ?? [],
            result.HasMore,
            result.NextCursor,
            result.TotalCount);
    }

    public Task<ExternalPromotion?> GetPromotionAsync(
        MarketplaceConnection connection,
        string externalPromotionId,
        CancellationToken ct = default)
        => _plugin.GetPromotionAsync(CreateContext(connection), externalPromotionId, ct);

    public Task<PromotionProductsResult> UpdatePromotionProductsAsync(
        MarketplaceConnection connection,
        string externalPromotionId,
        PromotionProductsUpdate update,
        CancellationToken ct = default)
        => _plugin.UpdatePromotionProductsAsync(CreateContext(connection), externalPromotionId, update, ct);

    #endregion

    #region Product Grouping

    public Models.Marketplace.DTOs.FieldBasedGroupingConfig? GetFieldBasedGroupingConfig()
    {
        var config = _plugin.GetFieldBasedGroupingConfig();
        if (config == null) return null;

        return new Models.Marketplace.DTOs.FieldBasedGroupingConfig(
            config.GroupFieldKey,
            config.MarketplaceGeneratesId,
            config.Description
        );
    }

    public async Task<Models.Marketplace.DTOs.CreateGroupResult> CreateGroupAsync(
        MarketplaceConnection connection,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.CreateGroupAsync(context, productExternalIds, groupName, ct);

        return new Models.Marketplace.DTOs.CreateGroupResult(
            result.Success,
            result.ExternalGroupId,
            result.ErrorMessage,
            result.Details
        );
    }

    public async Task<Models.Marketplace.DTOs.GroupMembershipResult> AddProductsToGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.AddProductsToGroupAsync(context, externalGroupId, productExternalIds, ct);

        return new Models.Marketplace.DTOs.GroupMembershipResult(
            result.Success,
            result.AddedCount,
            result.RemovedCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.GroupMembershipError(
                e.ExternalProductId,
                e.Message,
                e.Details
            )).ToList(),
            result.ErrorMessage
        );
    }

    public async Task<Models.Marketplace.DTOs.GroupMembershipResult> RemoveProductsFromGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.RemoveProductsFromGroupAsync(context, externalGroupId, productExternalIds, ct);

        return new Models.Marketplace.DTOs.GroupMembershipResult(
            result.Success,
            result.AddedCount,
            result.RemovedCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.GroupMembershipError(
                e.ExternalProductId,
                e.Message,
                e.Details
            )).ToList(),
            result.ErrorMessage
        );
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> DeleteGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.DeleteGroupAsync(context, externalGroupId, ct);

        return new Models.Marketplace.DTOs.ExportResult(
            result.Success,
            result.ExternalProductId,
            result.ErrorMessage,
            result.Details
        );
    }

    public async Task<Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalGroup>> ImportGroupsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.ImportGroupsAsync(context, pageSize, cursor, ct);

        return new Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalGroup>(
            result.Items.Select(g => new Models.Marketplace.DTOs.ExternalGroup(
                g.ExternalId,
                g.Name,
                g.ProductExternalIds,
                g.AdditionalData
            )).ToList(),
            result.HasMore,
            result.NextCursor,
            result.TotalCount
        );
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Create ConnectionContext from MarketplaceConnection.
    /// </summary>
    private ConnectionContext CreateContext(MarketplaceConnection connection)
    {
        var credentials = new Dictionary<string, string>();
        var settings = new Dictionary<string, string>();

        // Parse credentials JSON (decrypt if encrypted)
        var rawCreds = connection.credentialsJson;
        if (_encryptionService != null && !string.IsNullOrEmpty(rawCreds) && _encryptionService.IsEncrypted(rawCreds))
            rawCreds = _encryptionService.Decrypt(rawCreds);

        if (!string.IsNullOrEmpty(rawCreds))
        {
            try
            {
                var creds = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawCreds);
                if (creds != null)
                {
                    foreach (var kvp in creds)
                    {
                        credentials[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        // Parse settings JSON
        if (!string.IsNullOrEmpty(connection.settingsJson))
        {
            try
            {
                var setts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connection.settingsJson);
                if (setts != null)
                {
                    foreach (var kvp in setts)
                    {
                        settings[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        return new ConnectionContext
        {
            ConnectionId = connection.Id,
            StoreId = connection.storeId,
            AdapterKey = connection.adapterKey ?? _plugin.Key,
            Credentials = credentials,
            Settings = settings,
            Logger = _logger,
            HttpLogger = _httpLogger,
            MetadataCache = _metadataCache,
            MediaBlobReader = _mediaBlobReader,
            PageLoader = _pageLoader
        };
    }

    #endregion
}
