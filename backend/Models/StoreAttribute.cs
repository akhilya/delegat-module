using Delegat.Enums;
using Delegat.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Delegat.Models;

/// <summary>
/// Represents a store-level custom attribute definition that applies to all products in the store.
/// </summary>
public class StoreAttribute : IEntity, IMapToDTO<StoreAttribute.AttributeDTO>, IAddable<StoreAttribute.AddForm>, IUpdateable<StoreAttribute.UpdateForm>
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
    /// For text attributes: render as multiline textarea on the product page.
    /// </summary>
    public bool isMultiline { get; set; } = false;

    /// <summary>
    /// Whether this attribute is shown on the product edit page.
    /// </summary>
    public bool showOnProductPage { get; set; } = true;

    /// <summary>
    /// Display order within the store attributes list.
    /// </summary>
    public int sortOrder { get; set; } = 0;

    /// <summary>
    /// JSON-serialized list of allowed options (for select/multiselect types).
    /// </summary>
    [StringLength(ATTRIBUTE_OPTIONS_MAX_LENGTH)]
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
    /// Timestamp when the attribute was created.
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
    public Store store { get; set; } = null!;

    /// <summary>
    /// Collection of product values for this store attribute.
    /// </summary>
    public virtual ICollection<StoreProductAttributeValue> values { get; set; } = new List<StoreProductAttributeValue>();

    public bool AddFill(AddForm form)
    {
        name = form.name;
        key = form.key;
        type = form.type;
        isRequired = form.isRequired;
        isMultiline = form.isMultiline;
        showOnProductPage = form.showOnProductPage;
        sortOrder = form.sortOrder;
        options = form.options;
        defaultValue = form.defaultValue;
        description = form.description;
        storeId = form.storeId;
        createdAt = DateTime.UtcNow;
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
        if (form.isMultiline.HasValue)
            isMultiline = form.isMultiline.Value;
        if (form.showOnProductPage.HasValue)
            showOnProductPage = form.showOnProductPage.Value;
        if (form.sortOrder.HasValue)
            sortOrder = form.sortOrder.Value;
        options = form.options;
        defaultValue = form.defaultValue;
        description = form.description;
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
            isMultiline = isMultiline,
            showOnProductPage = showOnProductPage,
            sortOrder = sortOrder,
            options = options,
            defaultValue = defaultValue,
            description = description
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
    /// Internal form used to create a new store attribute (includes store context).
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
        /// For text attributes: render as multiline textarea on the product page.
        /// </summary>
        public bool isMultiline { get; set; }

        /// <summary>
        /// Whether this attribute is shown on the product edit page.
        /// </summary>
        public bool showOnProductPage { get; set; }

        /// <summary>
        /// Display order within the store attributes list.
        /// </summary>
        public int sortOrder { get; set; }

        /// <summary>
        /// JSON-serialized list of allowed options.
        /// </summary>
        [StringLength(ATTRIBUTE_OPTIONS_MAX_LENGTH)]
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
        /// Store identifier.
        /// </summary>
        public int storeId { get; set; }
    }

    /// <summary>
    /// User-facing form for creating a new store attribute.
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
        [StringLength(ATTRIBUTE_OPTIONS_MAX_LENGTH)]
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
    }

    /// <summary>
    /// Form for updating an existing store attribute.
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
        [StringLength(ATTRIBUTE_OPTIONS_MAX_LENGTH)]
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
    }

    /// <summary>
    /// Store attribute data transfer object for API responses.
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
        /// For text attributes: render as multiline textarea on the product page.
        /// </summary>
        public bool isMultiline { get; set; }

        /// <summary>
        /// Whether this attribute is shown on the product edit page.
        /// </summary>
        public bool showOnProductPage { get; set; }

        /// <summary>
        /// Display order within the store attributes list.
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
    }
}
