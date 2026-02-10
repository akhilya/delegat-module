using PrintO.Marketplace.SDK.DTOs;

namespace PrintO.Marketplace.SDK;

/// <summary>
/// Main interface for marketplace adapter plugins.
/// Implement this interface to create a marketplace integration.
/// </summary>
public interface IMarketplacePlugin
{
    #region Metadata

    /// <summary>
    /// Unique adapter key (e.g. "wildberries", "ozon", "yandex")
    /// Used for identification in the database
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Display name of the marketplace
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Plugin version
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Marketplace description
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Logo/icon URL (can be data URI)
    /// </summary>
    string? LogoUrl { get; }

    /// <summary>
    /// Capabilities supported by this adapter
    /// </summary>
    MarketplaceCapabilities Capabilities { get; }

    /// <summary>
    /// Get the schema for connection credentials
    /// </summary>
    CredentialsSchema GetCredentialsSchema();

    #endregion

    #region Connection

    /// <summary>
    /// Test connection to the marketplace API
    /// </summary>
    Task<ConnectionTestResult> TestConnectionAsync(ConnectionContext context, CancellationToken ct = default);

    #endregion

    #region Categories

    /// <summary>
    /// Get marketplace categories
    /// </summary>
    /// <param name="context">Connection context</param>
    /// <param name="parentId">Parent category ID (null for root)</param>
    /// <param name="searchQuery">Search by name</param>
    /// <param name="ct">Cancellation token</param>
    Task<List<ExternalCategory>> GetCategoriesAsync(
        ConnectionContext context,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get all fields for a category (required + optional)
    /// </summary>
    Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default);

    #endregion

    #region Products

    /// <summary>
    /// Import single product from marketplace
    /// </summary>
    Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default);

    /// <summary>
    /// Import multiple products by IDs in a single batch call.
    /// More efficient than calling ImportProductAsync N times.
    /// </summary>
    Task<List<ExternalProduct>> ImportProductsBatchAsync(
        ConnectionContext context,
        List<string> externalProductIds,
        CancellationToken ct = default);

    /// <summary>
    /// Import all products from marketplace (paginated)
    /// </summary>
    /// <param name="context">Connection context</param>
    /// <param name="pageSize">Page size</param>
    /// <param name="cursor">Cursor for pagination</param>
    /// <param name="offerIdFilter">Filter by offer_id/SKU (exact match)</param>
    /// <param name="ct">Cancellation token</param>
    Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export product to marketplace (create or update)
    /// </summary>
    /// <param name="context">Connection context</param>
    /// <param name="productData">Product data according to field mapping</param>
    /// <param name="existingExternalId">Existing product ID (null to create new)</param>
    /// <param name="ct">Cancellation token</param>
    Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export multiple products to marketplace in a single bulk operation.
    /// More efficient than calling ExportProductAsync N times.
    /// </summary>
    Task<BulkExportResult> ExportProductsBulkAsync(
        ConnectionContext context,
        List<BulkExportItem> items,
        CancellationToken ct = default);

    /// <summary>
    /// Update product SKU on marketplace
    /// Throws NotSupportedException if not supported
    /// </summary>
    Task<ExportResult> UpdateSkuAsync(
        ConnectionContext context,
        string externalProductId,
        string newSku,
        CancellationToken ct = default);

    #endregion

    #region Prices & Stocks

    /// <summary>
    /// Import prices from marketplace
    /// </summary>
    /// <param name="context">Connection context</param>
    /// <param name="externalProductIds">Product IDs (null for all)</param>
    /// <param name="ct">Cancellation token</param>
    Task<List<PriceInfo>> ImportPricesAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export prices to marketplace
    /// </summary>
    Task<ImportResult> ExportPricesAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct = default);

    /// <summary>
    /// Import stocks from marketplace
    /// </summary>
    Task<List<StockInfo>> ImportStocksAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Export stocks to marketplace
    /// </summary>
    Task<ImportResult> ExportStocksAsync(
        ConnectionContext context,
        List<StockInfo> stocks,
        CancellationToken ct = default);

    #endregion

    #region Product Grouping

    /// <summary>
    /// Get configuration for field-based grouping.
    /// Returns null if field-based grouping is not supported.
    /// The MappingService uses this to inject group_id into export data.
    /// </summary>
    FieldBasedGroupingConfig? GetFieldBasedGroupingConfig();

    /// <summary>
    /// Create a group via API endpoint (for endpoint-based grouping).
    /// </summary>
    /// <param name="context">Connection context</param>
    /// <param name="productExternalIds">External product IDs to include in the group</param>
    /// <param name="groupName">Optional group name</param>
    /// <param name="ct">Cancellation token</param>
    Task<CreateGroupResult> CreateGroupAsync(
        ConnectionContext context,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Add products to an existing group (for endpoint-based grouping).
    /// </summary>
    Task<GroupMembershipResult> AddProductsToGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default);

    /// <summary>
    /// Remove products from a group (for endpoint-based grouping).
    /// </summary>
    Task<GroupMembershipResult> RemoveProductsFromGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a group on the marketplace.
    /// </summary>
    Task<ExportResult> DeleteGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        CancellationToken ct = default);

    /// <summary>
    /// Import groups from marketplace (paginated).
    /// </summary>
    Task<PagedResult<ExternalGroup>> ImportGroupsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default);

    #endregion
}
