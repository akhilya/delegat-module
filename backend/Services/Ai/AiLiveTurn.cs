using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Delegat.Services.Ai;

/// <summary>
/// One in-memory cabinet-chat generation, independent of any HTTP subscriber.
/// Events are buffered as canonical <see cref="AiTurnEvent"/>; Publish stamps
/// trusted ownership and sequence under the same lock that appends and delivers,
/// so a subscriber can never observe completion before the final event.
/// The buffer is process-local: a restart loses the live turn and nothing replays.
/// </summary>
public sealed class AiLiveTurn : IDisposable
{
    private readonly object _gate = new();
    private readonly List<AiTurnEvent> _events = [];
    private readonly List<ChannelWriter<AiTurnEvent>> _listeners = [];
    private bool _completed;
    private bool _disposed;
    private bool _stopRequested;
    private bool _segmentTerminal;
    private long _sequence;
    private string? _messageId;
    private int? _dbId;
    private string? _phase;
    private string? _proposalId;
    private readonly string? _executionProposalId;
    private int _started;

    public AiLiveTurn(string sessionId, int storeId, int userId, string? executionProposalId = null)
    {
        SessionId = sessionId;
        StoreId = storeId;
        UserId = userId;
        RunId = Guid.NewGuid().ToString("N");
        _executionProposalId = executionProposalId;
        _proposalId = executionProposalId;
        Cts = new CancellationTokenSource();
    }

    public string SessionId { get; }
    public int StoreId { get; }
    public int UserId { get; }
    public string RunId { get; }
    public CancellationTokenSource Cts { get; }

    /// <summary>The proposal this run executes, when the run is an execution turn.</summary>
    public string? ExecutionProposalId => _executionProposalId;

    public string? MessageId
    {
        get { lock (_gate) return _messageId; }
    }

    public bool StopRequested
    {
        get { lock (_gate) return _stopRequested; }
    }
    public bool IsCompleted
    {
        get { lock (_gate) return _completed; }
    }

    /// <summary>Set exactly once by the hub when generation launches; a reserved
    /// turn can never start twice.</summary>
    internal bool TryMarkStarted() => Interlocked.Exchange(ref _started, 1) == 0;

    public IReadOnlyList<AiTurnEvent> SnapshotEvents()
    {
        lock (_gate)
            return _events.ToArray();
    }

    /// <summary>Compatibility view for existing tests/legacy callers only.</summary>
    public IReadOnlyList<string> SnapshotChunks()
    {
        lock (_gate)
            return _events.Select(e => e.ToSse()).ToArray();
    }

    public bool BelongsTo(int storeId, int userId) => StoreId == storeId && UserId == userId;

    /// <summary>
    /// Point new events at a different canonical message (execution -> review
    /// boundary). Every subsequent Publish carries this binding until re-bound.
    /// </summary>
    public void BindMessage(string messageId, int? dbId, string phase, string? proposalId = null)
    {
        lock (_gate)
        {
            _messageId = messageId;
            _dbId = dbId;
            _phase = phase;
            _proposalId = proposalId;
        }
    }

    /// <summary>
    /// Stamp trusted ownership onto the producer's event, buffer it, and deliver
    /// it to every listener - all under one lock so ordering is the order of
    /// publication. Producer-supplied identity fields are ignored.
    /// </summary>
    public AiTurnEvent Publish(AiTurnEvent item)
    {
        lock (_gate)
        {
            if (_completed) return item;
            var stamped = item with
            {
                RunId = RunId,
                MessageId = _messageId,
                DbId = _dbId,
                Phase = _phase,
                ProposalId = _proposalId,
                Sequence = ++_sequence,
            };
            _events.Add(stamped);
            if (item.Type is SseEventType.AssistantStart or SseEventType.MessageStart)
                _segmentTerminal = false;
            else if (item.Type is SseEventType.Done or SseEventType.Error or SseEventType.Stopped)
                _segmentTerminal = true;
            foreach (var writer in _listeners)
                writer.TryWrite(stamped);
            return stamped;
        }
    }

    public void Complete()
    {
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            foreach (var writer in _listeners)
                writer.TryComplete();
            _listeners.Clear();
        }
    }

    /// <summary>
    /// Explicit user stop. Refuses when the run is done or the current segment
    /// already reached a terminal event - a stop racing the final done must not
    /// rewrite a completed outcome.
    /// </summary>
    public bool TryStop()
    {
        lock (_gate)
        {
            if (_completed || _stopRequested || _segmentTerminal) return false;
            _stopRequested = true;
        }
        try { Cts.Cancel(); }
        catch (ObjectDisposedException) { /* already finished */ }
        return true;
    }

    /// <summary>Legacy entry point kept for existing callers.</summary>
    public void Stop() => TryStop();

    /// <summary>
    /// Replay buffered events then tail the live channel. A matching
    /// <paramref name="runId"/> plus <paramref name="afterSequence"/> replays only
    /// strictly later events; a different or missing run, or a cursor outside the
    /// buffer, replays the full current run.
    /// </summary>
    public async IAsyncEnumerable<string> Subscribe(
        [EnumeratorCancellation] CancellationToken ct = default,
        string? runId = null,
        long afterSequence = 0)
    {
        var channel = Channel.CreateUnbounded<AiTurnEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        AiTurnEvent[] replay;
        lock (_gate)
        {
            _listeners.Add(channel.Writer);
            // A cursor only applies when its runId proves this very run; a
            // missing or foreign run replays the full buffer.
            var sameRun = string.Equals(runId, RunId, StringComparison.Ordinal);
            var floor = sameRun && afterSequence >= 0 && afterSequence <= _sequence
                ? afterSequence
                : 0;
            replay = _events.Where(e => e.Sequence > floor).ToArray();
            if (_completed)
                channel.Writer.TryComplete();
        }

        try
        {
            foreach (var item in replay)
                yield return item.ToSse();

            await foreach (var item in channel.Reader.ReadAllAsync(ct))
                yield return item.ToSse();
        }
        finally
        {
            lock (_gate)
                _listeners.Remove(channel.Writer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Complete();
        Cts.Dispose();
    }
}
