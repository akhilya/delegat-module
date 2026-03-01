namespace PrintO.Services.Marketplace.Modifiers;

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
    /// Apply the modifier to a value.
    /// </summary>
    /// <param name="value">Input value (may be null).</param>
    /// <param name="parameters">Modifier parameters from JSON.</param>
    /// <returns>Transformed value.</returns>
    object? Apply(object? value, Dictionary<string, object> parameters);
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
}
