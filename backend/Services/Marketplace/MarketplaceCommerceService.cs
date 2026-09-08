using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Marketplace prices and promotions stay live. Orders are stored snapshots.
/// </summary>
public class MarketplaceCommerceService
{
    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;
    private readonly MarketplaceOrderSnapshotService _snapshots;

    public MarketplaceCommerceService(
        DataContext db,
        IMarketplaceAdapterRegistry adapters,
        MarketplaceOrderSnapshotService snapshots)
    {
        _db = db;
        _adapters = adapters;
        _snapshots = snapshots;
    }

    public async Task<(MarketplaceConnection Connection, IMarketplaceAdapter Adapter)> RequireAsync(
        int storeId,
        int connectionId,
        MarketplaceCapabilities required,
        CancellationToken ct)
    {
        var connection = await _db.marketplaceConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.storeId == storeId, ct);

        if (connection == null)
            throw ApiException.NotFound("Connection not found", code: "connection_not_found");

        CatalogMatchService.EnsureConnectionActive(connection);

        var adapter = _adapters.GetByKey(connection.adapterKey);
        if (adapter == null)
            throw ApiException.BadRequest($"Unknown adapter: {connection.adapterKey}", code: "adapter_not_found");

        if (!adapter.Capabilities.HasFlag(required))
        {
            throw ApiException.BadRequest(
                $"Adapter '{connection.adapterKey}' does not support {required}",
                code: "adapter_capability_missing");
        }

