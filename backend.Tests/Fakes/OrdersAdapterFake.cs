using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services.Marketplace;
using ExternalOrder = Delegat.Marketplace.SDK.DTOs.ExternalOrder;
using OrderQuery = Delegat.Marketplace.SDK.DTOs.OrderQuery;

namespace Delegat.Tests.Fakes;

public sealed class OrdersAdapterFake : IMarketplaceAdapter
{
    private readonly Dictionary<string, Queue<PagedResult<ExternalOrder>>> _pages =
        new(StringComparer.OrdinalIgnoreCase);

    public string Key { get; set; } = "ozon";
    public string DisplayName => Key;
    public string? Description => null;
    public string? LogoUrl => null;
    public MarketplaceCapabilities Capabilities { get; set; } = MarketplaceCapabilities.GetOrders;
    public string? ProductEditUrlTemplate => null;

    public int GetOrdersCalls { get; private set; }
    public int GetOrderCalls { get; private set; }
    public List<int> GetOrdersConnectionIds { get; } = [];
    public List<OrderQuery?> GetOrdersQueries { get; } = [];
    public Exception? GetOrdersException { get; set; }
    public Dictionary<string, ExternalOrder> OrdersById { get; } = new(StringComparer.Ordinal);

    public void EnqueuePages(string status, params PagedResult<ExternalOrder>[] pages)
    {
        if (!_pages.TryGetValue(status, out var queue))
        {
            queue = new Queue<PagedResult<ExternalOrder>>();
            _pages[status] = queue;
        }

        foreach (var page in pages)
            queue.Enqueue(page);
    }

    public static PagedResult<ExternalOrder> Page(
        IEnumerable<ExternalOrder> items,
        bool hasMore,
        string? nextCursor = null)
        => new([.. items], hasMore, nextCursor);

    public CredentialsSchema GetCredentialsSchema() => new();
    public RateLimitConfig? GetRateLimitConfig() => null;
    public Task<List<QuotaInfo>> GetQuotasAsync(MarketplaceConnection connection, CancellationToken ct = default)
        => Task.FromResult(new List<QuotaInfo>());
    public MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection) => null;
    public int ClearMetadataCaches(MarketplaceConnection connection) => 0;
    public Task<ConnectionTestResult> TestConnectionAsync(MarketplaceConnection connection, CancellationToken ct = default)
        => Task.FromResult(new ConnectionTestResult(true));
    public Task<List<ExternalCategory>> GetCategoriesAsync(MarketplaceConnection connection, string? parentId = null, string? searchQuery = null, CancellationToken ct = default)
        => Task.FromResult(new List<ExternalCategory>());
    public Task<List<ExternalField>> GetCategoryFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
        => Task.FromResult(new List<ExternalField>());
    public Task<List<ExternalField>> GetPriceFieldsAsync(MarketplaceConnection connection, string externalCategoryId, CancellationToken ct = default)
        => Task.FromResult(new List<ExternalField>());
    public Task<CreateCategoryResult> CreateCategoryAsync(MarketplaceConnection connection, CreateCategoryRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ExternalProduct?> ImportProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<List<ExternalProduct>> ImportProductsBatchAsync(MarketplaceConnection connection, List<string> externalProductIds, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, string? offerIdFilter = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ExportResult> ExportProductAsync(MarketplaceConnection connection, Dictionary<string, object> productData, string? existingExternalId = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<BulkExportResult> ExportProductsBulkAsync(MarketplaceConnection connection, List<BulkExportItem> items, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ExportResult> UpdateSkuAsync(MarketplaceConnection connection, string externalProductId, string newSku, string? currentSku = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ExportResult> ArchiveProductAsync(MarketplaceConnection connection, string externalProductId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<List<PriceInfo>> ImportPricesAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ImportResult> ExportPricesAsync(MarketplaceConnection connection, List<PriceInfo> prices, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<List<StockInfo>> ImportStocksAsync(MarketplaceConnection connection, List<string>? externalProductIds = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ImportResult> ExportStocksAsync(MarketplaceConnection connection, List<StockInfo> stocks, CancellationToken ct = default)
        => throw new NotImplementedException();
    public FieldBasedGroupingConfig? GetFieldBasedGroupingConfig() => null;
    public Task<CreateGroupResult> CreateGroupAsync(MarketplaceConnection connection, List<string> productExternalIds, string? groupName = null, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<GroupMembershipResult> AddProductsToGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<GroupMembershipResult> RemoveProductsFromGroupAsync(MarketplaceConnection connection, string externalGroupId, List<string> productExternalIds, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<ExportResult> DeleteGroupAsync(MarketplaceConnection connection, string externalGroupId, CancellationToken ct = default)
        => throw new NotImplementedException();
    public Task<PagedResult<ExternalGroup>> ImportGroupsAsync(MarketplaceConnection connection, int pageSize = 100, string? cursor = null, CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task<PagedResult<ExternalOrder>> GetOrdersAsync(
        MarketplaceConnection connection,
        OrderQuery? query = null,
        CancellationToken ct = default)
    {
        GetOrdersCalls++;
        GetOrdersConnectionIds.Add(connection.Id);
        GetOrdersQueries.Add(query);
        if (GetOrdersException != null)
            throw GetOrdersException;

        var status = query?.Status ?? "";
        if (_pages.TryGetValue(status, out var queue) && queue.Count > 0)
            return Task.FromResult(queue.Dequeue());

        return Task.FromResult(new PagedResult<ExternalOrder>([], false));
    }

    public Task<ExternalOrder?> GetOrderAsync(
        MarketplaceConnection connection,
        string externalOrderId,
        CancellationToken ct = default)
    {
        GetOrderCalls++;
        if (OrdersById.TryGetValue(externalOrderId, out var order))
            return Task.FromResult<ExternalOrder?>(order);
        return Task.FromResult<ExternalOrder?>(null);
    }
}
