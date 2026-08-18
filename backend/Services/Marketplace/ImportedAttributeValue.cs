using System.Collections;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Delegat.Enums;
using Delegat.Infrastructure;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace.Mapping;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Converts marketplace attribute payloads into the invariant strings the product form stores
/// (numbers with <c>.</c>, booleans <c>true</c>/<c>false</c>, multiselect JSON arrays).
/// </summary>
public static class ImportedAttributeValue
{
    private static readonly JsonSerializerOptions JsonWrite = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Dictionary<string, object> Collect(ExternalProduct product)
    {
        var attrs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (product.Attributes != null)
        {
            foreach (var (key, value) in product.Attributes)
            {
                if (string.IsNullOrWhiteSpace(key) || value == null)
                    continue;
                attrs[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(product.Brand))
        {
            attrs.TryAdd("brand", product.Brand);
            attrs.TryAdd("vendor", product.Brand);
            attrs.TryAdd("manufacturer", product.Brand);
        }

        return attrs;
    }

    public static bool TryGet(
        IReadOnlyDictionary<string, object> byKey,
        string? externalFieldKey,
        string? externalFieldName,
        out object value)
    {
        if (!string.IsNullOrWhiteSpace(externalFieldKey)
            && byKey.TryGetValue(externalFieldKey, out var fromKey)
            && fromKey != null)
        {
            value = fromKey;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(externalFieldName)
            && byKey.TryGetValue(externalFieldName, out var fromName)
            && fromName != null)
        {
            value = fromName;
            return true;
        }

        value = null!;
        return false;
    }

    public static string? ToStoredString(object? value, AttributeType type, string? optionsJson = null)
    {
        if (value == null)
            return null;

        if (value is JsonElement json && json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var stored = type switch
        {
            AttributeType.Number => FormatNumber(value),
            AttributeType.Boolean => FormatBoolean(value),
            AttributeType.MultiSelect => FormatMultiSelect(value, optionsJson),
            AttributeType.Select => FormatSelect(value, optionsJson),
            _ => FormatText(value),
        };

        return StringClip.ClipOptional(stored, ProductAttributeValue.VALUE_MAX_LENGTH);
    }

    /// <summary>
    /// Human-readable preview: dictionary ids become labels, multiselect is a comma-separated list.
    /// </summary>
    public static string? ToDisplayString(string? stored, AttributeType type, string? optionsJson)
    {
        if (string.IsNullOrEmpty(stored))
            return stored;
        if (type is not AttributeType.Select and not AttributeType.MultiSelect)
            return stored;

        var options = OptionAlignmentHelper.ParseAttributeOptions(optionsJson);
        var labels = ParseItems(stored, type)
            .Select(token => MapOption(token, options, toLabel: true))
            .ToList();
        return labels.Count == 0 ? stored : string.Join(", ", labels);
    }

    /// <summary>
    /// True when both strings are the same select/multiselect value (id ≡ label, order-insensitive).
    /// </summary>
    public static bool AreEquivalent(string? left, string? right, AttributeType type, string? optionsJson)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;
        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            return true;
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        if (type is not AttributeType.Select and not AttributeType.MultiSelect)
            return false;

        var options = OptionAlignmentHelper.ParseAttributeOptions(optionsJson);
        var a = ParseItems(left, type)
            .Select(token => MapOption(token, options, toLabel: false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var b = ParseItems(right, type)
            .Select(token => MapOption(token, options, toLabel: false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return a.SetEquals(b);
    }

    private static string? FormatNumber(object value)
    {
        if (!TryParseDecimal(value, out var number))
            return null;
        if (number == decimal.Truncate(number))
            return decimal.Truncate(number).ToString("0", CultureInfo.InvariantCulture);
        return number.ToString("0.############", CultureInfo.InvariantCulture);
    }

    internal static bool TryParseDecimal(object? value, out decimal number)
    {
        number = 0;
        switch (value)
        {
            case null:
                return false;
            case decimal m:
                number = m;
                return true;
            case double d when double.IsFinite(d):
                number = (decimal)d;
                return true;
            case float f when float.IsFinite(f):
                number = (decimal)f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case short s:
                number = s;
                return true;
            case JsonElement je:
                return TryParseDecimalJson(je, out number);
            case string text:
                return TryParseDecimalString(text, out number);
            case IEnumerable enumerable when value is not string:
                foreach (var item in enumerable)
                    return TryParseDecimal(item, out number);
                return false;
            default:
                return TryParseDecimalString(
                    Convert.ToString(value, CultureInfo.InvariantCulture), out number);
        }
    }

    private static bool TryParseDecimalJson(JsonElement je, out decimal number)
    {
        number = 0;
        if (je.ValueKind == JsonValueKind.Number)
        {
            if (je.TryGetDecimal(out number))
                return true;
            if (je.TryGetDouble(out var d) && double.IsFinite(d))
            {
                number = (decimal)d;
                return true;
            }
            return false;
        }

        if (je.ValueKind == JsonValueKind.String)
            return TryParseDecimalString(je.GetString(), out number);

        if (je.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
                return TryParseDecimalJson(el, out number);
        }

        return false;
    }

    internal static bool TryParseDecimalString(string? raw, out decimal number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var s = raw.Trim().Replace("\u00A0", "").Replace(" ", "");
        var lastComma = s.LastIndexOf(',');
        var lastDot = s.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            s = lastComma > lastDot
                ? s.Replace(".", "").Replace(',', '.')
                : s.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            s = s.Replace(',', '.');
        }

        return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static string? FormatBoolean(object value)
    {
        var token = UnwrapFirst(value)?.ToString()?.Trim();
        if (string.IsNullOrEmpty(token))
            return null;

        if (value is bool b)
            return b ? "true" : "false";
        if (value is JsonElement je && je.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return je.GetBoolean() ? "true" : "false";

        return token.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "\u0434\u0430" => "true",
            "false" or "0" or "no" or "\u043d\u0435\u0442" => "false",
            _ => null,
        };
    }

    private static string? FormatMultiSelect(object value, string? optionsJson)
    {
        var options = OptionAlignmentHelper.ParseAttributeOptions(optionsJson);
        var items = ParseItems(value, AttributeType.MultiSelect)
            .Select(token => MapOption(token, options, toLabel: false))
            .ToList();
        if (items.Count == 0)
            return null;
        return JsonSerializer.Serialize(items, JsonWrite);
    }

    private static string? FormatSelect(object value, string? optionsJson)
    {
        var first = ParseItems(value, AttributeType.Select).FirstOrDefault();
        if (string.IsNullOrEmpty(first))
            return null;
        var options = OptionAlignmentHelper.ParseAttributeOptions(optionsJson);
        return MapOption(first, options, toLabel: false);
    }

    private static List<string> ParseItems(object? value, AttributeType type)
    {
        var items = Flatten(value)
            .Select(FormatScalarText)
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .ToList();

        if (type == AttributeType.MultiSelect && items.Count == 1)
            items = SplitCommaList(items[0]);

        if (type == AttributeType.Select)
            return items.Count == 0 ? [] : [items[0]];

        return items;
    }

    private static List<string> SplitCommaList(string text)
    {
        if (!text.Contains(','))
            return [text];

        var parts = text
            .Split(',')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
        return parts.Count > 1 ? parts : [text];
    }

    private static string MapOption(
        string token,
        IReadOnlyList<OptionAlignmentHelper.ParsedOption> options,
        bool toLabel)
    {
        if (options.Count == 0)
            return token;

        var match = options.FirstOrDefault(option =>
            string.Equals(option.Value, token, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option.Label, token, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return token;
        return toLabel ? match.Label : match.Value;
    }

    private static string? FormatText(object value)
    {
        var parts = Flatten(value)
            .Select(v => FormatScalarText(v))
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .ToList();
        if (parts.Count == 0)
            return null;
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts);
    }

    private static string? FormatScalarText(object? value)
    {
        if (value == null)
            return null;
        if (TryParseDecimal(value, out var number) && value is not string and not JsonElement { ValueKind: JsonValueKind.String })
            return FormatNumber(number);
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => FormatNumber(je),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.ToString(),
            };
        }

        if (value is bool b)
            return b ? "true" : "false";
        if (value is IFormattable fmt)
            return fmt.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString()?.Trim();
    }

    private static object? UnwrapFirst(object value)
    {
        return Flatten(value).FirstOrDefault();
    }

    private static IEnumerable<object?> Flatten(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string s when TryParseJsonArray(s, out var parsed):
                foreach (var el in parsed.EnumerateArray())
                foreach (var inner in Flatten(el))
                    yield return inner;
                yield break;
            case string s:
                yield return s;
                yield break;
            case JsonElement je when je.ValueKind == JsonValueKind.Array:
                foreach (var el in je.EnumerateArray())
                foreach (var inner in Flatten(el))
                    yield return inner;
                yield break;
            case JsonElement je when je.ValueKind == JsonValueKind.String:
                foreach (var inner in Flatten(je.GetString()))
                    yield return inner;
                yield break;
            case JsonElement je:
                yield return je;
                yield break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                foreach (var inner in Flatten(item))
                    yield return inner;
                yield break;
            default:
                yield return value;
                break;
        }
    }

    private static bool TryParseJsonArray(string? text, out JsonElement array)
    {
        array = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        if (s.Length < 2 || s[0] != '[')
            return false;

        try
        {
            using var doc = JsonDocument.Parse(s);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;
            array = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
