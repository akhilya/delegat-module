using System.Reflection;
using Delegat.Infrastructure.Configuration;
using Delegat.Services;

namespace Delegat.Services.PlatformLink;

public sealed class PlatformHeartbeatHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformLinkOptions _options;
    private readonly ILogger<PlatformHeartbeatHostedService> _logger;

    public PlatformHeartbeatHostedService(
        IServiceScopeFactory scopeFactory,
        PlatformLinkOptions options,
        ILogger<PlatformHeartbeatHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
            return;

        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<IPlatformLinkClient>();
                var quota = scope.ServiceProvider.GetRequiredService<QuotaService>();
                var entitlements = scope.ServiceProvider.GetRequiredService<IPlatformEntitlementsService>();

                PlatformUsageReportDto? usage = null;
                try { usage = await quota.BuildUsageReportAsync(stoppingToken); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to build usage for heartbeat"); }

                await client.SendHeartbeatAsync(new PlatformHeartbeatDto
                {
                    InstanceId = _options.InstanceId ?? "",
                    OccurredAt = DateTime.UtcNow,
                    PlatformVersion = version,
                    Status = "ok",
                    Usage = usage,
                }, stoppingToken);

                var remote = await client.GetEntitlementsAsync(stoppingToken);
                if (remote != null)
                {
                    await entitlements.UpsertAsync(new PlatformEntitlementsCacheDto(
                        Linked: true,
                        PlanName: remote.PlanName,
                        PlanSlug: remote.PlanSlug,
                        SubscriptionStatus: remote.SubscriptionStatus,
                        OveragePurgeAt: remote.OveragePurgeAt,
                        UpdatedAt: DateTime.UtcNow), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Platform heartbeat failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(_options.HeartbeatMinutes), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
