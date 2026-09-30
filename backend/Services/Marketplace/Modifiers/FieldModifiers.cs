using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using static Delegat.Services.Marketplace.Modifiers.ModifierHelpers;

namespace Delegat.Services.Marketplace.Modifiers;

// ===== Text Modifiers =====

public class TruncateModifier : IFieldModifier
{
    public string Type => "truncate";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var maxLength = GetInt(parameters, "maxLength", str.Length);
        return str.Length <= maxLength ? str : str[..maxLength];
    }
}

public class SplitModifier : IFieldModifier
{
    public string Type => "split";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var delimiter = GetString(parameters, "delimiter", ",");
        var index = GetInt(parameters, "index", 0);
        var parts = str.Split(delimiter);
        return index >= 0 && index < parts.Length ? parts[index] : null;
    }
}

public class ReplaceModifier : IFieldModifier
{
    public string Type => "replace";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var search = GetString(parameters, "search", "");
        var replacement = GetString(parameters, "replacement", "");
        return string.IsNullOrEmpty(search) ? str : str.Replace(search, replacement);
    }
}

public class AppendModifier : IFieldModifier
{
    public string Type => "append";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var suffix = GetString(parameters, "suffix", "");
        return str + suffix;
    }
}

public class PrependModifier : IFieldModifier
{
    public string Type => "prepend";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var prefix = GetString(parameters, "prefix", "");
        return prefix + str;
    }
}

public class UppercaseModifier : IFieldModifier
{
    public string Type => "uppercase";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        return value?.ToString()?.ToUpperInvariant();
    }
}

public class LowercaseModifier : IFieldModifier
{
    public string Type => "lowercase";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        return value?.ToString()?.ToLowerInvariant();
    }
}

public class TrimModifier : IFieldModifier
{
    public string Type => "trim";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        return value?.ToString()?.Trim();
    }
}

/// <summary>
/// Rewrites text with a regular expression. An empty or invalid pattern, or a match that
/// runs too long, leaves the original string so a half-typed pattern does not wipe an export.
/// </summary>
public class RegexReplaceModifier : IFieldModifier
{
    public string Type => "regexReplace";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var pattern = GetString(parameters, "pattern", "");
        if (string.IsNullOrEmpty(pattern)) return str;
        var replacement = GetString(parameters, "replacement", "") ?? "";
        return ApplyRegex(str, pattern, GetBool(parameters, "ignoreCase", false), regex => regex.Replace(str, replacement));
    }

    internal static string ApplyRegex(string str, string pattern, bool ignoreCase, Func<Regex, string> apply)
    {
        try
        {
            var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = new Regex(pattern, options, TimeSpan.FromMilliseconds(200));
            return apply(regex);
        }
        catch (ArgumentException)
        {
            return str;
        }
        catch (RegexMatchTimeoutException)
        {
            return str;
        }
    }
}

/// <summary>
/// Returns one match. Group 0 is the whole match; a higher group returns that capture.
/// No match, an empty pattern, or an invalid pattern yields an empty string (no match)
/// or the original string (bad pattern), so the export keeps going.
/// </summary>
public class RegexExtractModifier : IFieldModifier
{
    public string Type => "regexExtract";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var str = value?.ToString();
        if (str == null) return null;
        var pattern = GetString(parameters, "pattern", "");
        if (string.IsNullOrEmpty(pattern)) return str;
        var group = GetInt(parameters, "group", 0);
        return RegexReplaceModifier.ApplyRegex(str, pattern, GetBool(parameters, "ignoreCase", false), regex =>
        {
            var match = regex.Match(str);
            if (!match.Success) return "";
            if (group < 0 || group >= match.Groups.Count) return "";
            return match.Groups[group].Value;
        });
    }
}

// ===== Number Modifiers =====

public class MultiplyModifier : IFieldModifier
{
    public string Type => "multiply";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var factor = GetDecimal(parameters, "factor", 1);
        return num.Value * factor;
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var factor = GetDecimal(parameters, "factor", 1);
        return factor == 0 ? value : num.Value / factor;
    }
}

