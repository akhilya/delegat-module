using Delegat.Marketplace.SDK.RateLimit;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;

namespace Delegat.Services.Marketplace;

/// <summary>
/// Base class for marketplace adapters.
/// Provides default implementations for optional methods.
/// Concrete adapters inherit from this class.
/// </summary>
public abstract class BaseMarketplaceAdapter : IMarketplaceAdapter
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger Logger;

    protected BaseMarketplaceAdapter(HttpClient httpClient, ILogger logger)
    {
        HttpClient = httpClient;
        Logger = logger;
    }

    #region Abstract Metadata (must be implemented)

    public abstract string Key { get; }
    public abstract string DisplayName { get; }
    public virtual string? Description => null;
    public virtual string? LogoUrl => null;
    public virtual string? ProductEditUrlTemplate => null;
    public abstract MarketplaceCapabilities Capabilities { get; }
    public abstract CredentialsSchema GetCredentialsSchema();

    public virtual RateLimitConfig? GetRateLimitConfig() => null;

    public virtual Task<List<QuotaInfo>> GetQuotasAsync(
        MarketplaceConnection connection, CancellationToken ct = default)
        => Task.FromResult(new List<QuotaInfo>());

    public virtual MarketplaceRateLimiter? GetRateLimiterForConnection(MarketplaceConnection connection)
        => null;

    #endregion

    #region Abstract Methods (must be implemented)

    public abstract Task<ConnectionTestResult> TestConnectionAsync(
        MarketplaceConnection connection,
        CancellationToken ct = default);

    #endregion

    #region Virtual Methods (can be overridden)

    public virtual Task<List<ExternalCategory>> GetCategoriesAsync(
        MarketplaceConnection connection,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.GetCategories);
        throw new NotImplementedException($"GetCategoriesAsync not implemented for {Key}");
    }

    public virtual Task<List<ExternalField>> GetCategoryFieldsAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.GetCategoryFields);
        throw new NotImplementedException($"GetCategoryFieldsAsync not implemented for {Key}");
    }

    public virtual Task<CreateCategoryResult> CreateCategoryAsync(
        MarketplaceConnection connection,
        CreateCategoryRequest request,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.CreateCategory);
        throw new NotImplementedException($"CreateCategoryAsync not implemented for {Key}");
    }

    public virtual Task<ExternalProduct?> ImportProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ImportProduct);
        throw new NotImplementedException($"ImportProductAsync not implemented for {Key}");
    }

    public virtual async Task<List<ExternalProduct>> ImportProductsBatchAsync(
        MarketplaceConnection connection,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        // Default fallback: call ImportProductAsync for each ID
        var results = new List<ExternalProduct>();
        foreach (var id in externalProductIds)
        {
            var product = await ImportProductAsync(connection, id, ct);
            if (product != null)
                results.Add(product);
        }
        return results;
    }

    public virtual Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ImportAllProducts);
        throw new NotImplementedException($"ImportAllProductsAsync not implemented for {Key}");
    }

    public virtual Task<ExportResult> ExportProductAsync(
        MarketplaceConnection connection,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ExportProduct);
        throw new NotImplementedException($"ExportProductAsync not implemented for {Key}");
    }

    public virtual async Task<BulkExportResult> ExportProductsBulkAsync(
        MarketplaceConnection connection,
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
                var result = await ExportProductAsync(connection, item.ProductData, item.ExistingExternalId, ct);
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

    public virtual Task<ExportResult> UpdateSkuAsync(
        MarketplaceConnection connection,
        string externalProductId,
        string newSku,
        string? currentSku = null,
        CancellationToken ct = default)
    {
        if (!Capabilities.HasFlag(MarketplaceCapabilities.UpdateSku))
        {
            throw new NotSupportedException($"Marketplace {DisplayName} does not support SKU updates");
        }
        throw new NotImplementedException($"UpdateSkuAsync not implemented for {Key}");
    }

    public virtual Task<ExportResult> ArchiveProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default)
    {
        if (!Capabilities.HasFlag(MarketplaceCapabilities.ArchiveProduct))
            throw new NotSupportedException($"Marketplace {DisplayName} does not support product archiving");
        throw new NotImplementedException($"ArchiveProductAsync not implemented for {Key}");
    }

    public virtual Task<List<PriceInfo>> ImportPricesAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ImportPrices);
        throw new NotImplementedException($"ImportPricesAsync not implemented for {Key}");
    }

    public virtual Task<ImportResult> ExportPricesAsync(
        MarketplaceConnection connection,
        List<PriceInfo> prices,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ExportPrices);
        throw new NotImplementedException($"ExportPricesAsync not implemented for {Key}");
    }

    public virtual Task<List<StockInfo>> ImportStocksAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ImportStocks);
        throw new NotImplementedException($"ImportStocksAsync not implemented for {Key}");
    }

    public virtual Task<ImportResult> ExportStocksAsync(
        MarketplaceConnection connection,
        List<StockInfo> stocks,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ExportStocks);
        throw new NotImplementedException($"ExportStocksAsync not implemented for {Key}");
    }

    #endregion

    #region Product Grouping

    public virtual FieldBasedGroupingConfig? GetFieldBasedGroupingConfig()
    {
        // By default, field-based grouping is not supported
        return null;
    }

    public virtual Task<CreateGroupResult> CreateGroupAsync(
        MarketplaceConnection connection,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.EndpointBasedGrouping);
        throw new NotImplementedException($"CreateGroupAsync not implemented for {Key}");
    }

    public virtual Task<GroupMembershipResult> AddProductsToGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ModifyGroupMembership);
        throw new NotImplementedException($"AddProductsToGroupAsync not implemented for {Key}");
    }

    public virtual Task<GroupMembershipResult> RemoveProductsFromGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ModifyGroupMembership);
        throw new NotImplementedException($"RemoveProductsFromGroupAsync not implemented for {Key}");
    }

    public virtual Task<ExportResult> DeleteGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.DeleteGroups);
        throw new NotImplementedException($"DeleteGroupAsync not implemented for {Key}");
    }

    public virtual Task<PagedResult<ExternalGroup>> ImportGroupsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default)
    {
        EnsureCapability(MarketplaceCapabilities.ImportGroups);
        throw new NotImplementedException($"ImportGroupsAsync not implemented for {Key}");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Verify that the adapter supports the specified capability
    /// </summary>
    protected void EnsureCapability(MarketplaceCapabilities capability)
    {
        if (!Capabilities.HasFlag(capability))
        {
            throw new NotSupportedException(
                $"Marketplace {DisplayName} does not support {capability}");
        }
    }

    /// <summary>
    /// Get a value from the credentials JSON
    /// </summary>
    protected string? GetCredential(MarketplaceConnection connection, string key)
    {
        if (string.IsNullOrEmpty(connection.credentialsJson))
            return null;

        try
        {
            var credentials = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                connection.credentialsJson);

            if (credentials != null && credentials.TryGetValue(key, out var value))
                return value;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to parse credentials for connection {ConnectionId}", connection.Id);
        }

        return null;
    }

    /// <summary>
    /// Get a required value from credentials
    /// </summary>
    protected string GetRequiredCredential(MarketplaceConnection connection, string key)
    {
        var value = GetCredential(connection, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required credential: {key}");
        }
        return value;
    }

    #endregion
}
