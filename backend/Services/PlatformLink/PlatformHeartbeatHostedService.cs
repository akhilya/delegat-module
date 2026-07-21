using System.Reflection;
using Delegat.Infrastructure.Configuration;

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
                await client.SendHeartbeatAsync(new PlatformHeartbeatDto
                {
                    InstanceId = _options.InstanceId ?? "",
                    OccurredAt = DateTime.UtcNow,
                    PlatformVersion = version,
                    Status = "ok",
                }, stoppingToken);
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