public class DivideModifier : IFieldModifier
{
    public string Type => "divide";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var divisor = GetDecimal(parameters, "divisor", 1);
        return divisor == 0 ? value : num.Value / divisor;
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var divisor = GetDecimal(parameters, "divisor", 1);
        return num.Value * divisor;
    }
}

public class AddModifier : IFieldModifier
{
    public string Type => "add";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var addend = GetDecimal(parameters, "addend", 0);
        return num.Value + addend;
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var addend = GetDecimal(parameters, "addend", 0);
        return num.Value - addend;
    }
}

public class SubtractModifier : IFieldModifier
{
    public string Type => "subtract";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var subtrahend = GetDecimal(parameters, "subtrahend", 0);
        return num.Value - subtrahend;
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var subtrahend = GetDecimal(parameters, "subtrahend", 0);
        return num.Value + subtrahend;
    }
}

public class RoundModifier : IFieldModifier
{
    public string Type => "round";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var decimals = GetInt(parameters, "decimals", 0);
        return Math.Round(num.Value, decimals);
    }
}

public class FloorModifier : IFieldModifier
{
    public string Type => "floor";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        return Math.Floor(num.Value);
    }
}

public class CeilingModifier : IFieldModifier
{
    public string Type => "ceiling";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        return Math.Ceiling(num.Value);
    }
}

public class ClampModifier : IFieldModifier
{
    public string Type => "clamp";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var num = ToDecimal(value);
        if (num == null) return value;
        var min = GetDecimal(parameters, "min", decimal.MinValue);
        var max = GetDecimal(parameters, "max", decimal.MaxValue);
        return Math.Clamp(num.Value, min, max);
    }
}

// ===== Select / Other Modifiers =====

public class ValueMapModifier : IFieldModifier
{
    public string Type => "valueMap";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        // Handle list/array values: map each element individually
        var elements = TryParseList(value);
        if (elements != null)
        {
            var mappings = GetMappingsList(parameters);
            var defaultValue = GetString(parameters, "default", null);
            var mapped = elements
                .Select(el => MapSingle(el, mappings, defaultValue))
                .ToList();
            return JsonSerializer.Serialize(mapped);
        }

        var str = value?.ToString();
        if (str == null) return GetString(parameters, "default", null);

        return MapSingle(str, GetMappingsList(parameters), GetString(parameters, "default", null));
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
    {
        var elements = TryParseList(value);
        if (elements != null)
        {
            var mappings = GetMappingsList(parameters);
            var mapped = elements
                .Select(el => ReverseMapSingle(el, mappings))
                .ToList();
            return JsonSerializer.Serialize(mapped);
        }

        var str = value?.ToString();
        if (str == null) return null;

        return ReverseMapSingle(str, GetMappingsList(parameters));
    }

    private static string MapSingle(string str, List<(string from, string to)> mappings, string? defaultValue)
    {
        foreach (var mapping in mappings)
        {
            if (string.Equals(mapping.from, str, StringComparison.OrdinalIgnoreCase))
                return mapping.to;
        }
        return defaultValue ?? str;
    }

    private static string ReverseMapSingle(string str, List<(string from, string to)> mappings)
    {
        foreach (var mapping in mappings)
        {
            if (string.Equals(mapping.to, str, StringComparison.OrdinalIgnoreCase))
                return mapping.from;
        }
        return str; // no reverse match - pass through
    }

    private static List<(string from, string to)> GetMappingsList(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("mappings", out var mappingsObj))
            return new List<(string, string)>();

        if (mappingsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            return ParseMapPairsFromJsonArray(jsonElement);

        if (mappingsObj is IEnumerable enumerable)
        {
            var result = new List<(string, string)>();
            foreach (var item in enumerable)
            {
                if (item is JsonElement el)
                {
                    var from = el.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null;
                    var to = el.TryGetProperty("to", out var toProp) ? toProp.GetString() : null;
                    if (from != null && to != null)
                        result.Add((from, to));
                    continue;
                }

                if (item is IDictionary dict)
                {
                    var from = dict.Contains("from") ? dict["from"]?.ToString() : null;
                    var to = dict.Contains("to") ? dict["to"]?.ToString() : null;
                    if (from != null && to != null)
                        result.Add((from, to));
                }
            }
            return result;
        }

        return new List<(string, string)>();
    }

