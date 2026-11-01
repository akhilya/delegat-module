namespace Delegat.Infrastructure.Storage;

internal static class StorageRangeCopy
{
    private const int BufferSize = 81920;

    public static async Task CopyExactlyAsync(Stream source, Stream destination, long length, CancellationToken ct)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        var buffer = new byte[BufferSize];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct);
            if (read == 0)
                throw new EndOfStreamException("Storage ended before the requested byte range was complete");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            remaining -= read;
        }
    }
}
