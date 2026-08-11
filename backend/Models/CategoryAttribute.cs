using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents an attribute definition within a product category (e.g., "Color", "Material").
/// </summary>
public class CategoryAttribute : IEntity, IMapToDTO<CategoryAttribute.AttributeDTO>, IAddable<CategoryAttribute.AddForm>, IUpdateable<CategoryAttribute.UpdateForm>
{
    /// <summary>
    /// Unique attribute identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Attribute display name.
    /// </summary>
    [StringLength(ATTRIBUTE_NAME_MAX_LENGTH, MinimumLength = 1)]
    public string name { get; set; } = null!;

    /// <summary>
    /// Machine-readable attribute key.
    /// </summary>
    [StringLength(ATTRIBUTE_KEY_MAX_LENGTH, MinimumLength = 1)]
    public string key { get; set; } = null!;

    /// <summary>
    /// Data type of the attribute (text, number, select, etc.).
    /// </summary>
    public AttributeType type { get; set; }

    /// <summary>
    /// Whether a value is required for this attribute.
    /// </summary>
    public bool isRequired { get; set; } = false;

    /// <summary>
    /// When true, values must be unique among products in the same category/store.
    /// </summary>
    public bool isUnique { get; set; } = false;

    /// <summary>
    /// For text attributes: render as multiline textarea on the product page.
    /// </summary>
    public bool isMultiline { get; set; } = false;

    /// <summary>
    /// Whether this attribute is shown on the product edit page.
    /// </summary>
    public bool showOnProductPage { get; set; } = true;

    /// <summary>
    /// Display order within the category.
    /// </summary>
    public int sortOrder { get; set; } = 0;

    /// <summary>
    /// JSON-serialized list of allowed options (for select/multiselect types). Uses LONGTEXT for large marketplace dictionaries.
    /// </summary>
    // No StringLength - uses LONGTEXT for large marketplace dictionaries
    public string? options { get; set; }

    /// <summary>
    /// Default value for this attribute.
    /// </summary>
    [StringLength(ATTRIBUTE_DEFAULT_MAX_LENGTH)]
    public string? defaultValue { get; set; }

    /// <summary>
    /// Human-readable description of the attribute.
    /// </summary>
    [StringLength(ATTRIBUTE_DESCRIPTION_MAX_LENGTH)]
    public string? description { get; set; }

    /// <summary>
    /// When true, description is shown in a tooltip instead of below the field on the product page.
    /// </summary>
    public bool descriptionInTooltip { get; set; } = false;

    /// <summary>
    /// Attribute group for visual grouping (e.g., "Dimensions and weight", "Media").
    /// </summary>
    [StringLength(ATTRIBUTE_GROUP_MAX_LENGTH)]
    public string? group { get; set; }

    /// <summary>
    /// Foreign key to the parent category.
    /// </summary>
    [ForeignKey(nameof(category))]
    public int categoryId { get; set; }

    /// <summary>
    /// Navigation property to the parent category.
    /// </summary>
    public Category category { get; set; } = null!;

    /// <summary>
    /// Collection of product attribute values using this attribute definition.
    /// </summary>
    public virtual ICollection<ProductAttributeValue> values { get; set; } = new List<ProductAttributeValue>();

    public bool AddFill(AddForm form)
    {
        name = form.name;
        key = form.key;
        type = form.type;
        isRequired = form.isRequired;
        isUnique = form.isUnique;
        isMultiline = form.isMultiline;
        showOnProductPage = form.isRequired || form.showOnProductPage;
        sortOrder = form.sortOrder;
        options = form.options;
        defaultValue = form.defaultValue;
        description = form.description;
        descriptionInTooltip = form.descriptionInTooltip;
        group = form.group;
        categoryId = form.categoryId;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (!string.IsNullOrEmpty(form.name))
            name = form.name;
        if (!string.IsNullOrEmpty(form.key))
            key = form.key;
        if (form.type.HasValue)
            type = form.type.Value;
        if (form.isRequired.HasValue)
            isRequired = form.isRequired.Value;
        if (form.isUnique.HasValue)
            isUnique = form.isUnique.Value;
        if (form.isMultiline.HasValue)
            isMultiline = form.isMultiline.Value;
        if (form.showOnProductPage.HasValue)
            showOnProductPage = form.showOnProductPage.Value;
        // Required attributes must always appear on the product page.
        if (isRequired)
            showOnProductPage = true;
        if (form.sortOrder.HasValue)
            sortOrder = form.sortOrder.Value;
        options = form.options;
        defaultValue = form.defaultValue;
        description = form.description;
        if (form.descriptionInTooltip.HasValue)
            descriptionInTooltip = form.descriptionInTooltip.Value;
        // Allow setting group to null or a value
        group = form.group;
        return true;
    }

