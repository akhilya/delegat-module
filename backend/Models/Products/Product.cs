using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Marketplace;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static Delegat.Models.Products.Product;

namespace Delegat.Models.Products;

/// <summary>
/// Represents a product in the store catalog.
/// </summary>
public class Product : IEntity, IMapToDTO<object>, IMapToDTO<ProductReviewDTO>, IAddable<AddForm>, IUpdateable<UpdateForm>
{
    /// <summary>
    /// Unique product identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Stock keeping unit code.
    /// </summary>
    [StringLength(PRODUCT_SKU_MAX_LENGTH, MinimumLength = 1)]
    public string sku { get; set; } = null!;

    /// <summary>
    /// EAN barcode (EAN-8 or EAN-13).
    /// </summary>
    [StringLength(PRODUCT_BARCODE_MAX_LENGTH)]
    public string? barcode { get; set; }

    /// <summary>
    /// Product display name.
    /// </summary>
    [StringLength(PRODUCT_NAME_MAX_LENGTH, MinimumLength = 1)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Product description text.
    /// </summary>
    [StringLength(PRODUCT_DESCRIPTION_MAX_LENGTH)]
    public string description { get; set; } = null!;

    /// <summary>
    /// Current quantity in warehouse storage.
    /// </summary>
    public int? storageQuantity { get; set; }

    /// <summary>
    /// Product price (stored in the store base currency).
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? price { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? oldPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? minPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? netPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? discountedPrice { get; set; }

    public int? discount { get; set; }

    /// <summary>Display/export currency code (storage is always the store base currency).</summary>
    [StringLength(3)]
    public string? currency { get; set; }

    /// <summary>
    /// Timestamp when the product was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the product was last updated.
    /// </summary>
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the product was soft-deleted. Null means active.
    /// </summary>
    public DateTime? deletedAt { get; set; }

    /// <summary>
    /// Foreign key to the owning store.
    /// </summary>
    [ForeignKey(nameof(store))]
    public int storeId { get; set; }

    /// <summary>
    /// Navigation property to the owning store.
    /// </summary>
    [InverseProperty(nameof(Store.products))]
    public Store store { get; set; } = null!;

    /// <summary>
    /// Foreign key to the product category.
    /// </summary>
    [ForeignKey(nameof(category))]
    public int? categoryId { get; set; }

    /// <summary>
    /// Navigation property to the product category.
    /// </summary>
    [InverseProperty(nameof(Models.Category.products))]
    public Category? category { get; set; }

    /// <summary>
    /// Collection of files attached to this product.
    /// </summary>
    public virtual ICollection<File> files { get; set; } = new List<File>();

    /// <summary>
    /// Collection of virtual folders for organizing product files.
    /// </summary>
    public virtual ICollection<ProductFolder> folders { get; set; } = new List<ProductFolder>();

    /// <summary>
    /// Collection of image references for this product.
    /// </summary>
    public virtual ICollection<ImageReference> images { get; set; } = new List<ImageReference>();

    /// <summary>
    /// Collection of notes associated with this product.
    /// </summary>
    public virtual ICollection<Note> notes { get; set; } = new List<Note>();

    /// <summary>
    /// Collection of category attribute values for this product.
    /// </summary>
    public virtual ICollection<ProductAttributeValue> attributeValues { get; set; } = new List<ProductAttributeValue>();

    /// <summary>
    /// Collection of store-level attribute values for this product.
    /// </summary>
    public virtual ICollection<StoreProductAttributeValue> storeAttributeValues { get; set; } = new List<StoreProductAttributeValue>();

    /// <summary>
    /// Collection of marketplace links for this product.
    /// </summary>
    public virtual ICollection<ProductMarketplaceLink> marketplaceLinks { get; set; } = new List<ProductMarketplaceLink>();

    /// <summary>
    /// Collection of product group memberships.
    /// </summary>
    public virtual ICollection<ProductGroupMember> groupMemberships { get; set; } = new List<ProductGroupMember>();

    /// <summary>
    /// Collection of product variants (sizes) across marketplace connections.
    /// </summary>
    public virtual ICollection<ProductVariant> variants { get; set; } = new List<ProductVariant>();

    /// <summary>
    /// Preferred export mode for the "export to all adapters" action: full | price | stock.
    /// </summary>
    [StringLength(Marketplace.PreferredExportModes.MaxLength)]
    public string preferredExportAllMode { get; set; } = Marketplace.PreferredExportModes.Full;

    public bool AddFill(AddForm form)
    {
        sku = form.userAddForm.sku;
        barcode = form.userAddForm.barcode;
        name = form.userAddForm.name;
        description = form.userAddForm.description;
        storeId = form.storeId;
        categoryId = form.categoryId;
        storageQuantity = form.userAddForm.storageQuantity;
        price = form.userAddForm.price;
        oldPrice = form.userAddForm.oldPrice;
        minPrice = form.userAddForm.minPrice;
        netPrice = form.userAddForm.netPrice;
        currency = form.userAddForm.currency;
        discountedPrice = price;
        discount = Delegat.Infrastructure.ProductPriceHelper.ComputeDiscountPercent(oldPrice, price);

        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.name))
            name = form.name;
        if (!string.IsNullOrEmpty(form.description))
            description = form.description;
        if (form.barcode != null)
            barcode = form.barcode == "" ? null : form.barcode;
        if (form.storageQuantity.HasValue)
            storageQuantity = form.storageQuantity.Value;
        if (form.price.HasValue)
            price = form.price.Value;
        if (form.oldPrice.HasValue)
            oldPrice = form.oldPrice;
        if (form.minPrice.HasValue)
            minPrice = form.minPrice;
        if (form.netPrice.HasValue)
            netPrice = form.netPrice;
        if (form.currency != null)
            currency = form.currency == "" ? null : form.currency;
        if (form.categoryId.HasValue)
            categoryId = form.categoryId.Value;

        discountedPrice = price;
        discount = Delegat.Infrastructure.ProductPriceHelper.ComputeDiscountPercent(oldPrice, price);

        updatedAt = DateTime.UtcNow;
        return true;
    }

