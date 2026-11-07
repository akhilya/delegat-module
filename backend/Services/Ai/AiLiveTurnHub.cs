using System.Collections.Concurrent;
using Delegat.Models.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Services.Ai;

/// <summary>
/// Process-local live cabinet turns. Generation is owned by a hub CTS, not the browser SSE request.
/// Reloads subscribe with <c>GET /api/ai/chat/stream</c>. Lost when the API process restarts.
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
    /// Starts a new turn when none is running. A completed turn for the same session is replaced
    /// (confirm-plan). Returns false when a turn is already running.
    /// </summary>
    public bool TryStart(
        string sessionId,
        int storeId,
        int userId,
        ChatMessage? userMessage,
        Func<IServiceProvider, CancellationToken, IAsyncEnumerable<string>> producer)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;

        if (_turns.TryGetValue(sessionId, out var existing))
        {
            if (!existing.BelongsTo(storeId, userId)) return false;
            if (!existing.IsCompleted) return false;
            if (_turns.TryRemove(KeyValuePair.Create(sessionId, existing)))
                existing.Dispose();
        }

        var turn = new AiLiveTurn(sessionId, storeId, userId);
        if (!_turns.TryAdd(sessionId, turn))
        {
            turn.Dispose();
            return false;
        }

        _ = Task.Run(() => RunTurnAsync(turn, userMessage, producer));
        return true;
    }

    public bool Stop(string sessionId, int storeId, int userId)
    {
        var turn = Find(sessionId, storeId, userId);
        if (turn == null || turn.IsCompleted) return false;
        if (AiSseTurnRecorder.FoldTurns(turn.SnapshotChunks())[^1].TurnEnded) return false;
        turn.Stop();
        return true;
    }

    private async Task RunTurnAsync(
        AiLiveTurn turn,
        ChatMessage? userMessage,
        Func<IServiceProvider, CancellationToken, IAsyncEnumerable<string>> producer)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetService<DataContext>();
            try
            {
                if (db != null)
                    await AiConversationTurnStore.AppendUserIfNeededAsync(
                        db, turn.SessionId, turn.StoreId, turn.UserId, userMessage, turn.Cts.Token);

                await foreach (var chunk in producer(sp, turn.Cts.Token).WithCancellation(turn.Cts.Token))
                    turn.Publish(chunk);
            }
            catch (OperationCanceledException) when (turn.Cts.IsCancellationRequested)
            {
                // Stop or process shutdown — persist whatever streamed so far.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live AI turn failed for session {SessionId}", turn.SessionId);
                turn.Publish(AiSseTurnRecorder.Format(SseEventType.Error, new { message = AgentToolResult.TurnInterruptedMessage }));
                turn.Publish(AiSseTurnRecorder.Format(SseEventType.Done, new { }));
            }
            finally
            {
                // Closing an HTTP subscription is not Stop. Only an explicit user
                // stop gets a durable terminal marker, including an empty reply.
                var snapshots = AiSseTurnRecorder.FoldTurns(turn.SnapshotChunks());
                if (turn.StopRequested && !snapshots[^1].TurnEnded)
                {
                    turn.Publish(AiSseTurnRecorder.Format(SseEventType.Stopped, new { }));
                    snapshots = AiSseTurnRecorder.FoldTurns(turn.SnapshotChunks());
                }
                if (db != null)
                {
                    try
                    {
                        await AiConversationTurnStore.UpsertAssistantAsync(
                            db, turn.SessionId, turn.StoreId, turn.UserId, snapshots[0], CancellationToken.None);
                        foreach (var followup in snapshots.Skip(1))
                            await AiConversationTurnStore.AppendAssistantAsync(
                                db, turn.SessionId, turn.StoreId, turn.UserId, followup, CancellationToken.None);
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
