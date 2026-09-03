using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Middleware;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Models.Marketplace;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Pass-through marketplace prices, orders, and promotions (no local persistence).
/// </summary>
public class MarketplaceCommerceService
{
    private readonly DataContext _db;
    private readonly IMarketplaceAdapterRegistry _adapters;

    public MarketplaceCommerceService(DataContext db, IMarketplaceAdapterRegistry adapters)
    {
        _db = db;
        _adapters = adapters;
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

    public static async Task<T> InvokeAsync<T>(Func<Task<T>> work)
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

    /// <summary>
    /// Fills missing item photos from a key → URL map (marketplace id / SKU / offer).
    /// Does not overwrite a URL the adapter already set.
    /// </summary>
    public static void ApplyLocalOrderItemImages(
        IEnumerable<ExternalOrder> orders,
        IReadOnlyDictionary<string, string> imagesByKey)
    {
        foreach (var order in orders)
        {
            foreach (var item in order.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.ImageUrl))
                    continue;
                foreach (var key in ItemImageKeys(item))
                {
                    if (imagesByKey.TryGetValue(key, out var url) && !string.IsNullOrWhiteSpace(url))
                    {
                        item.ImageUrl = url;
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
                if (!string.IsNullOrWhiteSpace(item.ImageUrl))
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
            .Select(link => new { link.externalProductId, link.externalSku, link.productId })
            .ToListAsync(ct);

        if (links.Count == 0)
            return;

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
}
