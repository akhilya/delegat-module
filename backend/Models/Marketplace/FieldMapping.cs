using PrintO.Enums;
using PrintO.Infrastructure.Interfaces;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PrintO.Models.Marketplace;

/// <summary>
/// Maps a marketplace field to a local data source (product field, attribute, or static value).
/// </summary>
public class FieldMapping : IEntity, IMapToDTO<FieldMapping.MappingDTO>,
    IAddable<FieldMapping.AddForm>, IUpdateable<FieldMapping.UpdateForm>
{
    /// <summary>
    /// Unique field mapping identifier.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to the parent category mapping.
    /// </summary>
    [ForeignKey(nameof(categoryMapping))]
    public int categoryMappingId { get; set; }

    /// <summary>
    /// Navigation property to the parent category mapping.
    /// </summary>
    [InverseProperty(nameof(Marketplace.CategoryMapping.fieldMappings))]
    public CategoryMapping categoryMapping { get; set; } = null!;

    /// <summary>
    /// Field key on the marketplace.
    /// </summary>
    [StringLength(EXTERNAL_FIELD_KEY_MAX_LENGTH)]
    public string externalFieldKey { get; set; } = null!;

    /// <summary>
    /// Cached field name on the marketplace for display purposes.
    /// </summary>
    [StringLength(EXTERNAL_FIELD_NAME_MAX_LENGTH)]
    public string? externalFieldName { get; set; }

    /// <summary>
    /// Whether the field is required on the marketplace.
    /// </summary>
    public bool isRequired { get; set; }

    /// <summary>
    /// Whether the field is enabled for synchronization. Required fields are always enabled.
    /// </summary>
    public bool isEnabled { get; set; } = true;

    /// <summary>
    /// Type of the data source (product field, category attribute, store attribute, or static).
    /// </summary>
    public FieldMappingSourceType sourceType { get; set; }

    /// <summary>
    /// For ProductField source: field name (sku, name, description, price, storageQuantity).
    /// </summary>
    [StringLength(SOURCE_FIELD_MAX_LENGTH)]
    public string? sourceField { get; set; }

    /// <summary>
    /// For CategoryAttribute or StoreAttribute source: attribute identifier.
    /// </summary>
    public int? sourceAttributeId { get; set; }

    /// <summary>
    /// For Static source: the static value to use.
    /// </summary>
    [StringLength(STATIC_VALUE_MAX_LENGTH)]
    public string? staticValue { get; set; }

    /// <summary>
    /// Optional value transformation expression (e.g., "multiply:1000", "append:_suffix").
    /// Legacy field — kept for backward compatibility. Prefer modifiersJson.
    /// </summary>
    [StringLength(TRANSFORM_MAX_LENGTH)]
    public string? transformExpression { get; set; }

    /// <summary>
    /// JSON-serialized modifier chain. Format: [{"type":"multiply","params":{"factor":1000}}, ...].
    /// Takes priority over transformExpression when present.
    /// </summary>
    public string? modifiersJson { get; set; }

    /// <summary>
    /// Cached field type from the marketplace (e.g. "string", "number", "boolean", "select", "multiselect").
    /// </summary>
    [StringLength(EXTERNAL_FIELD_TYPE_MAX_LENGTH)]
    public string? externalFieldType { get; set; }

    /// <summary>
    /// Cached JSON-serialized options for select/multiselect fields.
    /// Format: [{"value":"123","label":"Option 1"}, ...].
    /// </summary>
    public string? externalFieldOptions { get; set; }

    /// <summary>
    /// Display order for sorting.
    /// </summary>
    public int sortOrder { get; set; } = 0;

    #region IAddable / IUpdateable

    public bool AddFill(AddForm form)
    {
        categoryMappingId = form.categoryMappingId;
        externalFieldKey = form.externalFieldKey;
        externalFieldName = form.externalFieldName;
        isRequired = form.isRequired;
        isEnabled = form.isRequired || form.isEnabled; // обязательные всегда включены
        sourceType = form.sourceType;
        sourceField = form.sourceField;
        sourceAttributeId = form.sourceAttributeId;
        staticValue = form.staticValue;
        transformExpression = form.transformExpression;
        modifiersJson = form.modifiersJson;
        externalFieldType = form.externalFieldType;
        externalFieldOptions = form.externalFieldOptions;
        sortOrder = form.sortOrder;
        return true;
    }

    public bool UpdateFill(UpdateForm form)
    {
        if (form.isEnabled.HasValue && !isRequired) // обязательные нельзя отключить
            isEnabled = form.isEnabled.Value;
        if (form.sourceType.HasValue)
            sourceType = form.sourceType.Value;
        if (form.sourceField != null)
            sourceField = form.sourceField;
        if (form.sourceAttributeId.HasValue)
            sourceAttributeId = form.sourceAttributeId;
        if (form.staticValue != null)
            staticValue = form.staticValue;
        if (form.transformExpression != null)
            transformExpression = form.transformExpression;
        if (form.modifiersJson != null)
            modifiersJson = form.modifiersJson;
        if (form.sortOrder.HasValue)
            sortOrder = form.sortOrder.Value;
        return true;
    }

    #endregion

    #region IMapToDTO

    MappingDTO IMapToDTO<MappingDTO>.MapToDTO(IServiceProvider serviceProvider)
    {
        return new MappingDTO
        {
            id = Id,
            categoryMappingId = categoryMappingId,
            externalFieldKey = externalFieldKey,
            externalFieldName = externalFieldName,
            isRequired = isRequired,
            isEnabled = isEnabled,
            sourceType = sourceType,
            sourceField = sourceField,
            sourceAttributeId = sourceAttributeId,
            staticValue = staticValue,
            transformExpression = transformExpression,
            modifiersJson = modifiersJson,
            externalFieldType = externalFieldType,
            externalFieldOptions = externalFieldOptions,
            sortOrder = sortOrder
        };
    }

    #endregion

    #region Constants

    /// <summary>
    /// Maximum length for external field key.
    /// </summary>
    public const int EXTERNAL_FIELD_KEY_MAX_LENGTH = 100;

    /// <summary>
    /// Maximum length for external field name.
    /// </summary>
    public const int EXTERNAL_FIELD_NAME_MAX_LENGTH = 500;

    /// <summary>
    /// Maximum length for source field name.
    /// </summary>
    public const int SOURCE_FIELD_MAX_LENGTH = 50;

    /// <summary>
    /// Maximum length for static value.
    /// </summary>
    public const int STATIC_VALUE_MAX_LENGTH = 5000;

    /// <summary>
    /// Maximum length for transform expression.
    /// </summary>
    public const int TRANSFORM_MAX_LENGTH = 1000;

    /// <summary>
    /// Maximum length for serialized modifiers JSON.
    /// </summary>
    public const int MODIFIERS_MAX_LENGTH = 65535;

    /// <summary>
    /// Maximum length for external field type.
    /// </summary>
    public const int EXTERNAL_FIELD_TYPE_MAX_LENGTH = 50;

    /// <summary>
    /// Maximum length for serialized external field options JSON (stored as TEXT).
    /// </summary>
    public const int EXTERNAL_FIELD_OPTIONS_MAX_LENGTH = 65535;

    #endregion

    #region Forms & DTOs

    /// <summary>
    /// Internal form used to create a new field mapping.
    /// </summary>
    public struct AddForm
    {
        /// <summary>
        /// Parent category mapping identifier.
        /// </summary>
        public int categoryMappingId { get; set; }

        /// <summary>
        /// External field key on the marketplace.
        /// </summary>
        [StringLength(EXTERNAL_FIELD_KEY_MAX_LENGTH)]
        public string externalFieldKey { get; set; }

        /// <summary>
        /// External field name (cached for display).
        /// </summary>
        [StringLength(EXTERNAL_FIELD_NAME_MAX_LENGTH)]
        public string? externalFieldName { get; set; }

        /// <summary>
        /// Whether the field is required on the marketplace.
        /// </summary>
        public bool isRequired { get; set; }

        /// <summary>
        /// Whether the field is enabled for synchronization.
        /// </summary>
        public bool isEnabled { get; set; }

        /// <summary>
        /// Data source type.
        /// </summary>
        public FieldMappingSourceType sourceType { get; set; }

        /// <summary>
        /// Source product field name.
        /// </summary>
        [StringLength(SOURCE_FIELD_MAX_LENGTH)]
        public string? sourceField { get; set; }

        /// <summary>
        /// Source attribute identifier.
        /// </summary>
        public int? sourceAttributeId { get; set; }

        /// <summary>
        /// Static value for the field.
        /// </summary>
        [StringLength(STATIC_VALUE_MAX_LENGTH)]
        public string? staticValue { get; set; }

        /// <summary>
        /// Value transformation expression.
        /// </summary>
        [StringLength(TRANSFORM_MAX_LENGTH)]
        public string? transformExpression { get; set; }

        /// <summary>
        /// JSON-serialized modifier chain.
        /// </summary>
        public string? modifiersJson { get; set; }

        /// <summary>
        /// Cached external field type.
        /// </summary>
        [StringLength(EXTERNAL_FIELD_TYPE_MAX_LENGTH)]
        public string? externalFieldType { get; set; }

        /// <summary>
        /// Cached JSON-serialized external field options.
        /// </summary>
        public string? externalFieldOptions { get; set; }

        /// <summary>
        /// Display order.
        /// </summary>
        public int sortOrder { get; set; }
    }

    /// <summary>
    /// User-facing form for updating a field mapping.
    /// </summary>
    public struct UserUpdateForm
    {
        /// <summary>
        /// Updated enabled flag.
        /// </summary>
        public bool? isEnabled { get; set; }

        /// <summary>
        /// Updated data source type.
        /// </summary>
        public FieldMappingSourceType? sourceType { get; set; }

        /// <summary>
        /// Updated source product field name.
        /// </summary>
        [StringLength(SOURCE_FIELD_MAX_LENGTH)]
        public string? sourceField { get; set; }

        /// <summary>
        /// Updated source attribute identifier.
        /// </summary>
        public int? sourceAttributeId { get; set; }

        /// <summary>
        /// Updated static value.
        /// </summary>
        [StringLength(STATIC_VALUE_MAX_LENGTH)]
        public string? staticValue { get; set; }

        /// <summary>
        /// Updated transformation expression.
        /// </summary>
        [StringLength(TRANSFORM_MAX_LENGTH)]
        public string? transformExpression { get; set; }

        /// <summary>
        /// Updated modifier chain JSON.
        /// </summary>
        public string? modifiersJson { get; set; }
    }

    /// <summary>
    /// Internal form for updating an existing field mapping.
    /// </summary>
    public struct UpdateForm
    {
        /// <summary>
        /// Updated enabled flag.
        /// </summary>
        public bool? isEnabled { get; set; }

        /// <summary>
        /// Updated data source type.
        /// </summary>
        public FieldMappingSourceType? sourceType { get; set; }

        /// <summary>
        /// Updated source product field name.
        /// </summary>
        [StringLength(SOURCE_FIELD_MAX_LENGTH)]
        public string? sourceField { get; set; }

        /// <summary>
        /// Updated source attribute identifier.
        /// </summary>
        public int? sourceAttributeId { get; set; }

        /// <summary>
        /// Updated static value.
        /// </summary>
        [StringLength(STATIC_VALUE_MAX_LENGTH)]
        public string? staticValue { get; set; }

        /// <summary>
        /// Updated transformation expression.
        /// </summary>
        [StringLength(TRANSFORM_MAX_LENGTH)]
        public string? transformExpression { get; set; }

        /// <summary>
        /// Updated modifier chain JSON.
        /// </summary>
        public string? modifiersJson { get; set; }

        /// <summary>
        /// Updated display order.
        /// </summary>
        public int? sortOrder { get; set; }
    }

    /// <summary>
    /// Field mapping DTO for API responses.
    /// </summary>
    public struct MappingDTO
    {
        /// <summary>
        /// Unique field mapping identifier.
        /// </summary>
        public int id { get; set; }

        /// <summary>
        /// Parent category mapping identifier.
        /// </summary>
        public int categoryMappingId { get; set; }

        /// <summary>
        /// External field key on the marketplace.
        /// </summary>
        public string externalFieldKey { get; set; }

        /// <summary>
        /// External field name (cached for display).
        /// </summary>
        public string? externalFieldName { get; set; }

        /// <summary>
        /// Whether the field is required on the marketplace.
        /// </summary>
        public bool isRequired { get; set; }

        /// <summary>
        /// Whether the field is enabled for synchronization.
        /// </summary>
        public bool isEnabled { get; set; }

        /// <summary>
        /// Data source type.
        /// </summary>
        public FieldMappingSourceType sourceType { get; set; }

        /// <summary>
        /// Source product field name.
        /// </summary>
        public string? sourceField { get; set; }

        /// <summary>
        /// Source attribute identifier.
        /// </summary>
        public int? sourceAttributeId { get; set; }

        /// <summary>
        /// Static value for the field.
        /// </summary>
        public string? staticValue { get; set; }

        /// <summary>
        /// Value transformation expression.
        /// </summary>
        public string? transformExpression { get; set; }

        /// <summary>
        /// JSON-serialized modifier chain.
        /// </summary>
        public string? modifiersJson { get; set; }

        /// <summary>
        /// Cached external field type.
        /// </summary>
        public string? externalFieldType { get; set; }

        /// <summary>
        /// Cached JSON-serialized external field options.
        /// </summary>
        public string? externalFieldOptions { get; set; }

        /// <summary>
        /// Display order.
        /// </summary>
        public int sortOrder { get; set; }
    }

    #endregion
}
