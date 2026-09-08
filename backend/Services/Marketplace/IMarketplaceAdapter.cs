using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using AdapterToolDescriptor = Delegat.Marketplace.SDK.AdapterToolDescriptor;
using AdapterToolRunResult = Delegat.Marketplace.SDK.AdapterToolRunResult;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Marketplace adapter interface.
/// Each marketplace implements this interface as a plugin.
/// </summary>
public interface IMarketplaceAdapter
{
    #region Metadata

    /// <summary>
    /// Unique adapter key (e.g. "wildberries", "ozon", "yandex").
    /// Used for identification in the database.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Display name of the marketplace
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Marketplace description
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// URL of the marketplace logo/icon
    /// </summary>
    string? LogoUrl { get; }

    /// <summary>
    /// Capabilities of this adapter
    /// </summary>
    MarketplaceCapabilities Capabilities { get; }

    /// <summary>
    /// URL template for editing a product on the marketplace.
    /// Use {externalId} as placeholder.
    /// </summary>
    string? ProductEditUrlTemplate { get; }

    /// <summary>
    /// Connection settings schema (JSON Schema for UI).
    /// Defines which fields are required for connection (apiKey, clientId, etc.).
    /// </summary>
    CredentialsSchema GetCredentialsSchema();

    /// <summary>
    /// Get rate limit configuration, or null if not supported.
    /// </summary>
    RateLimitConfig? GetRateLimitConfig();

    /// <summary>
    /// Get current API quota usage from the marketplace.
    /// </summary>
    Task<List<QuotaInfo>> GetQuotasAsync(MarketplaceConnection connection, CancellationToken ct = default);

