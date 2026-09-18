using Microsoft.AspNetCore.Http.Features;

namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Prepares an HTTP response for proxying an object from storage.
/// Compression + a predetermined Content-Length break MinIO streaming
/// (HttpClient CopyToContentLengthAsync vs gzip wrapper).
/// </summary>
public static class StorageDownloadHttp
{
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
        if (contentLength is > 0)
            response.ContentLength = contentLength;
        response.Headers.ContentDisposition =
            $"{disposition}; filename=\"{encodedFileName}\"; filename*=UTF-8''{encodedFileName}";
    }
}
