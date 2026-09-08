using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Marketplace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delegat.Services.Marketplace;

public sealed class OrderSyncResult
{
    public bool Pulled { get; init; }
    public bool Skipped { get; init; }
    public int Upserted { get; init; }
    public string? Reason { get; init; }
}

public sealed class MarketplaceOrderSyncService
{
    public const string InFlightIdsStatus = "__inflight_ids";
    public const string PageCapReason = "page_cap";
    public const int MaxPagesPerStatusPerRun = 20;
    public const int MaxInFlightIdRefreshesPerRun = 200;

    private static readonly TimeSpan PageCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackgroundCooldown = TimeSpan.FromMinutes(15);

    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly MarketplaceOrderSnapshotService _snapshots;
    private readonly TimeProvider _time;
    private readonly ILogger<MarketplaceOrderSyncService> _logger;

    public MarketplaceOrderSyncService(
        DataContext db,
        IMarketplaceAdapterRegistry adapters,
        MarketplaceOrderSnapshotService snapshots,
        TimeProvider time,
        ILogger<MarketplaceOrderSyncService> logger)
    {
        _db = db;
        _adapters = adapters;
        _snapshots = snapshots;
        _time = time;
        _logger = logger;
    }

    public async Task<OrderSyncResult> SyncConnectionStatusesAsync(
        MarketplaceConnection connection,
        IReadOnlyList<string> statuses,
        string reason,
        CancellationToken ct)
    {
        var adapter = _adapters.GetByKey(connection.adapterKey);
        if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetOrders))
        {
            return new OrderSyncResult
            {
                Skipped = true,
                Reason = adapter == null ? "adapter_not_found" : "adapter_capability_missing"
            };
        }

        var upserted = 0;
        var pulledCount = 0;
        var skippedCount = 0;
        string? resultReason = null;
        var upsertedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var status in statuses)
        {
            var one = await PullStatusAsync(connection, adapter, status, reason, upsertedIds, ct);
            upserted += one.Upserted;
            if (one.Pulled)
                pulledCount++;
            if (one.Skipped)
                skippedCount++;
            resultReason = CombineReason(resultReason, one);
        }

        if (IsBackground(reason))
        {
            var inflight = await RefreshInFlightByIdAsync(connection, adapter, upsertedIds, ct);
            upserted += inflight.Upserted;
            resultReason = CombineReason(resultReason, inflight);
        }

        var pulled = pulledCount > 0;
        var allSkipped = pulledCount == 0 && resultReason != PageCapReason
            && (skippedCount > 0 || statuses.Count == 0)
            && (resultReason == null || resultReason == "cooldown");
        return new OrderSyncResult
        {
            Pulled = pulled,
            Skipped = !pulled && allSkipped,
            Upserted = upserted,
            Reason = resultReason
        };
    }

    public async Task<OrderSyncResult> SyncStoreTabAsync(
        int storeId,
        string tab,
        string reason,
        CancellationToken ct)
    {
        var statuses = MarketplaceOrderBuckets.StatusesForOverviewTab(tab);
        var connections = await _db.marketplaceConnections
            .AsNoTracking()
            .Where(c => c.storeId == storeId && c.isActive)
            .ToListAsync(ct);

        var upserted = 0;
        var pulledCount = 0;
        var skippedCount = 0;
        var attempted = 0;
        string? resultReason = null;

        foreach (var connection in connections)
        {
            var adapter = _adapters.GetByKey(connection.adapterKey);
            if (adapter == null || !adapter.Capabilities.HasFlag(MarketplaceCapabilities.GetOrders))
                continue;

            attempted++;
            var one = await SyncConnectionStatusesAsync(connection, statuses, reason, ct);
            upserted += one.Upserted;
            if (one.Pulled)
                pulledCount++;
            if (one.Skipped)
                skippedCount++;
            resultReason = CombineReason(resultReason, one);
        }

        var pulled = pulledCount > 0;
        return new OrderSyncResult
        {
            Pulled = pulled,
            Skipped = !pulled && resultReason != PageCapReason && (skippedCount > 0 || attempted == 0)
                && (resultReason == null || resultReason == "cooldown"),
            Upserted = upserted,
            Reason = resultReason
        };
    }

    private async Task<OrderSyncResult> PullStatusAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        string status,
        string reason,
        HashSet<string> upsertedIds,
        CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(connection.Id, status, ct);
        var cooldown = CooldownFor(reason);
        if (IsCoolingDown(state, cooldown))
        {
            return new OrderSyncResult { Skipped = true, Reason = "cooldown" };
        }

        var upserted = 0;
        try
        {
            var page = 1;
            string? cursor = null;
            var pagesFetched = 0;
            var terminal = MarketplaceOrderBuckets.IsTerminal(status);
            var hitPageCap = false;

            while (true)
            {
                var query = new OrderQuery
                {
                    Status = status,
                    PageSize = 50,
                    Page = page,
                    Cursor = cursor
                };
                var batch = await adapter.GetOrdersAsync(connection, query, ct);
                pagesFetched++;

                var items = batch.Items ?? [];
                upserted += await _snapshots.UpsertManyAsync(
                    connection.storeId, connection.Id, items, ct);
                foreach (var order in items)
                {
                    if (!string.IsNullOrWhiteSpace(order.ExternalId))
                        upsertedIds.Add(order.ExternalId);
                }

                if (terminal || !batch.HasMore)
                    break;

                if (pagesFetched >= MaxPagesPerStatusPerRun)
                {
                    hitPageCap = true;
                    break;
                }

                if (!string.IsNullOrEmpty(batch.NextCursor))
                    cursor = batch.NextCursor;
                else
                {
                    cursor = null;
                    page++;
                }
            }

            state.lastPulledAt = _time.GetUtcNow().UtcDateTime;
            state.lastError = null;
            await _db.SaveChangesAsync(ct);

            if (hitPageCap)
            {
                _logger.LogWarning(
                    "Marketplace order pull hit page cap ({PageCap}) for connection {ConnectionId} status {Status}",
                    MaxPagesPerStatusPerRun,
                    connection.Id,
                    status);
                return new OrderSyncResult { Pulled = true, Upserted = upserted, Reason = PageCapReason };
            }

            return new OrderSyncResult { Pulled = true, Upserted = upserted };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Marketplace order pull failed for connection {ConnectionId} status {Status}",
                connection.Id,
                status);
            state.lastError = Truncate(ex.Message);
            state.lastPulledAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new OrderSyncResult { Pulled = false, Upserted = upserted, Reason = state.lastError };
        }
    }

    private async Task<OrderSyncResult> RefreshInFlightByIdAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        HashSet<string> upsertedIds,
        CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(connection.Id, InFlightIdsStatus, ct);
        if (IsCoolingDown(state, BackgroundCooldown))
            return new OrderSyncResult { Skipped = true, Reason = "cooldown" };

        var upserted = 0;
        try
        {
            var terminal = MarketplaceOrderBuckets.TerminalStatusList;
            var pending = _db.marketplaceOrders
                .AsNoTracking()
                .Where(o => o.connectionId == connection.Id && !terminal.Contains(o.status));
            if (upsertedIds.Count > 0)
            {
                var skip = upsertedIds.ToList();
                pending = pending.Where(o => !skip.Contains(o.marketplaceOrderId));
            }

            var ids = await pending
                .OrderBy(o => o.syncedAt)
                .Select(o => o.marketplaceOrderId)
                .Take(MaxInFlightIdRefreshesPerRun)
                .ToListAsync(ct);

            var lives = new List<ExternalOrder>();
            foreach (var id in ids)
            {
                var live = await adapter.GetOrderAsync(connection, id, ct);
                if (live == null)
                    continue;

                lives.Add(live);
                if (!string.IsNullOrWhiteSpace(id))
                    upsertedIds.Add(id);
            }

            upserted += await _snapshots.UpsertManyAsync(
                connection.storeId, connection.Id, lives, ct);

            state.lastPulledAt = _time.GetUtcNow().UtcDateTime;
            state.lastError = null;
            await _db.SaveChangesAsync(ct);
            return new OrderSyncResult { Pulled = true, Upserted = upserted };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Marketplace in-flight order refresh failed for connection {ConnectionId}",
                connection.Id);
            state.lastError = Truncate(ex.Message);
            state.lastPulledAt = _time.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(CancellationToken.None);
            return new OrderSyncResult { Pulled = false, Upserted = upserted, Reason = state.lastError };
        }
    }

    private async Task<MarketplaceOrderSyncState> GetOrCreateStateAsync(
        int connectionId,
        string status,
        CancellationToken ct)
    {
        var state = await _db.marketplaceOrderSyncStates.FirstOrDefaultAsync(
            s => s.connectionId == connectionId && s.status == status,
            ct);
        if (state != null)
            return state;

        state = new MarketplaceOrderSyncState
        {
            connectionId = connectionId,
            status = status
        };
        _db.marketplaceOrderSyncStates.Add(state);
        await _db.SaveChangesAsync(ct);
        return state;
    }

    private bool IsCoolingDown(MarketplaceOrderSyncState state, TimeSpan cooldown)
    {
        if (state.lastPulledAt == null)
            return false;

        var now = _time.GetUtcNow().UtcDateTime;
        return now - state.lastPulledAt.Value < cooldown;
    }

    private static TimeSpan CooldownFor(string reason) =>
        IsBackground(reason) ? BackgroundCooldown : PageCooldown;

    private static bool IsBackground(string reason) =>
        string.Equals(reason, "background", StringComparison.OrdinalIgnoreCase);

    private static string? CombineReason(string? current, OrderSyncResult one)
    {
        if (one.Reason == PageCapReason)
            return current ?? PageCapReason;
        if (!one.Pulled && !one.Skipped && one.Reason != null)
            return current ?? one.Reason;
        return current;
    }

    private static string Truncate(string value, int max = 2000) =>
        value.Length <= max ? value : value[..max];
}