    object IMapToDTO<object>.MapToDTO(IServiceProvider serviceProvider)
    {
        IEnumerable<object>? files = null;

        Func<File, object> fileBuilder = (f) =>
        {
            return f.MapToDTO(serviceProvider);
        };
        files = this.files.Select(fileBuilder);

        return new
        {
            Id,
            sku,
            barcode,
            name,
            description,
            store = store?.MapToDTO(serviceProvider),
            category = category != null ? ((IMapToDTO<Category.CategoryDetailDTO>)category).MapToDTO(serviceProvider) : (object?)null,
            files,
            images = images.OrderBy(i => i.index).Select(i => i.MapToDTO(serviceProvider)),
            attributeValues = attributeValues.Select(av => ((IMapToDTO<ProductAttributeValue.ValueDTO>)av).MapToDTO(serviceProvider)),
            storeAttributeValues = storeAttributeValues.Select(sav => ((IMapToDTO<StoreProductAttributeValue.ValueDTO>)sav).MapToDTO(serviceProvider)),
            notes = notes.Select(n => n.MapToDTO(serviceProvider)),
            storageQuantity,
            price,
            oldPrice,
            minPrice,
            netPrice,
            discountedPrice,
            discount,
            currency
        };
    }

    ProductReviewDTO IMapToDTO<ProductReviewDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        ImageReference? primaryImageRef = images.OrderBy(i => i.index).FirstOrDefault();
        string? primaryImagePath = null;
        string? primaryImageThumbnail = null;

        // Prefer image from ImageReference, fall back to first image file
        var primaryImageFile = primaryImageRef?.file
            ?? files.FirstOrDefault(f => f.contentType.StartsWith("image/"));

        if (primaryImageFile is not null)
        {
            primaryImagePath = primaryImageFile.GetDownloadUrl();
            primaryImageThumbnail = $"/api/files/{primaryImageFile.Id}/thumbnail?w=256";
        }

