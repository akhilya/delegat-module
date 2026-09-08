using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delegat.Infrastructure.Json;

/// <summary>
/// Reads JSON strings, numbers, booleans, and arrays as a string.
/// Product/store characteristic values are stored as text (the SPA always sends strings);
/// assistants and API clients often send JSON numbers or arrays instead.
/// </summary>
public sealed class FlexibleJsonStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var n))
                    return n.ToString(CultureInfo.InvariantCulture);
                if (reader.TryGetDecimal(out var d))
                    return d.ToString(CultureInfo.InvariantCulture);
                return reader.GetDouble().ToString("G", CultureInfo.InvariantCulture);
            case JsonTokenType.StartArray:
            case JsonTokenType.StartObject:
                using (var doc = JsonDocument.ParseValue(ref reader))
                    return doc.RootElement.GetRawText();
            default:
                throw new JsonException($"Cannot read {reader.TokenType} as a string.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
