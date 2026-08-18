namespace Delegat.Models.Marketplace.DTOs;

/// <summary>
/// Value option for a select/multiselect field.
/// </summary>
/// <param name="Value">Value to send to the marketplace.</param>
/// <param name="Label">Display label for the UI.</param>
public record ExternalFieldOption(
    string Value,
    string Label
);

/// <summary>
/// Category field/attribute from an external marketplace.
/// </summary>
/// <param name="Key">Unique field key on the marketplace.</param>
/// <param name="Name">Display name of the field.</param>
/// <param name="IsRequired">Whether the field is required.</param>
/// <param name="Type">Data type: "string", "number", "boolean", "select", "multiselect".</param>
/// <param name="Options">Value options for select/multiselect.</param>
/// <param name="Description">Field description / hint.</param>
/// <param name="Unit">Unit of measurement (e.g. "mm", "kg").</param>
/// <param name="MaxLength">Maximum length for string fields.</param>
/// <param name="MinValue">Minimum value for numeric fields.</param>
/// <param name="MaxValue">Maximum value for numeric fields.</param>
/// <param name="ValidationPattern">Validation pattern (regex).</param>
/// <param name="Group">Field group/section for the UI.</param>
/// <param name="SortOrder">Sort order for display.</param>
/// <param name="IsAspect">Whether this is an aspect attribute (size, color) - e.g. Ozon is_aspect.</param>
    /// <param name="AllowCustomValues">When true, values outside Options are allowed (typed custom values).</param>
    /// <param name="MaxValues">Maximum number of selected values for select/multiselect. Null means unlimited.</param>
public record ExternalField(
    string Key,
    string Name,
    bool IsRequired,
    string Type,
    List<ExternalFieldOption>? Options = null,
    string? Description = null,
    string? Unit = null,
    int? MaxLength = null,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    string? ValidationPattern = null,
    string? Group = null,
    int SortOrder = 0,
    bool IsAspect = false,
    bool AllowCustomValues = false,
    int? MaxValues = null
);
