using System.Globalization;
using System.Text.Json;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Stable identity of a remote marketplace shop for one adapter.
/// Same cabinet / store URL / campaign must not be connected twice in one local store;
/// a second connection of the same adapter is allowed when the remote shop differs.
/// </summary>
public static class ConnectionRemoteIdentity
{
    private static readonly HashSet<string> NoiseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "bulkChunkSize",
        "languageId",
        "sandbox",
    };

    public static string Build(
        string adapterKey,
        IReadOnlyDictionary<string, string> credentials,
        IReadOnlyDictionary<string, object>? details = null)
    {
        var merged = Merge(credentials, details);
        var adapter = (adapterKey ?? "").Trim().ToLowerInvariant();
        var parts = new List<string>();

        switch (adapter)
        {
            case "ozon":
                if (TryGet(merged, "clientId", out var clientId))
                    parts.Add("clientId=" + clientId);
                break;
            case "woocommerce":
                if (TryGet(merged, "apiBaseUrl", out var wooUrl))
                    parts.Add("apiBaseUrl=" + NormalizeEndpoint(wooUrl));
                break;
            case "medusa":
                if (TryGet(merged, "apiBaseUrl", out var medusaUrl))
                    parts.Add("apiBaseUrl=" + NormalizeEndpoint(medusaUrl));
                break;
            case "opencart":
                if (TryGet(merged, "apiBaseUrl", out var ocUrl))
                    parts.Add("apiBaseUrl=" + NormalizeEndpoint(ocUrl));
                TryGet(merged, "storeId", out var storeId);
                parts.Add("storeId=" + (string.IsNullOrWhiteSpace(storeId) ? "0" : storeId));
                break;
            case "yandex":
                if (TryGet(merged, "campaignId", out var campaignId))
                    parts.Add("campaignId=" + campaignId);
                else if (TryGet(merged, "businessId", out var businessId))
                    parts.Add("businessId=" + businessId);
                break;
            case "wildberries":
                if (TryGet(merged, "apiKey", out var wbKey))
                    parts.Add("apiKey=" + wbKey);
                break;
            default:
                AddFingerprint(merged, parts);
                break;
        }

        if (parts.Count == 0)
            AddFingerprint(merged, parts);

        return adapter + "\n" + string.Join("\n", parts);
    }

    private static void AddFingerprint(IReadOnlyDictionary<string, string> merged, List<string> parts)
    {
        foreach (var key in merged.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (NoiseKeys.Contains(key))
                continue;
            if (!TryGet(merged, key, out var value))
                continue;
            parts.Add(key.ToLowerInvariant() + "=" + value);
        }
    }

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> credentials,
        IReadOnlyDictionary<string, object>? details)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (details != null)
        {
            foreach (var (key, raw) in details)
            {
                if (TryFormatDetail(raw, out var text))
                    merged[key] = text;
            }
        }

        if (credentials != null)
        {
            foreach (var (key, value) in credentials)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    merged[key] = value.Trim();
            }
        }

        return merged;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string> map, string key, out string value)
    {
        value = "";
        if (!map.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return false;
        value = raw.Trim();
        return true;
    }

    private static bool TryFormatDetail(object? raw, out string text)
    {
        text = raw switch
        {
            null => "",
            string s => s.Trim(),
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()?.Trim() ?? "",
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetRawText(),
            JsonElement je => je.ToString() ?? "",
            IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? "",
        };
        return !string.IsNullOrWhiteSpace(text);
    }

    internal static string NormalizeEndpoint(string raw)
    {
        var s = (raw ?? "").Trim();
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var host = uri.Host.ToLowerInvariant();
            var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
            var path = uri.AbsolutePath.TrimEnd('/');
            return $"{uri.Scheme.ToLowerInvariant()}://{host}{port}{path}{uri.Query}";
        }

        return s.TrimEnd('/');
    }
}