    private static List<(string from, string to)> ParseMapPairsFromJsonArray(JsonElement jsonElement)
    {
        var result = new List<(string, string)>();
        foreach (var item in jsonElement.EnumerateArray())
        {
            var from = item.TryGetProperty("from", out var fromProp) ? fromProp.GetString() : null;
            var to = item.TryGetProperty("to", out var toProp) ? toProp.GetString() : null;
            if (from != null && to != null)
                result.Add((from, to));
        }
        return result;
    }
}

/// <summary>
/// Same logic as ValueMapModifier but registered under "optionMap" type.
/// UI uses this to map source values to marketplace option values.
/// </summary>
public class OptionMapModifier : IContextAwareModifier
{
    public string Type => "optionMap";
    public bool IsReversible => true;

    private static readonly ValueMapModifier _inner = new();

    public object? Apply(object? value, Dictionary<string, object> parameters)
        => _inner.Apply(value, parameters);

    public object? ApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context)
    {
        var result = _inner.Apply(value, parameters);
        return ValidateTarget(result, context.TargetFieldOptions);
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
        => _inner.ReverseApply(value, parameters);

    public object? ReverseApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context)
        => ReverseApply(value, parameters);

    private static object? ValidateTarget(object? result, List<SourceOption>? allowed)
    {
        if (allowed is not { Count: > 0 } || result == null) return result;
        var allowedSet = new HashSet<string>(
            allowed.Select(a => a.value ?? "").Where(v => v.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        if (allowedSet.Count == 0) return result;

        var elements = ModifierHelpers.TryParseList(result);
        if (elements != null)
        {
            return JsonSerializer.Serialize(elements
                .Select(e => allowedSet.Contains(e) ? e : e)
                .ToList());
        }

        var s = result.ToString() ?? "";
        return allowedSet.Contains(s) ? s : s;
    }
}

// ===== Random modifiers (seeded, irreversible) =====

public class RandomIntModifier : IFieldModifier
{
    public string Type => "randomInt";
    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var min = ModifierHelpers.GetInt(parameters, "min", 0);
        var max = ModifierHelpers.GetInt(parameters, "max", 100);
        if (max < min) (min, max) = (max, min);
        var rng = RandomModifierHelper.CreateRng(parameters, value);
        return rng.Next(min, max + 1);
    }
}

public class RandomFloatModifier : IFieldModifier
{
    public string Type => "randomFloat";
    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var min = (double)ModifierHelpers.GetDecimal(parameters, "min", 0m);
        var max = (double)ModifierHelpers.GetDecimal(parameters, "max", 1m);
        var decimals = ModifierHelpers.GetInt(parameters, "decimals", 2);
        if (max < min) (min, max) = (max, min);
        var rng = RandomModifierHelper.CreateRng(parameters, value);
        var raw = min + rng.NextDouble() * (max - min);
        return Math.Round(raw, decimals).ToString(CultureInfo.InvariantCulture);
    }
}

public class RandomPickModifier : IFieldModifier
{
    public string Type => "randomPick";
    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var choices = ModifierHelpers.GetStringList(parameters, "choices");
        if (choices.Count == 0 && value != null)
            choices = ModifierHelpers.TryParseList(value) ?? [value.ToString()!];
        if (choices.Count == 0) return value;
        var rng = RandomModifierHelper.CreateRng(parameters, value);
        return choices[rng.Next(choices.Count)];
    }
}

public class RandomUuidModifier : IFieldModifier
{
    public string Type => "randomUuid";
    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var rng = RandomModifierHelper.CreateRng(parameters, value);
        var bytes = new byte[16];
        rng.NextBytes(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }
}

internal static class RandomModifierHelper
{
    internal static Random CreateRng(Dictionary<string, object> parameters, object? value)
    {
        var seed = parameters.ContainsKey("seed")
            ? ModifierHelpers.GetInt(parameters, "seed", 0)
            : HashCode.Combine(value?.ToString() ?? "", parameters.GetValueOrDefault("choices")?.ToString() ?? "");
        return new Random(seed);
    }
}

