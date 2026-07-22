using System.Text.Json;
using Delegat.Infrastructure.Security;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Decrypt / merge / public-field extraction for marketplace connection credentials.
/// </summary>
public static class ConnectionCredentialsHelper
{
    public static string? DecryptCredentialsJson(string? credentialsJson, EncryptionService? encryption)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson))
            return null;

        if (encryption != null && encryption.IsEncrypted(credentialsJson))
            return encryption.Decrypt(credentialsJson);

        return credentialsJson;
    }

    public static Dictionary<string, string> ParseCredentialsObject(string? plainJson)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(plainJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(plainJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => prop.Value.ToString()
                };
            }
        }
        catch
        {
            // ignore malformed credentials
        }

        return result;
    }

    /// <summary>
    /// Non-secret credential fields safe to return to the SPA.
    /// </summary>
    public static Dictionary<string, string> ExtractPublicCredentials(
        string? credentialsJson,
        CredentialsSchema? schema,
        EncryptionService? encryption)
    {
        var publicKeys = schema?.Fields
            .Where(f => !f.IsSecret)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (publicKeys.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var plain = DecryptCredentialsJson(credentialsJson, encryption);
        var all = ParseCredentialsObject(plain);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in publicKeys)
        {
            if (all.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                result[key] = value.Trim();
        }
        return result;
    }

    /// <summary>
    /// Merge incoming credentials with existing: empty secret fields keep the previous value.
    /// Returns encrypted JSON ready to store.
    /// </summary>
    public static string MergeAndEncrypt(
        string? existingCredentialsJson,
        string incomingPlainJson,
        CredentialsSchema? schema,
        EncryptionService encryption)
    {
        var existing = ParseCredentialsObject(DecryptCredentialsJson(existingCredentialsJson, encryption));
        var incoming = ParseCredentialsObject(incomingPlainJson);
        var secretKeys = schema?.Fields
            .Where(f => f.IsSecret)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in incoming)
        {
            var isSecret = secretKeys.Contains(key);
            if (isSecret && string.IsNullOrWhiteSpace(value))
                continue;

            if (!isSecret && string.IsNullOrWhiteSpace(value))
            {
                merged.Remove(key);
                continue;
            }

            merged[key] = value?.Trim() ?? "";
        }

        var json = JsonSerializer.Serialize(merged);
        return encryption.Encrypt(json);
    }

    /// <summary>
    /// Copy missing public fields (e.g. businessId) from test-connection details into credentials.
    /// Returns true when credentialsJson was updated.
    /// </summary>
    public static bool TryPersistMissingPublicFieldsFromDetails(
        MarketplaceConnection connection,
        Dictionary<string, object>? details,
        CredentialsSchema? schema,
        EncryptionService encryption,
        params string[] preferredKeys)
    {
        if (details == null || details.Count == 0 || schema == null)
            return false;

        var publicKeys = schema.Fields
            .Where(f => !f.IsSecret)
            .Select(f => f.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> keysToConsider = preferredKeys.Length > 0
            ? preferredKeys.Where(publicKeys.Contains)
            : publicKeys;

        var existing = ParseCredentialsObject(DecryptCredentialsJson(connection.credentialsJson, encryption));
        var changed = false;

        foreach (var key in keysToConsider)
        {
            if (existing.TryGetValue(key, out var cur) && !string.IsNullOrWhiteSpace(cur))
                continue;
            if (!TryGetDetailString(details, key, out var text) || string.IsNullOrWhiteSpace(text))
                continue;

            existing[key] = text.Trim();
            changed = true;
        }

        if (!changed)
            return false;

        connection.credentialsJson = encryption.Encrypt(JsonSerializer.Serialize(existing));
        return true;
    }

    private static bool TryGetDetailString(Dictionary<string, object> details, string key, out string? text)
    {
        text = null;
        // Details may use exact or different casing
        object? raw = null;
        foreach (var kvp in details)
        {
            if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                raw = kvp.Value;
                break;
            }
        }
        if (raw == null)
            return false;

        text = raw switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetRawText(),
            JsonElement je => je.ToString(),
            _ => Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)
        };
        return !string.IsNullOrWhiteSpace(text);
    }
}
