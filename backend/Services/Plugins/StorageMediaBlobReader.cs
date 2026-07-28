using Delegat.Infrastructure.Storage;
using Delegat.Marketplace.SDK;

namespace Delegat.Services.Plugins;

/// <summary>
/// Reads product media from Delegat object storage for marketplace plugins.
/// </summary>
public sealed class StorageMediaBlobReader : IMediaBlobReader
{
    private readonly IStorageService _storage;

    public StorageMediaBlobReader(IStorageService storage)
    {
        _storage = storage;
    }

    public async Task<byte[]?> ReadAllBytesAsync(string storagePath, int maxBytes, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || maxBytes <= 0)
            return null;

        await using var stream = await _storage.GetStreamAsync(storagePath, ct);
        if (stream == null)
            return null;

        if (stream.CanSeek)
        {
            if (stream.Length > maxBytes)
                return null;
            var buffered = new byte[stream.Length];
            var read = await stream.ReadAsync(buffered.AsMemory(0, buffered.Length), ct);
            if (read != buffered.Length)
                return null;
            return buffered;
        }

        using var ms = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            total += n;
            if (total > maxBytes)
                return null;
            await ms.WriteAsync(buffer.AsMemory(0, n), ct);
        }

        return ms.ToArray();
    }
}
