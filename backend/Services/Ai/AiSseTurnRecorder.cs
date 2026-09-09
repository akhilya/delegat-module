using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Services.Ai;

/// <summary>Parses buffered cabinet-chat SSE into the same assistant JSON the SPA persists.</summary>
public static class AiSseTurnRecorder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Format(string type, object data)
        => $"data: {JsonSerializer.Serialize(new { type, data }, JsonOpts)}\n\n";

    public static AiSseTurnSnapshot Fold(IEnumerable<string> chunks)
    {
        var content = new StringBuilder();
        var tools = new List<Dictionary<string, object?>>();
        var toolById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var timeline = new List<Dictionary<string, object?>>();
        JsonElement? plan = null;
        string? modelName = null;
        var planFailed = false;
        var turnEnded = false;

        foreach (var payload in SsePayloads(chunks))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(payload); }
            catch (JsonException) { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();
                if (!root.TryGetProperty("data", out var data))
                    data = default;

                switch (type)
                {
                    case SseEventType.Text:
                        if (data.ValueKind == JsonValueKind.Object
                            && data.TryGetProperty("delta", out var deltaEl))
                        {
                            var delta = deltaEl.GetString();
                            if (string.IsNullOrEmpty(delta)) break;
                            content.Append(delta);
                            AppendText(timeline, delta);
                        }
                        break;
                    case SseEventType.Tool:
                        ApplyTool(data, tools, toolById, timeline);
                        if (ToolDataFailed(data))
                            planFailed = true;
                        break;
                    case SseEventType.Plan:
                        if (data.ValueKind == JsonValueKind.Object)
                            plan = data.Clone();
                        break;
                    case SseEventType.Error:
                        turnEnded = true;
                        if (data.ValueKind == JsonValueKind.Object
                            && data.TryGetProperty("message", out var msgEl))
                        {
                            var message = msgEl.GetString();
                            if (string.IsNullOrEmpty(message)) break;
                            var piece = "\n\n" + message;
                            content.Append(piece);
                            AppendText(timeline, piece);
                        }
                        break;
                    case SseEventType.Done:
                        turnEnded = true;
                        if (data.ValueKind == JsonValueKind.Object)
                        {
                            if (data.TryGetProperty("modelName", out var modelEl))
                                modelName = modelEl.GetString();
                            if (data.TryGetProperty("ok", out var okEl)
                                && okEl.ValueKind == JsonValueKind.False)
                                planFailed = true;
                        }
                        break;
                }
            }
        }

        if (turnEnded)
            MarkInterruptedTools(tools);

        string? toolCallsJson = null;
        if (plan != null || tools.Count > 0)
        {
            var envelope = new Dictionary<string, object?>
            {
                ["_ui"] = "assistantTurn",
                ["tools"] = tools,
                ["timeline"] = timeline,
                ["plan"] = plan,
            };
            if (planFailed && plan == null)
                envelope["planFailed"] = true;
            toolCallsJson = JsonSerializer.Serialize(envelope, JsonOpts);
        }

        return new AiSseTurnSnapshot(content.ToString(), toolCallsJson, modelName);
    }

    private static void ApplyTool(
        JsonElement data,
        List<Dictionary<string, object?>> tools,
        Dictionary<string, Dictionary<string, object?>> toolById,
        List<Dictionary<string, object?>> timeline)
    {
        if (data.ValueKind != JsonValueKind.Object) return;
        var id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var name = data.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var key = id ?? name;
        if (string.IsNullOrEmpty(key)) return;

        if (!toolById.TryGetValue(key, out var row))
        {
            row = new Dictionary<string, object?>
            {
                ["id"] = id ?? name,
                ["name"] = name,
                ["input"] = data.TryGetProperty("input", out var inputEl) && inputEl.ValueKind == JsonValueKind.String
                    ? inputEl.GetString()
                    : null,
            };
            toolById[key] = row;
            tools.Add(row);
            timeline.Add(new Dictionary<string, object?> { ["type"] = "tool", ["id"] = row["id"] });
        }

        if (data.TryGetProperty("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.String)
            row["summary"] = summaryEl.GetString();

        if (data.TryGetProperty("result", out var resultEl) && resultEl.ValueKind != JsonValueKind.Undefined
            && resultEl.ValueKind != JsonValueKind.Null)
        {
            if (resultEl.ValueKind == JsonValueKind.String)
            {
                var raw = resultEl.GetString();
                if (!string.IsNullOrEmpty(raw))
                {
                    try { row["result"] = JsonSerializer.Deserialize<JsonElement>(raw); }
                    catch (JsonException) { row["result"] = raw; }
                }
            }
            else
            {
                row["result"] = resultEl.Clone();
            }
        }
    }

    private static void AppendText(List<Dictionary<string, object?>> timeline, string delta)
    {
        if (timeline.Count > 0
            && timeline[^1].TryGetValue("type", out var type)
            && type as string == "text")
        {
            timeline[^1]["text"] = (timeline[^1]["text"] as string ?? "") + delta;
            return;
        }

        timeline.Add(new Dictionary<string, object?> { ["type"] = "text", ["text"] = delta });
    }

    private static bool ToolDataFailed(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) return false;
        string? summary = data.TryGetProperty("summary", out var summaryEl)
            && summaryEl.ValueKind == JsonValueKind.String
            ? summaryEl.GetString()
            : null;
        object? result = null;
        if (data.TryGetProperty("result", out var resultEl)
            && resultEl.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            try
            {
                if (resultEl.ValueKind == JsonValueKind.String)
                {
                    var raw = resultEl.GetString();
                    if (!string.IsNullOrEmpty(raw))
                        result = JsonSerializer.Deserialize<object>(raw);
                }
                else
                {
                    result = JsonSerializer.Deserialize<object>(resultEl.GetRawText());
                }
            }
            catch (JsonException)
            {
                /* keep summary-only detection */
            }
        }

        return AgentToolResult.IsFailure(result, summary);
    }

    private static void MarkInterruptedTools(List<Dictionary<string, object?>> tools)
    {
        foreach (var row in tools)
        {
            if (row.ContainsKey("summary") || row.ContainsKey("result"))
                continue;
            row["summary"] = "interrupted";
        }
    }

    private static IEnumerable<string> SsePayloads(IEnumerable<string> chunks)
    {
        foreach (var chunk in chunks)
        {
            if (string.IsNullOrEmpty(chunk)) continue;
            var parts = chunk.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var line = part.Trim();
                if (line.StartsWith("data: ", StringComparison.Ordinal))
                    yield return line["data: ".Length..];
            }
        }
    }
}

public readonly record struct AiSseTurnSnapshot(string Content, string? ToolCallsJson, string? ModelName)
{
    public bool HasAssistantPayload =>
        !string.IsNullOrEmpty(Content) || !string.IsNullOrEmpty(ToolCallsJson);
}
