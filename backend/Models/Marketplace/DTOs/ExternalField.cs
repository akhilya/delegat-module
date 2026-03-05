namespace PrintO.Models.Marketplace.DTOs;

/// <summary>
/// Value option for a select/multiselect field
/// </summary>
public record ExternalFieldOption(
    /// <summary>
    /// Value to send to the marketplace
    /// </summary>
    string Value,

    /// <summary>
    /// Display label for the UI
    /// </summary>
    string Label
);

/// <summary>
/// Category field/attribute from an external marketplace
/// </summary>
public record ExternalField(
    /// <summary>
    /// Unique field key on the marketplace
    /// </summary>
    string Key,

    /// <summary>
    /// Display name of the field
    /// </summary>
    string Name,

    /// <summary>
    /// Whether the field is required
    /// </summary>
    bool IsRequired,

    /// <summary>
    /// Data type: "string", "number", "boolean", "select", "multiselect"
    /// </summary>
    string Type,

    /// <summary>
    /// Value options for select/multiselect
    /// </summary>
    List<ExternalFieldOption>? Options = null,

    /// <summary>
    /// Field description / hint
    /// </summary>
    string? Description = null,

    /// <summary>
    /// Unit of measurement (e.g. "mm", "kg")
    /// </summary>
    string? Unit = null,

    /// <summary>
    /// Maximum length for string fields
    /// </summary>
    int? MaxLength = null,

    /// <summary>
    /// Minimum value for numeric fields
    /// </summary>
    decimal? MinValue = null,

    /// <summary>
    /// Maximum value for numeric fields
    /// </summary>
    decimal? MaxValue = null,

    /// <summary>
    /// Validation pattern (regex)
    /// </summary>
    string? ValidationPattern = null,

    /// <summary>
    /// Field group/section for the UI
    /// </summary>
    string? Group = null,

    /// <summary>
    /// Sort order for display
    /// </summary>
    int SortOrder = 0,

    /// <summary>
    /// Whether this is an aspect attribute (size, color) — e.g. Ozon is_aspect.
    /// Aspect attributes define product variations.
    /// </summary>
    bool IsAspect = false
);
