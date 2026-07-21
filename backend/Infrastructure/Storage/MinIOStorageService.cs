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

        // Public endpoint for external URL generation (falls back to internal endpoint)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload stream to MinIO: {Path}, ContentLength: {ContentLength}", path, contentLength);
            return false;
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
            catch (Exception)
            {
                return null;
            }
        }

        return null;
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
    /// Download a file as a stream
    /// </summary>
    public async Task<Stream?> GetStreamAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var presignedUrl = await GetPresignedUrlAsync(path, 60);
            var httpClient = _httpClientFactory.CreateClient("MinIOStorage");
            using var response = await httpClient.GetAsync(presignedUrl, ct);
            response.EnsureSuccessStatusCode();
            var ms = new MemoryStream();
            await response.Content.CopyToAsync(ms, ct);
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

        // Bypass SDK GetObjectAsync - use presigned URL + HttpClient to avoid XML parse issues
        var presignedUrl = await GetPresignedUrlAsync(path, 60);
        var httpClient = _httpClientFactory.CreateClient("MinIOStorage");
        using var response = await httpClient.GetAsync(presignedUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.CopyToAsync(outputStream, ct);
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
        var exists = await _client.BucketExistsAsync(bucketExistsArgs, ct);

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
