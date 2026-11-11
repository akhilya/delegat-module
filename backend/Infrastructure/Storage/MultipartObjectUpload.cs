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
    public const int MaxProductCreateFiles = 500;
    public readonly record struct SavedObject(string FileName, string ContentType, string Path, long Length);
    public readonly record struct NamedSavedObject(string FieldName, SavedObject Object);
    public sealed record ProductCreateUpload(string Metadata, IReadOnlyList<NamedSavedObject> Files);

    public static async Task<List<SavedObject>> SaveAsync(
        HttpRequest request,
        IStorageService storage,
        Func<string, string> pathForFileName,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
            throw ApiException.BadRequest(code: "multipart_content_type_invalid");

        var boundary = request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
            throw ApiException.BadRequest(code: "multipart_boundary_missing");

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
                    throw ApiException.InternalServerError(code: "storage_upload_failed");

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

    /// <summary>
    /// Reads a product-create metadata field and streams named files to object storage.
    /// File field names are retained so the JSON payload can reference uploads by a
    /// client-generated temporary ID without relying on filenames being unique.
    /// </summary>
    public static async Task<ProductCreateUpload> SaveProductCreateAsync(
        HttpRequest request,
        IStorageService storage,
        Func<string, string, string> pathForFile,
        CancellationToken ct,
        Func<string, CancellationToken, Task>? validateMetadataAsync = null)
    {
        if (!request.HasFormContentType)
            throw ApiException.BadRequest(code: "multipart_content_type_invalid");

        var boundary = request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
            throw ApiException.BadRequest(code: "multipart_boundary_missing");

        const int maxMetadataBytes = 1_048_576;
        string? metadata = null;
        var seenFileFields = new HashSet<string>(StringComparer.Ordinal);
        var saved = new List<NamedSavedObject>();
        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            MultipartSection? section;
            var firstPart = true;
            while ((section = await reader.ReadNextSectionAsync(ct)) != null)
            {
                if (string.IsNullOrEmpty(section.ContentDisposition))
                    continue;

                var disposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
                var fieldName = Unquote(disposition.Name.HasValue ? disposition.Name.Value : null);
                if (firstPart && (disposition.IsFileDisposition() || fieldName != "product"))
                    throw ApiException.BadRequest(code: "product_metadata_required");
                firstPart = false;

                if (disposition.IsFileDisposition())
                {
                    if (saved.Count >= MaxProductCreateFiles)
                        throw ApiException.BadRequest(code: "too_many_uploaded_files");
                    if (string.IsNullOrWhiteSpace(fieldName))
                        throw ApiException.BadRequest(code: "upload_file_reference_missing");
                    if (!seenFileFields.Add(fieldName))
                        throw ApiException.BadRequest(code: "duplicate_uploaded_file_reference");

                    var rawName = disposition.FileName.HasValue ? disposition.FileName.Value : null;
                    var fileName = FileNameSafety.Sanitize(rawName);
                    var contentType = string.IsNullOrWhiteSpace(section.ContentType)
                        ? "application/octet-stream"
                        : section.ContentType;
                    var path = pathForFile(fieldName, fileName);
                    var result = await storage.UploadStreamAsync(
                        section.Body, path, contentType, ReadPartLength(section), ct);
                    if (result == null)
                    {
                        await storage.DeleteAsync(path, CancellationToken.None);
                        throw ApiException.InternalServerError(code: "storage_upload_failed");
                    }

                    saved.Add(new NamedSavedObject(
                        fieldName,
                        new SavedObject(fileName, result.ContentType, path, result.Length)));
                    continue;
                }

                if (!string.Equals(fieldName, "product", StringComparison.Ordinal))
                    continue;
                if (metadata != null)
                    throw ApiException.BadRequest(code: "product_metadata_duplicate");

                await using var buffer = new MemoryStream();
                var chunk = new byte[16 * 1024];
                while (true)
                {
                    var read = await section.Body.ReadAsync(chunk, ct);
                    if (read == 0) break;
                    if (buffer.Length + read > maxMetadataBytes)
                        throw ApiException.BadRequest(code: "product_metadata_too_large");
                    await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
                }
                metadata = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
                if (validateMetadataAsync != null)
                    await validateMetadataAsync(metadata, ct);
            }

            if (string.IsNullOrWhiteSpace(metadata))
                throw ApiException.BadRequest(code: "product_metadata_required");

            return new ProductCreateUpload(metadata, saved);
        }
        catch
        {
            foreach (var file in saved)
                await storage.DeleteAsync(file.Object.Path, CancellationToken.None);
            throw;
        }
    }

    private static string Unquote(string? value)
        => value?.Trim().Trim('"') ?? string.Empty;

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
