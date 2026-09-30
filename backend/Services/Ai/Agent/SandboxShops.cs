using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Security;
using Delegat.Services.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Live shop credentials for a sandbox job. The script calls the seller HTTP
/// itself; this only hands it the connection's keys and hosts.
/// </summary>
public static class SandboxShops
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<string> ForStoreAsync(
        DataContext db, EncryptionService? encryption, int storeId, CancellationToken ct)
    {
        var rows = await db.marketplaceConnections.AsNoTracking()
            .Where(c => c.storeId == storeId)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.name, c.adapterKey, c.isActive, c.credentialsJson })
            .ToListAsync(ct);

        var shops = rows.Select(c => Describe(
            c.Id,
            c.name,
            c.adapterKey,
            c.isActive,
            ConnectionCredentialsHelper.ParseCredentialsObject(
                ConnectionCredentialsHelper.DecryptCredentialsJson(c.credentialsJson, encryption))));
        return JsonSerializer.Serialize(shops, Json);
    }

    internal static object Describe(
        int id,
        string name,
        string adapterKey,
        bool active,
        IReadOnlyDictionary<string, string> credentials)
    {
        var adapter = (adapterKey ?? "").Trim().ToLowerInvariant();
        return new
        {
            id,
            name,
            adapter,
            active,
            credentials,
            hosts = Hosts(adapter, credentials),
            auth = Auth(adapter),
            docs = Docs(adapter),
        };
    }

    /// <summary>
    /// <c>platform</c> names the adapter whose daily snapshot Control Plane keeps.
    /// The script reads that copy and does not fetch the seller's documentation URL.
    /// Storefront bridges are omitted.
    /// </summary>
    internal static Dictionary<string, string>? Docs(string adapter) => adapter switch
    {
        "yandex" or "yandexmarket" => new Dictionary<string, string> { ["platform"] = "yandex" },
        "wildberries" or "ozon" => new Dictionary<string, string> { ["platform"] = adapter },
        _ => null,
    };

    internal static Dictionary<string, string> Hosts(string adapter, IReadOnlyDictionary<string, string> credentials)
    {
        switch (adapter)
        {
            case "wildberries":
                var sandbox = WildberriesSandbox(credentials);
                var mark = sandbox ? "-sandbox" : "";
                return new Dictionary<string, string>
                {
                    ["content"] = $"https://content-api{mark}.wildberries.ru",
                    ["prices"] = $"https://discounts-prices-api{mark}.wildberries.ru",
                    ["marketplace"] = $"https://marketplace-api{mark}.wildberries.ru",
                    ["calendar"] = sandbox
                        ? "https://discounts-prices-api-sandbox.wildberries.ru"
                        : "https://dp-calendar-api.wildberries.ru",
                };
            case "ozon":
                return new Dictionary<string, string>
                {
                    ["seller"] = "https://api-seller.ozon.ru",
                };
            case "yandex":
            case "yandexmarket":
                return new Dictionary<string, string>
                {
                    ["partner"] = "https://api.partner.market.yandex.ru",
                };
            default:
                var hosts = new Dictionary<string, string>();
                if (credentials.TryGetValue("apiBaseUrl", out var api) && !string.IsNullOrWhiteSpace(api))
                    hosts["api"] = api.Trim();
                return hosts;
        }
    }

    internal static string Auth(string adapter) => adapter switch
    {
        "wildberries" => "Header Authorization is credentials.apiKey, with no Bearer prefix.",
        "ozon" => "Headers Client-Id=credentials.clientId and Api-Key=credentials.apiKey.",
        "yandex" or "yandexmarket" => "Header Api-Key is credentials.apiKey.",
        "opencart" => "Header Authorization is Bearer plus credentials.apiKey. The URL is hosts.api.",
        "medusa" => "Header Authorization is Basic of base64(credentials.apiKey + ':'). The URL is hosts.api.",
        _ => "Send credentials the way that shop's API expects. hosts.api is the base URL when the connection stores one.",
    };

    public static List<string> SecretValues(string? shopsJson)
    {
        var secrets = new List<string>();
        if (string.IsNullOrWhiteSpace(shopsJson))
            return secrets;
        try
        {
            using var doc = JsonDocument.Parse(shopsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return secrets;
            foreach (var shop in doc.RootElement.EnumerateArray())
            {
                if (!shop.TryGetProperty("credentials", out var creds)
                    || creds.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var prop in creds.EnumerateObject())
                {
                    if (!IsSecretKey(prop.Name) || prop.Value.ValueKind != JsonValueKind.String)
                        continue;
                    var value = prop.Value.GetString();
                    if (string.IsNullOrEmpty(value) || value.Length < 8)
                        continue;
                    secrets.Add(value);
                }
            }
        }
        catch (JsonException)
        {
            return secrets;
        }

        secrets.Sort((a, b) => b.Length.CompareTo(a.Length));
        return secrets;
    }

    public static string? Redact(string? text, IReadOnlyList<string> secrets)
    {
        if (string.IsNullOrEmpty(text) || secrets.Count == 0)
            return text;
        foreach (var secret in secrets)
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return text;
    }

    private static bool IsSecretKey(string key) =>
        key.Contains("key", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase);

    internal static bool WildberriesSandbox(IReadOnlyDictionary<string, string> credentials)
    {
        if (credentials.TryGetValue("sandbox", out var raw) && TrySandboxOverride(raw, out var forced))
            return forced;
        credentials.TryGetValue("apiKey", out var apiKey);
        return TrySandboxClaim(apiKey);
    }

    private static bool TrySandboxOverride(string? raw, out bool sandbox)
    {
        sandbox = false;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "sandbox":
            case "true":
            case "1":
            case "yes":
                sandbox = true;
                return true;
            case "production":
            case "false":
            case "0":
            case "no":
                sandbox = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TrySandboxClaim(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;
        var parts = apiKey.Split('.');
        if (parts.Length < 2)
            return false;
        var json = DecodeBase64Url(parts[1]);
        if (json == null)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("t", out var t))
                return false;
            return t.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.Number => t.TryGetInt32(out var n) && n != 0,
                JsonValueKind.String => t.GetString() is "1" or "true",
                _ => false,
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? DecodeBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: return null;
        }
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
