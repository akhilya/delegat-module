using Delegat.Infrastructure.Interfaces;
using Delegat.Models.Products;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents a value for a category-level attribute on a specific product.
/// </summary>
public class ProductAttributeValue : IEntity, IMapToDTO<ProductAttributeValue.ValueDTO>, IAddable<ProductAttributeValue.AddForm>, IUpdateable<ProductAttributeValue.UpdateForm>
{
    /// <summary>
    /// Unique attribute value identifier.
    /// </summary>
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
    /// Foreign key to the category attribute definition.
    /// </summary>
    [ForeignKey(nameof(attribute))]
    public int attributeId { get; set; }

    /// <summary>
    /// Navigation property to the category attribute definition.
    /// </summary>
    public CategoryAttribute attribute { get; set; } = null!;

    /// <summary>
    /// String representation of the attribute value.
    /// </summary>
    [StringLength(VALUE_MAX_LENGTH)]
    public string value { get; set; } = null!;

    public bool AddFill(AddForm form)
    {
        productId = form.productId;
        attributeId = form.attributeId;
        value = form.value;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.value))
            value = form.value;
        return true;
    }

    ValueDTO IMapToDTO<ValueDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new ValueDTO
        {
            id = Id,
            attributeId = attributeId,
            attributeKey = attribute?.key ?? "",
            attributeName = attribute?.name ?? "",
            value = value
        };
    }

    /// <summary>
    /// Maximum length for attribute value.
    /// </summary>
    public const int VALUE_MAX_LENGTH = 10000;

    /// <summary>
    /// Internal form used to create a new product attribute value.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Product identifier.
        /// </summary>
        public int productId { get; set; }

        /// <summary>
        /// Category attribute definition identifier.
        /// </summary>
        public int attributeId { get; set; }

        /// <summary>
        /// Attribute value string.
        /// </summary>
        [StringLength(VALUE_MAX_LENGTH)]
        public string value { get; set; }
    }

    /// <summary>
    /// Form for updating an existing product attribute value.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated attribute value string.
        /// </summary>
        [StringLength(VALUE_MAX_LENGTH)]
        public string? value { get; set; }
    }

    /// <summary>
    /// Product attribute value DTO for API responses.
    /// </summary>
    public struct ValueDTO
    {
        /// <summary>
        /// Unique attribute value identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Category attribute definition identifier.
        /// </summary>
        public int attributeId { get; set; }

        /// <summary>
        /// Machine-readable key of the attribute.
        /// </summary>
        public string attributeKey { get; set; }

        /// <summary>
        /// Display name of the attribute.
        /// </summary>
        public string attributeName { get; set; }

        /// <summary>
        /// Attribute value string.
        /// </summary>
        public string value { get; set; }
    }

    /// <summary>
    /// User-facing form for setting a product attribute value.
    /// </summary>
    public struct UserValueForm
    {
        /// <summary>
        /// Category attribute definition identifier.
        /// </summary>
        public int attributeId { get; set; }

        /// <summary>
        /// Attribute value string.
        /// </summary>
        [StringLength(VALUE_MAX_LENGTH)]
        public string value { get; set; }
    }
}
