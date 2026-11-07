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

    /// <summary>A plan outcome and its model review are distinct persisted assistant messages.</summary>
    public static IReadOnlyList<AiSseTurnSnapshot> FoldTurns(IEnumerable<string> chunks)
    {
        var turns = new List<AiSseTurnSnapshot>();
        var current = new List<string>();
        foreach (var payload in SsePayloads(chunks))
        {
            using var document = ParsePayload(payload);
            if (document?.RootElement.TryGetProperty("type", out var type) == true
                && type.GetString() == SseEventType.AssistantStart)
            {
                turns.Add(Fold(current));
                current.Clear();
            }
            current.Add("data: " + payload + "\n\n");
        }
        turns.Add(Fold(current));
        return turns;
    }

    private static JsonDocument? ParsePayload(string payload)
    {
        try { return JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
    }

    public static AiSseTurnSnapshot Fold(IEnumerable<string> chunks)
    {
        var content = new StringBuilder();
        var tools = new List<Dictionary<string, object?>>();
        var toolById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var timeline = new List<Dictionary<string, object?>>();
        JsonElement? plan = null;
        string? modelName = null;
        string? requestErrorCode = null;
        int? requestErrorStatus = null;
        var planFailed = false;
        var turnEnded = false;
        var planExecuted = false;
        var turnStopped = false;
        string? executionProposalId = null;

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
                    case SseEventType.AssistantStart:
                        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("executionProposalId", out var parent))
                            executionProposalId = parent.GetString();
                        break;
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
                        {
                            plan = data.Clone();
                            if (data.TryGetProperty("discardDraftText", out var discard) && discard.ValueKind == JsonValueKind.True)
                            {
                                content.Clear();
                                timeline.RemoveAll(item => item.GetValueOrDefault("type") as string == "text");
                            }
                        }
                        break;
                    case SseEventType.Error:
                        turnEnded = true;
                        if (data.ValueKind == JsonValueKind.Object)
                        {
                            if (data.TryGetProperty("code", out var codeEl)
                                && codeEl.ValueKind == JsonValueKind.String)
                                requestErrorCode = codeEl.GetString();
                            if (data.TryGetProperty("status", out var statusEl)
                                && statusEl.TryGetInt32(out var status))
                                requestErrorStatus = status;
                        }
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
                    case SseEventType.Stopped:
                        turnEnded = true;
                        turnStopped = true;
                        break;
                    case SseEventType.Done:
                        turnEnded = true;
                        if (data.ValueKind == JsonValueKind.Object)
                        {
                            if (data.TryGetProperty("modelName", out var modelEl))
                                modelName = modelEl.GetString();
                            if (data.TryGetProperty("ok", out var okEl)
                                && okEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            {
                                planExecuted = true;
                                planFailed |= okEl.ValueKind == JsonValueKind.False;
                            }
                            if (data.TryGetProperty("cancelled", out var cancelledEl)
                                && cancelledEl.ValueKind == JsonValueKind.True)
                                turnStopped = true;
                        }
                        break;
                }
            }
        }

        if (turnEnded)
            MarkInterruptedTools(tools, turnStopped);

        string? toolCallsJson = null;
        if (plan != null || tools.Count > 0 || requestErrorCode != null || planExecuted || turnStopped || executionProposalId != null)
        {
            var envelope = new Dictionary<string, object?>
            {
                ["_ui"] = "assistantTurn",
                ["tools"] = tools,
                ["timeline"] = timeline,
                ["plan"] = plan,
            };
            if (requestErrorCode != null)
            {
                envelope["requestError"] = new
                {
                    code = requestErrorCode,
                    status = requestErrorStatus,
                };
            }
            if (planFailed && plan == null)
                envelope["planFailed"] = true;
            if (planExecuted && plan == null && !turnStopped)
                envelope["planExecuted"] = true;
            if (turnStopped)
                envelope["turnStopped"] = true;
            if (executionProposalId != null)
                envelope["executionProposalId"] = executionProposalId;
            toolCallsJson = JsonSerializer.Serialize(envelope, JsonOpts);
        }

        return new AiSseTurnSnapshot(content.ToString(), toolCallsJson, modelName) { TurnEnded = turnEnded };
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
        if (data.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
            row["status"] = statusEl.GetString();

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

    private static void MarkInterruptedTools(List<Dictionary<string, object?>> tools, bool stopped)
    {
        foreach (var row in tools)
        {
            var status = row.GetValueOrDefault("status") as string;
            if (status == "done" || (status != "running" && (row.ContainsKey("summary") || row.ContainsKey("result"))))
                continue;
            row["summary"] = "interrupted";
            row["status"] = stopped ? "stopped" : "done";
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
    public bool TurnEnded { get; init; }
    public bool HasAssistantPayload =>
        !string.IsNullOrEmpty(Content) || !string.IsNullOrEmpty(ToolCallsJson);
}
