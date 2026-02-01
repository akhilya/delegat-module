using Minio;
using Minio.DataModel.Args;
using PrintO.Infrastructure.Configuration;

namespace PrintO.Infrastructure.Storage;

/// <summary>
/// MinIO storage service implementation
/// Replaces Zorro.Services.MinIOService and MinIORepository
/// </summary>
public class MinIOStorageService : IStorageService
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly string _endpoint;
    private readonly bool _secure;

    public string Bucket => _bucket;

    public MinIOStorageService(MinIOSettings settings)
    {
        _bucket = settings.Bucket;
        _endpoint = settings.Endpoint;
        _secure = settings.Secure;

        _client = new MinioClient()
            .WithEndpoint(settings.Endpoint)
            .WithCredentials(settings.AccessKey, settings.SecretKey)
            .WithSSL(settings.Secure)
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
        catch (Exception)
        {
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
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Upload from URI (download and upload)
    /// </summary>
    public async Task<bool> UploadFromUriAsync(string uri, string path, CancellationToken ct = default)
    {
        try
        {
            await EnsureBucketExistsAsync(ct);

            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(uri, ct);
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
            return true;
        }
        catch (Exception)
        {
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
    /// Get full public URL for a file
    /// </summary>
    public string GetFullPath(string path)
    {
        var protocol = _secure ? "https" : "http";
        return $"{protocol}://{_endpoint}/{_bucket}/{path}";
    }

    /// <summary>
    /// Ensure the bucket exists, create if not
    /// </summary>
    private async Task EnsureBucketExistsAsync(CancellationToken ct = default)
    {
        var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucket);
        var exists = await _client.BucketExistsAsync(bucketExistsArgs, ct);

        if (!exists)
        {
            var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucket);
            await _client.MakeBucketAsync(makeBucketArgs, ct);
        }
    }
}
