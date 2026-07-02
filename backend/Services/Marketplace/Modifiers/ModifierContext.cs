namespace Delegat.Services.Marketplace.Modifiers;

/// <summary>
/// Context passed to context-aware modifiers, providing access to
/// product fields and attributes during template resolution.
/// </summary>
public class ModifierContext
{
    /// <summary>
    /// Resolves a reference like "Name", "attr:5", "storeAttr:3" to a value.
    /// </summary>
    public Func<string, object?>? ResolveReference { get; init; }

    /// <summary>
    /// Options of the source attribute (for select/multiselect fields).
    /// Used by ToTextModifier to resolve value→label.
    /// </summary>
    public List<SourceOption>? SourceAttributeOptions { get; init; }
}

/// <summary>
/// A single option entry from a select/multiselect attribute.
/// </summary>
public class SourceOption
{
    public string? value { get; set; }
    public string? label { get; set; }
}
