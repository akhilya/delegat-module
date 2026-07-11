using Microsoft.Extensions.Diagnostics.HealthChecks;
using Minio;
using Minio.DataModel.Args;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Infrastructure.Health;

public class MinIOHealthCheck : IHealthCheck
{
    private readonly MinIOSettings _settings;

    public MinIOHealthCheck(MinIOSettings settings)
    {
        _settings = settings;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var client = new MinioClient()
                .WithEndpoint(_settings.Endpoint)
                .WithCredentials(_settings.AccessKey, _settings.SecretKey)
                .WithSSL(_settings.Secure)
                .Build();

            await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_settings.Bucket), ct);
            return HealthCheckResult.Healthy("MinIO reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MinIO unreachable", ex);
        }
    }
}
