using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Products;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Marketplace;

/// <summary>
/// Links a local product to its counterpart on an external marketplace.
/// </summary>
public class ProductMarketplaceLink : IEntity, IMapToDTO<ProductMarketplaceLink.LinkDTO>,
    IAddable<ProductMarketplaceLink.AddForm>, IUpdateable<ProductMarketplaceLink.UpdateForm>
{
    /// <summary>
    /// Unique link identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the local product.
    /// </summary>
    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the local product.
    /// </summary>
    [InverseProperty(nameof(Products.Product.marketplaceLinks))]
    public Product product { get; set; } = null!;

    /// <summary>
    /// Foreign key to the marketplace connection.
    /// </summary>
    [ForeignKey(nameof(connection))]
    public int connectionId { get; set; }

    /// <summary>
    /// Navigation property to the marketplace connection.
    /// </summary>
    [InverseProperty(nameof(MarketplaceConnection.productLinks))]
    public MarketplaceConnection connection { get; set; } = null!;

    /// <summary>
    /// Product identifier on the marketplace.
    /// </summary>
    [StringLength(EXTERNAL_ID_MAX_LENGTH)]
    public string externalProductId { get; set; } = null!;

    /// <summary>
    /// SKU on the marketplace (may differ from local SKU).
    /// </summary>
    [StringLength(EXTERNAL_SKU_MAX_LENGTH)]
    public string? externalSku { get; set; }

    /// <summary>
    /// Current synchronization status.
    /// </summary>
    public ProductMarketplaceStatus status { get; set; } = ProductMarketplaceStatus.NotSynced;

    /// <summary>
    /// Error message if status is SyncError.
    /// </summary>
    [StringLength(ERROR_MESSAGE_MAX_LENGTH)]
    public string? lastErrorMessage { get; set; }

    /// <summary>
    /// Timestamp of the last synchronization.
    /// </summary>
    public DateTime? lastSyncAt { get; set; }

    /// <summary>
    /// Timestamp when the link was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Price on the marketplace (for comparison with local price).
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? externalPrice { get; set; }

    /// <summary>
    /// Stock quantity on the marketplace.
    /// </summary>
    public int? externalStock { get; set; }

    /// <summary>
    /// FK to the selected category mapping (when multiple mappings exist for a category).
    /// </summary>
    [ForeignKey(nameof(categoryMapping))]
    public int? categoryMappingId { get; set; }

    /// <summary>
    /// Navigation property to the selected category mapping.
    /// </summary>
    public CategoryMapping? categoryMapping { get; set; }

    /// <summary>
    /// Preferred export mode on the product Adapters tab: full | price | stock.
    /// </summary>
    [StringLength(Marketplace.PreferredExportModes.MaxLength)]
    public string preferredExportMode { get; set; } = PreferredExportModes.Full;

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        productId = form.productId;
        connectionId = form.connectionId;
        externalProductId = form.externalProductId;
        externalSku = form.externalSku;
        status = form.status;
        categoryMappingId = form.categoryMappingId;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.externalProductId))
            externalProductId = form.externalProductId;
        if (form.externalSku != null)
            externalSku = form.externalSku;
        if (form.status.HasValue)
            status = form.status.Value;
        if (form.lastErrorMessage != null)
            lastErrorMessage = form.lastErrorMessage;
        if (form.lastSyncAt.HasValue)
            lastSyncAt = form.lastSyncAt;
        if (form.externalPrice.HasValue)
            externalPrice = form.externalPrice;
        if (form.externalStock.HasValue)
            externalStock = form.externalStock;
        if (form.categoryMappingId.HasValue)
            categoryMappingId = form.categoryMappingId;
        return true;
    }

    #endregion

    #region IMapToDTO

    LinkDTO IMapToDTO<LinkDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new LinkDTO
        {
            id = Id,
            productId = productId,
            productSku = product?.sku,
            productName = product?.name,
            localPrice = product?.price,
            localStock = product?.storageQuantity,
            connectionId = connectionId,
            connectionName = connection?.name,
            adapterKey = connection?.adapterKey,
            externalProductId = externalProductId,
            externalSku = externalSku,
            status = status,
            lastErrorMessage = lastErrorMessage,
            lastSyncAt = lastSyncAt,
            externalPrice = externalPrice,
            externalStock = externalStock,
            categoryMappingId = categoryMappingId,
            categoryMappingName = categoryMapping?.name,
            createdAt = createdAt
        };
    }

    #endregion

    #region Constants

    /// <summary>
    /// Maximum length for external product identifier.
    /// </summary>
    public const int EXTERNAL_ID_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for external SKU.
    /// </summary>
    public const int EXTERNAL_SKU_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for error message.
    /// </summary>
    public const int ERROR_MESSAGE_MAX_LENGTH = 2000;

    #endregion

    #region Forms & DTOs

    /// <summary>
    /// Form for creating a new product marketplace link.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Local product identifier.
        /// </summary>
        public int productId { get; set; }

        /// <summary>
        /// Marketplace connection identifier.
        /// </summary>
        public int connectionId { get; set; }

        /// <summary>
        /// External product identifier on the marketplace.
        /// </summary>
        [StringLength(EXTERNAL_ID_MAX_LENGTH)]
        public string externalProductId { get; set; }

        /// <summary>
        /// External SKU on the marketplace.
        /// </summary>
        [StringLength(EXTERNAL_SKU_MAX_LENGTH)]
        public string? externalSku { get; set; }

        /// <summary>
        /// Initial synchronization status.
        /// </summary>
        public ProductMarketplaceStatus status { get; set; }

        /// <summary>
        /// Selected category mapping ID.
        /// </summary>
        public int? categoryMappingId { get; set; }
    }

    /// <summary>
    /// Form for updating an existing product marketplace link.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated external product identifier.
        /// </summary>
        [StringLength(EXTERNAL_ID_MAX_LENGTH)]
        public string? externalProductId { get; set; }

        /// <summary>
        /// Updated external SKU.
        /// </summary>
        [StringLength(EXTERNAL_SKU_MAX_LENGTH)]
        public string? externalSku { get; set; }

        /// <summary>
        /// Updated synchronization status.
        /// </summary>
        public ProductMarketplaceStatus? status { get; set; }

        /// <summary>
        /// Updated error message.
        /// </summary>
        [StringLength(ERROR_MESSAGE_MAX_LENGTH)]
        public string? lastErrorMessage { get; set; }

        /// <summary>
        /// Updated last sync timestamp.
        /// </summary>
        public DateTime? lastSyncAt { get; set; }

        /// <summary>
        /// Updated external price.
        /// </summary>
        public decimal? externalPrice { get; set; }

        /// <summary>
        /// Updated external stock quantity.
        /// </summary>
        public int? externalStock { get; set; }

        /// <summary>
        /// Updated selected category mapping ID.
        /// </summary>
        public int? categoryMappingId { get; set; }
    }

    /// <summary>
    /// Product marketplace link DTO for API responses.
    /// </summary>
    public struct LinkDTO
    {
        /// <summary>
        /// Unique link identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Local product identifier.
        /// </summary>
        public int productId { get; set; }

        /// <summary>
        /// Local product SKU.
        /// </summary>
        public string? productSku { get; set; }

        /// <summary>
        /// Local product name.
        /// </summary>
        public string? productName { get; set; }

        /// <summary>
        /// Local product price.
        /// </summary>
        public decimal? localPrice { get; set; }

        /// <summary>
        /// Local product stock quantity.
        /// </summary>
        public int? localStock { get; set; }

        /// <summary>
        /// Marketplace connection identifier.
        /// </summary>
        public int connectionId { get; set; }

        /// <summary>
        /// Marketplace connection display name.
        /// </summary>
        public string? connectionName { get; set; }

        /// <summary>
        /// Marketplace adapter key.
        /// </summary>
        public string? adapterKey { get; set; }

        /// <summary>
        /// External product identifier on the marketplace.
        /// </summary>
        public string externalProductId { get; set; }

        /// <summary>
        /// External SKU on the marketplace.
        /// </summary>
        public string? externalSku { get; set; }

        /// <summary>
        /// Current synchronization status.
        /// </summary>
        public ProductMarketplaceStatus status { get; set; }

        /// <summary>
        /// Error message if status is SyncError.
        /// </summary>
        public string? lastErrorMessage { get; set; }

        /// <summary>
        /// Timestamp of the last synchronization.
        /// </summary>
        public DateTime? lastSyncAt { get; set; }

        /// <summary>
        /// Price on the marketplace.
        /// </summary>
        public decimal? externalPrice { get; set; }

        /// <summary>
        /// Stock quantity on the marketplace.
        /// </summary>
        public int? externalStock { get; set; }

        /// <summary>
        /// Selected category mapping ID.
        /// </summary>
        public int? categoryMappingId { get; set; }

        /// <summary>
        /// Selected category mapping display name.
        /// </summary>
        public string? categoryMappingName { get; set; }

        /// <summary>
        /// Timestamp when the link was created.
        /// </summary>
        public DateTime createdAt { get; set; }
    }

    #endregion
}
