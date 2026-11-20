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
            .Include(s => s.command)
            .FirstAsync(s => s.Id == stageId, ct);
        // The claim above bypassed change tracking; mirror its post-claim values
        // so tracked writes stay consistent with the leased row.
        stage.claimedBy = _workerId;
        stage.claimedUntil = now + ClaimLease;
        stage.claimVersion = expectedClaimVersion + 1;
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
                    now, ct);
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
            attemptNumber, dispatchId);

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
            DateTime.UtcNow, ct, attempt);
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
        IntegrationAttempt? attempt = null)
    {
        var transition = await db.Database.CreateExecutionStrategy().ExecuteAsync<CommandTransition?>(
            async _ =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var previousState = stage.state;
                var (newState, nextCheckAt, terminal) = MapOutcome(result, stage, now);

                // Guarded stage write: applies only while our lease still owns
                // the row. An expired worker whose result arrives late cannot
                // overwrite a newer reconciliation outcome.
                var guardVersion = stage.claimVersion;
                var releasedVersion = guardVersion + 1;
                var mergedReceipt = result.ReceiptJson ?? stage.receiptJson;
                var mergedEvidence = result.EvidenceJson ?? stage.evidenceJson;
                var mergedIssues = result.IssuesJson ?? stage.issuesJson;
                var newFinishedAt = terminal ? (DateTime?)now : stage.finishedAt;
                var updated = await db.integrationStages
                    .Where(s => s.Id == stage.Id && s.claimVersion == guardVersion)
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
                    attempt.outcome = result.Outcome.ToString().ToLowerInvariant();
                    attempt.effectCertainty = result.Outcome switch
                    {
                        IntegrationDispatchOutcome.Applied or IntegrationDispatchOutcome.PartiallyApplied
                            => IntegrationEffectCertainty.Applied,
                        IntegrationDispatchOutcome.Rejected or IntegrationDispatchOutcome.Cancelled
                            => IntegrationEffectCertainty.None,
                        _ => IntegrationEffectCertainty.Possible
                    };
                    attempt.responseSummary = result.ResponseSummary;
                    attempt.issuesJson = result.IssuesJson;
                    attempt.completedAt = now;
                }

                if (updated == 0)
                {
                    // Lease lost: another worker owns this stage now. Persist
                    // only our attempt evidence (the dispatch really happened);
                    // stage and command belong to the newer owner.
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return null;
                }

                // Mirror the committed values for the aggregate read below.
                stage.state = newState;
                stage.nextCheckAt = nextCheckAt;
                stage.receiptJson = mergedReceipt;
                stage.evidenceJson = mergedEvidence;
                stage.issuesJson = mergedIssues;
                if (terminal)
                    stage.finishedAt = now;
                stage.claimedBy = null;
                stage.claimedUntil = null;
                stage.claimVersion = releasedVersion;

                var transition = await TransitionCommandIfTerminalAsync(db, command, now, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                if (previousState != newState || transition.TerminalState != null)
                    _logger.LogDebug(
                        "Integration stage {StageId} -> {State}, command {CommandId} -> {CommandState}",
                        stage.Id, newState, command.commandId, transition.TerminalState ?? command.state);
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
        if (IntegrationCommandState.IsTerminal(command.state))
            return;

        var transition = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var transition = await TransitionCommandIfTerminalAsync(db, command, now, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return transition;
            });

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
                transition.OperationStatus, transition.Summary);
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
    private async Task<CommandTransition> TransitionCommandIfTerminalAsync(
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
        if (command.state == IntegrationCommandState.Admitted)
            command.state = IntegrationCommandState.Executing;
        command.updatedAt = now;

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

            command.state = newState;
            command.summary = summary;
            command.finishedAt = now;
        }

        // Project into operations inside the same transaction.
        var operation = await db.operations
            .FirstOrDefaultAsync(o => o.operationId == command.operationId, ct);
        if (operation != null)
        {
            operation.totalItems = items.Count;
            operation.completedItems = completedItems;
            operation.failedItems = failedItems;
            if (newState != null)
            {
                var opStatus = MapCommandToOperationStatus(newState);
                if (OperationLifecycle.ShouldApplyFinish(operation.status, opStatus))
                {
                    operation.status = opStatus;
                    operation.summary = summary;
                    operation.finishedAt = now;
                }
            }
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
