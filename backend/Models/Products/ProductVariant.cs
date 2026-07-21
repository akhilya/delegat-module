using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models.Products;

/// <summary>
/// Represents a product variant (size) - a product-level concept.
/// Marketplace-specific IDs are stored in ProductVariantLink.
/// </summary>
public class ProductVariant : IEntity, IAddable<ProductVariant.AddForm>, IUpdateable<ProductVariant.UpdateForm>
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the product.
    /// </summary>
    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the product.
    /// </summary>
    public Product product { get; set; } = null!;

    /// <summary>
    /// Variant-specific SKU (optional, e.g. "TSHIRT-RED-M").
    /// </summary>
    [StringLength(50)]
    public string? sku { get; set; }

    /// <summary>
    /// Barcode for this variant.
    /// </summary>
    [StringLength(50)]
    public string? barcode { get; set; }

    /// <summary>
    /// Size label (e.g. "S", "M", "L", "42", "44").
    /// </summary>
    [StringLength(50)]
    public string? size { get; set; }

    /// <summary>
    /// Price for this variant (null = use product-level price).
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? price { get; set; }

    /// <summary>
    /// Stock quantity for this variant.
    /// </summary>
    public int? quantity { get; set; }

    /// <summary>
    /// Display order.
    /// </summary>
    public int sortOrder { get; set; }

    /// <summary>
    /// Timestamp when the variant was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the variant was last updated.
    /// </summary>
    public DateTime updatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Marketplace links for this variant.
    /// </summary>
    public virtual ICollection<ProductVariantLink> marketplaceLinks { get; set; } = new List<ProductVariantLink>();

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        productId = form.productId;
        sku = form.sku;
        barcode = form.barcode;
        size = form.size;
        price = form.price;
        quantity = form.quantity;
        sortOrder = form.sortOrder;
        createdAt = DateTime.UtcNow;
        updatedAt = DateTime.UtcNow;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (form.sku != null)
            sku = form.sku;
        if (form.barcode != null)
            barcode = form.barcode;
        if (form.size != null)
            size = form.size;
        if (form.price.HasValue)
            price = form.price.Value;
        if (form.quantity.HasValue)
            quantity = form.quantity.Value;
        if (form.sortOrder.HasValue)
            sortOrder = form.sortOrder.Value;
        updatedAt = DateTime.UtcNow;
        return true;
    }

    #endregion

    #region Forms

    public struct AddForm
    {
        public int productId { get; set; }
        [StringLength(50)]
        public string? sku { get; set; }
        [StringLength(50)]
        public string? barcode { get; set; }
        [StringLength(50)]
        public string? size { get; set; }
        public decimal? price { get; set; }
        public int? quantity { get; set; }
        public int sortOrder { get; set; }
    }

    public struct UpdateForm
    {
        [StringLength(50)]
        public string? sku { get; set; }
        [StringLength(50)]
        public string? barcode { get; set; }
        [StringLength(50)]
        public string? size { get; set; }
        public decimal? price { get; set; }
        public int? quantity { get; set; }
        public int? sortOrder { get; set; }
    }

    #endregion
}
