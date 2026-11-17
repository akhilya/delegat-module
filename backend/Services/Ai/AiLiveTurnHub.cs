using System.Collections.Concurrent;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.DependencyInjection;
using TurnBinding = Delegat.Services.Ai.AiConversationTurnStore.AiAssistantTurnBinding;

namespace Delegat.Services.Ai;

/// <summary>
/// Process-local live cabinet turns. Generation is owned by a hub CTS, not the browser SSE request.
/// Reloads subscribe with <c>GET /api/ai/chat/stream</c>. Lost when the API process restarts -
/// there are no durable jobs or checkpoints, and an interrupted turn never auto-replays
/// its side effects.
/// </summary>
public sealed class AiLiveTurnHub
{
    private readonly ConcurrentDictionary<string, AiLiveTurn> _turns = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AiLiveTurnHub> _logger;
    private readonly TimeSpan _completedLinger;

    public AiLiveTurnHub(IServiceScopeFactory scopes, ILogger<AiLiveTurnHub> logger)
        : this(scopes, logger, TimeSpan.FromSeconds(30))
    {
    }

    internal AiLiveTurnHub(IServiceScopeFactory scopes, ILogger<AiLiveTurnHub> logger, TimeSpan completedLinger)
    {
        _scopes = scopes;
        _logger = logger;
        _completedLinger = completedLinger;
    }

    public bool IsGenerating(string sessionId, int storeId, int userId)
    {
        var turn = Find(sessionId, storeId, userId);
        return turn is { IsCompleted: false };
    }

    public AiLiveTurn? Find(string sessionId, int storeId, int userId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        if (!_turns.TryGetValue(sessionId, out var turn)) return null;
        if (!turn.BelongsTo(storeId, userId)) return null;
        return turn;
    }

    /// <summary>
    /// Atomically reserve the session slot for a new turn. Returns null when a live
    /// turn already occupies the session (or the slot belongs to another
    /// store/user). Callers that reserve before a side effect (plan consumption)
    /// must either <see cref="StartReserved"/> or <see cref="ReleaseReservation"/>.
    /// </summary>
    public AiLiveTurn? TryReserve(
        string sessionId,
        int storeId,
        int userId,
        string? executionProposalId = null)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;

