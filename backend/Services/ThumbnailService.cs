using System.Collections.Concurrent;
using Delegat.Infrastructure.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Delegat.Services;

public class ThumbnailService
{
    private static readonly int[] AllowedWidths = [64, 128, 256, 512];
    private const string ThumbnailPrefix = "_thumbnails";
    private const int WebPQuality = 80;

    private readonly IStorageService _storage;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ThumbnailService(IStorageService storage, ILogger<ThumbnailService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public static bool IsAllowedWidth(int width) => AllowedWidths.Contains(width);

    public async Task<Stream?> GetOrGenerateAsync(string originalPath, int width, CancellationToken ct = default)
    {
        if (!IsAllowedWidth(width))
            return null;

        var thumbPath = GetThumbnailPath(originalPath, width);

        // Fast path: try cached thumbnail without a separate Exists/Stat round-trip.
        var cached = await _storage.GetStreamAsync(thumbPath, ct);
        if (cached != null)
            return cached;

        // Per-path lock to prevent duplicate generation
        var lockKey = thumbPath;
        var semaphore = _locks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock (another waiter may have generated it)
            cached = await _storage.GetStreamAsync(thumbPath, ct);
            if (cached != null)
                return cached;

            // Download original
            using var originalStream = await _storage.GetStreamAsync(originalPath, ct);
            if (originalStream is null)
                return null;

            // Resize to WebP (AutoOrient applies EXIF rotation to pixels)
            using var image = await Image.LoadAsync(originalStream, ct);
            image.Mutate(x => x
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Size = new Size(width, 0),
                    Mode = ResizeMode.Max,
                }));

            var outputStream = new MemoryStream();
            await image.SaveAsWebpAsync(outputStream, new WebpEncoder { Quality = WebPQuality }, ct);
            outputStream.Position = 0;

            // Store in MinIO cache
            var length = outputStream.Length;
            await _storage.UploadAsync(outputStream, thumbPath, length, "image/webp", ct);
            outputStream.Position = 0;

            return outputStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for {Path} at width {Width}", originalPath, width);
            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<string?> GetOrGenerateAsBase64Async(string originalPath, int width, CancellationToken ct = default)
    {
        using var stream = await GetOrGenerateAsync(originalPath, width, ct);
        if (stream is null)
            return null;

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return $"data:image/webp;base64,{Convert.ToBase64String(ms.ToArray())}";
    }

    public async Task DeleteThumbnailsAsync(string originalPath)
    {
        foreach (var width in AllowedWidths)
        {
            var thumbPath = GetThumbnailPath(originalPath, width);
            await _storage.DeleteAsync(thumbPath);
        }
    }

    private static string GetThumbnailPath(string originalPath, int width)
        => $"{ThumbnailPrefix}/{width}/{originalPath}";
}
