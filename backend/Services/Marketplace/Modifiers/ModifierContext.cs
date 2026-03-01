namespace PrintO.Services.Marketplace.Modifiers;

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
}
