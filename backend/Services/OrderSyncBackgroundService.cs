using Delegat;
using Delegat.Models.Marketplace;
using Delegat.Services.Marketplace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Delegat.Services;

/// <summary>
/// Pulls marketplace orders into the local snapshot on a 15-minute cycle.
/// </summary>
public sealed class OrderSyncBackgroundService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderSyncBackgroundService> _logger;

    public OrderSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Order sync cycle failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task ProcessOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataContext>();
        var adapters = scope.ServiceProvider.GetRequiredService<IMarketplaceAdapterRegistry>();
        var sync = scope.ServiceProvider.GetRequiredService<MarketplaceOrderSyncService>();

        var connections = await db.marketplaceConnections
            .AsNoTracking()
            .Where(c => c.isActive)
            .ToListAsync(ct);

        foreach (var connection in connections)
        {
            ct.ThrowIfCancellationRequested();
            var adapter = adapters.GetByKey(connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetOrders))
                continue;

            try
            {
                var statuses = MarketplaceOrderBuckets.ListStatusesForAdapter(connection.adapterKey);
                await sync.SyncConnectionStatusesAsync(connection, statuses, "background", ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Order sync skipped connection {ConnectionId} ({AdapterKey})",
                    connection.Id,
                    connection.adapterKey);
            }
        }
    }
}
