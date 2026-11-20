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
    private const int BatchSize = 20;
    private const int MaxStageAttempts = 3;

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
        var activeStates = new[]
        {
            IntegrationStageState.Ready,
            IntegrationStageState.Deferred,
            IntegrationStageState.Accepted,
            IntegrationStageState.InFlight
        };
        var openCommandStates = new[]
        {
            IntegrationCommandState.Admitted,
            IntegrationCommandState.Executing
        };

        // Candidates: open commands, unexpired claims excluded, eligible now.
        var candidates = await db.integrationStages
            .AsNoTracking()
            .Where(s => activeStates.Contains(s.state)
                && (s.nextCheckAt == null || s.nextCheckAt <= now)
                && (s.claimedUntil == null || s.claimedUntil <= now)
                && openCommandStates.Contains(s.command.state))
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
                // Claim expires on its own; the stage is retried or reconciled later.
                _logger.LogError(ex, "Failed to process integration stage {StageId}", candidate.Id);
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
        // so the release in FinalizeStageAsync is detected as a real change.
        stage.claimedBy = _workerId;
        stage.claimedUntil = now + ClaimLease;
        stage.claimVersion = expectedClaimVersion + 1;
        var command = stage.command;

        if (stage.state == IntegrationStageState.Ready)
        {
            var blockedBy = await GetBlockingDependencyAsync(db, stage, ct);
            if (blockedBy == DependencyBlocking.Waiting)
            {
                await ReleaseClaimAsync(db, stage, ct);
                return;
            }
            if (blockedBy == DependencyBlocking.Failed || command.stopRequested)
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
        // dispatchId is the stable per-stage idempotency window; requestKey is unique per attempt.
        var dispatchId = $"{command.commandId}:{stage.stageKey}";
        var requestKey = $"{dispatchId}/a{attemptNumber}";

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
            attemptNumber, requestKey);

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

        var depStates = await db.integrationStages
            .AsNoTracking()
            .Where(s => s.commandId == stage.commandId && deps.Contains(s.stageKey))
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
    private static async Task ReleaseClaimAsync(DataContext db, IntegrationStage stage, CancellationToken ct)
    {
        var releasedVersion = stage.claimVersion + 1;
        await db.integrationStages
            .Where(s => s.Id == stage.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.claimedBy, (string?)null)
                .SetProperty(x => x.claimedUntil, (DateTime?)null)
                .SetProperty(x => x.claimVersion, releasedVersion), ct);
        stage.claimedBy = null;
        stage.claimedUntil = null;
        stage.claimVersion = releasedVersion;
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
        await using var tx = await db.Database.BeginTransactionAsync(ct);

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

        stage.receiptJson = result.ReceiptJson ?? stage.receiptJson;
        stage.evidenceJson = result.EvidenceJson ?? stage.evidenceJson;
        stage.issuesJson = result.IssuesJson ?? stage.issuesJson;
        stage.updatedAt = now;
        // Released unconditionally via ExecuteUpdate below: tracked net-diff
        // writes can silently drop the release when the entity was admitted
        // through this same context.
        var releasedVersion = stage.claimVersion + 1;
        stage.claimedBy = null;
        stage.claimedUntil = null;
        stage.claimVersion = releasedVersion;

        var previousState = stage.state;
        switch (result.Outcome)
        {
            case IntegrationDispatchOutcome.Applied:
                stage.state = IntegrationStageState.Applied;
                stage.finishedAt = now;
                break;
            case IntegrationDispatchOutcome.PartiallyApplied:
                stage.state = IntegrationStageState.PartiallyApplied;
                stage.finishedAt = now;
                break;
            case IntegrationDispatchOutcome.Rejected:
                // A before-effect rejection is proven no-effect; bounded retry only.
                if (result.Retryable && stage.attemptCount < MaxStageAttempts)
                {
                    stage.state = IntegrationStageState.Ready;
                    stage.nextCheckAt = now + TimeSpan.FromSeconds(30 * stage.attemptCount);
                }
                else
                {
                    stage.state = IntegrationStageState.RejectedBeforeEffect;
                    stage.finishedAt = now;
                }
                break;
            case IntegrationDispatchOutcome.Accepted:
                stage.state = IntegrationStageState.Accepted;
                stage.nextCheckAt = result.NextCheckAt ?? now + TimeSpan.FromSeconds(15);
                break;
            case IntegrationDispatchOutcome.Pending:
                stage.state = IntegrationStageState.Deferred;
                stage.nextCheckAt = result.NextCheckAt ?? now + TimeSpan.FromSeconds(15);
                break;
            case IntegrationDispatchOutcome.Cancelled:
                stage.state = IntegrationStageState.CancelledBeforeEffect;
                stage.finishedAt = now;
                break;
            case IntegrationDispatchOutcome.Unknown:
                // Effect ambiguous: terminal for automation, blocks dependents.
                stage.state = IntegrationStageState.Unknown;
                stage.finishedAt = now;
                break;
        }

        var transition = await TransitionCommandIfTerminalAsync(db, operationLogger, stage, command, now, ct);
        await db.SaveChangesAsync(ct);
        await db.integrationStages
            .Where(s => s.Id == stage.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.claimedBy, (string?)null)
                .SetProperty(x => x.claimedUntil, (DateTime?)null)
                .SetProperty(x => x.claimVersion, releasedVersion), ct);
        await tx.CommitAsync(ct);

        // SignalR is emitted only after the projection committed.
        if (transition.TerminalState != null && transition.OperationStatus != null)
        {
            await operationLogger.FinishAsync(command.operationId, command.storeId,
                transition.OperationStatus, transition.Summary);
        }
        else
        {
            await operationLogger.ProgressAsync(command.operationId, command.storeId,
                transition.TotalItems, transition.CompletedItems, transition.FailedItems);
        }

        if (previousState != stage.state || transition.TerminalState != null)
            _logger.LogDebug(
                "Integration stage {StageId} -> {State}, command {CommandId} -> {CommandState}",
                stage.Id, stage.state, command.commandId, transition.TerminalState ?? command.state);
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
        IOperationLogger operationLogger,
        IntegrationStage stage,
        IntegrationCommand command,
        DateTime now,
        CancellationToken ct)
    {
        var stages = await db.integrationStages
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
            else if (command.stopRequested)
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