public class DefaultValueModifier : IFieldModifier
{
    public string Type => "defaultValue";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        if (value == null || (value is string s && string.IsNullOrEmpty(s)))
            return GetString(parameters, "value", null);
        return value;
    }
}

public class TakeFirstModifier : IFieldModifier
{
    public string Type => "takeFirst";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list != null && list.Count > 0)
            return list[0];
        return value;
    }
}

// ===== Array Modifiers =====

public class LimitModifier : IFieldModifier
{
    public string Type => "limit";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list == null) return value;
        var count = GetInt(parameters, "count", 5);
        return JsonSerializer.Serialize(list.Take(count).ToList());
    }
}

public class SkipModifier : IFieldModifier
{
    public string Type => "skip";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list == null) return value;
        var count = GetInt(parameters, "count", 1);
        return JsonSerializer.Serialize(list.Skip(count).ToList());
    }
}

public class ReverseModifier : IFieldModifier
{
    public string Type => "reverse";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list == null) return value;
        list.Reverse();
        return JsonSerializer.Serialize(list);
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
        => Apply(value, parameters);
}

public class JoinModifier : IFieldModifier
{
    public string Type => "join";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list == null) return value;
        var delimiter = GetString(parameters, "delimiter", ", ");
        return string.Join(delimiter ?? ", ", list);
    }
}

public class SortModifier : IFieldModifier
{
    public string Type => "sort";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list == null) return value;
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(list);
    }
}

public class AddItemsModifier : IFieldModifier
{
    public string Type => "addItems";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value) ?? new List<string>();
        var items = GetStringList(parameters, "items");
        list.AddRange(items);
        return JsonSerializer.Serialize(list);
    }
}

public class RemoveItemsModifier : IFieldModifier
{
    public string Type => "removeItems";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        var list = TryParseList(value);
        if (list == null) return value;
        var items = GetStringList(parameters, "items");
        var toRemove = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(list.Where(x => !toRemove.Contains(x)).ToList());
    }
}

public class ToTextModifier : IContextAwareModifier
{
    public string Type => "toText";
    public bool IsReversible => true;

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        return value?.ToString();
    }

    public object? ApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context)
    {
        if (value == null) return null;

        var useLabel = false;
        if (parameters.TryGetValue("useLabel", out var useLabelObj))
        {
            useLabel = useLabelObj is bool b ? b
                : useLabelObj is JsonElement je ? je.ValueKind == JsonValueKind.True
                : false;
        }

        var options = useLabel ? context.SourceAttributeOptions : null;

        if (options is { Count: > 0 })
        {
            // Handle array values: map each element
            var elements = TryParseList(value);
            if (elements != null)
            {
                var mapped = elements
                    .Select(el => LookupLabel(el, options) ?? el)
                    .ToList();
                return mapped;
            }

            // Single value
            var str = value.ToString();
            return LookupLabel(str, options) ?? str;
        }

        // No label lookup - just convert to string
        var list = TryParseList(value);
        if (list != null)
            return list.Select(el => el).ToList();

        return value.ToString();
    }

    public object? ReverseApply(object? value, Dictionary<string, object> parameters)
    {
        return value; // pass-through without context
    }

    public object? ReverseApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context)
    {
        if (value == null) return null;

        if (context.SourceAttributeOptions is { Count: > 0 } options)
        {
            var elements = TryParseList(value);
            if (elements != null)
            {
                var mapped = elements
                    .Select(el => LookupValue(el, options) ?? el)
                    .ToList();
                return JsonSerializer.Serialize(mapped);
            }

            var str = value.ToString();
            return LookupValue(str, options) ?? str;
        }

        return value; // pass-through for non-select types
    }

    private static string? LookupLabel(string? val, List<SourceOption> options)
    {
        if (val == null) return null;
        return options.FirstOrDefault(o =>
            string.Equals(o.value, val, StringComparison.OrdinalIgnoreCase))?.label;
    }

    private static string? LookupValue(string? label, List<SourceOption> options)
    {
        if (label == null) return null;
        return options.FirstOrDefault(o =>
            string.Equals(o.label, label, StringComparison.OrdinalIgnoreCase))?.value;
    }
}

