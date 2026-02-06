using System.Text.Json;
using PrintO.Marketplace.SDK;
using PrintO.Marketplace.SDK.DTOs;
using PrintO.Models.Marketplace;
using PrintO.Services.Marketplace;

namespace PrintO.Services.Plugins;

/// <summary>
/// Wraps an IMarketplacePlugin to implement IMarketplaceAdapter.
/// Handles conversion between MarketplaceConnection and ConnectionContext.
/// </summary>
public class PluginAdapterWrapper : IMarketplaceAdapter
{
    private readonly IMarketplacePlugin _plugin;

    public PluginAdapterWrapper(IMarketplacePlugin plugin)
    {
        _plugin = plugin;
    }

    #region Metadata

    public string Key => _plugin.Key;
    public string DisplayName => _plugin.DisplayName;
    public string? Description => _plugin.Description;
    public string? LogoUrl => _plugin.LogoUrl;

    public Marketplace.MarketplaceCapabilities Capabilities =>
        (Marketplace.MarketplaceCapabilities)(int)_plugin.Capabilities;

    public Marketplace.CredentialsSchema GetCredentialsSchema()
    {
        var schema = _plugin.GetCredentialsSchema();
        return new Marketplace.CredentialsSchema
        {
            Fields = schema.Fields.Select(f => new Marketplace.CredentialField
            {
                Key = f.Key,
                DisplayName = f.DisplayName,
                Description = f.Description,
                Type = f.Type,
                IsRequired = f.IsRequired,
                IsSecret = f.IsSecret,
                DefaultValue = f.DefaultValue,
                Placeholder = f.Placeholder,
                Options = f.Options?.Select(o => new Marketplace.SelectOption
                {
                    Value = o.Value,
                    Label = o.Label
                }).ToList()
            }).ToList()
        };
    }

    #endregion

    #region Connection

    public async Task<Models.Marketplace.DTOs.ConnectionTestResult> TestConnectionAsync(
        MarketplaceConnection connection, CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.TestConnectionAsync(context, ct);
        return new Models.Marketplace.DTOs.ConnectionTestResult(
            result.Success,
            result.Message,
            result.Details
        );
    }

    #endregion

    #region Categories

    public async Task<List<Models.Marketplace.DTOs.ExternalCategory>> GetCategoriesAsync(
        MarketplaceConnection connection,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var categories = await _plugin.GetCategoriesAsync(context, parentId, searchQuery, ct);

        return categories.Select(c => new Models.Marketplace.DTOs.ExternalCategory(
            c.Id,
            c.Name,
            c.ParentId,
            c.ParentName,
            c.FullPath,
            c.HasChildren,
            c.ChildrenCount
        )).ToList();
    }

    public async Task<List<Models.Marketplace.DTOs.ExternalField>> GetCategoryFieldsAsync(
        MarketplaceConnection connection,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var fields = await _plugin.GetCategoryFieldsAsync(context, externalCategoryId, ct);

        return fields.Select(f => new Models.Marketplace.DTOs.ExternalField(
            f.Key,
            f.Name,
            f.IsRequired,
            f.Type,
            f.Options?.Select(o => new Models.Marketplace.DTOs.ExternalFieldOption(o.Value, o.Label)).ToList(),
            f.Description,
            f.Unit,
            f.MaxLength,
            f.MinValue,
            f.MaxValue,
            f.ValidationPattern,
            f.Group,
            f.SortOrder
        )).ToList();
    }

    #endregion

    #region Products

    public async Task<Models.Marketplace.DTOs.ExternalProduct?> ImportProductAsync(
        MarketplaceConnection connection,
        string externalProductId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var product = await _plugin.ImportProductAsync(context, externalProductId, ct);

        if (product == null) return null;

        return new Models.Marketplace.DTOs.ExternalProduct(
            product.ExternalId,
            product.Sku,
            product.Name,
            product.Description,
            product.Price,
            product.Quantity,
            product.CategoryId,
            product.CategoryName,
            product.Attributes,
            product.ImageUrls,
            product.Barcode,
            product.Brand,
            product.RawData,
            product.GroupId,
            product.GroupName
        );
    }

