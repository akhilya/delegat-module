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

    public async Task UpsertAsync(int storeId, int connectionId, ExternalOrder order, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(order.ExternalId))
            return;

        var now = _time.GetUtcNow().UtcDateTime;
        var payload = JsonSerializer.Serialize(order, JsonOptions);
        var existing = await _db.marketplaceOrders.FirstOrDefaultAsync(
            o => o.storeId == storeId
                && o.connectionId == connectionId
                && o.marketplaceOrderId == order.ExternalId,
            ct);

        if (existing == null)
        {
            _db.marketplaceOrders.Add(new MarketplaceOrder
            {
                storeId = storeId,
                connectionId = connectionId,
                marketplaceOrderId = order.ExternalId,
                displayId = order.DisplayId,
                status = order.Status,
                channel = order.Channel,
                currency = order.Currency,
                total = order.Total,
                orderCreatedAt = ToUtc(order.CreatedAt),
                orderUpdatedAt = ToUtc(order.UpdatedAt),
                syncedAt = now,
                payloadJson = payload
            });
        }
        else
        {
            existing.displayId = order.DisplayId;
            existing.status = order.Status;
            existing.channel = order.Channel;
            existing.currency = order.Currency;
            existing.total = order.Total;
            existing.orderCreatedAt = ToUtc(order.CreatedAt);
            existing.orderUpdatedAt = ToUtc(order.UpdatedAt);
            existing.syncedAt = now;
            existing.payloadJson = payload;
        }

        await _db.SaveChangesAsync(ct);
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
        var byPosting = await _db.marketplaceOrders.FirstOrDefaultAsync(
            o => o.storeId == storeId
                && o.connectionId == connectionId
                && o.marketplaceOrderId == orderId,
            ct);
        if (byPosting != null)
            return byPosting;

        return await _db.marketplaceOrders.FirstOrDefaultAsync(
            o => o.storeId == storeId
                && o.connectionId == connectionId
                && o.displayId == orderId,
            ct);
    }

    public ExternalOrder ToExternalOrder(MarketplaceOrder row)
    {
        if (!string.IsNullOrWhiteSpace(row.payloadJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<ExternalOrder>(row.payloadJson, JsonOptions);
                if (parsed != null)
                    return parsed;
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
            UpdatedAt = ToOffset(row.orderUpdatedAt)
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

    private static DateTime? ToUtc(DateTimeOffset? value) => value?.UtcDateTime;

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