        var turn = new AiLiveTurn(sessionId, storeId, userId, executionProposalId);
        while (true)
        {
            if (_turns.TryGetValue(sessionId, out var existing))
            {
                if (!existing.BelongsTo(storeId, userId) || !existing.IsCompleted)
                {
                    turn.Dispose();
                    return null;
                }
                if (!_turns.TryRemove(KeyValuePair.Create(sessionId, existing)))
                    continue;
                existing.Dispose();
            }
            if (_turns.TryAdd(sessionId, turn))
                return turn;
        }
    }

    /// <summary>
    /// Launch generation on a reserved turn. The reservation is instance-checked
    /// and the launch happens at most once; generation runs on the turn CTS,
    /// independent of HTTP request cancellation.
    /// </summary>
    public void StartReserved(
        AiLiveTurn turn,
        ChatMessage? userMessage,
        Func<IServiceProvider, CancellationToken, IAsyncEnumerable<AiTurnEvent>> producer)
    {
        if (!_turns.TryGetValue(turn.SessionId, out var current) || !ReferenceEquals(current, turn))
            return;
        if (!turn.TryMarkStarted())
            return;
        _ = Task.Run(() => RunTurnAsync(turn, userMessage, producer));
    }

    /// <summary>Drop a reservation whose admission was rejected before launch.</summary>
    public void ReleaseReservation(AiLiveTurn turn)
    {
        if (_turns.TryRemove(KeyValuePair.Create(turn.SessionId, turn)))
            turn.Dispose();
    }

    /// <summary>
    /// Starts a new turn when none is running. A completed turn for the same session is replaced
    /// (confirm-plan). Returns false when a turn is already running.
    /// </summary>
    public bool TryStart(
        string sessionId,
        int storeId,
        int userId,
        ChatMessage? userMessage,
        Func<IServiceProvider, CancellationToken, IAsyncEnumerable<AiTurnEvent>> producer,
        string? executionProposalId = null)
    {
        var turn = TryReserve(sessionId, storeId, userId, executionProposalId);
        if (turn == null) return false;
        StartReserved(turn, userMessage, producer);
        return true;
    }

    public bool Stop(string sessionId, int storeId, int userId)
    {
        var turn = Find(sessionId, storeId, userId);
        return turn != null && turn.TryStop();
    }

    private async Task RunTurnAsync(
        AiLiveTurn turn,
        ChatMessage? userMessage,
        Func<IServiceProvider, CancellationToken, IAsyncEnumerable<AiTurnEvent>> producer)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetService<DataContext>();
            TurnBinding? binding = null;
            var segment = new List<AiTurnEvent>();
            try
            {
                if (db != null)
                {
                    // Persistence is not cancelled by Stop: a stop that lands before
                    // the first token must still leave the user line and an empty
                    // stopped assistant row behind.
                    await AiConversationTurnStore.AppendUserIfNeededAsync(
                        db, turn.SessionId, turn.StoreId, turn.UserId, userMessage, CancellationToken.None);

                    // Reserve the canonical assistant row BEFORE the first event:
                    // chat inserts an empty running row; execution re-binds the exact
                    // confirmed proposal it targets.
                    var phase = turn.ExecutionProposalId != null ? "execution" : "chat";
                    binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
                        db, turn.SessionId, turn.StoreId, turn.UserId, turn.RunId,
                        phase, turn.ExecutionProposalId, null, CancellationToken.None);
                    turn.BindMessage(binding.MessageId, binding.DbId, binding.Phase, binding.ProposalId);
                    turn.Publish(AiTurnEvent.Create(SseEventType.MessageStart, new { }));
                }

                await foreach (var item in producer(sp, turn.Cts.Token).WithCancellation(turn.Cts.Token))
                {
                    if (db != null && binding != null && item.Type == SseEventType.AssistantStart)
                    {
                        // Execution/review boundary: persist the finished segment first,
                        // then begin the fresh review row, then deliver the marker.
                        await AiConversationTurnStore.PersistAssistantTurnAsync(
                            db, turn.SessionId, turn.StoreId, turn.UserId,
                            binding, AiSseTurnRecorder.FoldEvents(segment), CancellationToken.None);
                        segment.Clear();
                        var parent = item.Data.ValueKind == System.Text.Json.JsonValueKind.Object
                            && item.Data.TryGetProperty("executionProposalId", out var p)
                            ? p.GetString()
                            : null;
                        // Detach the terminal binding BEFORE the new row begins:
                        // a failed review admission must never write back into
                        // the completed segment.
                        binding = null;
                        var pendingReviewId = Guid.NewGuid().ToString("N");
                        try
                        {
                            binding = await AiConversationTurnStore.BeginAssistantTurnAsync(
                                db, turn.SessionId, turn.StoreId, turn.UserId, turn.RunId,
                                "review", null, parent, CancellationToken.None, pendingReviewId);
                            turn.BindMessage(binding.MessageId, binding.DbId, binding.Phase, null);
                        }
                        catch (Exception beginError)
                        {
                            _logger.LogWarning(beginError,
                                "Failed to reserve canonical review row for session {SessionId}",
                                turn.SessionId);
                            // The review turn gets an ephemeral, unpersisted
                            // identity (DbId null); its parent link stays on the
                            // error marker, and nothing reopens segment A.
                            turn.BindMessage(pendingReviewId, null, "review", null);
                            segment.Add(turn.Publish(item));
                            segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Error,
                                new
                                {
                                    message = AgentToolResult.TurnInterruptedMessage,
                                    executionProposalId = parent,
                                })));
                            segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Done,
                                new { interrupted = true })));
                            // The review admission failed terminally: stop
                            // consuming the producer instead of running further
                            // model/tool events against a row that never began.
                            break;
                        }
                    }
                    var stamped = turn.Publish(item);
                    segment.Add(stamped);
                }
            }
            catch (OperationCanceledException) when (turn.Cts.IsCancellationRequested)
            {
                // Stop or process shutdown — persist whatever streamed so far.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live AI turn failed for session {SessionId}", turn.SessionId);
                segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Error,
                    new { message = AgentToolResult.TurnInterruptedMessage })));
                segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Done, new { interrupted = true })));
            }
            finally
            {
                // Closing an HTTP subscription is not Stop. Only an explicit user
                // stop gets a durable terminal marker, including an empty reply.
                var snapshot = AiSseTurnRecorder.FoldEvents(segment);
                if (!snapshot.TurnEnded)
                {
                    if (turn.StopRequested)
                    {
                        segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Stopped, new { })));
                    }
                    else
                    {
                        // Normal EOF without a terminal event: the outcome is unknown,
                        // never "nothing changed" and never planExecuted.
                        segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Error,
                            new { message = AgentToolResult.TurnInterruptedMessage })));
                        segment.Add(turn.Publish(AiTurnEvent.Create(SseEventType.Done, new { interrupted = true })));
                    }
                    snapshot = AiSseTurnRecorder.FoldEvents(segment);
                }
                if (db != null && binding != null)
                {
                    try
                    {
                        // Only the current segment is finalized here; earlier segments
                        // were already persisted at their assistant_start boundary.
                        await AiConversationTurnStore.PersistAssistantTurnAsync(
                            db, turn.SessionId, turn.StoreId, turn.UserId,
                            binding, snapshot, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist live AI turn {SessionId}", turn.SessionId);
                    }
                }

                turn.Complete();
                _ = DropAfterAsync(turn);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live AI turn host crashed for session {SessionId}", turn.SessionId);
            turn.Complete();
        }
    }

    private async Task DropAfterAsync(AiLiveTurn turn)
    {
        try
        {
            if (_completedLinger > TimeSpan.Zero)
                await Task.Delay(_completedLinger);
        }
        catch (Exception)
        {
            /* ignore */
        }

        if (_turns.TryRemove(KeyValuePair.Create(turn.SessionId, turn)))
            turn.Dispose();
    }
}