        return (connection, adapter);
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> work)
        => InvokeAsync(work, failureCode: null);

    public static async Task<T> InvokeAsync<T>(Func<Task<T>> work, string? failureCode)
    {
        try
        {
            return await work();
        }
        catch (ApiException)
        {
            throw;
        }
        catch (NotSupportedException ex)
        {
            throw ApiException.BadRequest(ex.Message, code: "adapter_capability_missing");
        }
        catch (ArgumentException ex)
        {
            throw ApiException.BadRequest(ex.Message, code: "validation_error");
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(failureCode))
        {
            throw ApiException.BadRequest(ex.Message, code: failureCode);
        }
    }

    public static OrderQuery BuildOrderQuery(
        int page,
        int pageSize,
        string? cursor,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? channel,
        string? search)
        => new()
        {
            Page = page,
            PageSize = pageSize,
            Cursor = cursor,
            Status = status,
            From = from,
            To = to,
            Channel = channel,
            Search = search
        };

    public async Task<(List<ExternalOrder> Items, int Total, bool HasMore, string? NextCursor)> ListStoredConnectionOrdersAsync(
        MarketplaceConnection connection,
        OrderQuery query,
        CancellationToken ct)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize;
        IReadOnlyList<string> statuses = string.IsNullOrWhiteSpace(query.Status)
            ? MarketplaceOrderBuckets.ListStatusesForAdapter(connection.adapterKey)
            : [query.Status];

        var rows = await _snapshots.ListAsync(
            connection.storeId,
            connection.Id,
            statuses,
            query.Channel,
            query.Search,
            query.From,
            query.To,
            page,
            pageSize,
            ct);
        var total = await _snapshots.CountAsync(
            connection.storeId,
            connection.Id,
            statuses,
            query.Channel,
            query.Search,
            query.From,
            query.To,
            ct);
        var items = rows.Select(_snapshots.ToExternalOrder).ToList();
        var hasMore = page * Math.Max(pageSize, 1) < total;
        return (items, total, hasMore, hasMore ? (page + 1).ToString() : null);
    }

    public async Task<(List<StoredOrderListItem> Items, int Total, bool HasMore, string? NextCursor)> ListStoredStoreTabAsync(
        int storeId,
        string tab,
        OrderQuery query,
        CancellationToken ct)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize;
        var statuses = MarketplaceOrderBuckets.StatusesForOverviewTab(tab);
        var rows = await _snapshots.ListAsync(
            storeId,
            null,
            statuses,
            query.Channel,
            query.Search,
            query.From,
            query.To,
            page,
            pageSize,
            ct);
        var total = await _snapshots.CountAsync(
            storeId,
            null,
            statuses,
            query.Channel,
            query.Search,
            query.From,
            query.To,
            ct);

        var connectionIds = rows.Select(r => r.connectionId).Distinct().ToList();
        var connections = await _db.marketplaceConnections
            .AsNoTracking()
            .Where(c => connectionIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var items = new List<StoredOrderListItem>(rows.Count);
        foreach (var row in rows)
        {
            connections.TryGetValue(row.connectionId, out var connection);
            items.Add(new StoredOrderListItem
            {
                ConnectionId = row.connectionId,
                AdapterKey = connection?.adapterKey ?? "",
                ConnectionName = connection?.name ?? "",
                Order = _snapshots.ToExternalOrder(row)
            });
        }

        var hasMore = page * Math.Max(pageSize, 1) < total;
        return (items, total, hasMore, hasMore ? (page + 1).ToString() : null);
    }

    public async Task<ExternalOrder?> GetStoredOrderAsync(
        MarketplaceConnection connection,
        IMarketplaceAdapter adapter,
        string orderId,
        bool liveFallback,
        CancellationToken ct)
    {
        var row = await _snapshots.FindAsync(connection.storeId, connection.Id, orderId, ct);
        if (row != null)
            return _snapshots.ToExternalOrder(row);

        if (!liveFallback)
            return null;

        var live = await adapter.GetOrderAsync(connection, orderId, ct);
        if (live == null)
            return null;
        await _snapshots.UpsertAsync(connection.storeId, connection.Id, live, ct);
        return live;
    }

    public Task PersistOrderAsync(int storeId, int connectionId, ExternalOrder? order, CancellationToken ct)
    {
        if (order == null || string.IsNullOrWhiteSpace(order.ExternalId))
            return Task.CompletedTask;
        return _snapshots.UpsertAsync(storeId, connectionId, order, ct);
    }

    public static PromotionQuery BuildPromotionQuery(
        int page,
        int pageSize,
        string? cursor,
        string? status,
        string? type)
        => new()
        {
            Page = page,
            PageSize = pageSize,
            Cursor = cursor,
            Status = status,
            Type = type
        };

    public static ShipmentQuery BuildShipmentQuery(int limit, string? cursor, bool openOnly)
        => new()
        {
            Limit = limit,
            Cursor = cursor,
            OpenOnly = openOnly
        };

    /// <summary>
    /// Fills missing item photos from a key → URL map (marketplace id / SKU / offer).
    /// Does not overwrite a URL the adapter already set.
    /// </summary>
    public static void ApplyLocalOrderItemImages(
        IEnumerable<ExternalOrder> orders,
        IReadOnlyDictionary<string, string> imagesByKey)
    {
        ApplyLocalOrderItemField(orders, imagesByKey, item => item.ImageUrl, (item, value) => item.ImageUrl = value);
    }

    /// <summary>
    /// Fills missing item names from a key → title map (marketplace id / SKU / offer).
    /// Does not overwrite a name the adapter already set.
    /// </summary>
    public static void ApplyLocalOrderItemNames(
        IEnumerable<ExternalOrder> orders,
        IReadOnlyDictionary<string, string> namesByKey)
    {
        ApplyLocalOrderItemField(orders, namesByKey, item => item.Name, (item, value) => item.Name = value);
    }

    /// <summary>
    /// Sets <see cref="ExternalOrderItem.LocalProductId"/> from marketplace id / SKU / offer keys.
    /// Does not overwrite an id the adapter already set.
    /// </summary>
    public static void ApplyLocalOrderItemProductIds(
        IEnumerable<ExternalOrder> orders,
        IReadOnlyDictionary<string, int> idsByKey)
    {
        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                if (item.LocalProductId is > 0)
                    continue;
                foreach (var key in ItemImageKeys(item))
                {
                    if (idsByKey.TryGetValue(key, out var id) && id > 0)
                    {
                        item.LocalProductId = id;
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// True when the host should resolve a local catalog link (missing product id, photo, or title).
    /// </summary>
    public static bool NeedsLocalOrderItemLookup(ExternalOrderItem item)
        => item.LocalProductId is not > 0
           || string.IsNullOrWhiteSpace(item.ImageUrl)
           || string.IsNullOrWhiteSpace(item.Name);

    private static void ApplyLocalOrderItemField(
        IEnumerable<ExternalOrder> orders,
        IReadOnlyDictionary<string, string> valuesByKey,
        Func<ExternalOrderItem, string?> read,
        Action<ExternalOrderItem, string> write)
    {
        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                if (!string.IsNullOrWhiteSpace(read(item)))
                    continue;
                foreach (var key in ItemImageKeys(item))
                {
                    if (valuesByKey.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    {
                        write(item, value);
                        break;
                    }
                }
            }
        }
    }

    public async Task AttachLocalOrderItemImagesAsync(
        int connectionId,
        IEnumerable<ExternalOrder> orders,
        CancellationToken ct)
    {
        var list = orders as IList<ExternalOrder> ?? orders.ToList();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in list)
        {
            foreach (var item in order.Items)
            {
                if (!NeedsLocalOrderItemLookup(item))
                    continue;
                foreach (var key in ItemImageKeys(item))
                    keys.Add(key);
            }
        }

        if (keys.Count == 0)
            return;

        var keyList = keys.ToList();
        var links = await _db.productMarketplaceLinks
            .AsNoTracking()
            .Where(link =>
                link.connectionId == connectionId &&
                (keyList.Contains(link.externalProductId) ||
                 (link.externalSku != null && keyList.Contains(link.externalSku))))
            .Select(link => new
            {
                link.externalProductId,
                link.externalSku,
                link.productId,
                ProductName = link.product.name
            })
            .ToListAsync(ct);

        if (links.Count == 0)
            return;

        var idsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var namesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            AddProductIdKey(idsByKey, link.externalProductId, link.productId);
            AddProductIdKey(idsByKey, link.externalSku, link.productId);
            if (string.IsNullOrWhiteSpace(link.ProductName))
                continue;
            AddImageKey(namesByKey, link.externalProductId, link.ProductName);
            AddImageKey(namesByKey, link.externalSku, link.ProductName);
        }

        ApplyLocalOrderItemProductIds(list, idsByKey);
        ApplyLocalOrderItemNames(list, namesByKey);

        var productIds = links.Select(link => link.productId).Distinct().ToList();
        var fileIds = await _db.images
            .AsNoTracking()
            .Where(image => productIds.Contains(image.productId))
            .Select(image => new { image.productId, image.index, image.fileId })
            .ToListAsync(ct);

        var fileByProduct = fileIds
            .GroupBy(row => row.productId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.index).First().fileId);

        var imagesByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (!fileByProduct.TryGetValue(link.productId, out var fileId))
                continue;
            var url = $"/api/files/{fileId}/thumbnail?w=256";
            AddImageKey(imagesByKey, link.externalProductId, url);
            AddImageKey(imagesByKey, link.externalSku, url);
        }

        ApplyLocalOrderItemImages(list, imagesByKey);
    }

    private static IEnumerable<string> ItemImageKeys(ExternalOrderItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExternalProductId))
            yield return item.ExternalProductId.Trim();
        if (!string.IsNullOrWhiteSpace(item.Sku))
            yield return item.Sku.Trim();
        if (!string.IsNullOrWhiteSpace(item.OfferId))
            yield return item.OfferId.Trim();
    }

    private static void AddImageKey(Dictionary<string, string> map, string? key, string url)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        map.TryAdd(key.Trim(), url);
    }

    private static void AddProductIdKey(Dictionary<string, int> map, string? key, int id)
    {
        if (string.IsNullOrWhiteSpace(key) || id <= 0)
            return;
        map.TryAdd(key.Trim(), id);
    }
}
