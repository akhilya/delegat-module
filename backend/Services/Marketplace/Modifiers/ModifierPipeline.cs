using System.Text.Json;

namespace PrintO.Services.Marketplace.Modifiers;

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
            new AppendModifier(),
            new PrependModifier(),
            new UppercaseModifier(),
            new LowercaseModifier(),
            new TrimModifier(),
            // Number
            new MultiplyModifier(),
            new DivideModifier(),
            new AddModifier(),
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
        };

        _modifiers = modifierInstances.ToDictionary(m => m.Type, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Apply modifier chain from JSON to a value.
    /// </summary>
    /// <param name="value">Input value.</param>
    /// <param name="modifiersJson">JSON array of modifier descriptors: [{"type":"...", "params":{...}}, ...]</param>
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

            if (!_modifiers.TryGetValue(descriptor.type, out var modifier))
                continue;

            var parameters = descriptor.@params ?? new Dictionary<string, object>();

            if (modifier is IContextAwareModifier contextAware && context != null)
                current = contextAware.ApplyWithContext(current, parameters, context);
            else
                current = modifier.Apply(current, parameters);
        }

        return current;
    }

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