    AttributeDTO IMapToDTO<AttributeDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new AttributeDTO
        {
            id = Id,
            name = name,
            key = key,
            type = type,
            isRequired = isRequired,
            isUnique = isUnique,
            isMultiline = isMultiline,
            showOnProductPage = isRequired || showOnProductPage,
            sortOrder = sortOrder,
            options = options,
            defaultValue = defaultValue,
            description = description,
            descriptionInTooltip = descriptionInTooltip,
            group = group
        };
    }

    /// <summary>
    /// Maximum length for attribute name.
    /// </summary>
    public const int ATTRIBUTE_NAME_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for attribute key.
    /// </summary>
    public const int ATTRIBUTE_KEY_MAX_LENGTH = 50;

    /// <summary>
    /// Maximum length for attribute options.
    /// </summary>
    public const int ATTRIBUTE_OPTIONS_MAX_LENGTH = 5000;

    /// <summary>
    /// Maximum length for default value.
    /// </summary>
    public const int ATTRIBUTE_DEFAULT_MAX_LENGTH = 1000;

    /// <summary>
    /// Maximum length for attribute description.
    /// </summary>
    public const int ATTRIBUTE_DESCRIPTION_MAX_LENGTH = 2000;

    /// <summary>
    /// Maximum length for attribute group name.
    /// </summary>
    public const int ATTRIBUTE_GROUP_MAX_LENGTH = 100;

    /// <summary>
    /// Internal form used to create a new category attribute (includes category context).
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Attribute display name.
        /// </summary>
        [StringLength(ATTRIBUTE_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Machine-readable attribute key.
        /// </summary>
        [StringLength(ATTRIBUTE_KEY_MAX_LENGTH, MinimumLength = 1)]
        public string key { get; set; }

        /// <summary>
        /// Data type of the attribute.
        /// </summary>
        public AttributeType type { get; set; }

        /// <summary>
        /// Whether a value is required.
        /// </summary>
        public bool isRequired { get; set; }

        /// <summary>
        /// Whether values must be unique within the category.
        /// </summary>
        public bool isUnique { get; set; }

        /// <summary>
        /// For text attributes: render as multiline textarea on the product page.
        /// </summary>
        public bool isMultiline { get; set; }

        /// <summary>
        /// Whether this attribute is shown on the product edit page.
        /// Callers should set true for marketplace imports (entity default is true).
        /// </summary>
        public bool showOnProductPage { get; set; }

        /// <summary>
        /// Display order within the category.
        /// </summary>
        public int sortOrder { get; set; }

        /// <summary>
        /// JSON-serialized list of allowed options.
        /// </summary>
        public string? options { get; set; }

        /// <summary>
        /// Default value for this attribute.
        /// </summary>
        [StringLength(ATTRIBUTE_DEFAULT_MAX_LENGTH)]
        public string? defaultValue { get; set; }

        /// <summary>
        /// Human-readable description.
        /// </summary>
        [StringLength(ATTRIBUTE_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Show description in a tooltip on the product page.
        /// </summary>
        public bool descriptionInTooltip { get; set; }

        /// <summary>
        /// Attribute group name for visual grouping.
        /// </summary>
        [StringLength(ATTRIBUTE_GROUP_MAX_LENGTH)]
        public string? group { get; set; }

        /// <summary>
        /// Parent category identifier.
        /// </summary>
        public int categoryId { get; set; }
    }

    /// <summary>
    /// User-facing form for creating a new category attribute.
    /// </summary>
    public struct UserAddForm
    {
        /// <summary>
        /// Attribute display name.
        /// </summary>
        [StringLength(ATTRIBUTE_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string name { get; set; }

        /// <summary>
        /// Machine-readable attribute key.
        /// </summary>
        [StringLength(ATTRIBUTE_KEY_MAX_LENGTH, MinimumLength = 1)]
        public string key { get; set; }

        /// <summary>
        /// Data type of the attribute.
        /// </summary>
        public AttributeType type { get; set; }

        /// <summary>
        /// Whether a value is required.
        /// </summary>
        public bool isRequired { get; set; }

        /// <summary>
        /// Whether values must be unique within the category.
        /// </summary>
        public bool isUnique { get; set; }

        /// <summary>
        /// For text attributes: render as multiline textarea on the product page.
        /// </summary>
        public bool isMultiline { get; set; }

        /// <summary>
        /// Whether this attribute is shown on the product edit page.
        /// </summary>
        public bool showOnProductPage { get; set; }

        /// <summary>
        /// JSON-serialized list of allowed options.
        /// </summary>
        public string? options { get; set; }

        /// <summary>
        /// Default value for this attribute.
        /// </summary>
        [StringLength(ATTRIBUTE_DEFAULT_MAX_LENGTH)]
        public string? defaultValue { get; set; }

        /// <summary>
        /// Human-readable description.
        /// </summary>
        [StringLength(ATTRIBUTE_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Show description in a tooltip on the product page.
        /// </summary>
        public bool descriptionInTooltip { get; set; }

        /// <summary>
        /// Attribute group name for visual grouping.
        /// </summary>
        [StringLength(ATTRIBUTE_GROUP_MAX_LENGTH)]
        public string? group { get; set; }
    }

    /// <summary>
    /// Form for updating an existing category attribute.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated attribute name.
        /// </summary>
        [StringLength(ATTRIBUTE_NAME_MAX_LENGTH, MinimumLength = 1)]
        public string? name { get; set; }

        /// <summary>
        /// Updated attribute key.
        /// </summary>
        [StringLength(ATTRIBUTE_KEY_MAX_LENGTH, MinimumLength = 1)]
        public string? key { get; set; }

        /// <summary>
        /// Updated data type.
        /// </summary>
        public AttributeType? type { get; set; }

        /// <summary>
        /// Updated required flag.
        /// </summary>
        public bool? isRequired { get; set; }

        /// <summary>
        /// Updated unique flag.
        /// </summary>
        public bool? isUnique { get; set; }

        /// <summary>
        /// Updated multiline flag for text attributes.
        /// </summary>
        public bool? isMultiline { get; set; }

        /// <summary>
        /// Updated product page visibility flag.
        /// </summary>
        public bool? showOnProductPage { get; set; }

        /// <summary>
        /// Updated display order.
        /// </summary>
        public int? sortOrder { get; set; }

        /// <summary>
        /// Updated options JSON.
        /// </summary>
        public string? options { get; set; }

        /// <summary>
        /// Updated default value.
        /// </summary>
        [StringLength(ATTRIBUTE_DEFAULT_MAX_LENGTH)]
        public string? defaultValue { get; set; }

        /// <summary>
        /// Updated description.
        /// </summary>
        [StringLength(ATTRIBUTE_DESCRIPTION_MAX_LENGTH)]
        public string? description { get; set; }

        /// <summary>
        /// Updated tooltip description flag.
        /// </summary>
        public bool? descriptionInTooltip { get; set; }

        /// <summary>
        /// Updated group name.
        /// </summary>
        [StringLength(ATTRIBUTE_GROUP_MAX_LENGTH)]
        public string? group { get; set; }
    }

    /// <summary>
    /// Category attribute data transfer object for API responses.
    /// </summary>
    public struct AttributeDTO
    {
        /// <summary>
        /// Unique attribute identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Attribute display name.
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Machine-readable attribute key.
        /// </summary>
        public string key { get; set; }

        /// <summary>
        /// Data type of the attribute.
        /// </summary>
        public AttributeType type { get; set; }

        /// <summary>
        /// Whether a value is required.
        /// </summary>
        public bool isRequired { get; set; }

        /// <summary>
        /// Whether values must be unique within the category.
        /// </summary>
        public bool isUnique { get; set; }

        /// <summary>
        /// For text attributes: render as multiline textarea on the product page.
        /// </summary>
        public bool isMultiline { get; set; }

        /// <summary>
        /// Whether this attribute is shown on the product edit page.
        /// </summary>
        public bool showOnProductPage { get; set; }

        /// <summary>
        /// Display order within the category.
        /// </summary>
        public int sortOrder { get; set; }

        /// <summary>
        /// JSON-serialized list of allowed options.
        /// </summary>
        public string? options { get; set; }

        /// <summary>
        /// Default value for this attribute.
        /// </summary>
        public string? defaultValue { get; set; }

        /// <summary>
        /// Human-readable description.
        /// </summary>
        public string? description { get; set; }

        /// <summary>
        /// Show description in a tooltip on the product page.
        /// </summary>
        public bool descriptionInTooltip { get; set; }

        /// <summary>
        /// Attribute group name for visual grouping.
        /// </summary>
        public string? group { get; set; }
    }
}
