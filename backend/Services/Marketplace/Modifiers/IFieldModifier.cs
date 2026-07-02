namespace Delegat.Services.Marketplace.Modifiers;

/// <summary>
/// Interface for a single field value modifier.
/// </summary>
public interface IFieldModifier
{
    /// <summary>
    /// Unique type identifier (e.g. "truncate", "multiply", "valueMap").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Whether this modifier can be reversed during import.
    /// Irreversible modifiers (truncate, uppercase, template, etc.) pass through on reverse.
    /// </summary>
    bool IsReversible => false;

    /// <summary>
    /// Apply the modifier to a value (export direction).
    /// </summary>
    object? Apply(object? value, Dictionary<string, object> parameters);

    /// <summary>
    /// Reverse-apply the modifier (import direction).
    /// Default implementation is pass-through for irreversible modifiers.
    /// </summary>
    object? ReverseApply(object? value, Dictionary<string, object> parameters) => value;
}

/// <summary>
/// Extended modifier interface that receives a ModifierContext
/// for resolving external references (e.g. product fields, attributes).
/// </summary>
public interface IContextAwareModifier : IFieldModifier
{
    /// <summary>
    /// Apply the modifier with access to a resolution context.
    /// </summary>
    object? ApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context);

    /// <summary>
    /// Reverse-apply the modifier with context (import direction).
    /// Default delegates to ReverseApply without context.
    /// </summary>
    object? ReverseApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context)
        => ReverseApply(value, parameters);
}
