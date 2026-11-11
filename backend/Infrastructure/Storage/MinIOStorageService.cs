using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Delegat.Infrastructure.Configuration;
using Delegat.Infrastructure.Security;

namespace Delegat.Infrastructure.Storage;

/// <summary>
/// MinIO storage service implementation
/// Replaces Zorro.Services.MinIOService and MinIORepository
/// </summary>
public class MinIOStorageService : IStorageService
{
    private readonly IMinioClient _client;
    private readonly IMinioClient _publicClient;
    private readonly string _bucket;
    private readonly string _endpoint;
    private readonly bool _secure;
    private readonly string _publicEndpoint;
    private readonly bool _publicSecure;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MinIOStorageService> _logger;
    private bool _bucketExistsVerified;

    public string Bucket => _bucket;

    public MinIOStorageService(MinIOSettings settings, IHttpClientFactory httpClientFactory, ILogger<MinIOStorageService> logger)
    {
        _bucket = settings.Bucket;
        _endpoint = settings.Endpoint;
        _secure = settings.Secure;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Public endpoint for external URL generation (falls back to internal endpoint).
        // Host[:port] only — scheme stripped in GetMinIOSettings (NormalizeMinioEndpoint).
        _publicEndpoint = !string.IsNullOrEmpty(settings.PublicEndpoint)
            ? settings.PublicEndpoint
            : settings.Endpoint;
        _publicSecure = settings.PublicSecure ?? settings.Secure;

        _client = new MinioClient()
            .WithEndpoint(settings.Endpoint)
            .WithCredentials(settings.AccessKey, settings.SecretKey)
            .WithSSL(settings.Secure)
            .Build();

        _publicClient = new MinioClient()
            .WithEndpoint(_publicEndpoint)
            .WithCredentials(settings.AccessKey, settings.SecretKey)
            .WithSSL(_publicSecure)
            .Build();
    }

