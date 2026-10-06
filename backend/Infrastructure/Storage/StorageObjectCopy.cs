using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Copies an object HTTP body to a caller stream.
/// A short read (MinIO or a proxy closed the socket before Content-Length)
/// is resumed with <c>Range</c> from the last byte already written.
/// Both HttpContent.CopyToAsync and ContentLengthReadStream.CopyToAsync
/// throw HttpIOException (ResponseEnded) instead of returning what they received.
/// </summary>
internal static class StorageObjectCopy
{
    public const int MaxAttempts = 5;
    private const int BufferSize = 81920;

    public static async Task<bool> CopyAsync(
        Func<long, CancellationToken, Task<HttpResponseMessage>> open,
        Stream destination,
        ILogger logger,
        string path,
        CancellationToken ct,
        Func<HttpResponseMessage, bool>? acceptFirst = null)
    {
        long offset = 0;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var response = await open(offset, ct);

            if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                return true;

            if (offset == 0 && acceptFirst != null && !acceptFirst(response))
                return false;

            if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                response.EnsureSuccessStatusCode();
                throw new HttpRequestException(
                    $"Storage did not resume '{path}' at byte {offset} (HTTP {(int)response.StatusCode}).");
            }

            response.EnsureSuccessStatusCode();

            var part = await CopyPartAsync(response, destination, ct);
            offset += part.Copied;
            if (part.Complete)
                return true;

            if (attempt >= MaxAttempts || ct.IsCancellationRequested)
            {
                throw new HttpIOException(
                    HttpRequestError.ResponseEnded,
                    $"Storage closed '{path}' after {offset} bytes.",
                    part.Error);
            }

            logger.LogWarning(
                part.Error,
                "Storage download of {Path} ended after {Offset} bytes (attempt {Attempt}/{Max}). Resuming.",
                path,
                offset,
                attempt,
                MaxAttempts);
            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
        }

        return true;
    }

    internal static bool IsPrematureEnd(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpIOException io && io.HttpRequestError == HttpRequestError.ResponseEnded)
                return true;
        }

        return false;
    }

    private static async Task<CopyPart> CopyPartAsync(
        HttpResponseMessage response,
        Stream destination,
        CancellationToken ct)
    {
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[BufferSize];
        long copied = 0;
        var expected = response.Content.Headers.ContentLength;

        while (expected is null || copied < expected.Value)
        {
            int read;
            try
            {
                var count = buffer.Length;
                if (expected is long remaining)
                    count = (int)Math.Min(count, remaining - copied);
                read = await source.ReadAsync(buffer.AsMemory(0, count), ct);
            }
            catch (Exception ex) when (IsPrematureEnd(ex))
            {
                return new CopyPart(copied, false, ex);
            }

            if (read == 0)
            {
                if (expected is long need && copied < need)
                {
                    return new CopyPart(
                        copied,
                        false,
                        new HttpIOException(
                            HttpRequestError.ResponseEnded,
                            "The response ended prematurely. (ResponseEnded)"));
                }

                return new CopyPart(copied, true, null);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
        }

        return new CopyPart(copied, true, null);
    }

    private readonly record struct CopyPart(long Copied, bool Complete, Exception? Error);
}
