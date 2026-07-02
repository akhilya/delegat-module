using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.SDK.RateLimit;

namespace Delegat.Marketplace.SDK;

/// <summary>
/// Base class for marketplace plugins.
/// Provides default implementations that throw NotSupportedException for optional operations.
/// Override only the methods your plugin supports.
/// </summary>
public abstract class MarketplacePluginBase : IMarketplacePlugin
{
    #region Abstract - Must implement

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string Version { get; }

    /// <inheritdoc />
    public abstract MarketplaceCapabilities Capabilities { get; }

    /// <inheritdoc />
    public abstract CredentialsSchema GetCredentialsSchema();

    /// <inheritdoc />
    public abstract Task<ConnectionTestResult> TestConnectionAsync(ConnectionContext context, CancellationToken ct = default);

    #endregion

    #region Rate Limiting

    /// <inheritdoc />
    public virtual RateLimitConfig? GetRateLimitConfig() => null;

    /// <inheritdoc />
    public virtual Task<List<QuotaInfo>> GetQuotasAsync(ConnectionContext context, CancellationToken ct = default)
        => Task.FromResult(new List<QuotaInfo>());

    /// <inheritdoc />
    public virtual MarketplaceRateLimiter? GetRateLimiterForConnection(ConnectionContext context) => null;

    #endregion

    #region Virtual - Can override

    /// <inheritdoc />
    public virtual bool IsSystem => false;

    /// <inheritdoc />
    public virtual string? Description => null;

    /// <inheritdoc />
    public virtual string? LogoUrl => null;

    /// <inheritdoc />
    public virtual string? ProductEditUrlTemplate => null;

    /// <inheritdoc />
    public virtual Task<List<ExternalCategory>> GetCategoriesAsync(
        ConnectionContext context,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support GetCategories");
    }

    /// <inheritdoc />
    public virtual Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support GetCategoryFields");
    }

    /// <inheritdoc />
    public virtual Task<CreateCategoryResult> CreateCategoryAsync(
        ConnectionContext context,
        CreateCategoryRequest request,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support CreateCategory");
    }

    /// <inheritdoc />
    public virtual Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ImportProduct");
    }

    /// <inheritdoc />
    public virtual async Task<List<ExternalProduct>> ImportProductsBatchAsync(
        ConnectionContext context,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        var results = new List<ExternalProduct>();
        foreach (var id in externalProductIds)
        {
            var product = await ImportProductAsync(context, id, ct);
            if (product != null)
                results.Add(product);
        }
        return results;
    }

    /// <inheritdoc />
    public virtual Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ImportAllProducts");
    }

    /// <inheritdoc />
    public virtual Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ExportProduct");
    }

    /// <inheritdoc />
    public virtual async Task<BulkExportResult> ExportProductsBulkAsync(
        ConnectionContext context,
        List<BulkExportItem> items,
        CancellationToken ct = default)
    {
        // Default fallback: call ExportProductAsync for each item sequentially
        var results = new List<ExportItemResult>();
        int successCount = 0;
        int failedCount = 0;

        foreach (var item in items)
        {
            try
            {
                var result = await ExportProductAsync(context, item.ProductData, item.ExistingExternalId, ct);
                results.Add(new ExportItemResult(
                    item.LocalProductId,
                    result.Success,
                    result.ExternalProductId,
                    result.ErrorMessage));

                if (result.Success) successCount++;
                else failedCount++;
            }
            catch (Exception ex)
            {
                results.Add(new ExportItemResult(
                    item.LocalProductId,
                    false,
                    ErrorMessage: ex.Message));
                failedCount++;
            }
        }

        return new BulkExportResult(items.Count, successCount, failedCount, results);
    }

    /// <inheritdoc />
    public virtual Task<ExportResult> UpdateSkuAsync(
        ConnectionContext context,
        string externalProductId,
        string newSku,
        string? currentSku = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support UpdateSku");
    }

    /// <inheritdoc />
    public virtual Task<ExportResult> ArchiveProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ArchiveProduct");
    }

    /// <inheritdoc />
    public virtual Task<List<PriceInfo>> ImportPricesAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ImportPrices");
    }

    /// <inheritdoc />
    public virtual Task<ImportResult> ExportPricesAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ExportPrices");
    }

    /// <inheritdoc />
    public virtual Task<List<StockInfo>> ImportStocksAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ImportStocks");
    }

    /// <inheritdoc />
    public virtual Task<ImportResult> ExportStocksAsync(
        ConnectionContext context,
        List<StockInfo> stocks,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ExportStocks");
    }

    #endregion

    #region Product Grouping

    /// <inheritdoc />
    public virtual FieldBasedGroupingConfig? GetFieldBasedGroupingConfig()
    {
        // Returns null by default - no field-based grouping supported
        return null;
    }

    /// <inheritdoc />
    public virtual Task<CreateGroupResult> CreateGroupAsync(
        ConnectionContext context,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support CreateGroup (EndpointBasedGrouping)");
    }

    /// <inheritdoc />
    public virtual Task<GroupMembershipResult> AddProductsToGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support AddProductsToGroup");
    }

    /// <inheritdoc />
    public virtual Task<GroupMembershipResult> RemoveProductsFromGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support RemoveProductsFromGroup");
    }

    /// <inheritdoc />
    public virtual Task<ExportResult> DeleteGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support DeleteGroup");
    }

    /// <inheritdoc />
    public virtual Task<PagedResult<ExternalGroup>> ImportGroupsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default)
    {
        throw new NotSupportedException($"Plugin '{Key}' does not support ImportGroups");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Helper to check if capability is supported
    /// </summary>
    protected void EnsureCapability(MarketplaceCapabilities required)
    {
        if (!Capabilities.HasFlag(required))
        {
            throw new NotSupportedException($"Plugin '{Key}' does not support {required}");
        }
    }

    #endregion
}