    /// <summary>
    /// Get the rate limiter instance for a specific connection.
    /// Returns null if rate limiting is not tracked.
    /// </summary>
    MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection);

    /// <summary>
    /// Clears host metadata cache entries and plugin-local caches for this connection.
    /// </summary>
    int ClearMetadataCaches(MarketplaceConnection connection);

    /// <summary>
    /// Job-mode tools this adapter contributes to /tools. Empty by default.
    /// </summary>
    IReadOnlyList<AdapterToolDescriptor> GetTools() => [];

    /// <summary>
    /// Run a job-mode tool from <see cref="GetTools"/>. Connection is null when the tool does not need one.
    /// </summary>
    Task<AdapterToolRunResult> RunToolAsync(
        MarketplaceConnection? connection,
        string toolId,
        IReadOnlyDictionary<string, string?> parameters,
        CancellationToken ct = default)
        => throw new NotSupportedException("This adapter does not provide tools");

    #endregion

    #region Connection

    /// <summary>
    /// Test connection to the marketplace API
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default);

    #endregion

    #region Categories

    /// <summary>
    /// Get marketplace categories
    /// </summary>
    /// <param name="connection">Connection</param>
    /// <param name="parentId">Parent category ID (null for root categories)</param>
    /// <param name="searchQuery">Search by name</param>
    /// <param name="ct">Cancellation token</param>
    Task<List<ExternalCategory>> GetCategoriesAsync(
        MarketplaceConnection connection,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get all fields for a category (required + optional)
    /// </summary>
    Task<List<ExternalField>> GetCategoryFieldsAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default);

    /// <summary>
    /// Get price-API fields for dedicated price sync (separate from card/category fields).
    /// </summary>
    Task<List<ExternalField>> GetPriceFieldsAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default);

    /// <summary>
    /// Create a marketplace category and fields from a local category definition.
    /// </summary>
    Task<CreateCategoryResult> CreateCategoryAsync(
        MarketplaceConnection connection,
        CreateCategoryRequest request,
        CancellationToken ct = default);

    #endregion

    #region Products

    /// <summary>
    /// Import a single product from the marketplace
    /// </summary>
    Task<ExternalProduct?> ImportProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default);

    /// <summary>
    /// Import multiple products by IDs in a single batch call.
    /// </summary>
    Task<List<ExternalProduct>> ImportProductsBatchAsync(
        MarketplaceConnection connection,
        List<string> externalProductIds,
        CancellationToken ct = default);

    /// <summary>
    /// Import all products from the marketplace (with pagination)
    /// </summary>
    /// <param name="connection">Connection</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="cursor">Cursor for pagination</param>
    /// <param name="offerIdFilter">Filter by offer_id/SKU (exact match)</param>
    /// <param name="ct">Cancellation token</param>
    Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Search the public storefront (any seller), not the connected cabinet.
    /// </summary>
    Task<PagedResult<ExternalProduct>> SearchPublicCatalogAsync(
        MarketplaceConnection connection,
        string query,
        int pageSize = 50,
        string? cursor = null,
        CancellationToken ct = default)
        => throw new NotSupportedException($"Adapter '{Key}' does not support SearchPublicCatalog");

    /// <summary>
    /// Fetch one public storefront card by marketplace id.
    /// </summary>
    Task<ExternalProduct?> GetPublicProductAsync(
        MarketplaceConnection connection,
        string publicId,
        CancellationToken ct = default)
        => throw new NotSupportedException($"Adapter '{Key}' does not support GetPublicProduct");

    /// <summary>
    /// Export a product to the marketplace (create or update)
    /// </summary>
    /// <param name="connection">Connection</param>
    /// <param name="productData">Product data according to field mapping</param>
    /// <param name="existingExternalId">Existing product ID (null to create a new one)</param>
    /// <param name="ct">Cancellation token</param>
    Task<ExportResult> ExportProductAsync(
        MarketplaceConnection connection,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export multiple products to the marketplace in a single bulk operation.
    /// </summary>
    Task<BulkExportResult> ExportProductsBulkAsync(
        MarketplaceConnection connection,
        List<BulkExportItem> items,
        CancellationToken ct = default);

    /// <summary>
    /// Update product SKU/article on the marketplace.
    /// Throws NotSupportedException if the adapter does not support this operation.
    /// </summary>
    Task<ExportResult> UpdateSkuAsync(
        MarketplaceConnection connection,
        string externalProductId,
        string newSku,
        string? currentSku = null,
        CancellationToken ct = default);

    /// <summary>
    /// Archive a product on the marketplace.
    /// </summary>
    Task<ExportResult> ArchiveProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default);

    #endregion

    #region Prices & Stocks

    /// <summary>
    /// Import prices from the marketplace
    /// </summary>
    /// <param name="connection">Connection</param>
    /// <param name="externalProductIds">Product IDs (null for all)</param>
    /// <param name="ct">Cancellation token</param>
    Task<List<PriceInfo>> ImportPricesAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export prices to the marketplace
    /// </summary>
    Task<ImportResult> ExportPricesAsync(
        MarketplaceConnection connection,
        List<PriceInfo> prices,
        CancellationToken ct = default);

    /// <summary>
    /// Import stock levels from the marketplace
    /// </summary>
    Task<List<StockInfo>> ImportStocksAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export stock levels to the marketplace
    /// </summary>
    Task<ImportResult> ExportStocksAsync(
        MarketplaceConnection connection,
        List<StockInfo> stocks,
        CancellationToken ct = default);

    #endregion

    #region Orders & promotions

    /// <summary>
    /// List orders from the connected cabinet (pass-through; not stored locally).
    /// </summary>
    Task<PagedResult<Delegat.Marketplace.SDK.DTOs.ExternalOrder>> GetOrdersAsync(
        MarketplaceConnection connection,
        Delegat.Marketplace.SDK.DTOs.OrderQuery? query = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("GetOrders is not supported");

    /// <summary>
    /// Get one order by marketplace id.
    /// </summary>
    Task<Delegat.Marketplace.SDK.DTOs.ExternalOrder?> GetOrderAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        CancellationToken ct = default)
        => throw new NotSupportedException("GetOrders is not supported");

    /// <summary>
    /// Confirm, pack, ship, complete, or cancel an order on the marketplace.
    /// </summary>
    Task<Delegat.Marketplace.SDK.DTOs.OrderStatusUpdateResult> UpdateOrderStatusAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        Delegat.Marketplace.SDK.DTOs.OrderStatusUpdate update,
        CancellationToken ct = default)
        => throw new NotSupportedException("UpdateOrderStatus is not supported");

    /// <summary>
    /// Download the packing label / barcode sticker for one order (pass-through; not stored).
    /// </summary>
    Task<Delegat.Marketplace.SDK.DTOs.OrderLabelResult> GetOrderLabelAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        CancellationToken ct = default)
        => throw new NotSupportedException("GetOrderLabels is not supported");

    Task<List<Delegat.Marketplace.SDK.DTOs.ExternalFulfillmentLocation>> GetFulfillmentLocationsAsync(
        MarketplaceConnection connection,
        Delegat.Marketplace.SDK.DTOs.FulfillmentLocationQuery? query = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("GetFulfillmentLocations is not supported");

    Task<PagedResult<Delegat.Marketplace.SDK.DTOs.ExternalShipment>> GetShipmentsAsync(
        MarketplaceConnection connection,
        Delegat.Marketplace.SDK.DTOs.ShipmentQuery? query = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.ExternalShipment?> GetShipmentAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.ShipmentMutationResult> CreateShipmentAsync(
        MarketplaceConnection connection,
        Delegat.Marketplace.SDK.DTOs.CreateShipmentRequest request,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.ShipmentMutationResult> AddOrdersToShipmentAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        Delegat.Marketplace.SDK.DTOs.ShipmentOrdersUpdate update,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.CreateShipmentBoxesResult> CreateShipmentBoxesAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        Delegat.Marketplace.SDK.DTOs.CreateShipmentBoxesRequest request,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<List<Delegat.Marketplace.SDK.DTOs.ExternalShipmentBox>> GetShipmentBoxesAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.ShipmentMutationResult> AssignOrdersToShipmentBoxAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        string externalBoxId,
        Delegat.Marketplace.SDK.DTOs.ShipmentOrdersUpdate update,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.OrderLabelResult> GetShipmentBoxLabelAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        string externalBoxId,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.OrderLabelResult> GetShipmentLabelAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    Task<Delegat.Marketplace.SDK.DTOs.ShipmentMutationResult> DeliverShipmentAsync(
        MarketplaceConnection connection,
        string externalShipmentId,
        CancellationToken ct = default)
        => throw new NotSupportedException("ManageShipments is not supported");

    /// <summary>
    /// List promotions, calendar campaigns, coupons, or auto-actions.
    /// </summary>
    Task<PagedResult<Delegat.Marketplace.SDK.DTOs.ExternalPromotion>> GetPromotionsAsync(
        MarketplaceConnection connection,
        Delegat.Marketplace.SDK.DTOs.PromotionQuery? query = null,
        CancellationToken ct = default)
        => throw new NotSupportedException("GetPromotions is not supported");

    /// <summary>
    /// Get one promotion, optionally including enrolled product ids.
    /// </summary>
    Task<Delegat.Marketplace.SDK.DTOs.ExternalPromotion?> GetPromotionAsync(
        MarketplaceConnection connection,
        string externalPromotionId,
        CancellationToken ct = default)
        => throw new NotSupportedException("GetPromotions is not supported");

    /// <summary>
    /// Add or remove products from a promotion.
    /// </summary>
    Task<Delegat.Marketplace.SDK.DTOs.PromotionProductsResult> UpdatePromotionProductsAsync(
        MarketplaceConnection connection,
        string externalPromotionId,
        Delegat.Marketplace.SDK.DTOs.PromotionProductsUpdate update,
        CancellationToken ct = default)
        => throw new NotSupportedException("UpdatePromotions is not supported");

    #endregion

    #region Product Grouping

    /// <summary>
    /// Get configuration for field-based grouping.
    /// Returns null if field-based grouping is not supported.
    /// MappingService uses this to inject group_id into export data.
    /// </summary>
    FieldBasedGroupingConfig? GetFieldBasedGroupingConfig();

    /// <summary>
    /// Create a group via API endpoint (for endpoint-based grouping).
    /// </summary>
    /// <param name="connection">Connection</param>
    /// <param name="productExternalIds">External product IDs to include in the group</param>
    /// <param name="groupName">Optional group name</param>
    /// <param name="ct">Cancellation token</param>
    Task<CreateGroupResult> CreateGroupAsync(
        MarketplaceConnection connection,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Add products to an existing group (for endpoint-based grouping).
    /// </summary>
    Task<GroupMembershipResult> AddProductsToGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default);

    /// <summary>
    /// Remove products from a group (for endpoint-based grouping).
    /// </summary>
    Task<GroupMembershipResult> RemoveProductsFromGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a group on the marketplace.
    /// </summary>
    Task<ExportResult> DeleteGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        CancellationToken ct = default);

    /// <summary>
    /// Import groups from the marketplace (with pagination).
    /// </summary>
    Task<PagedResult<ExternalGroup>> ImportGroupsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default);

    #endregion
}

/// <summary>
/// Connection settings field schema
/// </summary>
public class CredentialsSchema
{
    /// <summary>
    /// Configuration fields
    /// </summary>
    public List<CredentialField> Fields { get; set; } = new();
}

/// <summary>
/// Credential input field description
/// </summary>
public class CredentialField
{
    /// <summary>
    /// Field key (e.g. "apiKey", "clientId")
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Description/hint
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Field type: "text", "password", "number", "select"
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>
    /// Whether the field is required
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Hide the value (for passwords/tokens)
    /// </summary>
    public bool IsSecret { get; set; } = false;

    /// <summary>
    /// Default value
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Options for select
    /// </summary>
    public List<SelectOption>? Options { get; set; }

    /// <summary>
    /// Placeholder
    /// </summary>
    public string? Placeholder { get; set; }
}

/// <summary>
/// Option for a dropdown list
/// </summary>
public class SelectOption
{
    public string Value { get; set; } = null!;
    public string Label { get; set; } = null!;
}
