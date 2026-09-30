using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Delegat.Services.Marketplace;

public sealed class LinkVerifyWork
{
    public required string OperationId { get; init; }
    public required int ConnectionId { get; init; }
    public required int StoreId { get; init; }
    public required int UserId { get; init; }
    public required string ConnectionName { get; init; }
}

/// <summary>
/// One in-flight catalog check per connection. The HTTP request only enqueues it.
/// </summary>
public sealed class LinkVerifyQueue
{
    public const string OperationType = "link-verify";

    private readonly Channel<LinkVerifyWork> _channel = Channel.CreateUnbounded<LinkVerifyWork>();
    private readonly ConcurrentDictionary<int, string> _active = new();

    public string? ActiveOperationId(int connectionId) =>
        _active.TryGetValue(connectionId, out var operationId) ? operationId : null;

    public bool TryEnqueue(LinkVerifyWork work)
    {
        if (!_active.TryAdd(work.ConnectionId, work.OperationId))
            return false;
        if (_channel.Writer.TryWrite(work))
            return true;
        _active.TryRemove(work.ConnectionId, out _);
        return false;
    }

    public void Complete(int connectionId) => _active.TryRemove(connectionId, out _);

    public ChannelReader<LinkVerifyWork> Reader => _channel.Reader;
}
