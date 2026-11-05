using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents a product category within a store.
/// </summary>
public class Category : IEntity, IMapToDTO<Category.CategoryDTO>, IMapToDTO<Category.CategoryDetailDTO>, IAddable<Category.AddForm>, IUpdateable<Category.UpdateForm>
{
    /// <summary>
    /// Unique category identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Category display name.
    /// </summary>
    [StringLength(CATEGORY_NAME_MAX_LENGTH, MinimumLength = 1)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Optional category description.
    /// </summary>
    [StringLength(CATEGORY_DESCRIPTION_MAX_LENGTH)]
    public string? description { get; set; }

    /// <summary>
    /// Whether products in this category support sizes (e.g. clothing, shoes).
    /// </summary>
    public bool hasSizes { get; set; } = false;

    /// <summary>
    /// Timestamp when the category was created.
    /// </summary>
    public DateTime createdAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Foreign key to the owning store.
    /// </summary>
    [ForeignKey(nameof(store))]
    public int storeId { get; set; }

    /// <summary>
    /// Navigation property to the owning store.
    /// </summary>
    [InverseProperty(nameof(Store.categories))]
    public Store store { get; set; } = null!;

    /// <summary>
    /// Collection of attributes defined for this category.
    /// </summary>
    public virtual ICollection<CategoryAttribute> attributes { get; set; } = new List<CategoryAttribute>();

    /// <summary>
    /// Collection of products belonging to this category.
    /// </summary>
    public virtual ICollection<Products.Product> products { get; set; } = new List<Products.Product>();

    public bool AddFill(AddForm form)
    {
        name = form.name;
        description = form.description;
        hasSizes = form.hasSizes;
        storeId = form.storeId;
        createdAt = DateTime.UtcNow;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.name))
            name = form.name;
        description = form.description;
        if (form.hasSizes.HasValue)
            hasSizes = form.hasSizes.Value;
        return true;
    }

    CategoryDTO IMapToDTO<CategoryDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new CategoryDTO
        {
            id = Id,
            name = name,
            description = description,
            hasSizes = hasSizes,
            createdAt = createdAt,
            productsCount = products?.Count ?? 0,
            attributesCount = attributes?.Count ?? 0
        };
    }

    CategoryDetailDTO IMapToDTO<CategoryDetailDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new CategoryDetailDTO
        {
            id = Id,
            name = name,
            description = description,
            hasSizes = hasSizes,
            createdAt = createdAt,
            attributes = attributes?.OrderBy(a => a.sortOrder).Select(a => ((IMapToDTO<CategoryAttribute.AttributeDTO>)a).MapToDTO(serviceProvider)).ToList() ?? new List<CategoryAttribute.AttributeDTO>(),
            productsCount = products?.Count ?? 0
        };
    }

    /// <summary>
    /// Maximum length for category name.
    /// </summary>
    public const int CATEGORY_NAME_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for category description.
    /// </summary>
    public const int CATEGORY_DESCRIPTION_MAX_LENGTH = 500;

    /// <summary>
    /// Internal form used to create a new category (includes store context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Category display name.
        /// </summary>
        [StringLength(CATEGORY_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Optional category description.
        /// </summary>
        [StringLength(CATEGORY_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Store identifier the category belongs to.
        /// </summary>
        public int storeId { get; set; }

        /// <summary>
        /// Whether products in this category support sizes.
        /// </summary>
        public bool hasSizes { get; set; }
    }

    /// <summary>
    /// User-facing form for creating a new category.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Category display name.
        /// </summary>
        [StringLength(CATEGORY_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Optional category description.
        /// </summary>
        [StringLength(CATEGORY_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Whether products in this category support sizes.
        /// </summary>
        public bool hasSizes { get; set; }
    }

    /// <summary>
    /// Form for updating an existing category.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated category name.
        /// </summary>
        [StringLength(CATEGORY_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string? name { get; set; }

        /// <summary>
        /// Updated category description.
        /// </summary>
        [StringLength(CATEGORY_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Whether products in this category support sizes.
        /// </summary>
        public bool? hasSizes { get; set; }
    }

    /// <summary>
    /// Category summary DTO for list views.
    /// </summary>
    public struct CategoryDTO
    {
        /// <summary>
        /// Unique category identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Category display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Optional category description.
        /// </summary>
        public string? description { get; set; }

        /// <summary>
        /// Timestamp when the category was created.
        /// </summary>
        public DateTime createdAt { get; set; }

        /// <summary>
        /// Number of products in this category.
        /// </summary>
        public bool hasSizes { get; set; }

        public int productsCount { get; set; }

        /// <summary>
        /// Number of attributes defined for this category.
        /// </summary>
        public int attributesCount { get; set; }
    }

    /// <summary>Explicit category selection for an all-or-nothing cabinet delete.</summary>
    public sealed class BulkDeleteForm
    {
        public int[] categoryIds { get; set; } = [];
    }

    /// <summary>Category form metadata with lightweight attribute rows, not product-entry dictionaries.</summary>
    public sealed class EditorSummaryDTO
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string? description { get; set; }
        public bool hasSizes { get; set; }
        public List<AttributeDefinitionSummary> attributes { get; set; } = [];
    }

    /// <summary>
    /// Detailed category DTO including attributes list.
    /// </summary>
    public struct CategoryDetailDTO
    {
        /// <summary>
        /// Unique category identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Category display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Optional category description.
        /// </summary>
        public string? description { get; set; }

        /// <summary>
        /// Timestamp when the category was created.
        /// </summary>
        public DateTime createdAt { get; set; }

        /// <summary>
        /// Ordered list of category attributes.
        /// </summary>
        public List<CategoryAttribute.AttributeDTO> attributes { get; set; }

        public bool hasSizes { get; set; }

        /// <summary>
        /// Number of products in this category.
        /// </summary>
        public int productsCount { get; set; }
    }
}
