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

    public static async Task AppendAssistantAsync(DataContext db, string sessionId, int storeId, int userId,
        AiSseTurnSnapshot snapshot, CancellationToken ct)
    {
        if (!snapshot.HasAssistantPayload) return;
        var conversation = await EnsureConversationAsync(db, sessionId, storeId, userId, ct);
        db.aiConversationMessages.Add(new AiConversationMessage
        {
            conversationId = conversation.Id,
            role = "assistant",
            content = snapshot.Content,
            toolCallsJson = snapshot.ToolCallsJson,
            createdAt = DateTime.UtcNow,
        });
        conversation.messageCount++;
        conversation.lastMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static string ComputeStepsHash(JsonNode steps) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(steps.ToJsonString(PlanJsonOptions))));

    /// <summary>Client history sync may update display data, but cannot mint or rewrite a server plan.</summary>
    public static string? PreserveServerPlan(string? storedJson, string? incomingJson)
    {
        // Stale history sync must not erase a server-owned stop or partial reply.
        if (HasStoppedTurn(storedJson)) return storedJson;
        // An incompatible review identity must never acquire another message's
        // plan state - including via an explicit-but-wrong DbId update. A null or
        // non-envelope stored row has no identity to conflict with.
        if (storedJson != null && !HasCompatibleReviewIdentity(storedJson, incomingJson))
            return storedJson;
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

    internal static bool HasStoppedTurn(string? json) =>
        ReadTurn(json)?["turnStopped"] is JsonValue value
        && value.TryGetValue<bool>(out var stopped) && stopped;

    /// <summary>Server-minted canonical identity, when the envelope carries a valid one.</summary>
    internal static string? ReadTurnMessageId(string? json)
    {
        var turn = ReadTurn(json);
        if (turn == null || !turn.TryGetPropertyValue("messageId", out var node) || node == null)
            return null;
        return TryReadString(node, out var id) ? id : null;
    }

    /// <summary>
    /// True when the envelope carries a <c>messageId</c> field at all - valid,
    /// malformed, or an explicit JSON null. A present-but-invalid canonical
    /// identity is non-matching, not a missing identity that could fall
    /// through to content matching.
    /// </summary>
    internal static bool HasCanonicalMessageIdField(string? json)
    {
        var turn = ReadTurn(json);
        return turn != null && turn.TryGetPropertyValue("messageId", out _);
    }

    /// <summary>
    /// Metadata-only merge for a canonical server-owned row during client history
    /// sync. Server role/content/tools/timeline/plan/identity/lifecycle fields are
    /// immutable here; the client may only merge formReplies and monotonic user
    /// plan decisions (cancelled/superseded), and only while the plan was never
    /// confirmed or executed and the incoming own proposal matches the stored one.
    /// </summary>
    internal static bool MergeCanonicalMetadata(AiConversationMessage row, string? incomingJson)
    {
        var stored = ReadTurn(row.toolCallsJson);
        var incoming = ReadTurn(incomingJson);
        if (stored == null || incoming == null) return false;

        var changed = false;
        if (incoming["formReplies"] is JsonObject replies && replies.Count > 0)
        {
            var merged = stored["formReplies"] as JsonObject ?? new JsonObject();
            foreach (var pair in replies)
                merged[pair.Key] = pair.Value?.DeepClone();
            stored["formReplies"] = merged;
            changed = true;
        }

        var processed = (TryReadBool(stored["planConfirmed"], out var confirmed) && confirmed)
            || (TryReadBool(stored["planExecuted"], out var executed) && executed);
        var ownProposal = ReadOwnPlanId(stored, out _);
        var incomingProposal = ReadOwnPlanId(incoming, out _);
        if (!processed
            && ownProposal != null
            && ownProposal == incomingProposal)
        {
            // Only a literal boolean true counts as a user decision; malformed
            // client flags are ignored rather than throwing.
            foreach (var key in new[] { "planCancelled", "planSuperseded" })
            {
                if (TryReadBool(incoming[key], out var flag) && flag)
                {
                    stored[key] = true;
                    changed = true;
                }
            }
        }

        if (changed)
            row.toolCallsJson = stored.ToJsonString(PlanJsonOptions);
        return changed;
    }

    /// <summary>
    /// Constrains anonymous (role+content/rebase) matching: a review envelope
    /// (<c>executionProposalId</c>) may only merge with a row carrying the same
    /// parent link <em>and</em> the same own <c>plan.proposalId</c>. Malformed
    /// review fields fail the match instead of throwing. Explicit DbId updates
    /// and <see cref="PreserveServerPlan"/> semantics are unaffected.
    /// </summary>
    internal static bool HasCompatibleReviewIdentity(string? storedJson, string? incomingJson)
    {
        var stored = ReadTurn(storedJson);
        var incoming = ReadTurn(incomingJson);
        var storedParent = ReadParentLink(stored, out var storedBadParent);
        var incomingParent = ReadParentLink(incoming, out var incomingBadParent);
        if (storedBadParent || incomingBadParent)
            return false;
        if (storedParent == null && incomingParent == null)
            return true; // Ordinary non-review sync - unchanged.
        if (storedParent == null || incomingParent == null
            || !string.Equals(storedParent, incomingParent, StringComparison.Ordinal))
            return false;

        var storedOwn = ReadOwnPlanId(stored, out var storedBadPlan);
        var incomingOwn = ReadOwnPlanId(incoming, out var incomingBadPlan);
        if (storedBadPlan || incomingBadPlan)
            return false;
        if (storedOwn == null && incomingOwn == null)
            return true;
        return string.Equals(storedOwn, incomingOwn, StringComparison.Ordinal);
    }

    private static string? ReadParentLink(JsonObject? turn, out bool invalid)
    {
        invalid = false;
        if (turn == null || !turn.TryGetPropertyValue("executionProposalId", out var node) || node == null)
            return null;
        if (node is JsonValue value && value.TryGetValue<string>(out var link)
            && !string.IsNullOrEmpty(link))
            return link;
        invalid = true;
        return null;
    }

    private static string? ReadOwnPlanId(JsonObject? turn, out bool invalid)
    {
        invalid = false;
        if (turn == null || !turn.TryGetPropertyValue("plan", out var node) || node == null)
            return null;
        if (node is not JsonObject plan)
        {
            invalid = true;
            return null;
        }
        if (!plan.TryGetPropertyValue("proposalId", out var idNode) || idNode == null)
        {
            invalid = true;
            return null;
        }
        if (idNode is JsonValue value && value.TryGetValue<string>(out var id)
            && !string.IsNullOrEmpty(id))
            return id;
        invalid = true;
        return null;
    }

    private static bool TryReadBool(JsonNode? node, out bool value)
    {
        value = false;
        return node is JsonValue v && v.TryGetValue<bool>(out value);
    }

    private static bool TryReadString(JsonNode? node, out string? value)
    {
        value = null;
        return node is JsonValue v
            && v.TryGetValue<string>(out value)
            && !string.IsNullOrEmpty(value);
    }

    private static bool TryReadLong(JsonNode? node, out long value)
    {
        value = 0;
        return node is JsonValue v && v.TryGetValue<long>(out value);
    }

    private static bool TryReadDateTime(JsonNode? node, out DateTime value)
    {
        value = default;
        return node is JsonValue v && v.TryGetValue<DateTime>(out value);
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
        if (TryReadBool(turn["planCancelled"], out var cancelled) && cancelled)
            return Rejected("plan_cancelled");
        if ((TryReadBool(turn["planConfirmed"], out var confirmed) && confirmed)
            || HasExecutedPlan(last.toolCallsJson))
            return Rejected("already_processed");
        if (TryReadBool(turn["turnStopped"], out var stopped) && stopped)
            return Rejected("plan_cancelled");
        if (!TryReadString(plan["proposalId"], out var planProposalId)
            || !string.Equals(planProposalId, proposalId, StringComparison.Ordinal))
            return Rejected("proposal_mismatch");
        if (plan["steps"] is not JsonArray plannedSteps
            || plannedSteps.Count == 0
            || !TryReadString(plan["stepsHash"], out var expectedHash)
            || !TryReadDateTime(plan["createdAtUtc"], out var createdAtUtc)
            || !TryReadDateTime(plan["expiresAtUtc"], out var expiresAtUtc))
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
            || !TryReadString(plan["proposalNonce"], out var storedNonce)
            || !string.Equals(storedNonce, nonce, StringComparison.Ordinal))
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
        // A structured execution outcome keeps its summary in Content only;
        // it must never turn back into assistant prose on the timeline.
        if (execTimeline.Count == 0
            && !string.IsNullOrEmpty(snapshot.Content)
            && incoming["executionOutcome"] is null)
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
            // EOF, cancellation, or a generic chat done event is not a plan outcome.
            ["planExecuted"] = TryReadBool(incoming["planExecuted"], out var execFlag) && execFlag,
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
        if (incoming["requestError"] is { } requestError)
            merged["requestError"] = requestError.DeepClone();
        if (TryReadBool(incoming["turnStopped"], out var stoppedFlag) && stoppedFlag)
            merged["turnStopped"] = true;
        if (existing["planCancelled"] is JsonValue cancelled
            && cancelled.TryGetValue<bool>(out var cancelledFlag)
            && cancelledFlag)
            merged["planCancelled"] = true;
        if (existing["planSuperseded"] is JsonValue superseded
            && superseded.TryGetValue<bool>(out var supersededFlag)
            && supersededFlag)
            merged["planSuperseded"] = true;

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
            if (obj["_ui"] is not JsonValue uiValue
                || !uiValue.TryGetValue<string>(out var ui)
                || !string.Equals(ui, "assistantTurn", StringComparison.Ordinal))
                return null;
            return obj;
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

    // ----- canonical server-owned assistant turns (M4) -----
    //
    // The live hub is the single writer for generated assistant output. Every
    // segment (chat answer, plan execution, result review) binds to exactly one
    // row: chat/review rows are inserted empty BEFORE the first event, execution
    // rebinds the confirmed proposal row it targets. Identity lives in the
    // existing toolCallsJson envelope - no schema change.

    /// <summary>Canonical binding between a live-turn segment and its row.</summary>
    internal sealed record AiAssistantTurnBinding(
        int DbId,
        string MessageId,
        string RunId,
        string Phase,
        string? ProposalId);

    /// <summary>
    /// Reserve the canonical assistant row for one live-turn segment.
    /// Chat/review insert an empty running envelope (server minted messageId);
    /// review additionally records its execution parent link. Execution locates
    /// the exact confirmed, unexecuted proposal in this conversation - never the
    /// "last" or "matching content" row - stamps ownership and the honest
    /// proposal/execution boundary, and never falls back to a different plan.
    /// </summary>
    internal static async Task<AiAssistantTurnBinding> BeginAssistantTurnAsync(
        DataContext db,
        string sessionId,
        int storeId,
        int userId,
        string runId,
        string phase,
        string? proposalId,
        string? reviewParent,
        CancellationToken ct,
        string? messageId = null)
    {
        var conv = await EnsureConversationAsync(db, sessionId, storeId, userId, ct);

        if (phase == "execution")
        {
            if (string.IsNullOrEmpty(proposalId))
                throw new InvalidOperationException("Execution turn requires a proposal id.");

            var candidates = await db.aiConversationMessages
                .Where(m => m.conversationId == conv.Id && m.role == "assistant")
                .ToListAsync(ct);
            // Malformed historical rows are skipped, not fatal; a confirmed
            // unexecuted proposal must resolve to exactly one target.
            var matches = candidates
                .Select(row => (row, turn: ReadTurn(row.toolCallsJson)))
                .Where(x => TryReadString((x.turn?["plan"] as JsonObject)?["proposalId"], out var pid)
                    && pid == proposalId
                    && TryReadBool(x.turn!["planConfirmed"], out var confirmed) && confirmed
                    && !(TryReadBool(x.turn["planExecuted"], out var executed) && executed))
                .Select(x => x.row)
                .ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException(
                    $"Execution target '{proposalId}' is ambiguous in this conversation.");
            var target = matches.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"Execution target '{proposalId}' was not found in this conversation.");

            var turn = ReadTurn(target.toolCallsJson)
                ?? throw new InvalidOperationException(
                    $"Execution target '{proposalId}' has no turn envelope.");
            if (!TryReadString(turn["messageId"], out var boundMessageId) || boundMessageId == null)
                boundMessageId = Guid.NewGuid().ToString("N");
            turn["messageId"] = boundMessageId;
            turn["runId"] = runId;
            turn["phase"] = "execution";
            // This row owns the plan; never copy the review's parent link here.
            turn["planOwnerMessageId"] = boundMessageId;
            // The proposal/execution boundary is fixed before any step runs.
            turn["planExecStart"] = (turn["timeline"] as JsonArray)?.Count ?? 0;
            turn["messageState"] = "running";
            turn["lastEventSequence"] = 0;
            target.toolCallsJson = turn.ToJsonString(PlanJsonOptions);
            conv.lastMessageAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return new AiAssistantTurnBinding(target.Id, boundMessageId, runId, "execution", proposalId);
        }

        var newMessageId = !string.IsNullOrEmpty(messageId)
            ? messageId
            : Guid.NewGuid().ToString("N");
        var envelope = new JsonObject
        {
            ["_ui"] = "assistantTurn",
            ["tools"] = new JsonArray(),
            ["timeline"] = new JsonArray(),
            ["messageId"] = newMessageId,
            ["runId"] = runId,
            ["phase"] = phase,
            ["lastEventSequence"] = 0,
            ["messageState"] = "running",
        };
        if (!string.IsNullOrEmpty(reviewParent))
            envelope["executionProposalId"] = reviewParent;

        var row = new AiConversationMessage
        {
            conversationId = conv.Id,
            role = "assistant",
            content = "",
            toolCallsJson = envelope.ToJsonString(PlanJsonOptions),
            createdAt = DateTime.UtcNow,
        };
        db.aiConversationMessages.Add(row);
        conv.messageCount++;
        conv.lastMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new AiAssistantTurnBinding(row.Id, newMessageId, runId, phase, null);
    }

    /// <summary>
    /// Write the folded snapshot of one segment into its bound row. Lookup is by
    /// the bound DbId only - never by role, empty content, or row position - and
    /// the stored messageId/runId must match the binding. A repeat persist of the
    /// same run with an equal-or-lower sequence is a no-op.
    /// </summary>
    internal static async Task PersistAssistantTurnAsync(
        DataContext db,
        string sessionId,
        int storeId,
        int userId,
        AiAssistantTurnBinding binding,
        AiSseTurnSnapshot snapshot,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await PersistAssistantTurnAttemptAsync(db, sessionId, storeId, userId, binding, snapshot, ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                // Reload the bound row and re-apply the same snapshot; a tool is
                // never re-dispatched by a persist retry.
                db.ChangeTracker.Clear();
            }
        }
    }

    private static async Task PersistAssistantTurnAttemptAsync(
        DataContext db,
        string sessionId,
        int storeId,
        int userId,
        AiAssistantTurnBinding binding,
        AiSseTurnSnapshot snapshot,
        CancellationToken ct)
    {
        var conv = await EnsureConversationAsync(db, sessionId, storeId, userId, ct);
        var row = await db.aiConversationMessages
            .FirstOrDefaultAsync(m => m.Id == binding.DbId && m.conversationId == conv.Id, ct)
            ?? throw new InvalidOperationException(
                $"Canonical assistant row {binding.DbId} missing.");

        var stored = ReadTurn(row.toolCallsJson);
        if (stored == null
            || !TryReadString(stored["messageId"], out var storedMessageId)
            || storedMessageId != binding.MessageId
            || !TryReadString(stored["runId"], out var storedRunId)
            || storedRunId != binding.RunId)
            throw new InvalidOperationException(
                $"Canonical binding mismatch for row {binding.DbId}.");

        var incoming = ReadTurn(snapshot.ToolCallsJson);
        // An already-terminal execution row must never be reopened by a later
        // segment snapshot of the same run (a review-admission failure writes
        // error events with a higher sequence). Its tools and outcome are final.
        if (binding.Phase == "execution"
            && TryReadString(stored["messageState"], out var storedState)
            && storedState is "completed" or "stopped" or "interrupted")
            return;

        // Idempotent: an already-fresher stored segment wins over a stale repeat.
        if (incoming != null
            && TryReadLong(stored["lastEventSequence"], out var have)
            && have > 0
            && TryReadLong(incoming["lastEventSequence"], out var got)
            && have >= got)
            return;

        if (!string.IsNullOrEmpty(snapshot.ModelName) && conv.modelName == null)
            conv.modelName = snapshot.ModelName;

        JsonObject merged;
        if (binding.Phase == "execution"
            && TryMergePlanExecution(row, snapshot, out var mergedContent, out var mergedJson))
        {
            row.content = mergedContent;
            merged = ReadTurn(mergedJson)
                ?? throw new InvalidOperationException(
                    $"Execution merge produced an unreadable envelope for row {binding.DbId}.");
            // Canonical identity survives the merge.
            foreach (var key in new[] { "messageId", "runId", "phase",
                     "messageState", "lastEventSequence", "executionOutcome", "requestError" })
            {
                if (incoming != null && incoming[key] is { } value)
                    merged[key] = value.DeepClone();
                else
                    merged.Remove(key);
            }
            merged["messageId"] = binding.MessageId;
            merged["runId"] = binding.RunId;
            merged["phase"] = binding.Phase;
            // The executing row owns its plan; a recovery review additionally
            // keeps its own parent link - never the parent's ownership fields.
            merged["planOwnerMessageId"] = binding.MessageId;
            if (stored["executionProposalId"] is { } parent)
                merged["executionProposalId"] = parent.DeepClone();
        }
        else if (binding.Phase != "execution")
        {
            // Chat/review: the server snapshot is authoritative for text, tools,
            // timeline, the owned proposal, and lifecycle. A snapshot without an
            // envelope keeps the begin-time identity fields.
            row.content = snapshot.Content;
            merged = incoming ?? stored;
        }
        else
        {
            // The execution target lost its plan between Begin and Persist:
            // refuse to overwrite the signed envelope with a planless snapshot.
            throw new InvalidOperationException(
                $"Execution target {binding.DbId} has no mergeable plan.");
        }

        // Client-owned formReplies survive a server rewrite.
        if (stored?["formReplies"] is JsonObject replies && replies.Count > 0)
            merged["formReplies"] = replies.DeepClone();

        row.toolCallsJson = merged.ToJsonString(PlanJsonOptions);
        conv.lastMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Read-side projection for a canonical row left at <c>messageState:
    /// running</c> by a process loss. There is no durable recovery, but the UI
    /// must report interrupted/unknown rather than spin forever. The stored row
    /// is never rewritten and nothing replays. A row owned by the currently
    /// active run (<paramref name="activeRunId"/>) is returned unchanged.
    /// </summary>
    internal static string? ProjectTurnForRead(string? persistedJson, string? activeRunId)
    {
        var turn = ReadTurn(persistedJson);
        if (turn == null) return persistedJson;
        // Only a fully valid canonical envelope is projected; an unknown or
        // malformed identity is never normalized into canonical territory.
        if (!TryReadString(turn["messageId"], out var messageId) || messageId == null)
            return persistedJson;
        if (!TryReadString(turn["messageState"], out var state) || state != "running")
            return persistedJson;
        if (!TryReadString(turn["runId"], out var runId) || runId == null)
            return persistedJson;
        if (activeRunId != null
            && string.Equals(runId, activeRunId, StringComparison.Ordinal))
            return persistedJson;

        var projected = (JsonObject)turn.DeepClone();
        projected["messageState"] = "interrupted";
        projected["requestError"] = new JsonObject
        {
            ["code"] = "ai_turn_interrupted",
            ["status"] = null,
        };
        if (projected["tools"] is JsonArray tools)
        {
            // Running tools get the neutral interrupted summary/status - never a
            // planFailed or success inference.
            foreach (var node in tools)
            {
                if (node is not JsonObject tool) continue;
                if (!TryReadString(tool["status"], out var status)
                    || status == "running")
                {
                    tool["status"] = "done";
                    tool["summary"] = "interrupted";
                }
            }
        }
        if (TryReadString(projected["phase"], out var phase) && phase == "execution"
            && projected["executionOutcome"] == null)
        {
            projected["executionOutcome"] = new JsonObject
            {
                ["status"] = "interrupted",
                ["outcomeUnknown"] = true,
            };
        }
        return projected.ToJsonString(PlanJsonOptions);
    }
}
