namespace PrintO.Marketplace.SDK;

/// <summary>
/// Capabilities supported by a marketplace adapter plugin.
/// Each plugin defines which operations it supports.
/// </summary>
[Flags]
public enum MarketplaceCapabilities
{
    /// <summary>
    /// No capabilities supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// Get list of categories
    /// </summary>
    GetCategories = 1 << 0,

    /// <summary>
    /// Get category fields (required and optional)
    /// </summary>
    GetCategoryFields = 1 << 1,

    /// <summary>
    /// Import single product
    /// </summary>
    ImportProduct = 1 << 2,

    /// <summary>
    /// Bulk import products
    /// </summary>
    ImportAllProducts = 1 << 3,

    /// <summary>
    /// Export product to marketplace
    /// </summary>
    ExportProduct = 1 << 4,

    /// <summary>
    /// Update product SKU
    /// </summary>
    UpdateSku = 1 << 5,

    /// <summary>
    /// Import prices from marketplace
    /// </summary>
    ImportPrices = 1 << 6,

    /// <summary>
    /// Export prices to marketplace
    /// </summary>
    ExportPrices = 1 << 7,

    /// <summary>
    /// Import stocks from marketplace
    /// </summary>
    ImportStocks = 1 << 8,

    /// <summary>
    /// Export stocks to marketplace
    /// </summary>
    ExportStocks = 1 << 9,

    /// <summary>
    /// Get orders
    /// </summary>
    GetOrders = 1 << 10,

    /// <summary>
    /// Update order status
    /// </summary>
    UpdateOrderStatus = 1 << 11,

    #region Product Grouping

    /// <summary>
    /// Field-based grouping (e.g., Ozon).
    /// Products are grouped by setting a special field value during export.
    /// </summary>
    FieldBasedGrouping = 1 << 12,

    /// <summary>
    /// Endpoint-based grouping (e.g., Wildberries).
    /// Products are grouped via separate API calls after export.
    /// </summary>
    EndpointBasedGrouping = 1 << 13,

    /// <summary>
    /// Import groups from marketplace
    /// </summary>
    ImportGroups = 1 << 14,

    /// <summary>
    /// Delete groups on marketplace
    /// </summary>
    DeleteGroups = 1 << 15,

    /// <summary>
    /// Modify group membership (add/remove products)
    /// </summary>
    ModifyGroupMembership = 1 << 16,

    /// <summary>
    /// Batch import multiple products in one call
    /// </summary>
    ImportProductBatch = 1 << 17,

    /// <summary>
    /// Bulk export multiple products in one call
    /// </summary>
    ExportProductBulk = 1 << 18,

    /// <summary>
    /// Archive product on marketplace
    /// </summary>
    ArchiveProduct = 1 << 19,

    #endregion

    /// <summary>
    /// Basic capabilities for most marketplaces
    /// </summary>
    Basic = GetCategories | GetCategoryFields | ImportProduct | ExportProduct,

    /// <summary>
    /// Full product capabilities
    /// </summary>
    FullProducts = Basic | ImportAllProducts | ImportPrices | ExportPrices | ImportStocks | ExportStocks
}
