using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Marketplace;
using Microsoft.EntityFrameworkCore;

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

    private static readonly TimeSpan PageCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackgroundCooldown = TimeSpan.FromMinutes(15);

    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly MarketplaceOrderSnapshotService _snapshots;
    private readonly TimeProvider _time;

    public MarketplaceOrderSyncService(
        DataContext db,
        IMarketplaceAdapterRegistry adapters,
        MarketplaceOrderSnapshotService snapshots,
        TimeProvider time)
    {
        _db = db;
        _adapters = adapters;
        _snapshots = snapshots;
        _time = time;
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
        string? error = null;

        foreach (var status in statuses)
        {
            var one = await PullStatusAsync(connection, adapter, status, reason, ct);
            upserted += one.Upserted;
            if (one.Pulled)
                pulledCount++;
            if (one.Skipped)
                skippedCount++;
            if (!one.Pulled && !one.Skipped && one.Reason != null)
                error ??= one.Reason;
        }

        if (IsBackground(reason))
        {
            var inflight = await RefreshInFlightByIdAsync(connection, adapter, ct);
            upserted += inflight.Upserted;
        }

        var pulled = pulledCount > 0;
        var allSkipped = pulledCount == 0 && error == null && (skippedCount > 0 || statuses.Count == 0);
        return new OrderSyncResult
        {
            Pulled = pulled,
            Skipped = !pulled && error == null && allSkipped,
            Upserted = upserted,
            Reason = error
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
            .Where(c => c.storeId == storeId)
            .ToListAsync(ct);

        var upserted = 0;
        var pulledCount = 0;
        var skippedCount = 0;
        var attempted = 0;
        string? error = null;

        foreach (var connection in connections)
        {
            if (!connection.isActive)
                continue;

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
            if (!one.Pulled && !one.Skipped && one.Reason != null)
                error ??= one.Reason;
        }

        var pulled = pulledCount > 0;
        return new OrderSyncResult
        {
            Pulled = pulled,
            Skipped = !pulled && error == null && (skippedCount > 0 || attempted == 0),
            Upserted = upserted,
            Reason = error
        };
    }

    private async Task<OrderSyncResult> PullStatusAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        string status,
        string reason,
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

                foreach (var order in batch.Items ?? [])
                {
                    await _snapshots.UpsertAsync(connection.storeId, connection.Id, order, ct);
                    upserted++;
                }

                if (terminal || !batch.HasMore || pagesFetched >= 20)
                    break;

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

            return new OrderSyncResult { Pulled = true, Upserted = upserted };
        }
        catch (Exception ex)
        {
            state.lastError = Truncate(ex.Message);
            await _db.SaveChangesAsync(ct);
            return new OrderSyncResult { Pulled = false, Upserted = upserted, Reason = state.lastError };
        }
    }

    private async Task<OrderSyncResult> RefreshInFlightByIdAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        CancellationToken ct)
    {
        var state = await GetOrCreateStateAsync(connection.Id, InFlightIdsStatus, ct);
        if (IsCoolingDown(state, BackgroundCooldown))
            return new OrderSyncResult { Skipped = true, Reason = "cooldown" };

        var upserted = 0;
        try
        {
            var rows = await _db.marketplaceOrders
                .Where(o => o.connectionId == connection.Id)
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                if (MarketplaceOrderBuckets.IsTerminal(row.status))
                    continue;

                var live = await adapter.GetOrderAsync(connection, row.marketplaceOrderId, ct);
                if (live == null)
                    continue;

                await _snapshots.UpsertAsync(connection.storeId, connection.Id, live, ct);
                upserted++;
            }

            state.lastPulledAt = _time.GetUtcNow().UtcDateTime;
            state.lastError = null;
            await _db.SaveChangesAsync(ct);
            return new OrderSyncResult { Pulled = true, Upserted = upserted };
        }
        catch (Exception ex)
        {
            state.lastError = Truncate(ex.Message);
            await _db.SaveChangesAsync(ct);
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

    private static string Truncate(string value, int max = 2000) =>
        value.Length <= max ? value : value[..max];
}
