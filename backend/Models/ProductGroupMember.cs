using PrintO.Infrastructure.Interfaces;
using PrintO.Models.Products;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models;

/// <summary>
/// Join table between ProductGroup and Product
/// </summary>
public class ProductGroupMember : IEntity, IAddable<ProductGroupMember.AddForm>, IUpdateable<ProductGroupMember.UpdateForm>
{
    /// <summary>
    /// Unique membership record identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the product group.
    /// </summary>
    [ForeignKey(nameof(group))]
    public int groupId { get; set; }

    /// <summary>
    /// Navigation property to the product group.
    /// </summary>
    [InverseProperty(nameof(ProductGroup.members))]
    public ProductGroup group { get; set; } = null!;

    /// <summary>
    /// Foreign key to the product.
    /// </summary>
    [ForeignKey(nameof(product))]
    public int productId { get; set; }

    /// <summary>
    /// Navigation property to the product.
    /// </summary>
    [InverseProperty(nameof(Product.groupMemberships))]
    public Product product { get; set; } = null!;

    /// <summary>
    /// Order within the group
    /// </summary>
    public int sortOrder { get; set; }

    /// <summary>
    /// Whether this product is the "leader" variant (shown first, used for main image, etc.)
    /// </summary>
    public bool isVariantLeader { get; set; }

    /// <summary>
    /// Timestamp when the product was added to the group.
    /// </summary>
    public DateTime addedAt { get; set; } = DateTime.UtcNow;

    public bool AddFill(AddForm form)
    {
        groupId = form.groupId;
        productId = form.productId;
        sortOrder = form.sortOrder;
        addedAt = DateTime.UtcNow;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (form.sortOrder.HasValue)
            sortOrder = form.sortOrder.Value;
        return true;
    }

    /// <summary>
    /// Form for adding a product to a group.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Product group identifier.
        /// </summary>
        public int groupId { get; set; }

        /// <summary>
        /// Product identifier.
        /// </summary>
        public int productId { get; set; }

        /// <summary>
        /// Display order within the group.
        /// </summary>
        public int sortOrder { get; set; }
    }

    /// <summary>
    /// Form for updating a product group membership.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated display order within the group.
        /// </summary>
        public int? sortOrder { get; set; }
    }
}