public class ForceTypeModifier : IFieldModifier
{
    public string Type => "forceType";
    public bool IsReversible => true;
    public object? Apply(object? value, Dictionary<string, object> parameters) => value;
    public object? ReverseApply(object? value, Dictionary<string, object> parameters) => value;
}

// ===== Context-Aware Modifiers =====

public class TemplateModifier : IContextAwareModifier
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);

    public string Type => "template";

    public object? Apply(object? value, Dictionary<string, object> parameters)
    {
        // Without context, just do {{value}} replacement
        var template = GetString(parameters, "template", null);
        if (string.IsNullOrEmpty(template)) return value;

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (key.Equals("value", StringComparison.OrdinalIgnoreCase))
                return value?.ToString() ?? "";
            return match.Value; // leave unresolved
        });
    }

    public object? ApplyWithContext(object? value, Dictionary<string, object> parameters, ModifierContext context)
    {
        var template = GetString(parameters, "template", null);
        if (string.IsNullOrEmpty(template)) return value;

        return PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (key.Equals("value", StringComparison.OrdinalIgnoreCase))
                return value?.ToString() ?? "";

            var resolved = context.ResolveReference?.Invoke(key);
            return resolved?.ToString() ?? "";
        });
    }
}

/// <summary>
/// Shared helper methods for extracting typed parameters from the modifier params dictionary.
/// </summary>
internal static class ModifierHelpers
{
    public static string? GetString(Dictionary<string, object> parameters, string key, string? defaultValue)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is JsonElement je)
                return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();
            return val?.ToString();
        }
        return defaultValue;
    }

    public static bool GetBool(Dictionary<string, object> parameters, string key, bool defaultValue)
    {
        if (!parameters.TryGetValue(key, out var val) || val == null)
            return defaultValue;
        if (val is bool b) return b;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var parsed))
                return parsed;
        }
        if (bool.TryParse(val.ToString(), out var p)) return p;
        return defaultValue;
    }

    public static int GetInt(Dictionary<string, object> parameters, string key, int defaultValue)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i)) return i;
                if (int.TryParse(je.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
            if (val is int i2) return i2;
            if (int.TryParse(val?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return defaultValue;
    }

    public static decimal GetDecimal(Dictionary<string, object> parameters, string key, decimal defaultValue)
    {
        if (parameters.TryGetValue(key, out var val))
        {
            if (val is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var d)) return d;
                if (decimal.TryParse(je.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            }
            if (val is decimal d2) return d2;
            if (decimal.TryParse(val?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return defaultValue;
    }

    public static decimal? ToDecimal(object? value)
    {
        if (value == null) return null;
        if (value is decimal d) return d;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var jd)) return jd;
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    /// <summary>
    /// Try to parse value as a list of strings (List&lt;string&gt;, JsonElement array, or JSON array string).
    /// Returns null if value is not a list.
    /// </summary>
    public static List<string>? TryParseList(object? value)
    {
        if (value is List<string> list)
            return list;

        if (value is List<object> objList)
            return objList.Select(o => o?.ToString() ?? "").ToList();

        if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var item in je.EnumerateArray())
                result.Add(item.GetString() ?? item.ToString());
            return result;
        }

        var str = value?.ToString();
        if (str != null && str.StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(str);
                if (parsed != null)
                    return parsed;
            }
            catch (JsonException) { }
        }

        // Comma-separated fallback (legacy format)
        if (str != null && str.Contains(','))
        {
            var parts = str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
                return parts.ToList();
        }

        return null;
    }

    /// <summary>
    /// Get a list of strings from a parameter (supports JsonElement array or List&lt;string&gt;).
    /// </summary>
    public static List<string> GetStringList(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var val))
            return new List<string>();

        if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var item in je.EnumerateArray())
                result.Add(item.GetString() ?? item.ToString());
            return result;
        }

        if (val is List<string> list)
            return list;

        return TryParseList(val) ?? new List<string>();
    }
}
