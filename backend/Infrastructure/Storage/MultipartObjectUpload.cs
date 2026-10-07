using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Delegat.Infrastructure.Middleware;
using Delegat.Infrastructure.Security;

namespace Delegat.Infrastructure.Storage;

/// <summary>
/// Reads a multipart upload and stores each file as the bytes arrive.
/// Model binding would spool the whole body to disk first, then copy it again.
/// </summary>
internal static class MultipartObjectUpload
{
    public readonly record struct SavedObject(string FileName, string ContentType, string Path, long Length);

    public static async Task<List<SavedObject>> SaveAsync(
        HttpRequest request,
        IStorageService storage,
        Func<string, string> pathForFileName,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            throw ApiException.BadRequest("Expected multipart/form-data");

        var boundary = request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
            throw ApiException.BadRequest("Missing boundary in content-type header");

        var saved = new List<SavedObject>();
        var body = new ObservedRequestBody(request.Body);
        var started = Stopwatch.StartNew();
        try
        {
            var reader = new MultipartReader(boundary, body);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) != null)
            {
                if (string.IsNullOrEmpty(section.ContentDisposition))
                    continue;

                var contentDisposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
                if (!contentDisposition.IsFileDisposition())
                    continue;

                var rawName = contentDisposition.FileName.HasValue
                    ? contentDisposition.FileName.Value
                    : null;
                var fileName = FileNameSafety.Sanitize(rawName);
                var contentType = string.IsNullOrWhiteSpace(section.ContentType)
                    ? "application/octet-stream"
                    : section.ContentType;
                var path = pathForFileName(fileName);
                var knownLength = ReadPartLength(section);
                var result = await storage.UploadStreamAsync(section.Body, path, contentType, knownLength, ct);
                if (result == null)
                    throw ApiException.InternalServerError($"Failed to upload file {fileName} to storage");

                saved.Add(new SavedObject(fileName, result.ContentType, path, result.Length));
            }

            return saved;
        }
        catch (Exception ex)
        {
            foreach (var item in saved)
                await storage.DeleteAsync(item.Path, ct);

            if (!IsIncompleteMultipart(ex))
                throw;

            var detail = DescribeIncompleteUpload(request, body, saved.Count, started.ElapsedMilliseconds);
            request.HttpContext.RequestServices?.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(MultipartObjectUpload).FullName ?? nameof(MultipartObjectUpload))
                .LogWarning("{UploadFailure}", detail);
            throw new IOException(detail, ex);
        }
    }

    private static bool IsIncompleteMultipart(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is IOException && current.Message.Contains("Unexpected end of Stream", StringComparison.Ordinal))
                return true;
            if (current is InvalidDataException && current.Message.Contains("Multipart", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string DescribeIncompleteUpload(HttpRequest request, ObservedRequestBody body, int storedFiles, long elapsedMs)
    {
        var declared = request.ContentLength?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)";
        return "Upload body ended before the multipart boundary. " +
            $"Declared Content-Length: {declared}. Bytes received: {body.Total.ToString(System.Globalization.CultureInfo.InvariantCulture)}. " +
            $"Stored files before failure: {storedFiles.ToString(System.Globalization.CultureInfo.InvariantCulture)}. " +
            $"ElapsedMs: {elapsedMs.ToString(System.Globalization.CultureInfo.InvariantCulture)}. " +
            $"Protocol: {request.Protocol}. Host: {request.Host}. " +
            $"Transfer-Encoding: {Header(request, "Transfer-Encoding")}. " +
            $"Expect: {Header(request, "Expect")}. " +
            $"Connection: {Header(request, "Connection")}. " +
            $"Content-Type: {Clip(request.ContentType)}. " +
            $"ClientAborted: {request.HttpContext.RequestAborted.IsCancellationRequested}. " +
            $"PrefixHex: {body.PrefixHex}.";
    }

    private static string Header(HttpRequest request, string name)
    {
        if (!request.Headers.TryGetValue(name, out var value))
            return "(none)";
        var text = value.ToString();
        return string.IsNullOrEmpty(text) ? "(none)" : Clip(text);
    }

    private static string Clip(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "(none)";
        return value.Length <= 180 ? value : value[..180];
    }

    /// <summary>Counts bytes pulled from the request and keeps the first few for the failure log.</summary>
    private sealed class ObservedRequestBody : Stream
    {
        private const int PrefixLimit = 64;
        private readonly Stream _inner;
        private readonly byte[] _prefix = new byte[PrefixLimit];
        private int _prefixLength;

        public ObservedRequestBody(Stream inner) => _inner = inner;

        public long Total { get; private set; }

        public string PrefixHex => _prefixLength == 0
            ? "(empty)"
            : Convert.ToHexString(_prefix.AsSpan(0, _prefixLength));

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
            if (read > 0 && _prefixLength < PrefixLimit)
            {
                var take = Math.Min(read, PrefixLimit - _prefixLength);
                buffer.Span[..take].CopyTo(_prefix.AsSpan(_prefixLength));
                _prefixLength += take;
            }

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

    private static long? ReadPartLength(MultipartSection section)
    {
        if (section.Headers == null || !section.Headers.TryGetValue("Content-Length", out var lengthHeader))
            return null;
        return long.TryParse(lengthHeader.FirstOrDefault(), out var length) && length > 0
            ? length
            : null;
    }
}