    public async Task<Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct>> ImportAllProductsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.ImportAllProductsAsync(context, pageSize, cursor, offerIdFilter, ct);

        return new Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalProduct>(
            result.Items.Select(p => new Models.Marketplace.DTOs.ExternalProduct(
                p.ExternalId,
                p.Sku,
                p.Name,
                p.Description,
                p.Price,
                p.Quantity,
                p.CategoryId,
                p.CategoryName,
                p.Attributes,
                p.ImageUrls,
                p.Barcode,
                p.Brand,
                p.RawData,
                p.GroupId,
                p.GroupName
            )).ToList(),
            result.HasMore,
            result.NextCursor,
            result.TotalCount
        );
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> ExportProductAsync(
        MarketplaceConnection connection,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[PluginAdapterWrapper.ExportProductAsync] Calling plugin {_plugin.Key} with {productData.Count} data keys");
        var context = CreateContext(connection);
        var result = await _plugin.ExportProductAsync(context, productData, existingExternalId, ct);
        Console.WriteLine($"[PluginAdapterWrapper.ExportProductAsync] Plugin returned: Success={result.Success}, Error={result.ErrorMessage}");

        return new Models.Marketplace.DTOs.ExportResult(
            result.Success,
            result.ExternalProductId,
            result.ErrorMessage,
            result.Details
        );
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> UpdateSkuAsync(
        MarketplaceConnection connection,
        string externalProductId,
        string newSku,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.UpdateSkuAsync(context, externalProductId, newSku, ct);

        return new Models.Marketplace.DTOs.ExportResult(
            result.Success,
            result.ExternalProductId,
            result.ErrorMessage,
            result.Details
        );
    }

    #endregion

    #region Prices & Stocks

    public async Task<List<Models.Marketplace.DTOs.PriceInfo>> ImportPricesAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var prices = await _plugin.ImportPricesAsync(context, externalProductIds, ct);

        return prices.Select(p => new Models.Marketplace.DTOs.PriceInfo(
            p.ExternalProductId,
            p.Price,
            p.OldPrice,
            p.Stock,
            p.DiscountedPrice,
            p.Currency
        )).ToList();
    }

    public async Task<Models.Marketplace.DTOs.ImportResult> ExportPricesAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.PriceInfo> prices,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var sdkPrices = prices.Select(p => new PriceInfo(
            p.ExternalProductId,
            p.Price,
            p.OldPrice,
            p.Stock,
            p.DiscountedPrice,
            p.Currency
        )).ToList();

        var result = await _plugin.ExportPricesAsync(context, sdkPrices, ct);

