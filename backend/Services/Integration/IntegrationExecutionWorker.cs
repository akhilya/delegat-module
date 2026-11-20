using System.Text.Json;
using Delegat.Models;
using Delegat.Models.Integration;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Integration;

/// <summary>
/// The single polling worker of the durable executor. Claims due stages,
/// persists an attempt before any physical dispatch, applies the dispatch or
/// confirmation outcome, carries persisted waits, recovers in-flight work
/// after restarts through the confirmation path, and commits command/project
/// transitions atomically before emitting SignalR.
/// </summary>
public class IntegrationExecutionWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);
    /// <summary>Persisted re-check delay for stages blocked on item-local dependencies.</summary>
    private static readonly TimeSpan DependencyWaitDelay = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;
    private const int MaxStageAttempts = 3;

    private static readonly string[] ActiveStageStates =
    [
        IntegrationStageState.Ready,
        IntegrationStageState.Deferred,
        IntegrationStageState.Accepted,
        IntegrationStageState.InFlight
    ];
    private static readonly string[] OpenCommandStates =
    [
        IntegrationCommandState.Admitted,
        IntegrationCommandState.Executing
    ];
    private static readonly string[] TerminalStageStates =
    [
        IntegrationStageState.Applied,
        IntegrationStageState.PartiallyApplied,
        IntegrationStageState.RejectedBeforeEffect,
        IntegrationStageState.Unknown,
        IntegrationStageState.CancelledBeforeEffect
    ];
    private static readonly string[] OperationTerminalStates =
    [
        OperationLifecycle.Completed,
        OperationLifecycle.Failed,
        OperationLifecycle.Partial,
        OperationLifecycle.Cancelled
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationExecutionWorker> _logger;
    private readonly string _workerId = $"worker-{Guid.NewGuid():N}";

    public IntegrationExecutionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationExecutionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueStagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Integration execution worker iteration failed");
            }
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    /// <summary>One claim/dispatch cycle; internal so tests can drive it directly.</summary>
    internal async Task ProcessDueStagesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationStageDispatcher>();
        var operationLogger = scope.ServiceProvider.GetRequiredService<IOperationLogger>();

        var now = DateTime.UtcNow;

        // Candidates: open commands, unexpired claims excluded, eligible now.
        var candidates = await db.integrationStages
            .AsNoTracking()
            .Where(s => ActiveStageStates.Contains(s.state)
                && (s.nextCheckAt == null || s.nextCheckAt <= now)
                && (s.claimedUntil == null || s.claimedUntil <= now)
                && OpenCommandStates.Contains(s.command.state))
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.claimVersion })
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                await ProcessStageAsync(db, dispatcher, operationLogger, candidate.Id, candidate.claimVersion, now, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Drop poisoned tracked state so one failed stage cannot leak
                // pending writes into the next stage of this cycle. The claim
                // expires on its own; the stage is retried or reconciled later.
                db.ChangeTracker.Clear();
                _logger.LogError(ex, "Failed to process integration stage {StageId}", candidate.Id);
            }
        }

        // Stranded-command sweep: a command whose last stages finished on
        // different workers can be left executing with all-terminal stages.
        var stranded = await db.integrationCommands
            .Where(c => OpenCommandStates.Contains(c.state)
                && !db.integrationStages.Any(s => s.commandId == c.Id
                    && !TerminalStageStates.Contains(s.state)))
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var command in stranded)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                await FinalizeCommandAsync(db, operationLogger, command, DateTime.UtcNow, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                _logger.LogError(ex, "Failed to finalize integration command {CommandId}", command.commandId);
            }
        }
    }

    private async Task ProcessStageAsync(
        DataContext db,
        IIntegrationStageDispatcher dispatcher,
        IOperationLogger operationLogger,
        int stageId,
        int expectedClaimVersion,
        DateTime now,
        CancellationToken ct)
    {
        // Optimistic lease; only one worker wins.
        var claimed = await db.integrationStages
            .Where(s => s.Id == stageId && s.claimVersion == expectedClaimVersion)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.claimedBy, _workerId)
                .SetProperty(x => x.claimedUntil, now + ClaimLease)
                .SetProperty(x => x.claimVersion, expectedClaimVersion + 1)
                .SetProperty(x => x.updatedAt, now), ct);
        if (claimed == 0)
            return;

        var stage = await db.integrationStages
            .FirstAsync(s => s.Id == stageId, ct);
        // Committed state, not tracked state: ExecuteUpdate writes (claim and
        // earlier finalizations on a shared context) leave stale in-memory
        // values that would misroute the dispatch/confirm branch below and
        // renumber attempts wrongly. Reload also refreshes the snapshot, so
        // the attempt-before-send SaveChanges diffs against the real row.
        await db.Entry(stage).ReloadAsync(ct);
        await db.Entry(stage).Reference(s => s.command).LoadAsync(ct);
        await db.Entry(stage.command).ReloadAsync(ct);
        var command = stage.command;

        if (stage.state == IntegrationStageState.Ready)
        {
            var blockedBy = await GetBlockingDependencyAsync(db, stage, ct);
            if (blockedBy == DependencyBlocking.Waiting)
            {
                // Persisted wait: the stage is not eligible again until the delay
                // passes, so blocked stages cannot monopolize the polling batch.
                await ReleaseClaimAsync(db, stage, now + DependencyWaitDelay, ct);
                return;
            }

            // Re-read the stop flag: a Stop committed since the tracked command
            // was loaded must still prevent new writes.
            var stopRequested = await db.integrationCommands
                .AsNoTracking()
                .Where(c => c.Id == command.Id)
                .Select(c => c.stopRequested)
                .FirstAsync(ct);

            if (blockedBy == DependencyBlocking.Failed || stopRequested)
            {
                await FinalizeStageAsync(db, operationLogger, stage, command,
                    new IntegrationDispatchResult(
                        IntegrationDispatchOutcome.Cancelled,
                        IssuesJson: blockedBy == DependencyBlocking.Failed
                            ? "[{\"code\":\"dependency_failed\",\"message\":\"A required stage did not complete\"}]"
                            : "[{\"code\":\"stopped\",\"message\":\"Operation stopped before this stage was sent\"}]"),
                    now, ct, expectedClaimVersion + 1);
                return;
            }
        }

        var isDispatch = stage.state == IntegrationStageState.Ready;
        var attemptNumber = stage.attemptCount + 1;
        // dispatchId is the stable per-stage idempotency window the provider
        // sees; requestKey uniquely identifies this attempt for evidence.
        // For confirmation of a committed attempt the original dispatch
        // identity is reused verbatim - never a freshly derived key.
        var dispatchId = isDispatch
            ? $"{command.commandId}:{stage.Id}"
            : await db.integrationAttempts
                .AsNoTracking()
                .Where(a => a.stageId == stage.Id)
                .OrderByDescending(a => a.attemptNumber)
                .Select(a => (string?)a.dispatchId)
                .FirstOrDefaultAsync(ct)
              ?? $"{command.commandId}:{stage.Id}";
        var requestKey = attemptNumber == 1 ? dispatchId : $"{dispatchId}/a{attemptNumber}";

        var attempt = new IntegrationAttempt
        {
            commandId = command.Id,
            stageId = stage.Id,
            stageKey = stage.stageKey,
            dispatchId = dispatchId,
            attemptNumber = attemptNumber,
            requestKey = requestKey,
            requestHash = IntegrationCoordinator.ComputeHash(stage.payloadJson ?? ""),
            createdAt = now
        };

        // Attempt-before-send: committed so a lost response is never read as "not sent".
        stage.state = IntegrationStageState.InFlight;
        stage.attemptCount = attemptNumber;
        stage.updatedAt = now;
        db.integrationAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        var request = new DispatchStageRequest(
            command.Id, command.commandId, command.kind,
            stage.Id, stage.itemKey, stage.stageKey, stage.kind,
            stage.payloadJson, stage.payloadFileKey, stage.receiptJson,
            attemptNumber, dispatchId)
        {
            TransportAttempt = new(command.Id, stage.Id, attempt.Id, expectedClaimVersion + 1,
                isDispatch && stage.isEffectful, new DateTimeOffset(now + ClaimLease))
        };

        IntegrationDispatchResult result;
        try
        {
            result = isDispatch
                ? await dispatcher.DispatchAsync(request, ct)
                : await dispatcher.ConfirmAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Stage dispatch/confirmation fault for stage {StageId}", stage.Id);
            result = new IntegrationDispatchResult(
                IntegrationDispatchOutcome.Unknown,
                ResponseSummary: "dispatch_fault");
        }

        await FinalizeStageAsync(db, operationLogger, stage, command, result,
            DateTime.UtcNow, ct, expectedClaimVersion + 1, attempt);
    }

    private enum DependencyBlocking { None, Waiting, Failed }

    private static async Task<DependencyBlocking> GetBlockingDependencyAsync(
        DataContext db, IntegrationStage stage, CancellationToken ct)
    {
        var deps = JsonSerializer.Deserialize<List<string>>(stage.dependsOnJson) ?? [];
        if (deps.Count == 0)
            return DependencyBlocking.None;

        // Dependency edges are item-local: the same stageKey repeats across
        // items, so matching only on command+stageKey would let one item's
        // failure cancel the others.
        var depStates = await db.integrationStages
            .AsNoTracking()
            .Where(s => s.commandId == stage.commandId
                && s.itemKey == stage.itemKey
                && deps.Contains(s.stageKey))
            .Select(s => s.state)
            .ToListAsync(ct);

        if (depStates.Count < deps.Count || depStates.Any(s =>
                s is IntegrationStageState.RejectedBeforeEffect
                    or IntegrationStageState.Unknown
                    or IntegrationStageState.CancelledBeforeEffect))
            return DependencyBlocking.Failed;
        return depStates.All(IntegrationStageState.SatisfiesDependency)
            ? DependencyBlocking.None
            : DependencyBlocking.Waiting;
    }

    /// <summary>
    /// Releases a claim unconditionally. Claims are acquired through
    /// ExecuteUpdate (bypassing change tracking), so tracked net-diff writes
    /// can silently drop the release; it must go through ExecuteUpdate too.
    /// </summary>
    private static async Task ReleaseClaimAsync(
        DataContext db, IntegrationStage stage, DateTime nextCheckAt, CancellationToken ct)
    {
        var releasedVersion = stage.claimVersion + 1;
        await db.integrationStages
            .Where(s => s.Id == stage.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.claimedBy, (string?)null)
                .SetProperty(x => x.claimedUntil, (DateTime?)null)
                .SetProperty(x => x.claimVersion, releasedVersion)
                .SetProperty(x => x.nextCheckAt, nextCheckAt), ct);
        stage.claimedBy = null;
        stage.claimedUntil = null;
        stage.claimVersion = releasedVersion;
        stage.nextCheckAt = nextCheckAt;
    }

    private async Task FinalizeStageAsync(
        DataContext db,
        IOperationLogger operationLogger,
        IntegrationStage stage,
        IntegrationCommand command,
        IntegrationDispatchResult result,
        DateTime now,
        CancellationToken ct,
        int claimedVersion,
        IntegrationAttempt? attempt = null)
    {
        var transition = await db.Database.CreateExecutionStrategy().ExecuteAsync<CommandTransition?>(
            async _ =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var previousState = stage.state;
                var (newState, nextCheckAt, terminal) = MapOutcome(result, stage, now);

                // Guarded stage write: applies only while our lease still owns
                // the row. The guard checks the version our claim acquired -
                // an immutable local, never the tracked entity, so a strategy
                // retry after rollback re-checks the same lease rather than a
                // released version. An expired worker cannot overwrite a newer
                // reconciliation outcome.
                var releasedVersion = claimedVersion + 1;
                var mergedReceipt = result.ReceiptJson ?? stage.receiptJson;
                var mergedEvidence = result.EvidenceJson ?? stage.evidenceJson;
                var mergedIssues = result.IssuesJson ?? stage.issuesJson;
                var newFinishedAt = terminal ? (DateTime?)now : stage.finishedAt;
                var updated = await db.integrationStages
                    .Where(s => s.Id == stage.Id && s.claimVersion == claimedVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.state, newState)
                        .SetProperty(x => x.receiptJson, mergedReceipt)
                        .SetProperty(x => x.evidenceJson, mergedEvidence)
                        .SetProperty(x => x.issuesJson, mergedIssues)
                        .SetProperty(x => x.nextCheckAt, nextCheckAt)
                        .SetProperty(x => x.finishedAt, newFinishedAt)
                        .SetProperty(x => x.updatedAt, now)
                        .SetProperty(x => x.claimedBy, (string?)null)
                        .SetProperty(x => x.claimedUntil, (DateTime?)null)
                        .SetProperty(x => x.claimVersion, releasedVersion), ct);

                if (attempt != null)
                {
                    var outcome = result.Outcome.ToString().ToLowerInvariant();
                    var certainty = result.Outcome switch
                    {
                        IntegrationDispatchOutcome.Applied or IntegrationDispatchOutcome.PartiallyApplied
                            => IntegrationEffectCertainty.Applied,
                        IntegrationDispatchOutcome.Rejected or IntegrationDispatchOutcome.Cancelled
                            => IntegrationEffectCertainty.None,
                        _ => IntegrationEffectCertainty.Possible
                    };
                    // The attempt row was committed before dispatch; evidence is
                    // written directly so a lost lease or a strategy retry never
                    // flushes stale tracked state on top of a newer owner. The
                    // tracked entity stays untouched and clean, so the later
                    // SaveChanges cannot rewrite it either.
                    await db.integrationAttempts
                        .Where(a => a.Id == attempt.Id)
                        .ExecuteUpdateAsync(a => a
                            .SetProperty(x => x.outcome, outcome)
                            .SetProperty(x => x.effectCertainty, certainty)
                            .SetProperty(x => x.responseSummary, result.ResponseSummary)
                            .SetProperty(x => x.issuesJson, result.IssuesJson)
                            .SetProperty(x => x.completedAt, (DateTime?)now), ct);
                }

                if (updated == 0)
                {
                    // A zero-row guard is ambiguous under commit retries: a rival
                    // may own the stage, or our own previous attempt may have
                    // committed while its acknowledgment was lost. The row
                    // carrying exactly our outcome and our released lease means
                    // the earlier commit landed - the transition below must
                    // still run so the finish event is not lost.
                    var alreadyFinalizedByUs = await db.integrationStages
                        .AsNoTracking()
                        .AnyAsync(s => s.Id == stage.Id
                            && s.state == newState
                            && s.claimVersion == releasedVersion, ct);
                    if (!alreadyFinalizedByUs)
                    {
                        // Lease lost: another worker owns this stage now. Only
                        // our attempt evidence was persisted (the dispatch
                        // really happened); tracked leftovers must not flush
                        // into a row we no longer own.
                        db.ChangeTracker.Clear();
                        await tx.CommitAsync(ct);
                        return null;
                    }
                }

                var transition = await TransitionCommandIfTerminalAsync(db, command, now, ct);
                await tx.CommitAsync(ct);

                if (previousState != newState || transition?.TerminalState != null)
                    _logger.LogDebug(
                        "Integration stage {StageId} -> {State}, command {CommandId} -> {CommandState}",
                        stage.Id, newState, command.commandId, transition?.TerminalState ?? "open");
                return transition;
            }, ct);

        if (transition == null)
            return;
        await EmitTransitionAsync(operationLogger, command, transition);
    }

    private static (string State, DateTime? NextCheckAt, bool Terminal) MapOutcome(
        IntegrationDispatchResult result, IntegrationStage stage, DateTime now)
    {
        switch (result.Outcome)
        {
            case IntegrationDispatchOutcome.Applied:
                return (IntegrationStageState.Applied, null, true);
            case IntegrationDispatchOutcome.PartiallyApplied:
                return (IntegrationStageState.PartiallyApplied, null, true);
            case IntegrationDispatchOutcome.Rejected:
                // A before-effect rejection is proven no-effect; bounded retry only.
                return result.Retryable && stage.attemptCount < MaxStageAttempts
                    ? (IntegrationStageState.Ready, now + TimeSpan.FromSeconds(30 * stage.attemptCount), false)
                    : (IntegrationStageState.RejectedBeforeEffect, null, true);
            case IntegrationDispatchOutcome.Accepted:
                return (IntegrationStageState.Accepted,
                    result.NextCheckAt ?? now + TimeSpan.FromSeconds(15), false);
            case IntegrationDispatchOutcome.Pending:
                return (IntegrationStageState.Deferred,
                    result.NextCheckAt ?? now + TimeSpan.FromSeconds(15), false);
            case IntegrationDispatchOutcome.Cancelled:
                return (IntegrationStageState.CancelledBeforeEffect, null, true);
            default:
                // Effect ambiguous: terminal for automation, blocks dependents.
                return (IntegrationStageState.Unknown, null, true);
        }
    }

    /// <summary>
    /// Re-aggregates an open command whose stages are all terminal (used by the
    /// stranded-command sweep when two workers split the last stages).
    /// </summary>
    private async Task FinalizeCommandAsync(
        DataContext db,
        IOperationLogger operationLogger,
        IntegrationCommand command,
        DateTime now,
        CancellationToken ct)
    {
        // The sweep query may return a stale tracked instance; the terminal
        // check must see committed state.
        await db.Entry(command).ReloadAsync(ct);
        if (IntegrationCommandState.IsTerminal(command.state))
            return;

        var transition = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var transition = await TransitionCommandIfTerminalAsync(db, command, now, ct);
                await tx.CommitAsync(ct);
                return transition;
            });

        if (transition != null)
            await EmitTransitionAsync(operationLogger, command, transition);
    }

    private static async Task EmitTransitionAsync(
        IOperationLogger operationLogger,
        IntegrationCommand command,
        CommandTransition transition)
    {
        // SignalR is emitted only after the projection committed. The finish
        // path uses a notify-only call because the authoritative row is already
        // terminal and FinishAsync would return before publishing.
        if (transition.TerminalState != null && transition.OperationStatus != null)
        {
            await operationLogger.NotifyFinishedAsync(command.operationId, command.storeId,
                transition.OperationStatus, transition.Summary,
                transition.TotalItems, transition.CompletedItems, transition.FailedItems);
        }
        else
        {
            await operationLogger.ProgressAsync(command.operationId, command.storeId,
                transition.TotalItems, transition.CompletedItems, transition.FailedItems);
        }
    }

    private sealed record CommandTransition(
        string? TerminalState,
        string? OperationStatus,
        string? Summary,
        int TotalItems,
        int CompletedItems,
        int FailedItems);

    /// <summary>
    /// Aggregates item/command state and, on terminal transition, updates the
    /// operation projection inside the same transaction.
    /// </summary>
    private async Task<CommandTransition?> TransitionCommandIfTerminalAsync(
        DataContext db,
        IntegrationCommand command,
        DateTime now,
        CancellationToken ct)
    {
        // Aggregate over committed state: tracked entities in this context may
        // carry stale in-memory states from earlier poll candidates.
        var stages = await db.integrationStages
            .AsNoTracking()
            .Where(s => s.commandId == command.Id)
            .ToListAsync(ct);

        var allTerminal = stages.All(s => IntegrationStageState.IsTerminal(s.state));

        // Per-item projection counts.
        var items = stages.GroupBy(s => s.itemKey).Select(g => new
        {
            Completed = g.All(s => s.state is IntegrationStageState.Applied
                or IntegrationStageState.PartiallyApplied),
            Failed = g.Any(s => s.state is IntegrationStageState.RejectedBeforeEffect
                or IntegrationStageState.Unknown
                    or IntegrationStageState.CancelledBeforeEffect)
        }).ToList();
        var completedItems = items.Count(i => i.Completed);
        var failedItems = items.Count(i => i.Failed);

        string? newState = null;
        string? summary = null;
        if (allTerminal)
        {
            // The tracked command may be stale within a polling batch; read the
            // committed stop flag for the terminal classification.
            var stopRequested = await db.integrationCommands
                .AsNoTracking()
                .Where(c => c.Id == command.Id)
                .Select(c => c.stopRequested)
                .FirstAsync(ct);
            var anyUnknown = stages.Any(s => s.state == IntegrationStageState.Unknown);
            var anyApplied = stages.Any(s => s.state is IntegrationStageState.Applied
                or IntegrationStageState.PartiallyApplied);
            var allApplied = stages.All(s => s.state is IntegrationStageState.Applied
                or IntegrationStageState.PartiallyApplied);
            var partial = stages.Any(s => s.state == IntegrationStageState.PartiallyApplied);

            if (anyUnknown)
            {
                newState = IntegrationCommandState.Unknown;
                summary = "Outcome unknown for at least one stage - reconciliation required before replay";
            }
            else if (stopRequested)
            {
                newState = anyApplied ? IntegrationCommandState.Partial : IntegrationCommandState.Cancelled;
                summary = "Stopped" + (anyApplied ? $" after {completedItems} item(s) completed" : "");
            }
            else if (allApplied && !partial)
            {
                newState = IntegrationCommandState.Completed;
                summary = "All stages applied";
            }
            else if (!anyApplied)
            {
                newState = IntegrationCommandState.Failed;
                summary = "All stages failed before effect";
            }
            else
            {
                newState = IntegrationCommandState.Partial;
                summary = $"{completedItems} item(s) completed, {failedItems} failed";
            }
        }

        // All writes go through guarded ExecuteUpdate: the lambda may run inside
        // a retried execution strategy, and tracked mutations accepted by an
        // earlier SaveChanges would silently skip their writes after a rollback,
        // publishing a finish the projection never committed. A zero-row guard
        // means another worker finalized the command first: it owns the
        // projection and the finish notification, so the loser returns without
        // writing or announcing anything.
        if (newState != null)
        {
            // The finisher stamps its worker identity so commit retries after
            // a lost acknowledgment can prove the terminal row is their own
            // (a timestamp cannot identify a finalizer). A terminal row owned
            // by a rival loses the guard and announces nothing.
            var applied = await db.integrationCommands
                .Where(c => c.Id == command.Id
                    && (OpenCommandStates.Contains(c.state)
                        || (c.state == newState && c.finalizedBy == _workerId)))
                .ExecuteUpdateAsync(c => c
                    .SetProperty(x => x.state, newState)
                    .SetProperty(x => x.summary, summary)
                    .SetProperty(x => x.finishedAt, (DateTime?)now)
                    .SetProperty(x => x.finalizedBy, _workerId)
                    .SetProperty(x => x.updatedAt, now), ct);
            if (applied == 0)
                return null;
        }
        else
        {
            var touched = await db.integrationCommands
                .Where(c => c.Id == command.Id && OpenCommandStates.Contains(c.state))
                .ExecuteUpdateAsync(c => c
                    .SetProperty(x => x.state, x => x.state == IntegrationCommandState.Admitted
                        ? IntegrationCommandState.Executing
                        : x.state)
                    .SetProperty(x => x.updatedAt, now), ct);
            if (touched == 0)
                return null;
        }

        // Project into operations inside the same transaction. Counts update
        // unconditionally; the finish follows ShouldApplyFinish as a predicate:
        // a non-cancelled finish applies only over non-terminal statuses, while
        // cancelled is sticky-safe to (re)apply.
        await db.operations
            .Where(o => o.operationId == command.operationId)
            .ExecuteUpdateAsync(o => o
                .SetProperty(x => x.totalItems, items.Count)
                .SetProperty(x => x.completedItems, completedItems)
                .SetProperty(x => x.failedItems, failedItems), ct);

        if (newState != null)
        {
            var opStatus = MapCommandToOperationStatus(newState);
            await db.operations
                .Where(o => o.operationId == command.operationId
                    && (opStatus == OperationLifecycle.Cancelled
                        || !OperationTerminalStates.Contains(o.status)))
                .ExecuteUpdateAsync(o => o
                    .SetProperty(x => x.status, opStatus)
                    .SetProperty(x => x.summary, summary)
                    .SetProperty(x => x.finishedAt, (DateTime?)now), ct);
        }
        return new CommandTransition(
            newState,
            newState == null ? null : MapCommandToOperationStatus(newState),
            summary,
            items.Count,
            completedItems,
            failedItems);
    }

    private static string MapCommandToOperationStatus(string commandState) =>
        commandState switch
        {
            IntegrationCommandState.Completed => OperationLifecycle.Completed,
            IntegrationCommandState.Partial => OperationLifecycle.Partial,
            IntegrationCommandState.Cancelled => OperationLifecycle.Cancelled,
            // Unknown stays honest: terminal failure with an explicit
            // unknown-outcome summary, never a silent success.
            _ => OperationLifecycle.Failed
        };
}
