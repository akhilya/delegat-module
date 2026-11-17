using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Services.Ai;

/// <summary>
/// Projects canonical typed turn events into the same assistant JSON the SPA persists.
/// The legacy string-SSE <see cref="Fold"/>/<see cref="FoldTurns"/> remain compatibility
/// parsers for old fixtures and readers; production hub input is <see cref="FoldEvents"/>.
/// </summary>
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

    /// <summary>
    /// Split one run's typed events into per-message snapshots. A new segment starts
    /// at assistant_start (the execution/review boundary); message_start belongs to
    /// the current segment. No empty segment is emitted when assistant_start is first.
    /// </summary>
    public static IReadOnlyList<AiSseTurnSnapshot> FoldEventTurns(IEnumerable<AiTurnEvent> events)
    {
        var turns = new List<AiSseTurnSnapshot>();
        var current = new List<AiTurnEvent>();
        foreach (var item in events)
        {
            if (item.Type == SseEventType.AssistantStart && current.Count > 0)
            {
                turns.Add(FoldEvents(current));
                current.Clear();
            }
            current.Add(item);
        }
        turns.Add(FoldEvents(current));
        return turns;
    }

    /// <summary>Parse raw SSE chunks into typed events (compatibility reader for old callers/tests).</summary>
    public static IEnumerable<AiTurnEvent> ParseEvents(IEnumerable<string> chunks)
    {
        foreach (var payload in SsePayloads(chunks))
        {
            AiTurnEvent? item = null;
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
                    item = new AiTurnEvent(type.GetString()!, data)
                    {
                        RunId = ReadString(root, "runId"),
                        MessageId = ReadString(root, "messageId"),
                        Sequence = ReadLong(root, "sequence"),
                        DbId = ReadInt(root, "dbId"),
                        Phase = ReadString(root, "phase"),
                        ProposalId = ReadString(root, "proposalId"),
                    };
                }
            }
            catch (JsonException)
            {
                /* skip malformed payload */
            }
            if (item != null) yield return item;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number) ? number : null;

    private static int? ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number) ? number : null;

    private static JsonDocument? ParsePayload(string payload)
    {
        try { return JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
    }

    /// <summary>Legacy compatibility entry point: parses raw SSE chunks, then folds typed events.</summary>
    public static AiSseTurnSnapshot Fold(IEnumerable<string> chunks)
        => FoldEvents(ParseEvents(chunks));

    public static AiSseTurnSnapshot FoldEvents(IEnumerable<AiTurnEvent> events)
    {
        var content = new StringBuilder();
        var tools = new List<Dictionary<string, object?>>();
        var toolById = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var timeline = new List<Dictionary<string, object?>>();
        JsonElement? plan = null;
        JsonElement? executionOutcome = null;
        string? modelName = null;
        string? requestErrorCode = null;
        int? requestErrorStatus = null;
        var planFailed = false;
        var turnEnded = false;
        var planExecuted = false;
        var turnStopped = false;
        string? executionProposalId = null;

        // Canonical ownership, stamped by the live turn - never inferred from
        // role, content, or the review's parent execution link.
        string? messageId = null;
        string? runId = null;
        int? dbId = null;
        string? phase = null;
        long lastEventSequence = 0;
        var sawEvent = false;
        var messageState = "running";

        foreach (var item in events)
        {
            sawEvent = true;
            var type = item.Type;
            var data = item.Data;

            messageId ??= item.MessageId;
            runId ??= item.RunId;
            dbId ??= item.DbId;
            phase ??= item.Phase;
            if (item.Sequence is { } sequence && sequence > lastEventSequence)
                lastEventSequence = sequence;

            switch (type)
            {
                case SseEventType.MessageStart:
                    break;
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
                            timeline.RemoveAll(item2 => item2.GetValueOrDefault("type") as string == "text");
                        }
                    }
                    break;
                case SseEventType.ExecutionStatus:
                    // Structured outcome, not assistant prose: the status data goes to
                    // the envelope, the short summary goes to Content only (never a
                    // Text timeline item, so the UI cannot render it as a reply).
                    if (data.ValueKind == JsonValueKind.Object)
                    {
                        executionOutcome = data.Clone();
                        if (data.TryGetProperty("summary", out var summaryEl)
                            && summaryEl.ValueKind == JsonValueKind.String
                            && summaryEl.GetString() is { Length: > 0 } summaryText)
                            content.Append(summaryText);
                    }
                    break;
                case SseEventType.Error:
                    turnEnded = true;
                    messageState = "interrupted";
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
                    messageState = "stopped";
                    break;
                case SseEventType.Done:
                    turnEnded = true;
                    if (data.ValueKind == JsonValueKind.Object)
                    {
                        if (data.TryGetProperty("interrupted", out var interruptedEl)
                            && interruptedEl.ValueKind == JsonValueKind.True)
                            messageState = "interrupted";
                        else if (messageState == "running")
                            messageState = "completed";
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
                        {
                            turnStopped = true;
                            messageState = "stopped";
                        }
                    }
                    else if (messageState == "running")
                    {
                        messageState = "completed";
                    }
                    break;
            }
        }

        if (turnEnded)
            MarkInterruptedTools(tools, turnStopped);

        var identified = messageId != null || runId != null;
        string? toolCallsJson = null;
        if (identified || plan != null || tools.Count > 0 || requestErrorCode != null
            || planExecuted || turnStopped || executionProposalId != null || executionOutcome != null)
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
            if (executionOutcome != null)
                envelope["executionOutcome"] = executionOutcome.Value;
            if (messageId != null)
                envelope["messageId"] = messageId;
            if (runId != null)
                envelope["runId"] = runId;
            if (phase != null)
                envelope["phase"] = phase;
            if (dbId != null)
                envelope["dbId"] = dbId.Value;
            if (sawEvent && (identified || phase != null))
            {
                envelope["lastEventSequence"] = lastEventSequence;
                envelope["messageState"] = messageState;
            }
            // The message that proposes a plan owns it; the review parent link must
            // never be copied into ownership.
            if (plan != null && messageId != null)
                envelope["planOwnerMessageId"] = messageId;
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
