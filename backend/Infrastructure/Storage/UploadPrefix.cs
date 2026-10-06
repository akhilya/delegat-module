namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Peeks at an upload so a small file can be stored in one request.
/// A longer file keeps this prefix and continues from the same stream.
/// </summary>
internal static class UploadPrefix
{
    /// <summary>MinIO single-part PUT accepts an object smaller than 5 MiB.</summary>
    public const int SinglePutMaxBytes = 5 * 1024 * 1024 - 1;

    public static async Task<(byte[] Data, bool FitsSinglePut)> ReadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[SinglePutMaxBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0)
                break;
            read += n;
        }

        if (read <= SinglePutMaxBytes)
            return (buffer[..read], true);

        return (buffer[..read], false);
    }
}

/// <summary>Reads a buffered prefix, then the unread tail of the original stream.</summary>
internal sealed class PrefixThenRemainingStream : Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _rest;
    private int _offset;

    public PrefixThenRemainingStream(byte[] prefix, Stream rest)
    {
        _prefix = prefix;
        _rest = rest;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset < _prefix.Length)
        {
            var count = Math.Min(buffer.Length, _prefix.Length - _offset);
            _prefix.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            return count;
        }

        return await _rest.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class CountingReadStream : Stream
{
    private readonly Stream _inner;

    public CountingReadStream(Stream inner) => _inner = inner;

    public long Total { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken);
        Total += read;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
