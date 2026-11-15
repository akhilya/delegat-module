using Delegat.Enums;
using Delegat.Models;
using Delegat.Models.Marketplace.DTOs;
using System.Text.Json;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Store-engine adapters (Medusa) export local category attributes as extra
/// product fields. Marketplace adapters (Ozon, WB) have a fixed remote schema
/// and must not grow extra sinks from the local category.
/// </summary>
public static class LocalCategoryAdapterFields
{
    public static bool ShouldExposeLocalAttributes(string? adapterKey)
        => string.Equals(adapterKey, "medusa", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Store engines serialize arbitrary field keys, so mapping-local custom
    /// fields can be exported. Marketplace adapters with a fixed remote schema
    /// (Ozon, WB, Yandex, Avito) would silently drop them.
    /// </summary>
    public static bool SupportsCustomFields(string? adapterKey)
        => adapterKey is "medusa" or "woocommerce" or "opencart";

    public static List<ExternalField> Merge(
        IReadOnlyCollection<ExternalField> adapterFields,
        IEnumerable<CategoryAttribute> attributes)
    {
        var fields = adapterFields.ToList();
        var keys = new HashSet<string>(
            fields.Select(f => f.Key),
            StringComparer.OrdinalIgnoreCase);
        var nextSort = fields.Count == 0 ? 1 : fields.Max(f => f.SortOrder) + 1;

        foreach (var attribute in attributes
            .OrderBy(a => a.sortOrder)
            .ThenBy(a => a.name, StringComparer.OrdinalIgnoreCase))
        {
            var key = (attribute.key ?? "").Trim();
            if (key.Length == 0 || !keys.Add(key))
                continue;

            fields.Add(new ExternalField(
                key,
                string.IsNullOrWhiteSpace(attribute.name) ? key : attribute.name,
                false,
                MapType(attribute.type),
                ParseOptions(attribute.options),
                attribute.description,
                Unit: AttributeUnits.Find(attribute.unitCode)?.Symbol ?? attribute.unitCode,
                Group: string.IsNullOrWhiteSpace(attribute.group) ? "Attributes" : attribute.group,
                SortOrder: attribute.sortOrder > 0 ? attribute.sortOrder : nextSort++,
                UnitCode: attribute.unitCode));
        }

        return fields;
    }

    private static string MapType(AttributeType type)
        => type switch
        {
            AttributeType.Number => "number",
            AttributeType.Boolean => "boolean",
            AttributeType.Select => "select",
            AttributeType.MultiSelect => "multiselect",
            AttributeType.Barcode => "string",
            _ => "string"
        };

    private static List<ExternalFieldOption>? ParseOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var options = new List<ExternalFieldOption>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        options.Add(new ExternalFieldOption(text, text));
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var value = item.TryGetProperty("value", out var valueEl)
                    ? valueEl.GetString()
                    : null;
                var label = item.TryGetProperty("label", out var labelEl)
                    ? labelEl.GetString()
                    : value;
                if (!string.IsNullOrWhiteSpace(value))
                    options.Add(new ExternalFieldOption(value, string.IsNullOrWhiteSpace(label) ? value : label));
            }

            return options.Count == 0 ? null : options;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