    /// <summary>
    /// Upload file from IFormFile
    /// </summary>
    public async Task<bool> UploadAsync(IFormFile file, string path, CancellationToken ct = default)
    {
        try
        {
            await EnsureBucketExistsAsync(ct);

            using var stream = file.OpenReadStream();
            var request = new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path)
                .WithObjectSize(file.Length)
                .WithStreamData(stream)
                .WithContentType(file.ContentType);

            await _client.PutObjectAsync(request, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to MinIO: {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Upload from stream
    /// </summary>
    public async Task<bool> UploadAsync(Stream stream, string path, long contentLength, string contentType, CancellationToken ct = default)
    {
        try
        {
            await EnsureBucketExistsAsync(ct);

            var request = new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path)
                .WithObjectSize(contentLength)
                .WithStreamData(stream)
                .WithContentType(contentType);

            await _client.PutObjectAsync(request, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload stream to MinIO: {Path}, ContentLength: {ContentLength}", path, contentLength);
            return false;
        }
    }

    /// <summary>
    /// Small files go in one PUT. Larger files are multipart from the bytes already read,
    /// so storage starts before the browser has finished sending the file.
    /// </summary>
    public async Task<FileUploadResult?> UploadStreamAsync(
        Stream stream,
        string path,
        string contentType,
        long? contentLength,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            contentType = "application/octet-stream";

        if (contentLength is > 0)
        {
            var uploaded = await UploadAsync(stream, path, contentLength.Value, contentType, ct);
            return uploaded ? new FileUploadResult(contentType, contentLength.Value) : null;
        }

        try
        {
            await EnsureBucketExistsAsync(ct);
            var (prefix, fitsSinglePut) = await UploadPrefix.ReadAsync(stream, ct);
            if (fitsSinglePut)
            {
                using var buffered = new MemoryStream(prefix, writable: false);
                var uploaded = await UploadAsync(buffered, path, prefix.Length, contentType, ct);
                return uploaded ? new FileUploadResult(contentType, prefix.Length) : null;
            }

            var counting = new CountingReadStream(new PrefixThenRemainingStream(prefix, stream));
            var request = new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path)
                .WithObjectSize(-1)
                .WithStreamData(counting)
                .WithContentType(contentType);
            await _client.PutObjectAsync(request, ct);
            return new FileUploadResult(contentType, counting.Total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stream upload to MinIO: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Upload from URI (download and upload)
    /// </summary>
    public async Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default)
    {
        var result = await UploadFromUriWithMetadataAsync(uri, path, ct);
        return result != null;
    }

    /// <summary>
    /// Upload from URI and return metadata.
    /// Includes retry logic with exponential backoff for rate-limited scenarios.
    /// </summary>
    public async Task<FileUploadResult?> UploadFromUriWithMetadataAsync(string uri, string path, CancellationToken ct = default)
    {
        if (!UrlSafety.TryValidateHttpUrl(uri, out var safeUri, out _))
            return null;

        const int maxRetries = 3;
        const int baseDelayMs = 1000;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await EnsureBucketExistsAsync(ct);

                using var httpClient = _httpClientFactory.CreateClient("MinIOStorage");

                using var response = await httpClient.GetAsync(safeUri, ct);

                // Check for rate limiting (429 Too Many Requests)
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < maxRetries - 1)
                    {
                        var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                        await Task.Delay(delayMs, ct);
                        continue;
                    }
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                var contentLength = response.Content.Headers.ContentLength ?? 0;

                using var stream = await response.Content.ReadAsStreamAsync(ct);

                var request = new PutObjectArgs()
                    .WithBucket(_bucket)
                    .WithObject(path)
                    .WithObjectSize(contentLength)
                    .WithStreamData(stream)
                    .WithContentType(contentType);

                await _client.PutObjectAsync(request, ct);
                return new FileUploadResult(contentType, contentLength);
            }
            catch (HttpRequestException) when (attempt < maxRetries - 1)
            {
                // Network error - retry with backoff
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delayMs, ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxRetries - 1)
            {
                // Timeout - retry with backoff
                var delayMs = baseDelayMs * (int)Math.Pow(2, attempt);
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Safely downloads a direct video asset with redirect and size limits.</summary>
    public async Task<FileUploadResult?> UploadVideoFromUriWithMetadataAsync(
        string uri,
        string path,
        long maxBytes,
        CancellationToken ct = default)
    {
        if (maxBytes <= 0 || !UrlSafety.TryValidateHttpUrl(uri, out var currentUri, out _))
            return null;

        const int maxRedirects = 5;
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("MinIOStorage");
            for (var redirect = 0; redirect <= maxRedirects; redirect++)
            {
                // Validate every hop; the named client has automatic redirects disabled.
                if (!UrlSafety.TryValidateHttpUrl(currentUri.AbsoluteUri, out currentUri, out _))
                    return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                using var response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == maxRedirects || response.Headers.Location == null)
                        return null;
                    currentUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength > maxBytes)
                    return null;

                await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
                await using var bounded = new MaxBytesReadStream(responseStream, maxBytes);
                var signature = new byte[16];
                var signatureLength = 0;
                while (signatureLength < signature.Length)
                {
                    var read = await bounded.ReadAsync(signature.AsMemory(signatureLength), ct);
                    if (read == 0) break;
                    signatureLength += read;
                }

                if (!TryGetVideoContentType(
                        response.Content.Headers.ContentType?.MediaType,
                        signature.AsSpan(0, signatureLength),
                        out var contentType))
                    return null;

                await using var replay = new PrefixReadStream(signature.AsMemory(0, signatureLength).ToArray(), bounded);
                var uploaded = await UploadStreamAsync(replay, path, contentType, declaredLength, ct);
                if (uploaded == null)
                    await DeleteAsync(path, CancellationToken.None);
                return uploaded;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DeleteAsync(path, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Safe video download failed for storage object {Path}", path);
            await DeleteAsync(path, CancellationToken.None);
        }

        return null;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    internal static bool TryGetVideoContentType(string? declared, ReadOnlySpan<byte> prefix, out string contentType)
    {
        contentType = "";
        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix.Slice(8, 4).SequenceEqual("AVI "u8))
            contentType = "video/x-msvideo";
        else if (prefix.Length >= 8 && prefix.Slice(4, 4).SequenceEqual("ftyp"u8))
            contentType = "video/mp4";
        else if (prefix.Length >= 4 && prefix[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
            contentType = "video/webm";

        if (contentType.Length == 0)
            return false;
        return string.IsNullOrWhiteSpace(declared)
            || declared.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || declared.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MaxBytesReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0) return 0;
            if (_read == maxBytes)
            {
                var probe = new byte[1];
                if (await inner.ReadAsync(probe, cancellationToken) > 0)
                    throw new InvalidDataException("Video exceeds the configured download limit.");
                return 0;
            }
            var allowed = (int)Math.Min(buffer.Length, maxBytes - _read);
            var count = await inner.ReadAsync(buffer[..allowed], cancellationToken);
            _read += count;
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    }

    private sealed class PrefixReadStream(byte[] prefix, Stream remainder) : Stream
    {
        private int _offset;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - _offset);
                prefix.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return ValueTask.FromResult(count);
            }
            return remainder.ReadAsync(buffer, cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Server-side copy from one object key to another in the same bucket.
    /// Uses MinIO CopyObject API - payload doesn't transit our backend.
    /// </summary>
    public async Task<bool> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        try
        {
            await EnsureBucketExistsAsync(ct);

            var copySource = new CopySourceObjectArgs()
                .WithBucket(_bucket)
                .WithObject(sourcePath);

            var copyArgs = new CopyObjectArgs()
                .WithBucket(_bucket)
                .WithObject(destinationPath)
                .WithCopyObjectSource(copySource);

            await _client.CopyObjectAsync(copyArgs, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy object in MinIO: {Source} -> {Dest}", sourcePath, destinationPath);
            return false;
        }
    }

    /// <summary>
    /// Delete a file
    /// </summary>
    public async Task<bool> DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var request = new RemoveObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path);

            await _client.RemoveObjectAsync(request, ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Max bytes buffered into memory by <see cref="GetStreamAsync"/>.
    /// Larger objects must use <see cref="StreamToAsync"/>.
    /// </summary>
    public const long MaxBufferedDownloadBytes = 32L * 1024 * 1024;

    /// <summary>
    /// Download a file as a seekable in-memory stream (capped).
    /// Returns null if missing, failed, or larger than <see cref="MaxBufferedDownloadBytes"/>.
    /// </summary>
    public async Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var ms = new MemoryStream();
            var copied = await StorageObjectCopy.CopyAsync(
                (offset, token) => OpenObjectGetAsync(path, offset, token),
                ms,
                _logger,
                path,
                ct,
                response =>
                {
                    if (!response.IsSuccessStatusCode)
                        return false;

                    var contentLength = response.Content.Headers.ContentLength;
                    if (contentLength is > MaxBufferedDownloadBytes)
                    {
                        _logger.LogWarning(
                            "Skipping buffered MinIO download for {Path}: Content-Length {Length} exceeds {Max} bytes; use StreamToAsync",
                            path, contentLength, MaxBufferedDownloadBytes);
                        return false;
                    }

                    if (contentLength is > 0 and <= int.MaxValue)
                        ms.Capacity = (int)contentLength.Value;
                    return true;
                });
            if (!copied)
                return null;

            if (ms.Length > MaxBufferedDownloadBytes)
            {
                _logger.LogWarning(
                    "Skipping buffered MinIO download for {Path}: actual size {Length} exceeds {Max} bytes",
                    path, ms.Length, MaxBufferedDownloadBytes);
                return null;
            }

            ms.Position = 0;
            return ms;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from MinIO: {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Check if file exists
    /// </summary>
    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var request = new StatObjectArgs()
                .WithBucket(_bucket)
                .WithObject(path);

            await _client.StatObjectAsync(request, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get full public URL for a file.
    /// Uses PublicEndpoint if configured, otherwise falls back to Endpoint.
    /// </summary>
    public string GetFullPath(string path)
    {
        var protocol = _publicSecure ? "https" : "http";
        return $"{protocol}://{_publicEndpoint}/{_bucket}/{path}";
    }

    /// <summary>
    /// Stream a file directly to an output stream without buffering in memory.
    /// </summary>
    public async Task StreamToAsync(string path, Stream outputStream, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(ct);

        // Presigned GET + HttpClient (SDK GetObjectAsync can XML-parse binary bodies).
        // A dropped MinIO socket resumes with Range; CopyToAsync would throw ResponseEnded
        // and fail the download after the client had already received part of the file.
        await StorageObjectCopy.CopyAsync(
            (offset, token) => OpenObjectGetAsync(path, offset, token),
            outputStream,
            _logger,
            path,
            ct);
    }

    public async Task StreamRangeToAsync(
        string path,
        long offset,
        long length,
        Stream outputStream,
        CancellationToken ct = default)
    {
        if (offset < 0 || length < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length == 0)
            return;

        await EnsureBucketExistsAsync(ct);
        var end = checked(offset + length - 1);
        using var response = await OpenObjectGetAsync(path, offset, end, ct);
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            response.EnsureSuccessStatusCode();
            throw new HttpRequestException("Storage did not honor the requested byte range.");
        }

        if (response.Content.Headers.ContentLength is long contentLength && contentLength != length)
            throw new HttpRequestException("Storage returned a different byte-range length than requested.");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await StorageRangeCopy.CopyExactlyAsync(source, outputStream, length, ct);
    }

    private async Task<HttpResponseMessage> OpenObjectGetAsync(string path, long offset, CancellationToken ct)
        => await OpenObjectGetAsync(path, offset, null, ct);

    private async Task<HttpResponseMessage> OpenObjectGetAsync(
        string path,
        long offset,
        long? endOffset,
        CancellationToken ct)
    {
        HttpRequestException? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                // Match HttpClient timeout so a slow first-byte wait cannot expire the signature.
                var presignedUrl = await GetPresignedUrlAsync(path, 1800);
                var httpClient = _httpClientFactory.CreateClient("MinIOStorage");
                using var request = new HttpRequestMessage(HttpMethod.Get, presignedUrl)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                // Range is not part of the presigned signature; MinIO accepts it on GET.
                if (offset > 0 || endOffset.HasValue)
                    request.Headers.Range = new RangeHeaderValue(offset, endOffset);
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                return response;
            }
            catch (HttpRequestException ex) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                last = ex;
                _logger.LogWarning(ex, "MinIO GET failed for {Path} (attempt {Attempt}/3)", path, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
        }

        throw last ?? new HttpRequestException($"Failed to download '{path}' from MinIO");
    }

    /// <summary>
    /// Generate a presigned (temporary) URL for a file
    /// </summary>
    public async Task<string> GetPresignedUrlAsync(string path, int expirySeconds = 3600)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(path)
            .WithExpiry(expirySeconds);
        return await _client.PresignedGetObjectAsync(args);
    }

    /// <summary>
    /// Generate a presigned (temporary) URL using the public endpoint for external consumers
    /// </summary>
    public async Task<string> GetPublicPresignedUrlAsync(string path, int expirySeconds = 3600)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(_bucket)
            .WithObject(path)
            .WithExpiry(expirySeconds);
        return await _publicClient.PresignedGetObjectAsync(args);
    }

    /// <summary>
    /// List all object keys under a given prefix
    /// </summary>
    public async Task<List<string>> ListObjectsByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(ct);

        var keys = new List<string>();
        var args = new ListObjectsArgs()
            .WithBucket(_bucket)
            .WithPrefix(prefix)
            .WithRecursive(false);

        await foreach (var item in _client.ListObjectsEnumAsync(args, ct))
        {
            keys.Add(item.Key);
        }

        return keys;
    }

    /// <summary>
    /// Ensure the bucket exists and has private access policy (no public listing or download).
    /// </summary>
    private async Task EnsureBucketExistsAsync(CancellationToken ct = default)
    {
        if (_bucketExistsVerified)
            return;

        var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucket);
        bool exists;
        try
        {
            exists = await _client.BucketExistsAsync(bucketExistsArgs, ct);
        }
        catch (NullReferenceException)
        {
            // MinIO.NET 6.0.5 bug when the bucket does not exist yet.
            exists = false;
        }

        if (!exists)
        {
            var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucket);
            await _client.MakeBucketAsync(makeBucketArgs, ct);
        }

        // Remove any public bucket policy to prevent anonymous listing and download
        await SetPrivateBucketPolicyAsync(ct);

        _bucketExistsVerified = true;
    }

    /// <summary>
    /// Sets an empty (private) bucket policy, removing any public access rules.
    /// This prevents anonymous users from listing or downloading files directly from MinIO.
    /// </summary>
    private async Task SetPrivateBucketPolicyAsync(CancellationToken ct = default)
    {
        try
        {
            var args = new RemovePolicyArgs().WithBucket(_bucket);
            await _client.RemovePolicyAsync(args, ct);
            _logger.LogInformation("MinIO bucket '{Bucket}' policy removed (private by default)", _bucket);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove bucket policy for '{Bucket}'", _bucket);
        }
    }
}
