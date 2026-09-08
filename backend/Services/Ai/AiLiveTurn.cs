using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Delegat.Services.Ai;

/// <summary>One in-memory cabinet-chat generation, independent of any HTTP subscriber.</summary>
public sealed class AiLiveTurn : IDisposable
{
    private readonly object _gate = new();
    private readonly List<string> _chunks = [];
    private readonly List<ChannelWriter<string>> _listeners = [];
    private bool _completed;
    private bool _disposed;

    public AiLiveTurn(string sessionId, int storeId, int userId)
    {
        SessionId = sessionId;
        StoreId = storeId;
        UserId = userId;
        Cts = new CancellationTokenSource();
    }

    public string SessionId { get; }
    public int StoreId { get; }
    public int UserId { get; }
    public CancellationTokenSource Cts { get; }
    public bool IsCompleted
    {
        get { lock (_gate) return _completed; }
    }

    public IReadOnlyList<string> SnapshotChunks()
    {
        lock (_gate)
            return _chunks.ToArray();
    }

    public bool BelongsTo(int storeId, int userId) => StoreId == storeId && UserId == userId;

    public void Publish(string chunk)
    {
        ChannelWriter<string>[] writers;
        lock (_gate)
        {
            if (_completed) return;
            _chunks.Add(chunk);
            writers = _listeners.ToArray();
        }

        foreach (var writer in writers)
            writer.TryWrite(chunk);
    }

    public void Complete()
    {
        ChannelWriter<string>[] writers;
        lock (_gate)
        {
            if (_completed) return;
            _completed = true;
            writers = _listeners.ToArray();
            _listeners.Clear();
        }

        foreach (var writer in writers)
            writer.TryComplete();
    }

    public void Stop()
    {
        try { Cts.Cancel(); }
        catch (ObjectDisposedException) { /* already finished */ }
    }

    public async IAsyncEnumerable<string> Subscribe([EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        string[] replay;
        lock (_gate)
        {
            _listeners.Add(channel.Writer);
            replay = _chunks.ToArray();
            if (_completed)
                channel.Writer.TryComplete();
        }

        try
        {
            foreach (var chunk in replay)
                yield return chunk;

            await foreach (var chunk in channel.Reader.ReadAllAsync(ct))
                yield return chunk;
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
