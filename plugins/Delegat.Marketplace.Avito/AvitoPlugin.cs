using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Collections.Concurrent;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Marketplace.SDK.RateLimit;
using Microsoft.Extensions.Logging;

namespace Delegat.Marketplace.Avito;

/// <summary>Avito API adapter for account access, listing import, category metadata, and price updates.</summary>
public sealed class AvitoPlugin : MarketplacePluginBase, IMarketplaceSandboxReadBroker
{
    private static readonly ConcurrentDictionary<int, MarketplaceRateLimiter> BrokerRateLimiters = new();
    private readonly Func<HttpMessageHandler?> _handlerFactory;

    public AvitoPlugin() : this(() => null)
    {
    }

    internal AvitoPlugin(Func<HttpMessageHandler?> handlerFactory)
    {
        _handlerFactory = handlerFactory;
    }

    public override string Key => "avito";
    public override string DisplayName => "Avito";
    public override string Version => "0.2.0";
    public override PluginStability Stability => PluginStability.Experimental;
    public override bool IsSystem => true;
    public override string? Description => "Avito listings import, category metadata, and price updates";
    public override string? LogoUrl => "https://www.avito.ru/";
    public override MarketplaceCapabilities Capabilities =>
        MarketplaceCapabilities.GetCategories |
        MarketplaceCapabilities.GetCategoryFields |
        MarketplaceCapabilities.ImportProduct |
        MarketplaceCapabilities.ImportAllProducts |
        MarketplaceCapabilities.ImportProductBatch |
        MarketplaceCapabilities.ImportPrices |
        MarketplaceCapabilities.ExportPrices;

    public override RateLimitConfig? GetRateLimitConfig() => new() { MaxRequestsPerSecond = 1 };

    public async Task<MarketplaceSandboxReadResponse> ReadAsync(
        ConnectionContext context, string hostKey, string path, CancellationToken ct = default)
    {
        if (!string.Equals(hostKey, "api", StringComparison.Ordinal))
            return new(null, null, false, "host_not_allowed");
        if (!IsSafeBrokerPath(path))
            return new(null, null, false, "invalid_path");
        if (string.IsNullOrWhiteSpace(context.GetCredential("clientId"))
            || string.IsNullOrWhiteSpace(context.GetCredential("clientSecret")))
            return new(null, null, false, "credentials_missing");

        var limiter = BrokerRateLimiters.GetOrAdd(
            context.ConnectionId, _ => new MarketplaceRateLimiter(maxRequestsPerSecond: 1, maxConcurrent: 1));
        await limiter.AcquireAsync(ct);

        try
        {
            using var client = CreateClient(context, disableRedirects: true);
            return await client.ReadOnlyBrokerAsync(path, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Logger?.LogWarning(ex, "Avito read broker request failed for connection {ConnectionId}", context.ConnectionId);
            return new(null, null, false, "upstream_unavailable");
        }
    }

    private static bool IsSafeBrokerPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Length <= 2_048
        && path[0] == '/'
        && !path.StartsWith("//", StringComparison.Ordinal)
        && !path.Contains('\\')
        && !path.Contains('#')
        && !path.Any(char.IsControl);

    public override CredentialsSchema GetCredentialsSchema() => new()
    {
        Fields =
        [
            new CredentialField
            {
                Key = "clientId",
                DisplayName = "Client ID",
                Description = "Avito API application client ID",
                Type = "text",
                IsRequired = true,
                Placeholder = "Client ID"
            },
            new CredentialField
            {
                Key = "clientSecret",
                DisplayName = "Client secret",
                Description = "Avito API application client secret",
                Type = "password",
                IsRequired = true,
                IsSecret = true,
                Placeholder = "Client secret"
            }
        ]
    };

    public override async Task<ConnectionTestResult> TestConnectionAsync(ConnectionContext context, CancellationToken ct = default)
    {
        try
        {
            var api = new AvitoApiClient(context, _handlerFactory());
            var account = await api.GetCurrentAccountAsync(ct);
            return new ConnectionTestResult(true, "Avito API connection established", new Dictionary<string, object>
            {
                ["accountId"] = account.Id,
                ["name"] = account.Name ?? ""
            });
        }
        catch (AvitoApiException ex)
        {
            var message = ex.StatusCode == HttpStatusCode.Unauthorized
                ? "Avito rejected the client ID or client secret. Check the API application credentials."
                : $"Avito API request failed ({(int)ex.StatusCode}): {ex.Message}";
            return new ConnectionTestResult(false, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, $"Avito connection failed: {ex.Message}");
        }
    }

    public override async Task<List<ExternalCategory>> GetCategoriesAsync(
        ConnectionContext context,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        var categories = await GetFlatCategoriesAsync(context, ct);
        if (!string.IsNullOrWhiteSpace(searchQuery))
            return categories.Where(c => c.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)
                    || (c.FullPath?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        return categories.Where(c => string.Equals(c.ParentId, parentId, StringComparison.Ordinal)).ToList();
    }

    public override async Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalCategoryId))
            return [];