        return new Models.Marketplace.DTOs.ImportResult(
            result.TotalCount,
            result.SuccessCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.ImportError(
                e.ExternalId,
                e.Message,
                e.Details
            )).ToList()
        );
    }

    public async Task<List<Models.Marketplace.DTOs.StockInfo>> ImportStocksAsync(
        MarketplaceConnection connection,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var stocks = await _plugin.ImportStocksAsync(context, externalProductIds, ct);

        return stocks.Select(s => new Models.Marketplace.DTOs.StockInfo(
            s.ExternalProductId,
            s.Quantity,
            s.WarehouseId,
            s.WarehouseName
        )).ToList();
    }

    public async Task<Models.Marketplace.DTOs.ImportResult> ExportStocksAsync(
        MarketplaceConnection connection,
        List<Models.Marketplace.DTOs.StockInfo> stocks,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var sdkStocks = stocks.Select(s => new StockInfo(
            s.ExternalProductId,
            s.Quantity,
            s.WarehouseId,
            s.WarehouseName
        )).ToList();

        var result = await _plugin.ExportStocksAsync(context, sdkStocks, ct);

        return new Models.Marketplace.DTOs.ImportResult(
            result.TotalCount,
            result.SuccessCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.ImportError(
                e.ExternalId,
                e.Message,
                e.Details
            )).ToList()
        );
    }

    #endregion

    #region Product Grouping

    public Models.Marketplace.DTOs.FieldBasedGroupingConfig? GetFieldBasedGroupingConfig()
    {
        var config = _plugin.GetFieldBasedGroupingConfig();
        if (config == null) return null;

        return new Models.Marketplace.DTOs.FieldBasedGroupingConfig(
            config.GroupFieldKey,
            config.MarketplaceGeneratesId,
            config.Description
        );
    }

    public async Task<Models.Marketplace.DTOs.CreateGroupResult> CreateGroupAsync(
        MarketplaceConnection connection,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.CreateGroupAsync(context, productExternalIds, groupName, ct);

        return new Models.Marketplace.DTOs.CreateGroupResult(
            result.Success,
            result.ExternalGroupId,
            result.ErrorMessage,
            result.Details
        );
    }

    public async Task<Models.Marketplace.DTOs.GroupMembershipResult> AddProductsToGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.AddProductsToGroupAsync(context, externalGroupId, productExternalIds, ct);

        return new Models.Marketplace.DTOs.GroupMembershipResult(
            result.Success,
            result.AddedCount,
            result.RemovedCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.GroupMembershipError(
                e.ExternalProductId,
                e.Message,
                e.Details
            )).ToList(),
            result.ErrorMessage
        );
    }

    public async Task<Models.Marketplace.DTOs.GroupMembershipResult> RemoveProductsFromGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.RemoveProductsFromGroupAsync(context, externalGroupId, productExternalIds, ct);

        return new Models.Marketplace.DTOs.GroupMembershipResult(
            result.Success,
            result.AddedCount,
            result.RemovedCount,
            result.FailedCount,
            result.Errors?.Select(e => new Models.Marketplace.DTOs.GroupMembershipError(
                e.ExternalProductId,
                e.Message,
                e.Details
            )).ToList(),
            result.ErrorMessage
        );
    }

    public async Task<Models.Marketplace.DTOs.ExportResult> DeleteGroupAsync(
        MarketplaceConnection connection,
        string externalGroupId,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.DeleteGroupAsync(context, externalGroupId, ct);

        return new Models.Marketplace.DTOs.ExportResult(
            result.Success,
            result.ExternalProductId,
            result.ErrorMessage,
            result.Details
        );
    }

    public async Task<Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalGroup>> ImportGroupsAsync(
        MarketplaceConnection connection,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default)
    {
        var context = CreateContext(connection);
        var result = await _plugin.ImportGroupsAsync(context, pageSize, cursor, ct);

        return new Models.Marketplace.DTOs.PagedResult<Models.Marketplace.DTOs.ExternalGroup>(
            result.Items.Select(g => new Models.Marketplace.DTOs.ExternalGroup(
                g.ExternalId,
                g.Name,
                g.ProductExternalIds,
                g.AdditionalData
            )).ToList(),
            result.HasMore,
            result.NextCursor,
            result.TotalCount
        );
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Create ConnectionContext from MarketplaceConnection.
    /// </summary>
    private static ConnectionContext CreateContext(MarketplaceConnection connection)
    {
        var credentials = new Dictionary<string, string>();
        var settings = new Dictionary<string, string>();

        // Parse credentials JSON
        if (!string.IsNullOrEmpty(connection.credentialsJson))
        {
            try
            {
                var creds = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connection.credentialsJson);
                if (creds != null)
                {
                    foreach (var kvp in creds)
                    {
                        credentials[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        // Parse settings JSON
        if (!string.IsNullOrEmpty(connection.settingsJson))
        {
            try
            {
                var setts = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(connection.settingsJson);
                if (setts != null)
                {
                    foreach (var kvp in setts)
                    {
                        settings[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        return new ConnectionContext
        {
            ConnectionId = connection.Id,
            Credentials = credentials,
            Settings = settings
        };
    }

    #endregion
}
