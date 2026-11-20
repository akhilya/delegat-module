using System.Text.Json;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.DependencyInjection;
using TurnBinding = Delegat.Services.Ai.AiConversationTurnStore.AiAssistantTurnBinding;

namespace Delegat.Services.Ai;

/// <summary>
/// Resumes only committed execution prefixes. Started external calls and model streams
/// are not replayable; a lease cannot revoke an external request already in flight.
/// </summary>
public sealed class AiDurableTurnWorker(
    IServiceScopeFactory scopes, ILogger<AiDurableTurnWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PollAsync(stoppingToken)));

    private async Task PollAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await Store(s => s.ClaimAsync(stoppingToken));
                if (job == null)
                    await Task.Delay(AiDurableTurnStore.PollInterval, stoppingToken);
                else
                    await RunClaimedAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Durable AI turn polling failed");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    internal async Task RunClaimedAsync(AiTurnJob claimed, CancellationToken stoppingToken)
    {
        var job = claimed;
        TurnBinding? binding = null;
        var segment = new List<AiTurnEvent>();
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = HeartbeatAsync(claimed, runCts, heartbeatCts.Token);
        var leaseLost = false;
        var finalized = false;
        try
        {
            var request = JsonSerializer.Deserialize<AiDurableTurnRequest>(job.requestJson, AiDurableTurnStore.Json)
                ?? throw new InvalidOperationException("Missing durable request.");
            if (job.payloadVersion != 1 || job.engine is not ("legacy" or "maf")
                || job.kind is not ("chat" or "execution"))
                throw new InvalidOperationException("Unsupported durable turn format.");
            var history = await Store(s => s.ReadEventsAsync(job.Id, stoppingToken));
            binding = job.bindingJson == null ? null
                : JsonSerializer.Deserialize<TurnBinding>(job.bindingJson, AiDurableTurnStore.Json);
            if (binding != null)
                segment = history.Where(e => e.MessageId == binding.MessageId).ToList();
            if (binding == null)
            {
                binding = await Store(s => s.BeginPhaseAsync(job, job.kind,
                    request.ProposalId, null, stoppingToken));
                await Emit(AiTurnEvent.Create(SseEventType.MessageStart, new { }));
            }
            var last = segment.LastOrDefault();
            if (last?.Type is SseEventType.Stopped
                || (last?.Type == SseEventType.Done && binding.Phase != "execution"))
            {
                await Finish(last.Type == SseEventType.Stopped ? "stopped"
                    : IsTrue(last.Data, "interrupted") ? "interrupted" : "completed");
                return;
            }
            if (job.cancelRequested) throw new OperationCanceledException();
            if (job.attempts > 1 && job.kind == "execution" && job.executionEnded
                && last?.Type == SseEventType.Done
                && (IsTrue(last.Data, "cancelled") || IsTrue(last.Data, "interrupted")))
            {
                await Finish("interrupted");
                return;
            }
            if (job.attempts > 1 && job.startedStep == null
                && binding.Phase != "execution" && segment.Any(e => e.Type == SseEventType.Plan))
            {
                // A validated proposal already exists. Finish it, never ask the model to mint another.
                await Emit(AiTurnEvent.Create(SseEventType.Done, new { }));
                await Finish("completed");
                return;
            }
            if (job.startedStep != null || (job.attempts > 1 && binding.Phase != "execution"))
            {
                await Interrupted();
                return;
            }

            using var producerScope = scopes.CreateScope();
            var checkpoints = JsonSerializer.Deserialize<List<AiExecutionStepCheckpoint>>(
                job.checkpointsJson, AiDurableTurnStore.Json)!;
            var producer = producerScope.ServiceProvider.GetRequiredService<IAiDurableTurnProducer>()
                .RunAsync(job, request, new AiExecutionResume(checkpoints, job.executionEnded), runCts.Token);
            await foreach (var item in producer.WithCancellation(runCts.Token))
            {
                stoppingToken.ThrowIfCancellationRequested();
                if (item.Type == SseEventType.AssistantStart)
                {
                    // Detach A before attempting B: review admission must never reopen execution.
                    binding = null;
                    segment = [];
                    binding = await Store(s => s.BeginPhaseAsync(
                        job, "review", null, request.ProposalId, stoppingToken));
                }
                var completedTool = item.Type == SseEventType.Tool
                    && item.Data.TryGetProperty("status", out var state) && state.GetString() == "done";
                await Emit(item, completedTool);
                if (job.cancelRequested) throw new OperationCanceledException();
            }
            if (!AiSseTurnRecorder.FoldEvents(segment).TurnEnded)
                await Interrupted();
            else
                await Finish(segment.LastOrDefault()?.Type == SseEventType.Stopped ? "stopped"
                    : segment.LastOrDefault()?.Type == SseEventType.Error
                        || (segment.LastOrDefault() is { } terminal && IsTrue(terminal.Data, "interrupted"))
                            ? "interrupted" : "completed");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Retain the last checkpoint. The next owner decides whether it is safely resumable.
        }
        catch (OperationCanceledException)
        {
            try
            {
                job = await Store(s => s.OwnedAsync(job.Id, claimed.leaseOwner!, CancellationToken.None));
                if (job.cancelRequested && binding != null)
                {
                    await EnsureUnfinishedBinding();
                    await Emit(AiTurnEvent.Create(SseEventType.Stopped, new { }), true);
                    await Finish("stopped");
                }
                else
                {
                    leaseLost = true;
                }
            }
            catch (AiTurnLeaseLostException) { leaseLost = true; }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Durable AI turn {RunId} failed", job.runId);
            try
            {
                if (binding != null) await Interrupted();
                else await Finish("interrupted");
            }
            catch (AiTurnLeaseLostException) { leaseLost = true; }
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeat; }
            catch (OperationCanceledException) { }
            if (!finalized && !leaseLost)
            {
                try { await Store(s => s.ReleaseAsync(claimed, CancellationToken.None)); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not release AI turn lease {RunId}", job.runId); }
            }
        }

        async Task Emit(AiTurnEvent item, bool allowCancelled = false)
        {
            stoppingToken.ThrowIfCancellationRequested();
            var target = binding ?? throw new InvalidOperationException("Missing canonical binding.");
            var stamped = item with
            {
                RunId = job.runId, MessageId = target.MessageId, DbId = target.DbId,
                Phase = target.Phase, ProposalId = target.ProposalId, Sequence = job.lastSequence + 1,
            };
            var nextSegment = segment.Append(stamped).ToList();
            // Execution merge consumes a whole segment, not incremental prefixes.
            // Repeated partial merges would duplicate the proposal/tool timeline.
            var projection = item.Type is SseEventType.Done or SseEventType.Stopped
                ? AiSseTurnRecorder.FoldEvents(nextSegment) : (AiSseTurnSnapshot?)null;
            job = await Store(s => s.AppendAsync(
                job, stamped, target, projection, CancellationToken.None, allowCancelled));
            segment = nextSegment;
        }

        async Task Interrupted()
        {
            await EnsureUnfinishedBinding();
            if (binding?.Phase == "execution" && !job.executionEnded)
            {
                var request = JsonSerializer.Deserialize<AiDurableTurnRequest>(job.requestJson, AiDurableTurnStore.Json)!;
                var checkpoints = JsonSerializer.Deserialize<List<AiExecutionStepCheckpoint>>(
                    job.checkpointsJson, AiDurableTurnStore.Json)!;
                await Emit(AiTurnEvent.Create(SseEventType.ExecutionStatus, new
                {
                    status = "interrupted",
                    succeeded = checkpoints.Count(c => c.Outcome.GetProperty("status").GetString() is "completed" or "queued"),
                    total = request.Steps.Count,
                    outcomeUnknown = true,
                    summary = AgentToolResult.TurnInterruptedMessage,
                }), true);
            }
            await Emit(AiTurnEvent.Create(SseEventType.Error,
                new { message = AgentToolResult.TurnInterruptedMessage }), true);
            await Emit(AiTurnEvent.Create(SseEventType.Done, new { interrupted = true }), true);
            await Finish("interrupted");
        }

        async Task EnsureUnfinishedBinding()
        {
            if (binding?.Phase != "execution" || !job.executionEnded) return;
            binding = null;
            segment = [];
            var request = JsonSerializer.Deserialize<AiDurableTurnRequest>(job.requestJson, AiDurableTurnStore.Json)!;
            binding = await Store(s => s.BeginPhaseAsync(job, "review", null, request.ProposalId, CancellationToken.None));
            await Emit(AiTurnEvent.Create(SseEventType.AssistantStart,
                new { executionProposalId = request.ProposalId }), true);
        }

        async Task Finish(string status)
        {
            await Store(s => s.CompleteAsync(job, status, CancellationToken.None,
                binding, binding == null ? null : AiSseTurnRecorder.FoldEvents(segment)));
            finalized = true;
        }
    }

    private async Task HeartbeatAsync(AiTurnJob lease, CancellationTokenSource run, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(AiDurableTurnStore.HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (await Store(s => s.RenewAsync(lease.Id, lease.leaseOwner!, ct))) continue;
                run.Cancel();
                break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI turn heartbeat failed for {RunId}", lease.runId);
            run.Cancel();
        }
    }

    private async Task<T> Store<T>(Func<AiDurableTurnStore, Task<T>> action)
    {
        using var scope = scopes.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AiDurableTurnStore>());
    }

    private async Task Store(Func<AiDurableTurnStore, Task> action)
    {
        using var scope = scopes.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AiDurableTurnStore>());
    }

    private static bool IsTrue(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var flag)
        && flag.ValueKind == JsonValueKind.True;
}
