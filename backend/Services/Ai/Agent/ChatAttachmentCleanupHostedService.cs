namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Drops staged chat files after <see cref="AgentFileUploadService.ChatAttachTtl"/>.
/// Names stay on the conversation; bytes are gone.
/// </summary>
public sealed class ChatAttachmentCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ChatAttachmentCleanupHostedService> _log;

    public ChatAttachmentCleanupHostedService(
        IServiceScopeFactory scopes,
        ILogger<ChatAttachmentCleanupHostedService> log)
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
                var uploads = scope.ServiceProvider.GetRequiredService<AgentFileUploadService>();
                var n = await uploads.PurgeExpiredAsync(stoppingToken);
                if (n > 0)
                    _log.LogInformation("Purged {Count} expired chat attachments", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Chat attachment cleanup failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
