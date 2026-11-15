namespace Delegat.Services.Marketplace;

/// <summary>
/// Marketplace adapter capabilities.
/// Each adapter defines which operations it supports.
/// </summary>
[Flags]
public enum MarketplaceCapabilities
{
    None = 0,

    /// <summary>
    /// Retrieving the list of categories
    /// </summary>
    GetCategories = 1 << 0,

    /// <summary>
    /// Retrieving category fields (required and optional)
    /// </summary>
    GetCategoryFields = 1 << 1,

    /// <summary>
    /// Importing a single product
    /// </summary>
    ImportProduct = 1 << 2,

    /// <summary>
    /// Bulk product import
    /// </summary>
    ImportAllProducts = 1 << 3,

    /// <summary>
    /// Exporting a product to the marketplace
    /// </summary>
    ExportProduct = 1 << 4,

    /// <summary>
    /// Updating product SKU/article
    /// </summary>
    UpdateSku = 1 << 5,

    /// <summary>
    /// Importing prices from the marketplace
    /// </summary>
    ImportPrices = 1 << 6,

    /// <summary>
    /// Exporting prices to the marketplace
    /// </summary>
    ExportPrices = 1 << 7,

    /// <summary>
    /// Importing stock levels from the marketplace
    /// </summary>
    ImportStocks = 1 << 8,

    /// <summary>
    /// Exporting stock levels to the marketplace
    /// </summary>
    ExportStocks = 1 << 9,

    /// <summary>
    /// Retrieving orders
    /// </summary>
    GetOrders = 1 << 10,

    /// <summary>
    /// Updating order status
    /// </summary>
    UpdateOrderStatus = 1 << 11,

    #region Product Grouping

    /// <summary>
    /// Field-based grouping (e.g. Ozon).
    /// Products are grouped via a special field during export.
    /// </summary>
    FieldBasedGrouping = 1 << 12,

    /// <summary>
    /// Endpoint-based grouping (e.g. Wildberries).
    /// Products are grouped via separate API calls after export.
    /// </summary>
    EndpointBasedGrouping = 1 << 13,

    /// <summary>
    /// Importing groups from the marketplace
    /// </summary>
    ImportGroups = 1 << 14,

    /// <summary>
    /// Deleting groups on the marketplace
    /// </summary>
    DeleteGroups = 1 << 15,

    /// <summary>
    /// Modifying group membership (adding/removing products)
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

    /// <summary>
    /// Create marketplace category and fields from a local category definition
    /// </summary>
    CreateCategory = 1 << 20,

    /// <summary>
    /// Search another seller’s public storefront and fetch a public card by id
    /// (not the connected cabinet). Used by the Catalog scrape tool.
    /// </summary>
    SearchPublicCatalog = 1 << 21,

    /// <summary>
    /// List marketplace promotions, calendar campaigns, coupons, or auto-actions.
    /// </summary>
    GetPromotions = 1 << 22,

    /// <summary>
    /// Enroll or remove products in marketplace promotions.
    /// </summary>
    UpdatePromotions = 1 << 23,

    /// <summary>
    /// Download a packing label / barcode sticker for one live order.
    /// </summary>
    GetOrderLabels = 1 << 24,

    /// <summary>
    /// List seller warehouses and marketplace offices (pass-through).
    /// </summary>
    GetFulfillmentLocations = 1 << 25,

    /// <summary>
    /// List, create, and box live shipments / supplies.
    /// </summary>
    ManageShipments = 1 << 26,

    #endregion

    /// <summary>
    /// Basic capability set for most marketplaces
    /// </summary>
    Basic = GetCategories | GetCategoryFields | ImportProduct | ExportProduct,

    /// <summary>
    /// Full product capability set
    /// </summary>
    FullProducts = Basic | ImportAllProducts | ImportPrices | ExportPrices | ImportStocks | ExportStocks
}
