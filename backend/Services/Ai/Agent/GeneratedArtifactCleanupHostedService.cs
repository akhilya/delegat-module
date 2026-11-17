namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Drops expired generated artifacts under <c>__ai-generated/</c> only. Chat
/// staging, imported destination objects and import receipts are untouched: a
/// started receipt records an unknown write that needs operator reconciliation.
/// </summary>
public sealed class GeneratedArtifactCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GeneratedArtifactCleanupHostedService> _log;

    public GeneratedArtifactCleanupHostedService(
        IServiceScopeFactory scopes,
        ILogger<GeneratedArtifactCleanupHostedService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var artifacts = scope.ServiceProvider.GetRequiredService<GeneratedArtifactService>();
                var n = await artifacts.PurgeExpiredAsync(stoppingToken);
                if (n > 0)
                    _log.LogInformation("Purged {Count} expired generated artifacts", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Generated artifact cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