        using var client = CreateClient(context);
        using var document = await client.GetCategoryFieldsAsync(externalCategoryId, ct);
        var fields = new List<ExternalField>();
        if (document.RootElement.TryGetProperty("fields", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
                await AddFieldsAsync(item, fields, client, ct);
        }
        return fields;
    }

    public override Task<List<ExternalField>> GetPriceFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
        => Task.FromResult(new List<ExternalField>
        {
            new("price", "Price", IsRequired: true, Type: "number", MinValue: 0,
                Description: "Avito listing price in whole RUB units.", Group: "Price", SortOrder: 0)
        });

    public override async Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalProductId)) return null;
        var requested = externalProductId.Trim();
        using var client = CreateClient(context);
        for (var page = 1; page <= 10000; page++)
        {
            var items = await client.GetItemsPageAsync(page, 100, ct);
            var match = items.FirstOrDefault(item => item.Id == requested);
            if (match != null) return ToExternalProduct(match);
            if (items.Count < 100) return null;
        }
        return null;
    }

    public override async Task<List<ExternalProduct>> ImportProductsBatchAsync(
        ConnectionContext context,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        var pending = externalProductIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
        if (pending.Count == 0) return [];

        var found = new List<ExternalProduct>();
        using var client = CreateClient(context);
        for (var page = 1; page <= 10000 && pending.Count > 0; page++)
        {
            var items = await client.GetItemsPageAsync(page, 100, ct);
            foreach (var item in items.Where(item => pending.Remove(item.Id)))
                found.Add(ToExternalProduct(item));
            if (items.Count < 100) break;
        }
        return found;
    }

    public override async Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(offerIdFilter))
            throw new NotSupportedException("Avito's listing API does not expose seller article filtering.");
        pageSize = Math.Clamp(pageSize, 1, 100);
        var page = int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPage)
            ? Math.Max(1, parsedPage)
            : 1;
        using var client = CreateClient(context);
        var items = await client.GetItemsPageAsync(page, pageSize, ct);
        var hasMore = items.Count == pageSize;
        return new PagedResult<ExternalProduct>(
            items.Select(ToExternalProduct).ToList(), hasMore, hasMore ? (page + 1).ToString(CultureInfo.InvariantCulture) : null);
    }

    public override async Task<List<PriceInfo>> ImportPricesAsync(
        ConnectionContext context,
        List<string>? externalProductIds = null,
        CancellationToken ct = default)
    {
        var requested = externalProductIds?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).ToHashSet(StringComparer.Ordinal);
        var result = new List<PriceInfo>();
        using var client = CreateClient(context);
        for (var page = 1; page <= 10000; page++)
        {
            var items = await client.GetItemsPageAsync(page, 100, ct);
            result.AddRange(items.Where(item => requested == null || requested.Contains(item.Id))
                .Select(item => new PriceInfo(item.Id, item.Price)));
            if (items.Count < 100 || (requested != null && requested.All(id => result.Any(x => x.ExternalProductId == id))))
                break;
        }
        return result;
    }

    public override async Task<ImportResult> ExportPricesAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct = default)
    {
        var errors = new List<ImportError>();
        var successCount = 0;
        using var client = CreateClient(context);
        foreach (var item in prices)
        {
            if (string.IsNullOrWhiteSpace(item.ExternalProductId)
                || item.Price is null
                || item.Price < 0
                || item.Price > int.MaxValue
                || decimal.Truncate(item.Price.Value) != item.Price.Value)
            {
                errors.Add(new ImportError(item.ExternalProductId, "Avito price must be a non-negative whole number."));
                continue;
            }

            try
            {
                if (await client.UpdatePriceAsync(item.ExternalProductId, decimal.ToInt32(item.Price.Value), ct))
                    successCount++;
                else
                    errors.Add(new ImportError(item.ExternalProductId, "Avito did not confirm the price update."));
            }
            catch (AvitoApiException ex)
            {
                errors.Add(new ImportError(item.ExternalProductId,
                    MarketplaceApiErrorText.Format("Avito", (int)ex.StatusCode, ex.Message, null)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new ImportError(item.ExternalProductId, MarketplaceApiErrorText.Format("Avito", ex)));
            }
        }
        return new ImportResult(prices.Count, successCount, errors.Count, errors.Count == 0 ? null : errors);
    }

    private AvitoApiClient CreateClient(ConnectionContext context, bool disableRedirects = false) =>
        new(context, _handlerFactory(), disableRedirects);

    private async Task<List<ExternalCategory>> GetFlatCategoriesAsync(ConnectionContext context, CancellationToken ct)
    {
        const string cacheKey = "avito:autoload:category-tree";
        if (context.MetadataCache?.TryGetValue(cacheKey, out List<ExternalCategory>? cached) == true && cached != null)
            return cached;

        using var client = CreateClient(context);
        var roots = await client.GetCategoryTreeAsync(ct);
        var result = new List<ExternalCategory>();
        foreach (var root in roots)
            FlattenCategory(root, null, "", result);
        context.MetadataCache?.Set(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }

    private static void FlattenCategory(AvitoCategoryNode node, string? parentId, string path, List<ExternalCategory> result)
    {
        if (string.IsNullOrWhiteSpace(node.Slug))
            return;
        var id = node.Slug;
        var fullPath = string.IsNullOrEmpty(path) ? node.Name : $"{path} / {node.Name}";
        result.Add(new ExternalCategory(id, node.Name, parentId, FullPath: fullPath,
            HasChildren: node.Children.Count > 0, ChildrenCount: node.Children.Count));
        foreach (var child in node.Children)
            FlattenCategory(child, id, fullPath, result);
    }

    private static async Task AddFieldsAsync(JsonElement item, List<ExternalField> output, AvitoApiClient client, CancellationToken ct)
    {
        var key = GetString(item, "tag");
        var label = GetString(item, "label");
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(label))
        {
            var content = item.TryGetProperty("content", out var contentValue) && contentValue.ValueKind == JsonValueKind.Array
                ? contentValue.EnumerateArray().ToList()
                : [];
            var fieldType = content.Select(x => GetString(x, "field_type")).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "input";
            var dataType = content.Select(x => GetString(x, "data_type")).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "string";
            var type = fieldType switch
            {
                "select" => "select",
                "checkbox" => "multiselect",
                _ when dataType is "integer" or "float" => "number",
                _ => "string"
            };
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rule in content)
            {
                if (rule.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                    AddOptions(values, options);
                var valuesUrl = GetString(rule, "values_link_json");
                if (!string.IsNullOrWhiteSpace(valuesUrl))
                {
                    try
                    {
                        foreach (var value in await client.GetFieldValuesAsync(valuesUrl, ct))
                            options.TryAdd(value.Value, value.Label);
                    }
                    catch (AvitoApiException)
                    {
                        // Preserve the field even when Avito's optional dictionary is unavailable.
                    }
                }
            }

            var required = content.Any(x => GetBoolean(x, "required") || GetBoolean(x, "required_by_dependency"));
            var range = content.Select(x => x.TryGetProperty("values_range_v2", out var value) ? value
                : x.TryGetProperty("values_range", out var fallback) ? fallback : default)
                .FirstOrDefault(x => x.ValueKind == JsonValueKind.Object);
            var min = GetDecimal(range, "min");
            var max = GetDecimal(range, "max");
            int? maxLength = type == "string" && max.HasValue ? decimal.ToInt32(max.Value) : null;
            output.Add(new ExternalField(
                Key: key,
                Name: label,
                IsRequired: required,
                Type: type,
                Options: options.Count == 0 ? null : options.Select(x => new ExternalFieldOption(x.Key, x.Value)).ToList(),
                Description: GetString(item, "descriptions"),
                MaxLength: maxLength,
                MinValue: min,
                MaxValue: type == "number" ? max : null,
                Group: "Avito listing fields",
                SortOrder: output.Count));
        }

        if (item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                await AddFieldsAsync(child, output, client, ct);
    }

    private static void AddOptions(JsonElement values, Dictionary<string, string> options)
    {
        foreach (var value in values.EnumerateArray())
        {
            var optionValue = value.ValueKind == JsonValueKind.String ? value.GetString()
                : value.ValueKind == JsonValueKind.Object ? GetString(value, "value") : null;
            if (!string.IsNullOrWhiteSpace(optionValue))
                options.TryAdd(optionValue, value.ValueKind == JsonValueKind.Object ? GetString(value, "description") ?? optionValue : optionValue);
        }
    }

    private static ExternalProduct ToExternalProduct(AvitoItem item)
        => new(item.Id, null, item.Name, Price: item.Price, CategoryId: item.CategoryId,
            CategoryName: item.CategoryName, RawData: new Dictionary<string, object>
            {
                ["status"] = item.Status ?? "",
                ["url"] = item.Url ?? ""
            });

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBoolean(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.True;

    private static decimal? GetDecimal(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
}
