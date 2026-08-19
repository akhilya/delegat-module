using System.Globalization;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>
/// Coerce a graph/export value to an adapter field type.
/// Used by <see cref="GraphEvaluator"/> (via <see cref="GraphEvaluationContext.Coerce"/>)
/// and the product-sync export path.
/// </summary>
public static class MappingFieldCoercion
{
    public static object? Coerce(object? value, string? fieldType)
    {
        if (value == null || string.IsNullOrEmpty(fieldType))
            return value;

        return fieldType.ToLowerInvariant() switch
        {
            "string" or "text" => value is System.Collections.IEnumerable && value is not string
                ? value
                : value.ToString(),
            "number" => ToDecimalOrPassthrough(value),
            "integer" => ToLongOrPassthrough(value),
            "boolean" => ToBoolOrPassthrough(value),
            _ => value // select, multiselect, array - no coercion
        };
    }

    private static object? ToDecimalOrPassthrough(object? value)
    {
        if (value is decimal) return value;
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;
        return value;
    }

    private static object? ToLongOrPassthrough(object? value)
    {
        if (value is long) return value;
        if (value is int i) return (long)i;
        if (long.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
            return l;
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return (long)Math.Truncate(d);
        return value;
    }

    private static object? ToBoolOrPassthrough(object? value)
    {
        if (value is bool) return value;
        var str = value?.ToString()?.Trim().ToLowerInvariant();
        return str switch
        {
            "true" or "1" or "yes" or "\u0434\u0430" => true,
            "false" or "0" or "no" or "\u043d\u0435\u0442" => false,
            _ => value
        };
    }
}
