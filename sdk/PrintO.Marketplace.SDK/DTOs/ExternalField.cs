namespace PrintO.Marketplace.SDK.DTOs;

/// <summary>
/// Option value for select/multiselect fields
/// </summary>
public record ExternalFieldOption(
    /// <summary>
    /// Value to send to marketplace
    /// </summary>
    string Value,

    /// <summary>
    /// Display label for UI
    /// </summary>
    string Label
);

/// <summary>
/// Category field/attribute from external marketplace
/// </summary>
public record ExternalField(
    /// <summary>
    /// Unique field key on marketplace
    /// </summary>
    string Key,

    /// <summary>
    /// Display name
    /// </summary>
    string Name,

    /// <summary>
    /// Is the field required
    /// </summary>
    bool IsRequired,

    /// <summary>
    /// Data type: "string", "number", "boolean", "select", "multiselect"
    /// </summary>
    string Type,

    /// <summary>
    /// Options for select/multiselect
    /// </summary>
    List<ExternalFieldOption>? Options = null,

    /// <summary>
    /// Field description/hint
    /// </summary>
    string? Description = null,

    /// <summary>
    /// Unit of measurement (e.g. "mm", "kg")
    /// </summary>
    string? Unit = null,

    /// <summary>
    /// Max length for string fields
    /// </summary>
    int? MaxLength = null,

    /// <summary>
    /// Min value for numeric fields
    /// </summary>
    decimal? MinValue = null,

    /// <summary>
    /// Max value for numeric fields
    /// </summary>
    decimal? MaxValue = null,

    /// <summary>
    /// Validation pattern (regex)
    /// </summary>
    string? ValidationPattern = null,

    /// <summary>
    /// Field group for UI
    /// </summary>
    string? Group = null,

    /// <summary>
    /// Sort order for display
    /// </summary>
    int SortOrder = 0
);