        return new ProductReviewDTO()
        {
            id = Id,
            sku = sku,
            barcode = barcode,
            name = name,
            primaryImage = primaryImagePath,
            primaryImageThumbnail = primaryImageThumbnail,
            categoryId = categoryId,
            categoryName = category?.name,
            storageQuantity = storageQuantity,
            price = price,
            createdAt = createdAt,
            updatedAt = updatedAt,
            deletedAt = deletedAt,
            storeAttributeValues = storeAttributeValues?.Select(sav => ((IMapToDTO<StoreProductAttributeValue.ValueDTO>)sav).MapToDTO(serviceProvider)),
            adapterLinks = marketplaceLinks?
                .Where(l => l.connection != null)
                .Select(l =>
                {
                    var savedMappingWasDeleted = l.categoryMappingId != null && l.categoryMapping == null;
                    return new AdapterLinkSummary
                    {
                        linkId = l.Id,
                        connectionId = l.connectionId,
                        adapterKey = l.connection.adapterKey,
                        status = l.status.ToString(),
                        externalProductId = l.externalProductId,
                        externalSku = l.externalSku,
                        lastSyncAt = l.lastSyncAt,
                        preferredExportMode = l.preferredExportMode,
                        // hasMapping / mappingMissing completed by ProductController enrichment
                        // (needs store category-mapping lookup). Seed deleted-mapping case here.
                        hasMapping = l.categoryMapping != null,
                        mappingMissing = savedMappingWasDeleted,
                    };
                }),
        };
    }

    /// <summary>
    /// Maximum length for product SKU.
    /// </summary>
    public const int PRODUCT_SKU_MAX_LENGTH = 20;

    /// <summary>
    /// Maximum length for product barcode (EAN-13).
    /// </summary>
    public const int PRODUCT_BARCODE_MAX_LENGTH = 13;

    /// <summary>
    /// Maximum length for product name.
    /// </summary>
    public const int PRODUCT_NAME_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for product description.
    /// </summary>
    public const int PRODUCT_DESCRIPTION_MAX_LENGTH = 5000;

    /// <summary>
    /// Internal form used to create a new product (includes store context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// User-provided product creation data.
        /// </summary>
        public UserAddForm userAddForm { get; set; }

        /// <summary>
        /// Store identifier the product belongs to.
        /// </summary>
        public int storeId { get; set; }

        /// <summary>
        /// Optional category identifier.
        /// </summary>
        public int? categoryId { get; set; }

        public AddForm(UserAddForm userAddForm, int storeId, int? categoryId = null)
        {
            this.userAddForm = userAddForm;
            this.storeId = storeId;
            this.categoryId = categoryId;
        }
    }

    /// <summary>
    /// User-facing form for creating a new product.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Stock keeping unit code.
        /// </summary>
        [StringLength(PRODUCT_SKU_MAX_LENGTH, MinimumLength = 1)]
        public string sku { get; set; }

        /// <summary>
        /// EAN barcode (EAN-8 or EAN-13).
        /// </summary>
        [StringLength(PRODUCT_BARCODE_MAX_LENGTH)]
        public string? barcode { get; set; }

        /// <summary>
        /// Product display name.
        /// </summary>
        [StringLength(PRODUCT_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Product description text.
        /// </summary>
        [StringLength(PRODUCT_DESCRIPTION_MAX_LENGTH)]
        public string description { get; set; }

        /// <summary>
        /// Initial warehouse storage quantity.
        /// </summary>
        public int? storageQuantity { get; set; }

        /// <summary>
        /// Product price (store base currency).
        /// </summary>
        public decimal? price { get; set; }

        public decimal? oldPrice { get; set; }
        public decimal? minPrice { get; set; }
        public decimal? netPrice { get; set; }
        public decimal? discountedPrice { get; set; }
        public int? discount { get; set; }
        public string? currency { get; set; }

        /// <summary>
        /// Optional category identifier.
        /// </summary>
        public int? categoryId { get; set; }

        /// <summary>
        /// Category attribute values to set on creation.
        /// </summary>
        public List<ProductAttributeValue.UserValueForm>? attributeValues { get; set; }

        /// <summary>
        /// Store attribute values to set on creation.
        /// </summary>
        public List<StoreProductAttributeValue.UserValueForm>? storeAttributeValues { get; set; }
    }

    /// <summary>
    /// Form for updating an existing product.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated product name.
        /// </summary>
        [StringLength(PRODUCT_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string? name { get; set; }

        /// <summary>
        /// Updated EAN barcode.
        /// </summary>
        [StringLength(PRODUCT_BARCODE_MAX_LENGTH)]
        public string? barcode { get; set; }

        /// <summary>
        /// Updated product description.
        /// </summary>
        [StringLength(PRODUCT_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Updated warehouse storage quantity.
        /// </summary>
        public int? storageQuantity { get; set; }

        /// <summary>
        /// Updated product price (store base currency).
        /// </summary>
        public decimal? price { get; set; }

        public decimal? oldPrice { get; set; }
        public decimal? minPrice { get; set; }
        public decimal? netPrice { get; set; }
        public decimal? discountedPrice { get; set; }
        public int? discount { get; set; }
        public string? currency { get; set; }

        /// <summary>
        /// Updated category identifier.
        /// </summary>
        public int? categoryId { get; set; }

        /// <summary>
        /// Updated category attribute values.
        /// </summary>
        public List<ProductAttributeValue.UserValueForm>? attributeValues { get; set; }

        /// <summary>
        /// Updated store attribute values.
        /// </summary>
        public List<StoreProductAttributeValue.UserValueForm>? storeAttributeValues { get; set; }
    }

    /// <summary>
    /// Lightweight product DTO used in list/table views.
    /// </summary>
    public struct ProductReviewDTO
    {
        /// <summary>
        /// Unique product identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Stock keeping unit code.
        /// </summary>
        public string sku { get; set; }

        /// <summary>
        /// EAN barcode (EAN-8 or EAN-13).
        /// </summary>
        public string? barcode { get; set; }

        /// <summary>
        /// Product display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// URL of the primary product image.
        /// </summary>
        public object? primaryImage { get; set; }

        /// <summary>
        /// Relative URL to the primary image thumbnail (WebP, 256px).
        /// </summary>
        public string? primaryImageThumbnail { get; set; }

        /// <summary>
        /// Category identifier.
        /// </summary>
        public int? categoryId { get; set; }

        /// <summary>
        /// Category display name.
        /// </summary>
        public string? categoryName { get; set; }

        /// <summary>
        /// Current quantity in warehouse storage.
        /// </summary>
        public int? storageQuantity { get; set; }

        /// <summary>
        /// Product price.
        /// </summary>
        public decimal? price { get; set; }

        /// <summary>
        /// Timestamp when the product was created.
        /// </summary>
        public DateTime createdAt { get; set; }

        /// <summary>
        /// Timestamp when the product was last updated.
        /// </summary>
        public DateTime updatedAt { get; set; }

        public DateTime? deletedAt { get; set; }

        /// <summary>
        /// Store-level attribute values for table columns.
        /// </summary>
        public IEnumerable<StoreProductAttributeValue.ValueDTO>? storeAttributeValues { get; set; }

        /// <summary>
        /// Marketplace link summaries for the adapters column (status per connection).
        /// </summary>
        public IEnumerable<AdapterLinkSummary>? adapterLinks { get; set; }
    }

    /// <summary>
    /// Compact marketplace link info for product list rows.
    /// </summary>
    public struct AdapterLinkSummary
    {
        public int linkId { get; set; }
        public int connectionId { get; set; }
        public string adapterKey { get; set; }
        public string status { get; set; }
        public string externalProductId { get; set; }
        public string? externalSku { get; set; }
        public DateTime? lastSyncAt { get; set; }
        public string preferredExportMode { get; set; }

        /// <summary>
        /// Whether a category mapping exists for this product category + connection.
        /// </summary>
        public bool hasMapping { get; set; }

        /// <summary>
        /// Linked but mapping missing or deleted — UI should treat as error (not Synced green).
        /// </summary>
        public bool mappingMissing { get; set; }

        /// <summary>
        /// The product's category has several mappings on this connection and none is chosen.
        /// </summary>
        public bool mappingUnselected { get; set; }
    }
}
