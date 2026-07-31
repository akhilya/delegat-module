using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Infrastructure.Health;

/// <summary>
/// MinIO readiness for /api/health (Control Plane provision polls this).
/// MinIO.NET 6.0.5 throws <see cref="NullReferenceException"/> from BucketExistsAsync
/// when the bucket is missing (fresh volume) — treat that as "does not exist" and create it.
/// </summary>
public sealed class MinIOHealthCheck : IHealthCheck
{
    private readonly MinIOSettings _settings;
    private readonly ILogger<MinIOHealthCheck> _logger;

    public MinIOHealthCheck(MinIOSettings settings, ILogger<MinIOHealthCheck> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await MinioBootstrap.EnsureReachableAndBucketAsync(_settings, _logger, ct);
            return HealthCheckResult.Healthy("MinIO reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MinIO unreachable", ex);
        }
    }
}

/// <summary>Shared MinIO connect + ensure-bucket used by health checks and startup.</summary>
public static class MinioBootstrap
{
    public static IMinioClient CreateClient(MinIOSettings settings) =>
        new MinioClient()
            .WithEndpoint(settings.Endpoint)
            .WithCredentials(settings.AccessKey, settings.SecretKey)
            .WithSSL(settings.Secure)
            .Build();

    public static async Task EnsureReachableAndBucketAsync(
        MinIOSettings settings,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint)
            || string.IsNullOrWhiteSpace(settings.AccessKey)
            || string.IsNullOrWhiteSpace(settings.SecretKey)
            || string.IsNullOrWhiteSpace(settings.Bucket))
        {
            throw new InvalidOperationException(
                "MinIO settings incomplete (endpoint/access/secret/bucket).");
        }

        var client = CreateClient(settings);

        // Proves TCP + credentials without requiring the app bucket to exist.
        await client.ListBucketsAsync(ct);

        if (await BucketExistsSafeAsync(client, settings.Bucket, ct))
            return;

        logger.LogInformation("Creating MinIO bucket '{Bucket}'", settings.Bucket);
        await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(settings.Bucket), ct);
    }

    /// <summary>
    /// MinIO.NET 6.0.5: BucketExistsAsync can NRE when the bucket is absent.
    /// </summary>
    public static async Task<bool> BucketExistsSafeAsync(
        IMinioClient client,
        string bucket,
        CancellationToken ct = default)
    {
        try
        {
            return await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct);
        }
        catch (NullReferenceException)
        {
            return false;
        }
    }
}
