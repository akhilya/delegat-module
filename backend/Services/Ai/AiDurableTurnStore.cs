using System.Runtime.CompilerServices;
using System.Text.Json;
using Delegat.Models.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TurnBinding = Delegat.Services.Ai.AiConversationTurnStore.AiAssistantTurnBinding;

namespace Delegat.Services.Ai;

/// <summary>Database ownership, admission and replay for session-bound cabinet turns.</summary>
public sealed class AiDurableTurnStore(DataContext db)
{
    private sealed class SingleAttempt(DbContext context) : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    internal async Task<AiTurnJob> EnqueueAsync(
        string sessionId, int storeId, int userId, string kind, string engine,
        AiDurableTurnRequest request, ChatMessage? userMessage, CancellationToken ct)
    {
        if (sessionId.Length is < 1 or > 36 || engine is not ("legacy" or "maf")
            || kind is not ("chat" or "execution"))
            throw new InvalidOperationException("Invalid durable turn.");
        var runId = Guid.NewGuid().ToString("N");
        var retry = false;
        try
        {
            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (retry) db.ChangeTracker.Clear();
                retry = true;
                // A transient error after commit must return the same admission.
                var committed = await db.aiTurnJobs.AsNoTracking()
                    .SingleOrDefaultAsync(j => j.runId == runId && j.storeId == storeId && j.userId == userId, ct);
                if (committed != null) return committed;
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var active = await FindActiveAsync(sessionId, storeId, userId, ct);
                if (active != null)
                {
                    if (kind == "chat") return active;
                    throw new AiTurnAdmissionException("turn_running");
                }
                if (kind == "chat")
                    await AiConversationTurnStore.AppendUserIfNeededAsync(
                        db, sessionId, storeId, userId, userMessage, ct);
                var conversation = await db.aiConversations.AsNoTracking().SingleOrDefaultAsync(
                    c => c.sessionId == sessionId && c.storeId == storeId && c.userId == userId, ct)
                    ?? throw new AiTurnAdmissionException("conversation_missing");
                var now = DateTime.UtcNow;
                var job = new AiTurnJob
                {
                    runId = runId, sessionId = sessionId, activeSessionId = sessionId,
                    conversationId = conversation.Id, storeId = storeId, userId = userId,
                    kind = kind, phase = kind, engine = engine,
                    createdAtUtc = now, updatedAtUtc = now,
                };
                // The unique active-session slot is acquired before consuming approval.
                db.aiTurnJobs.Add(job);
                await db.SaveChangesAsync(ct);
                if (kind == "execution")
                {
                    var confirmation = await AiConversationTurnStore.ConsumePendingPlanDetailedAsync(
                        db, sessionId, request.ProposalId!, storeId, userId, ct);
                    if (confirmation.Steps == null)
                        throw new AiTurnAdmissionException(confirmation.RejectionReason ?? "invalid_plan");
                    request = request with { Steps = confirmation.Steps.ToList() };
                }
                job.requestJson = JsonSerializer.Serialize(request, Json);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return job;
            });
        }
        catch (DbUpdateException)
        {
            // Concurrent inserts violate the slot index, not an EF concurrency token.
            db.ChangeTracker.Clear();
            var active = await FindActiveAsync(sessionId, storeId, userId, ct);
            if (kind == "chat" && active != null) return active;
            if (active != null) throw new AiTurnAdmissionException("turn_running");
            throw;
        }
    }

    internal Task<AiTurnJob?> FindActiveAsync(string sessionId, int storeId, int userId, CancellationToken ct) =>
        db.aiTurnJobs.AsNoTracking().SingleOrDefaultAsync(
            j => j.activeSessionId == sessionId && j.storeId == storeId && j.userId == userId, ct);

    public Task<AiTurnJob?> FindLatestAsync(string sessionId, int storeId, int userId, CancellationToken ct) =>
        db.aiTurnJobs.AsNoTracking()
            .Where(j => j.sessionId == sessionId && j.storeId == storeId && j.userId == userId)
            .OrderByDescending(j => j.Id).FirstOrDefaultAsync(ct);

    internal async Task<AiTurnJob?> ClaimAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var candidate = await db.aiTurnJobs.AsNoTracking()
            .Where(j => j.activeSessionId != null && (j.status == "pending"
                || (j.status == "running" && (j.leaseExpiresAtUtc == null || j.leaseExpiresAtUtc <= now))))
            .OrderBy(j => j.Id).FirstOrDefaultAsync(ct);
        if (candidate == null) return null;
        return await TryClaimCandidateAsync(candidate, now, ct);
    }

    internal async Task<AiTurnJob?> TryClaimCandidateAsync(
        AiTurnJob candidate, DateTime now, CancellationToken ct)
    {
        var owner = Guid.NewGuid().ToString("N");
        var until = now + LeaseDuration;
        // Expiry is tested by the UPDATE itself: a heartbeat may have renewed it since SELECT.
        await db.aiTurnJobs
            .Where(j => j.Id == candidate.Id && j.revision == candidate.revision
                && j.activeSessionId != null && (j.status == "pending"
                    || (j.status == "running" && (j.leaseExpiresAtUtc == null || j.leaseExpiresAtUtc <= now))))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.status, "running")
                .SetProperty(j => j.leaseOwner, owner).SetProperty(j => j.leaseExpiresAtUtc, until)
                .SetProperty(j => j.updatedAtUtc, now).SetProperty(j => j.attempts, j => j.attempts + 1)
                .SetProperty(j => j.revision, j => j.revision + 1), ct);
        return await db.aiTurnJobs.AsNoTracking().SingleOrDefaultAsync(
            j => j.Id == candidate.Id && j.leaseOwner == owner && j.status == "running", ct);
    }

    internal async Task<bool> RenewAsync(long id, string owner, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.aiTurnJobs
            .Where(j => j.Id == id && j.leaseOwner == owner && j.status == "running"
                && j.leaseExpiresAtUtc > now && !j.cancelRequested)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.leaseExpiresAtUtc, now + LeaseDuration), ct) == 1;
    }

    public async Task<bool> RequestStopAsync(string sessionId, int storeId, int userId, CancellationToken ct) =>
        await db.aiTurnJobs.Where(j => j.activeSessionId == sessionId && j.storeId == storeId
                && j.userId == userId && !j.cancelRequested && j.lastEventType != SseEventType.Done
                && j.lastEventType != SseEventType.Stopped && j.lastEventType != SseEventType.Error)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.cancelRequested, true), ct) == 1;

    internal async Task<AiTurnJob> OwnedAsync(long id, string owner, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.aiTurnJobs.AsNoTracking().SingleOrDefaultAsync(
            j => j.Id == id && j.leaseOwner == owner && j.status == "running"
                && j.leaseExpiresAtUtc > now, ct) ?? throw new AiTurnLeaseLostException();
    }

    internal Task<List<AiTurnEvent>> ReadEventsAsync(long id, CancellationToken ct) =>
        ReadEventsAfterAsync(id, 0, ct);

    internal Task<TurnBinding> BeginPhaseAsync(
        AiTurnJob lease, string phase, string? proposalId, string? parent, CancellationToken ct) =>
        new SingleAttempt(db).ExecuteAsync(() => BeginPhaseCoreAsync(lease, phase, proposalId, parent, ct));

    private async Task<TurnBinding> BeginPhaseCoreAsync(
        AiTurnJob lease, string phase, string? proposalId, string? parent, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await OwnedAsync(lease.Id, lease.leaseOwner!, ct);
        if (job.bindingJson != null && job.phase == phase)
            return JsonSerializer.Deserialize<TurnBinding>(job.bindingJson, Json)!;
        await FenceAsync(job, false, ct);
        var binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
            db, job.sessionId, job.storeId, job.userId, job.runId, phase, proposalId, parent, ct);
        var encoded = JsonSerializer.Serialize(binding, Json);
        await db.aiTurnJobs.Where(j => j.Id == job.Id && j.leaseOwner == job.leaseOwner)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.bindingJson, encoded)
                .SetProperty(j => j.phase, phase), ct);
        await tx.CommitAsync(ct);
        return binding;
    }

    internal Task<AiTurnJob> AppendAsync(
        AiTurnJob lease, AiTurnEvent item, TurnBinding binding,
        AiSseTurnSnapshot? projection, CancellationToken ct, bool allowCancelled = false) =>
        new SingleAttempt(db).ExecuteAsync(() => AppendCoreAsync(
            lease, item, binding, projection, ct, allowCancelled));

    private async Task<AiTurnJob> AppendCoreAsync(
        AiTurnJob lease, AiTurnEvent item, TurnBinding binding,
        AiSseTurnSnapshot? projection, CancellationToken ct, bool allowCancelled = false)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await OwnedAsync(lease.Id, lease.leaseOwner!, ct);
        if (item.RunId != job.runId || item.Sequence != job.lastSequence + 1
            || item.DbId != binding.DbId || item.MessageId != binding.MessageId
            || item.Phase != binding.Phase || item.ProposalId != binding.ProposalId)
            throw new InvalidOperationException("Durable event identity mismatch.");
        var checkpoints = JsonSerializer.Deserialize<List<AiExecutionStepCheckpoint>>(job.checkpointsJson, Json)!;
        var starting = false;
        if (binding.Phase == "execution" && item.Type == SseEventType.Tool
            && item.Data.TryGetProperty("stepIndex", out var index))
        {
            var stepIndex = index.GetInt32();
            var state = item.Data.GetProperty("status").GetString();
            if (state == "running")
            {
                if (job.startedStep != null || stepIndex != checkpoints.Count)
                    throw new InvalidOperationException("Invalid execution checkpoint.");
                job.startedStep = stepIndex;
                starting = true;
            }
            else if (state == "done")
            {
                if (job.startedStep != stepIndex || stepIndex != checkpoints.Count)
                    throw new InvalidOperationException("Invalid execution completion.");
                checkpoints.Add(new AiExecutionStepCheckpoint(stepIndex,
                    item.Data.GetProperty("name").GetString()!,
                    item.Data.GetProperty("input").GetString()!,
                    item.Data.GetProperty("id").GetString()!,
                    item.Data.GetProperty("executionOutcome").Clone()));
                job.startedStep = null;
            }
        }
        await FenceAsync(job, starting || !allowCancelled, ct);
        var checkpointJson = JsonSerializer.Serialize(checkpoints, Json);
        var ended = job.executionEnded || (binding.Phase == "execution" && item.Type == SseEventType.Done);
        await db.aiTurnJobs.Where(j => j.Id == job.Id && j.leaseOwner == job.leaseOwner)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.startedStep, job.startedStep)
                .SetProperty(j => j.checkpointsJson, checkpointJson)
                .SetProperty(j => j.executionEnded, ended)
                .SetProperty(j => j.lastSequence, item.Sequence!.Value)
                .SetProperty(j => j.lastEventType, item.Type), ct);
        db.aiTurnJournalEvents.Add(new AiTurnJournalEvent
        {
            jobId = job.Id, sequence = item.Sequence!.Value,
            eventJson = JsonSerializer.Serialize(item, Json),
        });
        // The projection helper may clear its tracker on a concurrency retry.
        // Save the journal first, inside this same transaction, so it cannot be detached and lost.
        await db.SaveChangesAsync(ct);
        if (projection != null)
            await AiConversationTurnStore.PersistAssistantTurnAsync(
                db, job.sessionId, job.storeId, job.userId, binding, projection.Value, ct);
        await tx.CommitAsync(ct);
        return await OwnedAsync(job.Id, job.leaseOwner!, ct);
    }

    private async Task FenceAsync(AiTurnJob job, bool starting, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var changed = await db.aiTurnJobs.Where(j => j.Id == job.Id && j.leaseOwner == job.leaseOwner
                && j.revision == job.revision && j.status == "running" && j.leaseExpiresAtUtc > now
                && (!starting || !j.cancelRequested))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.revision, j => j.revision + 1)
                .SetProperty(j => j.updatedAtUtc, now), ct);
        if (changed != 1) throw new AiTurnLeaseLostException();
    }

    internal Task CompleteAsync(
        AiTurnJob lease, string status, CancellationToken ct,
        TurnBinding? binding = null, AiSseTurnSnapshot? projection = null) =>
        new SingleAttempt(db).ExecuteAsync(() => CompleteCoreAsync(lease, status, ct, binding, projection));

    private async Task CompleteCoreAsync(
        AiTurnJob lease, string status, CancellationToken ct,
        TurnBinding? binding = null, AiSseTurnSnapshot? projection = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await OwnedAsync(lease.Id, lease.leaseOwner!, ct);
        await FenceAsync(job, false, ct);
        if (binding != null && projection != null)
            await AiConversationTurnStore.PersistAssistantTurnAsync(
                db, job.sessionId, job.storeId, job.userId, binding, projection.Value, ct);
        await db.aiTurnJobs.Where(j => j.Id == job.Id && j.leaseOwner == job.leaseOwner)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.status, status)
                .SetProperty(j => j.activeSessionId, (string?)null)
                .SetProperty(j => j.leaseOwner, (string?)null)
                .SetProperty(j => j.leaseExpiresAtUtc, (DateTime?)null), ct);
        await tx.CommitAsync(ct);
    }

    internal Task ReleaseAsync(AiTurnJob lease, CancellationToken ct) =>
        db.aiTurnJobs.Where(j => j.Id == lease.Id && j.leaseOwner == lease.leaseOwner && j.status == "running")
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.leaseExpiresAtUtc, DateTime.UtcNow), ct);

    private async Task<List<AiTurnEvent>> ReadEventsAfterAsync(long id, long after, CancellationToken ct)
    {
        var rows = await db.aiTurnJournalEvents.AsNoTracking()
            .Where(e => e.jobId == id && e.sequence > after)
            .OrderBy(e => e.sequence).Select(e => e.eventJson).ToListAsync(ct);
        return rows.Select(s => JsonSerializer.Deserialize<AiTurnEvent>(s, Json)
            ?? throw new InvalidOperationException("Invalid durable event.")).ToList();
    }

    public async IAsyncEnumerable<string> SubscribeAsync(
        AiTurnJob initial, string? runId, long afterSequence,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var after = runId == initial.runId && afterSequence >= 0 && afterSequence <= initial.lastSequence
            ? afterSequence : 0;
        while (true)
        {
            var current = await db.aiTurnJobs.AsNoTracking().SingleOrDefaultAsync(
                j => j.Id == initial.Id && j.storeId == initial.storeId && j.userId == initial.userId, ct);
            if (current == null) yield break;
            // Only the committed cursor is observable, even if a writer has an open transaction.
            foreach (var item in await ReadEventsAfterAsync(current.Id, after, ct))
            {
                if (item.Sequence > current.lastSequence) break;
                after = item.Sequence!.Value;
                yield return item.ToSse();
            }
            if (current.activeSessionId == null && after >= current.lastSequence) yield break;
            await Task.Delay(PollInterval, ct);
        }
    }
}

internal sealed class AiTurnAdmissionException(string reason) : InvalidOperationException(reason)
{
    public string Reason { get; } = reason;
}
