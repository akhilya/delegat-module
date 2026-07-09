using System.Text.Json;
using System.Text.Json.Serialization;

namespace Delegat.Services.Ai.Tools;

/// <summary>
/// Base class for all AI tool handler classes.
/// Provides shared JSON serialization options and the ParseInput helper.
/// </summary>
public abstract class ToolHandlerBase : IToolHandler
{
    protected static readonly JsonSerializerOptions _jsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    protected static T ParseInput<T>(string json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, _jsonOpts) ?? new T(); }
        catch { return new T(); }
    }
}
