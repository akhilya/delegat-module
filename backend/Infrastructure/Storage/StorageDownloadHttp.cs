using Microsoft.AspNetCore.Http.Features;

namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Prepares an HTTP response for proxying an object from storage.
/// Compression + a predetermined Content-Length break MinIO streaming
/// (HttpClient CopyToContentLengthAsync vs gzip wrapper).
/// </summary>
public static class StorageDownloadHttp
{
    public enum RangeKind
    {
        Ignore,
        Partial,
        Unsatisfiable
    }

    public readonly record struct ByteRange(long Offset, long Length, long End);

    public readonly record struct RangeResult(RangeKind Kind, ByteRange Range)
    {
        public static RangeResult Ignore => new(RangeKind.Ignore, default);
        public static RangeResult Unsatisfiable => new(RangeKind.Unsatisfiable, default);
    }

    /// <summary>Parses a single byte range. Malformed or unsupported headers are ignored.</summary>
    public static RangeResult ParseRange(string? header, long resourceLength)
    {
        if (string.IsNullOrWhiteSpace(header))
            return RangeResult.Ignore;

        var value = header.Trim();
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return RangeResult.Ignore;

        var range = value[6..].Trim();
        if (range.Length == 0 || range.Contains(','))
            return RangeResult.Ignore;

        var separator = range.IndexOf('-');
        if (separator < 0 || separator != range.LastIndexOf('-'))
            return RangeResult.Ignore;

        var first = range[..separator];
        var last = range[(separator + 1)..];
        if (resourceLength < 0)
            resourceLength = 0;

        if (first.Length == 0)
        {
            if (!long.TryParse(last, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var suffixLength))
                return RangeResult.Ignore;
            if (suffixLength == 0 || resourceLength == 0)
                return RangeResult.Unsatisfiable;
            var offset = suffixLength >= resourceLength ? 0 : resourceLength - suffixLength;
            return new RangeResult(
                RangeKind.Partial,
                new ByteRange(offset, resourceLength - offset, resourceLength - 1));
        }

        if (!long.TryParse(first, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var start))
            return RangeResult.Ignore;
        if (resourceLength == 0 || start >= resourceLength)
            return RangeResult.Unsatisfiable;

        long end;
        if (last.Length == 0)
        {
            end = resourceLength - 1;
        }
        else
        {
            if (!long.TryParse(last, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var requestedEnd))
                return RangeResult.Ignore;
            if (requestedEnd < start)
                return RangeResult.Unsatisfiable;
            end = Math.Min(requestedEnd, resourceLength - 1);
        }

        return new RangeResult(
            RangeKind.Partial,
            new ByteRange(start, end - start + 1, end));
    }

    public static void Prepare(HttpResponse response, string contentType, string fileName, bool inline, long? contentLength = null)
    {
        var http = response.HttpContext;
        var compression = http.Features.Get<IHttpsCompressionFeature>();
        if (compression != null)
            compression.Mode = HttpsCompressionMode.DoNotCompress;
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var disposition = inline ? "inline" : "attachment";
        var encodedFileName = Uri.EscapeDataString(fileName);

        response.ContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType;
        if (contentLength is >= 0)
            response.ContentLength = contentLength;
        response.Headers.ContentDisposition =
            $"{disposition}; filename=\"{encodedFileName}\"; filename*=UTF-8''{encodedFileName}";
    }

    /// <summary>Writes an authorized storage object with single-range and HEAD support.</summary>
    public static async Task StreamAsync(
        HttpRequest request,
        HttpResponse response,
        IStorageService storage,
        string path,
        string contentType,
        string fileName,
        bool inline,
        long contentLength,
        CancellationToken ct = default)
    {
        response.Headers["Accept-Ranges"] = "bytes";
        var range = ParseRange(request.Headers.Range.ToString(), contentLength);
        if (range.Kind == RangeKind.Unsatisfiable)
        {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers["Content-Range"] = $"bytes */{Math.Max(contentLength, 0)}";
            response.ContentLength = 0;
            return;
        }

        if (range.Kind == RangeKind.Partial)
        {
            var requested = range.Range;
            response.StatusCode = StatusCodes.Status206PartialContent;
            response.Headers["Content-Range"] = $"bytes {requested.Offset}-{requested.End}/{contentLength}";
            Prepare(response, contentType, fileName, inline, requested.Length);
            if (HttpMethods.IsHead(request.Method)) return;
            await storage.StreamRangeToAsync(path, requested.Offset, requested.Length, response.Body, ct);
            return;
        }

        response.StatusCode = StatusCodes.Status200OK;
        Prepare(response, contentType, fileName, inline, contentLength);
        if (HttpMethods.IsHead(request.Method)) return;
        await storage.StreamToAsync(path, response.Body, ct);
    }
}
