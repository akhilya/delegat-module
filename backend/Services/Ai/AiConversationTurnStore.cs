using System.Text.Json;
using System.Text.Json.Nodes;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai;

/// <summary>Persists the live user/assistant turn so a reload can restore it from MySQL.</summary>
public static class AiConversationTurnStore
{
    public const string ConfirmedPlanUserText = "Confirmed, proceed";

    public static string? UserText(ChatMessage? message)
    {
        if (message == null) return null;
        if (!string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase)) return null;
        if (message.content.ValueKind != JsonValueKind.String) return null;
        var text = message.content.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static async Task AppendUserIfNeededAsync(
        DataContext db,
        string sessionId,
        int storeId,
        int userId,
        ChatMessage? userMessage,
        CancellationToken ct)
    {
        var text = UserText(userMessage);
        if (text == null) return;
        if (string.Equals(text, ConfirmedPlanUserText, StringComparison.Ordinal)) return;

        var conv = await EnsureConversationAsync(db, sessionId, storeId, userId, ct);
        var last = await LastMessageAsync(db, conv.Id, ct);
        if (last != null
            && last.role == "user"
            && string.Equals(last.content, text, StringComparison.Ordinal))
            return;

        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conv.Id,
            role = "user",
            content = text,
            createdAt = DateTime.UtcNow,
        });
        conv.lastMessageAt = DateTime.UtcNow;
        conv.messageCount++;
        await db.SaveChangesAsync(ct);
    }

    public static async Task UpsertAssistantAsync(
        DataContext db,
        string sessionId,
        int storeId,
        int userId,
        AiSseTurnSnapshot snapshot,
        CancellationToken ct)
    {
        if (!snapshot.HasAssistantPayload) return;

        var conv = await EnsureConversationAsync(db, sessionId, storeId, userId, ct);
        if (!string.IsNullOrEmpty(snapshot.ModelName) && conv.modelName == null)
            conv.modelName = snapshot.ModelName;

        var last = await LastMessageAsync(db, conv.Id, ct);
        if (last is { role: "assistant" })
        {
            if (HasExecutedPlan(last.toolCallsJson))
                return;

            if (TryMergePlanExecution(last, snapshot, out var mergedContent, out var mergedJson))
            {
                last.content = mergedContent;
                last.toolCallsJson = mergedJson;
                conv.lastMessageAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            last.content = snapshot.Content;
            last.toolCallsJson = snapshot.ToolCallsJson;
            conv.lastMessageAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conv.Id,
            role = "assistant",
            content = snapshot.Content,
            toolCallsJson = snapshot.ToolCallsJson,
            createdAt = DateTime.UtcNow,
        });
        conv.lastMessageAt = DateTime.UtcNow;
        conv.messageCount++;
        await db.SaveChangesAsync(ct);
    }

    internal static bool HasExecutedPlan(string? toolCallsJson)
        => ReadTurn(toolCallsJson) is { } turn
           && turn["plan"] is JsonObject
           && turn["planExecuted"] is JsonValue executed
           && executed.TryGetValue<bool>(out var flag)
           && flag;

    internal static bool TryMergePlanExecution(
        AiConversationMessage last,
        AiSseTurnSnapshot snapshot,
        out string content,
        out string? toolCallsJson)
    {
        content = snapshot.Content;
        toolCallsJson = snapshot.ToolCallsJson;

        var existing = ReadTurn(last.toolCallsJson);
        if (existing?["plan"] is not JsonObject plan)
            return false;

        var incoming = ReadTurn(snapshot.ToolCallsJson) ?? new JsonObject
        {
            ["_ui"] = "assistantTurn",
            ["tools"] = new JsonArray(),
            ["timeline"] = new JsonArray(),
        };
        if (incoming["plan"] is JsonObject)
            return false;

        var proposalTimeline = existing["timeline"] as JsonArray ?? new JsonArray();
        var execTimeline = incoming["timeline"] as JsonArray ?? new JsonArray();
        var proposalTools = existing["tools"] as JsonArray ?? new JsonArray();
        var execTools = incoming["tools"] as JsonArray ?? new JsonArray();

        var mergedTimeline = CloneArray(proposalTimeline);
        foreach (var node in execTimeline)
            mergedTimeline.Add(node?.DeepClone());
        if (execTimeline.Count == 0 && !string.IsNullOrEmpty(snapshot.Content))
        {
            mergedTimeline.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = snapshot.Content,
            });
        }

        var mergedTools = CloneArray(proposalTools);
        foreach (var node in execTools)
            mergedTools.Add(node?.DeepClone());

        var merged = new JsonObject
        {
            ["_ui"] = "assistantTurn",
            ["tools"] = mergedTools,
            ["timeline"] = mergedTimeline,
            ["plan"] = plan.DeepClone(),
            ["planConfirmed"] = true,
            ["planExecuted"] = true,
            ["planExecStart"] = proposalTimeline.Count,
        };
        if (IncomingPlanFailed(incoming))
            merged["planFailed"] = true;
        if (existing["planCancelled"] is JsonValue cancelled
            && cancelled.TryGetValue<bool>(out var cancelledFlag)
            && cancelledFlag)
            merged["planCancelled"] = true;

        var left = last.content ?? "";
        var right = snapshot.Content ?? "";
        content = string.IsNullOrEmpty(left)
            ? right
            : string.IsNullOrEmpty(right)
                ? left
                : left + "\n\n" + right;
        toolCallsJson = merged.ToJsonString();
        return true;
    }

    private static bool IncomingPlanFailed(JsonObject incoming)
    {
        if (incoming["planFailed"] is JsonValue flag
            && flag.TryGetValue<bool>(out var failed)
            && failed)
            return true;
        if (incoming["tools"] is not JsonArray tools)
            return false;
        foreach (var node in tools)
        {
            if (node is not JsonObject tool) continue;
            var summary = tool["summary"]?.GetValue<string>();
            object? result = null;
            if (tool["result"] != null)
            {
                try
                {
                    result = JsonSerializer.Deserialize<object>(tool["result"]!.ToJsonString());
                }
                catch (JsonException)
                {
                    /* summary only */
                }
            }
            if (AgentToolResult.IsFailure(result, summary))
                return true;
        }
        return false;
    }

    private static JsonObject? ReadTurn(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            var ui = obj["_ui"]?.GetValue<string>();
            return string.Equals(ui, "assistantTurn", StringComparison.Ordinal) ? obj : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonArray CloneArray(JsonArray source)
    {
        var next = new JsonArray();
        foreach (var node in source)
            next.Add(node?.DeepClone());
        return next;
    }

    private static async Task<AiConversation> EnsureConversationAsync(
        DataContext db, string sessionId, int storeId, int userId, CancellationToken ct)
    {
        var conv = await db.aiConversations
            .FirstOrDefaultAsync(
                c => c.sessionId == sessionId && c.storeId == storeId && c.userId == userId,
                ct);
        if (conv != null) return conv;

        conv = new AiConversation
        {
            sessionId = sessionId,
            storeId = storeId,
            userId = userId,
            lastMessageAt = DateTime.UtcNow,
        };
        db.aiConversations.Add(conv);
        await db.SaveChangesAsync(ct);
        return conv;
    }

    private static Task<AiConversationMessage?> LastMessageAsync(
        DataContext db, int conversationId, CancellationToken ct) =>
        db.aiConversationMessages
            .Where(m => m.conversationId == conversationId)
            .OrderByDescending(m => m.createdAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);
}
