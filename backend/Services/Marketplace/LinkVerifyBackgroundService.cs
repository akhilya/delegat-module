using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Services;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Runs a full connection link check off the HTTP request and records it as its own operation.
/// </summary>
public sealed class LinkVerifyBackgroundService : BackgroundService
{
    private readonly LinkVerifyQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperationLogger _operationLogger;
    private readonly ILogger<LinkVerifyBackgroundService> _logger;

    public LinkVerifyBackgroundService(
        LinkVerifyQueue queue,
        IServiceScopeFactory scopeFactory,
        IOperationLogger operationLogger,
        ILogger<LinkVerifyBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _operationLogger = operationLogger;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LinkVerifyBackgroundService started");
        await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunAsync(work, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Link check failed for connection {ConnectionId}", work.ConnectionId);
                await _operationLogger.LogAsync(work.OperationId, work.StoreId, "error", ex.Message);
                await _operationLogger.FinishAsync(work.OperationId, work.StoreId, OperationLifecycle.Failed, ex.Message);
            }
            finally
            {
                _queue.Complete(work.ConnectionId);
            }
        }

        _logger.LogInformation("LinkVerifyBackgroundService stopped");
    }

    private async Task RunAsync(LinkVerifyWork work, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        if (await OperationLifecycle.IsStopRequestedAsync(db, work.OperationId, stoppingToken))
        {
            await _operationLogger.FinishAsync(
                work.OperationId, work.StoreId, OperationLifecycle.Cancelled, "Stopped");
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var watch = OperationStopWatcher.WatchAsync(_scopeFactory, work.OperationId, cts);
        try
        {
            await _operationLogger.LogAsync(
                work.OperationId, work.StoreId, "info", "Reading the shop catalog and comparing saved external ids...");

            var sync = scope.ServiceProvider.GetRequiredService<ProductSyncService>();
            var result = await sync.VerifyLinksAsync(work.ConnectionId, linkId: null, cts.Token);
            var summary =
                $"Checked {result.Checked}: {result.Confirmed} confirmed, {result.Broken} missing, {result.Recovered} restored";
            await _operationLogger.LogAsync(work.OperationId, work.StoreId, "success", summary);
            await _operationLogger.FinishAsync(work.OperationId, work.StoreId, OperationLifecycle.Completed, summary);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            await _operationLogger.FinishAsync(
                work.OperationId, work.StoreId, OperationLifecycle.Cancelled, "Stopped");
        }
        finally
        {
            await cts.CancelAsync();
            try { await watch; } catch (OperationCanceledException) { }
        }
    }
}
