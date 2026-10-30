using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Delegat.Infrastructure.Middleware;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Delegat.Services.Ai;

public sealed record PlanConfirmationAttempt(
    IReadOnlyList<PlanStepInput>? Steps,
    string? RejectionReason);

/// <summary>Persists the live user/assistant turn so a reload can restore it from MySQL.</summary>
public static class AiConversationTurnStore
{
    public const string ConfirmedPlanUserText = "Confirmed, proceed";
    private static readonly TimeSpan PlanProposalTtl = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await UpsertAssistantAttemptAsync(db, sessionId, storeId, userId, snapshot, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                // Client history sync may update the same assistant row while the live turn
                // finishes. Reload on the next attempt so server-owned plan state is merged
                // against the current row rather than failing to persist the completed turn.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static async Task UpsertAssistantAttemptAsync(
        DataContext db,
        string sessionId,
        int storeId,
        int userId,
        AiSseTurnSnapshot snapshot,
        CancellationToken ct)
    {
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
            last.toolCallsJson = snapshot.ToolCallsJson == null
                ? last.toolCallsJson
                : PreserveServerPlan(last.toolCallsJson, snapshot.ToolCallsJson);
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

    public static string ComputeStepsHash(JsonNode steps) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(steps.ToJsonString(PlanJsonOptions))));

    /// <summary>Client history sync may update display data, but cannot mint or rewrite a server plan.</summary>
    public static string? PreserveServerPlan(string? storedJson, string? incomingJson)
    {
        var stored = ReadTurn(storedJson);
        if (stored?["plan"] is not JsonObject storedPlan)
        {
            var incomingWithoutPlan = ReadTurn(incomingJson);
            if (incomingWithoutPlan == null) return incomingJson;
            incomingWithoutPlan.Remove("plan");
            RemoveServerPlanFlags(incomingWithoutPlan);
            return incomingWithoutPlan.ToJsonString(PlanJsonOptions);
        }

        var incoming = ReadTurn(incomingJson);
        if (incoming == null) return storedJson;
        incoming["plan"] = storedPlan.DeepClone();
        foreach (var key in new[]
                 { "planConfirmed", "planConfirmedBy", "planConfirmedAtUtc", "planConfirmationId", "planExecuted", "planExecStart", "planFailed" })
        {
            if (stored[key] is { } value)
                incoming[key] = value.DeepClone();
            else
                incoming.Remove(key);
        }
        return incoming.ToJsonString(PlanJsonOptions);
    }

    private static void RemoveServerPlanFlags(JsonObject turn)
    {
        foreach (var key in new[]
                 { "planConfirmed", "planConfirmedBy", "planConfirmedAtUtc", "planConfirmationId", "planExecuted", "planExecStart", "planFailed" })
            turn.Remove(key);
    }

    public static async Task<IReadOnlyList<PlanStepInput>?> ConsumePendingPlanAsync(
        DataContext db,
        string sessionId,
        string proposalId,
        int storeId,
        int userId,
        CancellationToken ct) =>
        (await ConsumePendingPlanDetailedAsync(db, sessionId, proposalId, storeId, userId, ct)).Steps;

    public static async Task<PlanConfirmationAttempt> ConsumePendingPlanDetailedAsync(
        DataContext db,
        string sessionId,
        string proposalId,
        int storeId,
        int userId,
        CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        var firstAttempt = true;
        return await strategy.ExecuteAsync(async () =>
        {
            // A failed transient attempt can leave tracked entities in a modified state.
            // Clear those changes before retrying the whole transaction as one unit.
            if (!firstAttempt)
                db.ChangeTracker.Clear();
            firstAttempt = false;

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct)
                : null;

            var attempt = await ConsumePendingPlanAttemptAsync(
                db, sessionId, proposalId, storeId, userId, ct);
            if (attempt.Steps != null && transaction != null)
                await transaction.CommitAsync(ct);

            return attempt;
        });
    }

    private static async Task<PlanConfirmationAttempt> ConsumePendingPlanAttemptAsync(
        DataContext db,
        string sessionId,
        string proposalId,
        int storeId,
        int userId,
        CancellationToken ct)
    {
        var conversation = await db.aiConversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.sessionId == sessionId
                && c.storeId == storeId
                && c.userId == userId, ct);
        if (conversation == null) return Rejected("conversation_missing");

        var lastId = await db.aiConversationMessages.AsNoTracking()
            .Where(m => m.conversationId == conversation.Id)
            .OrderByDescending(m => m.createdAt)
            .ThenByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct);
        if (!lastId.HasValue) return Rejected("conversation_empty");

        var last = db.Database.IsRelational()
            ? await db.aiConversationMessages
                .FromSqlInterpolated($"SELECT * FROM aiConversationMessages WHERE Id = {lastId.Value} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : await db.aiConversationMessages.FirstOrDefaultAsync(m => m.Id == lastId.Value, ct);
        if (last is not { role: "assistant" }) return Rejected("conversation_advanced");

        var turn = ReadTurn(last.toolCallsJson);
        if (turn?["plan"] is not JsonObject plan)
            return Rejected("plan_missing");
        if (turn["planCancelled"]?.GetValue<bool>() == true)
            return Rejected("plan_cancelled");
        if (turn["planConfirmed"]?.GetValue<bool>() == true || HasExecutedPlan(last.toolCallsJson))
            return Rejected("already_processed");
        if (!string.Equals(plan["proposalId"]?.GetValue<string>(), proposalId, StringComparison.Ordinal))
            return Rejected("proposal_mismatch");
        if (plan["steps"] is not JsonArray plannedSteps
            || plannedSteps.Count == 0
            || plan["stepsHash"]?.GetValue<string>() is not { } expectedHash
            || plan["createdAtUtc"]?.GetValue<DateTime>() is not { } createdAtUtc
            || plan["expiresAtUtc"]?.GetValue<DateTime>() is not { } expiresAtUtc)
            return Rejected("invalid_plan");

        var now = DateTime.UtcNow;
        if (createdAtUtc > now || now - createdAtUtc > PlanProposalTtl || expiresAtUtc <= now)
            return Rejected("expired");
        if (!string.Equals(expectedHash, ComputeStepsHash(plannedSteps), StringComparison.Ordinal))
            return Rejected("steps_changed");

        if (!AiPlanProposalToken.TryValidate(
                proposalId,
                sessionId,
                storeId,
                userId,
                expectedHash,
                expiresAtUtc,
                out var nonce)
            || !string.Equals(plan["proposalNonce"]?.GetValue<string>(), nonce, StringComparison.Ordinal))
            return Rejected("signature_invalid");

        if (await db.aiAgentAudits.AnyAsync(a => a.storeId == storeId
                && a.userId == userId
                && a.tool == "plan_confirmation"
                && a.summary.StartsWith($"proposal:{proposalId}:"), ct))
            return Rejected("already_processed");

        var steps = JsonSerializer.Deserialize<List<PlanStepInput>>(plannedSteps.ToJsonString(PlanJsonOptions), PlanJsonOptions);
        if (steps is not { Count: > 0 }) return Rejected("invalid_plan");

        var confirmedAt = DateTime.UtcNow;
        turn["planConfirmed"] = true;
        turn["planConfirmedBy"] = userId;
        turn["planConfirmedAtUtc"] = confirmedAt;
        turn["planConfirmationId"] = proposalId;
        last.toolCallsJson = turn.ToJsonString(PlanJsonOptions);
        var audit = new AiAgentAudit
        {
            userId = userId,
            storeId = storeId,
            tool = "plan_confirmation",
            summary = $"proposal:{proposalId}: confirmed; execution started",
            createdAt = confirmedAt,
        };
        db.aiAgentAudits.Add(audit);

        try
        {
            await db.SaveChangesAsync(ct);
            return new PlanConfirmationAttempt(steps, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(last).State = EntityState.Detached;
            db.Entry(audit).State = EntityState.Detached;
            return Rejected("already_processed");
        }

        static PlanConfirmationAttempt Rejected(string reason) => new(null, reason);
    }

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
        foreach (var metadataKey in new[]
                 { "planConfirmedBy", "planConfirmedAtUtc", "planConfirmationId" })
        {
            if (existing[metadataKey] is { } metadata)
                merged[metadataKey] = metadata.DeepClone();
        }
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

    public static async Task<AiConversation> EnsureConversationAsync(
        DataContext db, string sessionId, int storeId, int userId, CancellationToken ct)
    {
        var conv = await db.aiConversations
            .FirstOrDefaultAsync(c => c.sessionId == sessionId, ct);
        if (conv != null)
            return OwnedConversation(conv, userId);

        conv = new AiConversation
        {
            sessionId = sessionId,
            storeId = storeId,
            userId = userId,
            lastMessageAt = DateTime.UtcNow,
        };
        db.aiConversations.Add(conv);
        try
        {
            await db.SaveChangesAsync(ct);
            return conv;
        }
        catch (DbUpdateException ex) when (IsDuplicateSessionId(ex))
        {
            db.Entry(conv).State = EntityState.Detached;
            var existing = await db.aiConversations
                .FirstOrDefaultAsync(c => c.sessionId == sessionId, ct)
                ?? throw new InvalidOperationException(
                    $"Conversation '{sessionId}' missing after duplicate-key insert.");
            return OwnedConversation(existing, userId);
        }
    }

    private static AiConversation OwnedConversation(AiConversation conv, int userId)
    {
        if (conv.userId != userId)
            throw ApiException.Forbidden("Conversation does not belong to you", code: "access_denied");
        return conv;
    }

    private static bool IsDuplicateSessionId(DbUpdateException ex)
    {
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
        {
            if (e is MySqlException mysql && mysql.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
                return true;
            if (e.Message.Contains("IX_aiConversations_sessionId", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static Task<AiConversationMessage?> LastMessageAsync(
        DataContext db, int conversationId, CancellationToken ct) =>
        db.aiConversationMessages
            .Where(m => m.conversationId == conversationId)
            .OrderByDescending(m => m.createdAt)
            .ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);
}
