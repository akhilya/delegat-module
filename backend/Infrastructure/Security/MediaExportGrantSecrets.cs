using System.Text.RegularExpressions;
using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Delegat.Infrastructure.Security;

/// <summary>Redacts durable media bearer tokens from application and persisted logs.</summary>
public static partial class MediaExportGrantSecrets
{
    private static readonly Regex TokenUrl = new(
        @"(?i)(/api/pub/media-export/)[0-9a-f]{64}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? value)
        => string.IsNullOrEmpty(value) ? value ?? string.Empty : TokenUrl.Replace(value, "$1[redacted]");

    public static string RedactPath(PathString path) => Redact(path.Value);

    public static Dictionary<string, object>? RedactDetails(Dictionary<string, object>? details)
        => details?.ToDictionary(pair => pair.Key, pair => RedactValue(pair.Value)!);

    private static object? RedactValue(object? value)
    {
        if (value is null) return null;
        if (value is string text) return Redact(text);
        if (value is Uri uri) return Redact(uri.ToString());
        if (value is JsonElement json)
        {
            var sanitized = Redact(json.ToString());
            try
            {
                using var document = JsonDocument.Parse(sanitized);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return sanitized;
            }
        }
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key != null)
                    result[entry.Key.ToString() ?? ""] = RedactValue(entry.Value);
            }
            return result;
        }
        if (value is IEnumerable sequence && value is not byte[])
            return sequence.Cast<object?>().Select(RedactValue).ToArray();
        return value;
    }
}
