using Delegat.Infrastructure.Configuration;
using Delegat.Services.PlatformLink;

namespace Delegat.Services;

/// <summary>
/// When Platform Link reports an overdue <c>OveragePurgeAt</c> and the instance is still over quota, apply trim.
/// </summary>
public sealed class OverageTrimHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformLinkOptions _options;
    private readonly ILogger<OverageTrimHostedService> _logger;

    public OverageTrimHostedService(
        IServiceScopeFactory scopeFactory,
        PlatformLinkOptions options,
        ILogger<OverageTrimHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEnabled)
            return;

        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var entitlements = scope.ServiceProvider.GetRequiredService<IPlatformEntitlementsService>();
                var trim = scope.ServiceProvider.GetRequiredService<IOverageTrimService>();
                var cached = await entitlements.GetCachedAsync(stoppingToken);

                if (cached.Linked
                    && cached.OveragePurgeAt is { } purgeAt
                    && purgeAt <= DateTimeOffset.UtcNow)
                {
                    var preview = await trim.PreviewAsync(stoppingToken);
                    if (preview.IsOverQuota)
                    {
                        _logger.LogWarning(
                            "Overage purge due ({PurgeAt}); trimming {Products} products, {Connections} connections, {Files} files",
                            purgeAt,
                            preview.ProductCount,
                            preview.ConnectionCount,
                            preview.FileCount);
                        await trim.ExecuteAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Overage trim worker iteration failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
