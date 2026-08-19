using System.Collections;
using System.Globalization;
using System.Text.Json;
using Delegat.Models.Products;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Shared shape of <c>_variants</c> on marketplace export payloads.
/// Local size rows are not mapping-graph fields; adapters read this bag.
/// </summary>
public static class ProductVariantExport
{
    public static Dictionary<string, object?> ToPayload(ProductVariant variant, ProductVariantLink? link)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = variant.Id,
            ["sku"] = variant.sku,
            ["variantId"] = link?.externalVariantId,
            ["techSize"] = variant.size,
            ["marketplaceSize"] = link?.marketplaceSize,
            ["barcode"] = variant.barcode,
            ["price"] = variant.price,
            ["quantity"] = variant.quantity
        };
    }

    /// <summary>
    /// Reads adapter <c>variantIds</c> export details (local variant id → marketplace id).
    /// </summary>
    public static Dictionary<int, string> ReadExportedVariantIds(IReadOnlyDictionary<string, object>? details)
    {
        var result = new Dictionary<int, string>();
        if (details == null)
            return result;
        if (!details.TryGetValue("variantIds", out var raw) && !details.TryGetValue("variant_ids", out raw))
            return result;
        if (raw == null)
            return result;

        foreach (var (key, value) in EnumeratePairs(raw))
        {
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localId)
                || localId <= 0)
                continue;
            var externalId = value?.Trim();
            if (string.IsNullOrEmpty(externalId))
                continue;
            result[localId] = externalId;
        }

        return result;
    }

    private static IEnumerable<(string Key, string? Value)> EnumeratePairs(object raw)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.Object } je)
        {
            foreach (var prop in je.EnumerateObject())
                yield return (prop.Name, JsonValueString(prop.Value));
            yield break;
        }

        if (raw is IDictionary<string, string> stringMap)
        {
            foreach (var kv in stringMap)
                yield return (kv.Key, kv.Value);
            yield break;
        }

        if (raw is IDictionary<string, object> objectMap)
        {
            foreach (var kv in objectMap)
                yield return (kv.Key, kv.Value?.ToString());
            yield break;
        }

        if (raw is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrEmpty(key))
                    continue;
                yield return (key, entry.Value?.ToString());
            }
        }
    }

    private static string? JsonValueString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => value.ToString()
    };
}
