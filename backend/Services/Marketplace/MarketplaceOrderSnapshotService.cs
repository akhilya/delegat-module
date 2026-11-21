using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Marketplace;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Marketplace;

public sealed class MarketplaceOrderSnapshotService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DataContext _db;
    private readonly TimeProvider _time;

    public MarketplaceOrderSnapshotService(DataContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public Task UpsertAsync(int storeId, int connectionId, ExternalOrder order, CancellationToken ct)
        => UpsertManyAsync(storeId, connectionId, [order], ct);

    public async Task<int> UpsertManyAsync(
        int storeId,
        int connectionId,
        IReadOnlyList<ExternalOrder> orders,
        CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var wanted = new List<ExternalOrder>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (string.IsNullOrWhiteSpace(order.ExternalId))
                continue;
            if (seen.TryGetValue(order.ExternalId, out var index))
                wanted[index] = order;
            else
            {
                seen[order.ExternalId] = wanted.Count;
                wanted.Add(order);
            }
        }

        if (wanted.Count == 0)
            return 0;

        var ids = wanted.Select(o => o.ExternalId).ToList();
        var existing = await _db.marketplaceOrders
            .Where(o =>
                o.storeId == storeId
                && o.connectionId == connectionId
                && ids.Contains(o.marketplaceOrderId))
            .ToListAsync(ct);
        var byId = existing.ToDictionary(o => o.marketplaceOrderId, StringComparer.Ordinal);

        foreach (var order in wanted)
        {
            var payload = JsonSerializer.Serialize(order, JsonOptions);
            if (byId.TryGetValue(order.ExternalId, out var row))
            {
                if (MarketplaceSellerOrderStatus.ShouldKeepStoredStatus(
                        row.status, row.syncedAt, order.Status, now))
                    continue;
                Apply(row, order, payload, now);
            }
            else
            {
                var added = new MarketplaceOrder
                {
                    storeId = storeId,
                    connectionId = connectionId,
                    marketplaceOrderId = order.ExternalId
                };
                Apply(added, order, payload, now);
                _db.marketplaceOrders.Add(added);
                byId[order.ExternalId] = added;
            }
        }

        await _db.SaveChangesAsync(ct);
        return wanted.Count;
    }

    public async Task<(IReadOnlyList<MarketplaceOrder> Items, int Total)> ListPageAsync(
        int storeId,
        int? connectionId,
        IReadOnlyList<string> statuses,
        string? channel,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var q = ApplyFilters(storeId, connectionId, statuses, channel, search, from, to);
        var total = await q.CountAsync(ct);
        var pageNumber = page < 1 ? 1 : page;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;
        var items = await q
            .OrderByDescending(o => o.orderCreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<MarketplaceOrder>> ListAsync(
        int storeId,
        int? connectionId,
        IReadOnlyList<string> statuses,
        string? channel,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var pageNumber = page < 1 ? 1 : page;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;
        return await ApplyFilters(storeId, connectionId, statuses, channel, search, from, to)
            .OrderByDescending(o => o.orderCreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<string, int>> CountByStatusAsync(
        int storeId,
        int? connectionId,
        string? channel,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var rows = await ApplyFilters(storeId, connectionId, statuses: null, channel, search, from, to)
            .GroupBy(o => o.status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Status, r => r.Count, StringComparer.OrdinalIgnoreCase);
    }

    public Task<int> CountAsync(
        int storeId,
        int? connectionId,
        IReadOnlyList<string> statuses,
        string? channel,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
        => ApplyFilters(storeId, connectionId, statuses, channel, search, from, to).CountAsync(ct);

    public async Task<MarketplaceOrder?> FindAsync(int storeId, int connectionId, string orderId, CancellationToken ct)
    {
        var matches = await _db.marketplaceOrders
            .AsNoTracking()
            .Where(o =>
                o.storeId == storeId
                && o.connectionId == connectionId
                && (o.marketplaceOrderId == orderId || o.displayId == orderId))
            .ToListAsync(ct);
        return matches.Find(o => o.marketplaceOrderId == orderId) ?? matches.FirstOrDefault();
    }

    public ExternalOrder ToExternalOrder(MarketplaceOrder row)
    {
        if (!string.IsNullOrWhiteSpace(row.payloadJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ExternalOrder>(row.payloadJson, JsonOptions);
                if (parsed != null)
                {
                    parsed.SyncedAt = new DateTimeOffset(DateTime.SpecifyKind(row.syncedAt, DateTimeKind.Utc));
                    return parsed;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ExternalOrder
        {
            ExternalId = row.marketplaceOrderId,
            DisplayId = row.displayId,
            Status = row.status,
            Channel = row.channel,
            Currency = row.currency,
            Total = row.total,
            CreatedAt = ToOffset(row.orderCreatedAt),
            UpdatedAt = ToOffset(row.orderUpdatedAt),
            SyncedAt = new DateTimeOffset(DateTime.SpecifyKind(row.syncedAt, DateTimeKind.Utc))
        };
    }

    private IQueryable<MarketplaceOrder> ApplyFilters(
        int storeId,
        int? connectionId,
        IReadOnlyList<string>? statuses,
        string? channel,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var q = _db.marketplaceOrders.Where(o => o.storeId == storeId);

        if (connectionId.HasValue)
            q = q.Where(o => o.connectionId == connectionId.Value);

        if (statuses is { Count: > 0 })
            q = q.Where(o => statuses.Contains(o.status));

        if (!string.IsNullOrWhiteSpace(channel))
        {
            var ch = channel.Trim().ToLower();
            q = q.Where(o => o.channel.ToLower() == ch);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(o =>
                o.marketplaceOrderId.Contains(search)
                || (o.displayId != null && o.displayId.Contains(search)));
        }

        if (from.HasValue)
        {
            var fromUtc = from.Value.UtcDateTime;
            q = q.Where(o => o.orderCreatedAt != null && o.orderCreatedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = to.Value.UtcDateTime;
            q = q.Where(o => o.orderCreatedAt != null && o.orderCreatedAt <= toUtc);
        }

        return q;
    }

    private static void Apply(MarketplaceOrder row, ExternalOrder order, string payload, DateTime now)
    {
        row.displayId = order.DisplayId;
        row.status = order.Status;
        row.channel = order.Channel;
        row.currency = order.Currency;
        row.total = order.Total;
        row.orderCreatedAt = ToUtc(order.CreatedAt);
        row.orderUpdatedAt = ToUtc(order.UpdatedAt);
        row.syncedAt = now;
        row.payloadJson = payload;
    }

    private static DateTime? ToUtc(DateTimeOffset? value) => value?.UtcDateTime;

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
