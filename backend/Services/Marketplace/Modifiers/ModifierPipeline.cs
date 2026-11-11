using System.Text.Json;

namespace Delegat.Services.Marketplace.Modifiers;

/// <summary>
/// Applies a chain of modifiers to a field value.
/// Deserializes modifiersJson and applies each modifier in order.
/// </summary>
public static class ModifierPipeline
{
    private static readonly Dictionary<string, IFieldModifier> _modifiers;

    static ModifierPipeline()
    {
        var modifierInstances = new IFieldModifier[]
        {
            // Text
            new TruncateModifier(),
            new SplitModifier(),
            new ReplaceModifier(),
            new RegexReplaceModifier(),
            new RegexExtractModifier(),
            new AppendModifier(),
            new PrependModifier(),
            new UppercaseModifier(),
            new LowercaseModifier(),
            new TrimModifier(),
            // Number
            new UnitConversionModifier(),
            new MultiplyModifier(),
            new DivideModifier(),
            new AddModifier(),
            new SubtractModifier(),
            new RoundModifier(),
            new FloorModifier(),
            new CeilingModifier(),
            new ClampModifier(),
            // Select / Other
            new ValueMapModifier(),
            new OptionMapModifier(),
            new ToTextModifier(),
            new ForceTypeModifier(),
            new DefaultValueModifier(),
            new TakeFirstModifier(),
            // Array
            new LimitModifier(),
            new SkipModifier(),
            new ReverseModifier(),
            new JoinModifier(),
            new SortModifier(),
            new AddItemsModifier(),
            new RemoveItemsModifier(),
            // Context-aware
            new TemplateModifier(),
            // Random (seeded)
            new RandomIntModifier(),
            new RandomFloatModifier(),
            new RandomPickModifier(),
            new RandomUuidModifier(),
        };

        _modifiers = modifierInstances.ToDictionary(m => m.Type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Apply modifier chain from JSON to a value.
    /// </summary>
    /// <param name="value">Input value.</param>
    /// <param name="modifiersJson">JSON array of modifier descriptors: [{"type":"...", "params":{...}}, ...]</param>
    /// <param name="context">Optional modifier evaluation context.</param>
    /// <returns>Transformed value.</returns>
    public static object? ApplyModifiers(object? value, string? modifiersJson, ModifierContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(modifiersJson))
            return value;

        List<ModifierDescriptor>? descriptors;
        try
        {
            descriptors = JsonSerializer.Deserialize<List<ModifierDescriptor>>(modifiersJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return value;
        }

        if (descriptors == null || descriptors.Count == 0)
            return value;

        var current = value;
        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrEmpty(descriptor.type))
                continue;

            if (descriptor.enabled == false)
                continue;

            current = ApplySingle(descriptor.type, current, descriptor.@params, context);
        }

        return current;
    }

    /// <summary>
    /// Apply a single modifier by type to a value. Shared by the linear chain
    /// (<see cref="ApplyModifiers"/>) and the visual node-graph evaluator so both paths
    /// run the exact same modifier implementations. Unknown types pass the value through.
    /// </summary>
    public static object? ApplySingle(string type, object? value, Dictionary<string, object>? parameters,
        ModifierContext? context = null)
    {
        if (string.IsNullOrEmpty(type))
            return value;

        if (!_modifiers.TryGetValue(type, out var modifier))
            return value;

        var p = parameters ?? new Dictionary<string, object>();

        if (modifier is IContextAwareModifier contextAware && context != null)
            return contextAware.ApplyWithContext(value, p, context);

        return modifier.Apply(value, p);
    }

    /// <summary>
    /// Whether a modifier type is registered. Used by graph validation.
    /// </summary>
    public static bool IsKnownModifier(string? type) =>
        !string.IsNullOrEmpty(type) && _modifiers.ContainsKey(type);

    /// <summary>
    /// All registered modifier type ids - for validation error messages and AI tool schemas.
    /// </summary>
    public static IReadOnlyCollection<string> KnownModifierTypes => _modifiers.Keys;

    /// <summary>
    /// Whether a single modifier type is reversible (import direction).
    /// </summary>
    public static bool IsReversible(string? type) =>
        !string.IsNullOrEmpty(type) && _modifiers.TryGetValue(type, out var m) && m.IsReversible;

    /// <summary>
    /// Apply modifier chain in reverse (import direction).
    /// Deserializes descriptors, reverses order, and calls ReverseApply on each.
    /// </summary>
    public static object? ApplyModifiersReverse(object? value, string? modifiersJson, ModifierContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(modifiersJson))
            return value;

        List<ModifierDescriptor>? descriptors;
        try
        {
            descriptors = JsonSerializer.Deserialize<List<ModifierDescriptor>>(modifiersJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return value;
        }

        if (descriptors == null || descriptors.Count == 0)
            return value;

        // Reverse the order for import
        descriptors.Reverse();

        var current = value;
        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrEmpty(descriptor.type))
                continue;

            if (descriptor.enabled == false)
                continue;

            if (!_modifiers.TryGetValue(descriptor.type, out var modifier))
                continue;

            var parameters = descriptor.@params ?? new Dictionary<string, object>();

            if (modifier is IContextAwareModifier contextAware && context != null)
                current = contextAware.ReverseApplyWithContext(current, parameters, context);
            else
                current = modifier.ReverseApply(current, parameters);
        }

        return current;
    }

    /// <summary>
    /// Check if a modifier chain JSON contains any irreversible modifiers.
    /// </summary>
    public static bool HasIrreversibleModifiers(string? modifiersJson)
    {
        if (string.IsNullOrWhiteSpace(modifiersJson))
            return false;

        List<ModifierDescriptor>? descriptors;
        try
        {
            descriptors = JsonSerializer.Deserialize<List<ModifierDescriptor>>(modifiersJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return false;
        }

        if (descriptors == null || descriptors.Count == 0)
            return false;

        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrEmpty(descriptor.type))
                continue;

            if (descriptor.enabled == false)
                continue;

            if (_modifiers.TryGetValue(descriptor.type, out var modifier) && !modifier.IsReversible)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Descriptor for a single modifier in the chain.
    /// </summary>
    private class ModifierDescriptor
    {
        public string type { get; set; } = "";
        public bool? enabled { get; set; }  // null = true (backward compat)

        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, object>? _extra { get; set; }

        public Dictionary<string, object>? @params { get; set; }
    }
}
