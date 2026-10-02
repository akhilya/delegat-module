using System.Text.Json;

namespace Delegat.Services;

/// <summary>
/// Folds a graph/modifiers body into the params dictionary the canvas stores.
/// Pattern and replacement sent beside modifierType are kept, so a flat body does not save an empty regex block.
/// </summary>
public static class GraphModifierParams
{
    public static Dictionary<string, object>? Merge(
        string? modifierType,
        Dictionary<string, object>? fromParams,
        string? pattern,
        string? replacement,
        bool? ignoreCase,
        int? group)
    {
        Dictionary<string, object>? map = fromParams == null
            ? null
            : new Dictionary<string, object>(fromParams, StringComparer.Ordinal);

        void Set(string key, object value)
        {
            map ??= new Dictionary<string, object>(StringComparer.Ordinal);
            if (!map.ContainsKey(key))
                map[key] = value;
        }

        if (pattern != null)
            Set("pattern", pattern);
        if (replacement != null)
            Set("replacement", replacement);
        if (ignoreCase != null)
            Set("ignoreCase", ignoreCase.Value);
        if (group != null)
            Set("group", group.Value);

        if (IsRegex(modifierType) && string.IsNullOrWhiteSpace(ReadString(map, "pattern")))
        {
            throw new InvalidOperationException(
                "regexReplace and regexExtract require params.pattern. Put pattern and replacement inside params, not next to modifierType.");
        }

        return map is { Count: > 0 } ? map : null;
    }

    private static bool IsRegex(string? type) =>
        string.Equals(type, "regexReplace", StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, "regexExtract", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(Dictionary<string, object>? map, string key)
    {
        if (map == null || !map.TryGetValue(key, out var val) || val == null)
            return null;
        if (val is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
        return val.ToString();
    }
}
