using System.Net.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Infrastructure.Health;

/// <summary>
/// MinIO readiness for /api/health (Control Plane provision polls this).
/// MinIO.NET 6.0.5: <c>ListBucketsAsync</c> can swallow connection errors;
/// <c>BucketExistsAsync</c> can NRE when the bucket is missing.
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
            return HealthCheckResult.Healthy(
                $"MinIO reachable ({_settings.Endpoint}, secure={_settings.Secure})");
        }
        catch (Exception ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message.Trim();
            if (detail.Length > 280)
                detail = detail[..280];
            return HealthCheckResult.Unhealthy($"MinIO unhealthy: {detail}", ex);
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
            .WithTimeout(5_000)
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

        // Plain HTTP(S) liveness — independent of MinIO.NET quirks / credentials.
        await ProbeLiveAsync(settings, ct);

        var client = CreateClient(settings);

        // Do not use ListBucketsAsync: MinIO.NET 6.0.5 can ignore connection failures.
        if (await BucketExistsSafeAsync(client, settings.Bucket, ct))
            return;

        logger.LogInformation(
            "Creating MinIO bucket '{Bucket}' at {Endpoint} (secure={Secure})",
            settings.Bucket,
            settings.Endpoint,
            settings.Secure);
        try
        {
            await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(settings.Bucket), ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"MakeBucket '{settings.Bucket}' failed at {settings.Endpoint} (secure={settings.Secure}): {ex.Message}",
                ex);
        }
    }

    /// <summary>GET /minio/health/live — same probe MinIO documents for load balancers.</summary>
    public static async Task ProbeLiveAsync(MinIOSettings settings, CancellationToken ct = default)
    {
        var scheme = settings.Secure ? "https" : "http";
        var endpoint = settings.Endpoint.Trim().TrimEnd('/');
        var liveUrl = $"{scheme}://{endpoint}/minio/health/live";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(liveUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"live HTTP {(int)response.StatusCode} at {liveUrl}");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"live probe failed at {liveUrl} (secure={settings.Secure}). " +
                $"API must reach MinIO on the Docker network (usually minio:9000, MINIO_SECURE=false). {ex.Message}",
                ex);
        }
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
