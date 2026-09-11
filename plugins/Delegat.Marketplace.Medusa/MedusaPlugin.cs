using System.Collections;
using System.Globalization;
using System.Text.Json;
using Delegat.Marketplace.Medusa.Models;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.SDK.DTOs;

namespace Delegat.Marketplace.Medusa;

public class MedusaPlugin : MarketplacePluginBase
{
    private const int DefaultBulkChunkSize = 50;
    private const int MaxBulkChunkSize = 200;

    private static readonly HashSet<string> InternalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "_categoryId",
        "_fieldTypes",
        "_fieldLabels",
        "_fieldOptions",
        "_shopSpecs",
        "_localGroupId",
        "_localGroupName",
        "_variants",
        "_imageUrls",
        "_imageStoragePaths",
        "_localProductId"
    };

    private static readonly HashSet<string> KnownProductKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "sku", "name", "description", "short_description", "shortDescription",
        "price", "sale_price", "salePrice", "oldPrice", "old_price",
        "quantity", "stock", "storageQuantity", "barcode", "brand",
        "status", "delegat_product_id", "delegatProductId", "product_id", "productId",
        "images", "image_urls", "imageUrls",
        "group_id", "groupId", "group_name", "groupName"
    };

    private static readonly HashSet<string> PriceFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "price", "sale_price", "salePrice", "old_price", "oldPrice"
    };

    public override string Key => "medusa";
    public override string DisplayName => "Medusa";
    public override string Version => "0.2.1";
    public override PluginStability Stability => PluginStability.Experimental;
    public override bool IsSystem => true;
    public override string? Description =>
        "Medusa store via Delegat bridge: categories, product groups, products, prices, stocks, and orders.";
    public override string? LogoUrl => "https://raw.githubusercontent.com/medusajs/medusa/develop/www/apps/book/app/images/logo.svg";

    public override MarketplaceCapabilities Capabilities =>
        MarketplaceCapabilities.GetCategories |
        MarketplaceCapabilities.GetCategoryFields |
        MarketplaceCapabilities.CreateCategory |
        MarketplaceCapabilities.ImportProduct |
        MarketplaceCapabilities.ExportProduct |
        MarketplaceCapabilities.ExportProductBulk |
        MarketplaceCapabilities.ExportPrices |
        MarketplaceCapabilities.ExportStocks |
        MarketplaceCapabilities.EndpointBasedGrouping |
        MarketplaceCapabilities.ImportGroups |
        MarketplaceCapabilities.DeleteGroups |
        MarketplaceCapabilities.GetOrders |
        MarketplaceCapabilities.UpdateOrderStatus;

    public override CredentialsSchema GetCredentialsSchema()
    {
        return new CredentialsSchema
        {
            Fields =
            [
                new CredentialField
                {
                    Key = "apiBaseUrl",
                    DisplayName = "Delegat bridge base URL",
                    Description = "Medusa admin bridge, e.g. http://localhost:9000/admin/delegat",
                    Type = "text",
                    IsRequired = true,
                    Placeholder = "http://localhost:9000/admin/delegat"
                },
                new CredentialField
                {
                    Key = "apiKey",
                    DisplayName = "Secret API key",
                    Description = "Medusa secret API key (sk_…). Settings → Developer → Secret API Keys.",
                    Type = "password",
                    IsRequired = true,
                    IsSecret = true
                },
                new CredentialField
                {
                    Key = "bulkChunkSize",
                    DisplayName = "Bulk chunk size",
                    Description = "Maximum products per bulk request to the bridge",
                    Type = "number",
                    IsRequired = false,
                    DefaultValue = DefaultBulkChunkSize.ToString(CultureInfo.InvariantCulture)
                }
            ]
        };
    }

    public override async Task<ConnectionTestResult> TestConnectionAsync(
        ConnectionContext context,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var health = await client.GetHealthAsync(ct);
            if (!health.Success)
                return new ConnectionTestResult(false, "Bridge health check returned success=false");

            return new ConnectionTestResult(
                true,
                "Medusa bridge connection established",
                new Dictionary<string, object>
                {
                    ["storeName"] = health.StoreName ?? "",
                    ["medusaVersion"] = health.MedusaVersion ?? "",
                    ["apiVersion"] = health.ApiVersion ?? "",
                    ["capabilities"] = health.Capabilities ?? []
                });
        }
        catch (MedusaApiException ex)
        {
            return new ConnectionTestResult(
                false,
                MarketplaceApiErrorText.Format("Medusa", ex.StatusCode, ex.Message, ex.ResponseBody));
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, $"Error: {ex.Message}");
        }
    }

    public override async Task<List<ExternalCategory>> GetCategoriesAsync(
        ConnectionContext context,
        string? parentId = null,
        string? searchQuery = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);
        var response = await client.GetCategoriesAsync(parentId, searchQuery, ct);
        if (!response.Success)
            throw new MedusaApiException(response.Error ?? "Failed to list categories", 500);

        return (response.Categories ?? [])
            .Select(c => new ExternalCategory(
                c.Id,
                c.Name,
                c.ParentId,
                c.ParentName,
                c.FullPath,
                c.HasChildren,
                c.ChildrenCount))
            .ToList();
    }

    public override async Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);
        try
        {
            var response = await client.GetCategoryFieldsAsync(externalCategoryId, ct);
            var rawFields = response.Fields;
            if (rawFields is { Count: > 0 })
            {
                var fromBridge = rawFields
                    .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !IsPriceFieldKey(f.Key))
                    .OrderBy(f => f.SortOrder)
                    .Select(f => new ExternalField(
                        f.Key,
                        string.IsNullOrWhiteSpace(f.Name) ? f.Key : f.Name,
                        f.IsRequired,
                        NormalizeFieldType(f.Type),
                        f.Options?.Select(o => new ExternalFieldOption(o.Value, o.Label)).ToList(),
                        f.Description,
                        Group: f.Group,
                        SortOrder: f.SortOrder))
                    .ToList();
                if (fromBridge.Count > 0)
                    return EnsureDefaultCardFields(fromBridge);
            }
        }
        catch (MedusaApiException ex) when (ex.StatusCode is 404 or 405)
        {
        }

        return GetDefaultFields();
    }

    public override Task<List<ExternalField>> GetPriceFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
        => Task.FromResult(GetPriceFields());

    public override async Task<CreateCategoryResult> CreateCategoryAsync(
        ConnectionContext context,
        CreateCategoryRequest request,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var response = await client.UpsertCategoryAsync(new MedusaUpsertCategoryRequest
            {
                Name = request.Name,
                Description = request.Description,
                ParentId = request.ParentId,
                SortOrder = request.SortOrder,
                Fields = request.Fields
                    .Where(f => !string.IsNullOrWhiteSpace(f.Key))
                    .Select(f => new MedusaFieldDto
                    {
                        Key = f.Key.Trim(),
                        Name = string.IsNullOrWhiteSpace(f.Name) ? f.Key.Trim() : f.Name.Trim(),
                        Type = NormalizeFieldType(f.Type),
                        IsRequired = false,
                        Description = f.Description,
                        Group = f.Group ?? "Attributes",
                        SortOrder = f.SortOrder,
                        Options = f.Options?.Select(o => new MedusaFieldOptionDto
                        {
                            Value = o.Value,
                            Label = o.Label
                        }).ToList()
                    })
                    .ToList()
            }, ct);

            if (!response.Success || string.IsNullOrWhiteSpace(response.CategoryId))
                return new CreateCategoryResult(false, ErrorMessage: response.Error ?? "Category upsert failed");

            return new CreateCategoryResult(
                true,
                response.CategoryId,
                response.Name ?? request.Name,
                MergeCategoryFields(request.Fields));
        }
        catch (MedusaApiException ex)
        {
            return new CreateCategoryResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex.StatusCode, ex.Message, ex.ResponseBody));
        }
    }

    public override async Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalProductId))
            return null;

        try
        {
            using var client = CreateClient(context);
            var response = await client.GetProductAsync(externalProductId.Trim(), ct);
            if (response is null || !response.Success || string.IsNullOrWhiteSpace(response.ProductId))
                return null;

            return new ExternalProduct(
                ExternalId: response.ProductId,
                Sku: response.Sku,
                Name: response.Name ?? "",
                Description: response.Description,
                Price: response.Price,
                Quantity: response.Quantity,
                CategoryId: string.IsNullOrWhiteSpace(response.CategoryId) ? null : response.CategoryId.Trim(),
                CategoryName: string.IsNullOrWhiteSpace(response.CategoryName) ? null : response.CategoryName.Trim(),
                ImageUrls: response.ImageUrls);
        }
        catch (MedusaApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public override async Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = BuildProductPayload(productData, existingExternalId);
            if (string.IsNullOrWhiteSpace(payload.Name))
                return new ExportResult(false, ErrorMessage: "Product name is required");

            using var client = CreateClient(context);
            var response = await client.UpsertProductAsync(payload, ct);
            if (!response.Success || string.IsNullOrWhiteSpace(response.ProductId))
                return new ExportResult(false, ErrorMessage: FormatBridgeError(response.Error) ?? "Product upsert failed");

            return new ExportResult(
                true,
                response.ProductId,
                Details: ExportIdentityDetails(existingExternalId, response.ProductId, response.VariantIds));
        }
        catch (MedusaApiException ex)
        {
            return new ExportResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex));
        }
        catch (Exception ex)
        {
            return new ExportResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex));
        }
    }

    public override async Task<BulkExportResult> ExportProductsBulkAsync(
        ConnectionContext context,
        List<BulkExportItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return new BulkExportResult(0, 0, 0, []);

        var chunkSize = GetBulkChunkSize(context);
        var allResults = new List<ExportItemResult>();
        var success = 0;
        var failed = 0;

        using var client = CreateClient(context);

        foreach (var chunk in Chunk(items, chunkSize))
        {
            var payloads = new List<MedusaProductPayload>();
            var indexMap = new List<BulkExportItem>();

            foreach (var item in chunk)
            {
                try
                {
                    var payload = BuildProductPayload(item.ProductData, item.ExistingExternalId);
                    payload.RawFields ??= new Dictionary<string, object>();
                    payload.RawFields["local_product_id"] = item.LocalProductId;
                    if (string.IsNullOrWhiteSpace(payload.DelegatProductId))
                        payload.DelegatProductId = item.LocalProductId.ToString(CultureInfo.InvariantCulture);
                    payloads.Add(payload);
                    indexMap.Add(item);
                }
                catch (Exception ex)
                {
                    allResults.Add(new ExportItemResult(item.LocalProductId, false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex)));
                    failed++;
                }
            }

            if (payloads.Count == 0)
                continue;

            try
            {
                var response = await client.UpsertProductsBulkAsync(
                    new MedusaBulkProductRequest { Products = payloads }, ct);

                if (response.Results is { Count: > 0 })
                {
                    foreach (var result in response.Results)
                    {
                        var localId = result.LocalProductId
                            ?? (result.Index >= 0 && result.Index < indexMap.Count
                                ? indexMap[result.Index].LocalProductId
                                : 0);
                        var existingId = indexMap.ElementAtOrDefault(result.Index)?.ExistingExternalId
                            ?? indexMap.FirstOrDefault(i => i.LocalProductId == localId)?.ExistingExternalId;
                        allResults.Add(new ExportItemResult(
                            localId,
                            result.Success,
                            result.ProductId,
                            FormatBridgeError(result.Error),
                            result.Success
                                ? ExportIdentityDetails(existingId, result.ProductId, result.VariantIds)
                                : null));
                        if (result.Success) success++;
                        else failed++;
                    }
                }
                else
                {
                    foreach (var item in indexMap)
                    {
                        allResults.Add(new ExportItemResult(
                            item.LocalProductId,
                            response.Success,
                            ErrorMessage: response.Success ? null : FormatBridgeError(response.Error) ?? "Bulk export failed"));
                        if (response.Success) success++;
                        else failed++;
                    }
                }
            }
            catch (Exception ex)
            {
                foreach (var item in indexMap)
                {
                    allResults.Add(new ExportItemResult(item.LocalProductId, false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex)));
                    failed++;
                }
            }
        }

        return new BulkExportResult(items.Count, success, failed, allResults);
    }

    public override async Task<ImportResult> ExportPricesAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct = default)
    {
        if (prices.Count == 0)
            return new ImportResult(0, 0, 0);

        try
        {
            using var client = CreateClient(context);
            var request = new MedusaBulkPricesRequest
            {
                Prices = prices.Select(p => new MedusaPriceItem
                {
                    ProductId = p.ExternalProductId,
                    VariantId = p.VariantId,
                    Price = p.OldPrice ?? p.Price,
                    SalePrice = p.DiscountedPrice ?? (p.OldPrice.HasValue ? p.Price : null)
                }).ToList()
            };

            var response = await client.ExportPricesAsync(request, ct);
            return ToImportResult(response, prices.Count);
        }
        catch (Exception ex)
        {
            return new ImportResult(prices.Count, 0, prices.Count, [new ImportError(null, ex.Message)]);
        }
    }

    public override async Task<ImportResult> ExportStocksAsync(
        ConnectionContext context,
        List<StockInfo> stocks,
        CancellationToken ct = default)
    {
        if (stocks.Count == 0)
            return new ImportResult(0, 0, 0);

        try
        {
            using var client = CreateClient(context);
            var request = new MedusaBulkStocksRequest
            {
                Stocks = stocks.Select(s => new MedusaStockItem
                {
                    ProductId = s.ExternalProductId,
                    VariantId = s.VariantId,
                    Quantity = s.Quantity
                }).ToList()
            };

            var response = await client.ExportStocksAsync(request, ct);
            return ToImportResult(response, stocks.Count);
        }
        catch (Exception ex)
        {
            return new ImportResult(stocks.Count, 0, stocks.Count, [new ImportError(null, ex.Message)]);
        }
    }

    public override async Task<CreateGroupResult> CreateGroupAsync(
        ConnectionContext context,
        List<string> productExternalIds,
        string? groupName = null,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var response = await client.UpsertGroupAsync(new MedusaUpsertGroupRequest
            {
                Name = string.IsNullOrWhiteSpace(groupName) ? "Группа" : groupName.Trim(),
                ProductIds = productExternalIds
            }, ct);
            if (!response.Success || string.IsNullOrWhiteSpace(response.CollectionId))
                return new CreateGroupResult(false, ErrorMessage: response.Error ?? "Group upsert failed");
            return new CreateGroupResult(true, response.CollectionId);
        }
        catch (Exception ex)
        {
            return new CreateGroupResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex));
        }
    }

    public override async Task<GroupMembershipResult> AddProductsToGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var response = await client.AddProductsToGroupAsync(
                externalGroupId,
                new MedusaGroupMembershipRequest { ProductIds = productExternalIds },
                ct);
            return new GroupMembershipResult(
                response.Success,
                AddedCount: response.Added,
                FailedCount: response.Failed,
                ErrorMessage: response.Error);
        }
        catch (Exception ex)
        {
            return new GroupMembershipResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex));
        }
    }

    public override async Task<GroupMembershipResult> RemoveProductsFromGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        List<string> productExternalIds,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var response = await client.RemoveProductsFromGroupAsync(
                externalGroupId,
                new MedusaGroupMembershipRequest { ProductIds = productExternalIds },
                ct);
            return new GroupMembershipResult(
                response.Success,
                RemovedCount: response.Removed,
                ErrorMessage: response.Error);
        }
        catch (Exception ex)
        {
            return new GroupMembershipResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex));
        }
    }

    public override async Task<ExportResult> DeleteGroupAsync(
        ConnectionContext context,
        string externalGroupId,
        CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient(context);
            var response = await client.DeleteGroupAsync(externalGroupId, ct);
            return new ExportResult(response.Success, response.CollectionId, response.Error);
        }
        catch (Exception ex)
        {
            return new ExportResult(false, ErrorMessage: MarketplaceApiErrorText.Format("Medusa", ex));
        }
    }

    public override async Task<PagedResult<ExternalGroup>> ImportGroupsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);
        var response = await client.GetGroupsAsync(ct);
        if (!response.Success)
            throw new MedusaApiException(response.Error ?? "Failed to list groups", 500);

        var items = (response.Groups ?? [])
            .Select(g => new ExternalGroup(
                g.Id,
                g.Name,
                g.ProductIds ?? []))
            .ToList();
        return new PagedResult<ExternalGroup>(items, false, TotalCount: items.Count);
    }

    private static ImportResult ToImportResult(MedusaBulkMutationResponse response, int total)
    {
        if (response.Results is { Count: > 0 })
        {
            var ok = response.Results.Count(r => r.Success);
            var fail = response.Results.Count - ok;
            var errors = response.Results
                .Where(r => !r.Success)
                .Select(r => new ImportError(r.ProductId, r.Error ?? "Failed"))
                .ToList();
            return new ImportResult(total, ok, fail, errors);
        }

        if (response.Success)
            return new ImportResult(total, total, 0);

        return new ImportResult(total, 0, total, [new ImportError(null, response.Error ?? "Bulk mutation failed")]);
    }

    private MedusaProductPayload BuildProductPayload(
        Dictionary<string, object> data,
        string? existingExternalId)
    {
        var payload = new MedusaProductPayload
        {
            ProductId = existingExternalId ?? GetString(data, "product_id") ?? GetString(data, "productId"),
            DelegatProductId = GetString(data, "delegat_product_id") ?? GetString(data, "delegatProductId"),
            Sku = GetString(data, "sku"),
            Name = GetString(data, "name") ?? "",
            Description = GetString(data, "description"),
            ShortDescription = GetString(data, "short_description") ?? GetString(data, "shortDescription"),
            Price = GetDecimal(data, "price"),
            SalePrice = GetDecimal(data, "sale_price")
                ?? GetDecimal(data, "salePrice")
                ?? GetDecimal(data, "discountedPrice"),
            Quantity = GetInt(data, "quantity") ?? GetInt(data, "stock") ?? GetInt(data, "storageQuantity"),
            Barcode = GetString(data, "barcode"),
            Brand = GetString(data, "brand"),
            Status = GetString(data, "status") ?? "publish",
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            RawFields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        };

        var oldPrice = GetDecimal(data, "oldPrice") ?? GetDecimal(data, "old_price");
        if (oldPrice.HasValue && payload.Price.HasValue && oldPrice > payload.Price)
        {
            payload.SalePrice = payload.Price;
            payload.Price = oldPrice;
        }

        var categoryId = GetString(data, "_categoryId")
            ?? GetString(data, "category_id")
            ?? GetString(data, "categoryId");
        if (!string.IsNullOrWhiteSpace(categoryId))
            payload.CategoryIds = [categoryId];

        payload.GroupId = FirstNonEmpty(
            GetString(data, "group_id"),
            GetString(data, "groupId"),
            GetString(data, "_localGroupId"));
        payload.GroupName = FirstNonEmpty(
            GetString(data, "group_name"),
            GetString(data, "groupName"),
            GetString(data, "_localGroupName"));

        payload.ImageUrls = ExtractImageUrls(data);
        payload.ImageKeys = ExtractImageKeys(data);
        payload.Variants = ExtractVariants(data);
        payload.Specs = ExtractShopSpecs(data);

        var fieldLabels = ExtractFieldLabels(data);
        var fieldOptions = ExtractFieldOptions(data);

        foreach (var (key, value) in data)
        {
            if (InternalKeys.Contains(key) || KnownProductKeys.Contains(key))
                continue;

            if (key.StartsWith("attr:", StringComparison.OrdinalIgnoreCase))
            {
                var attrKey = key["attr:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(attrKey))
                {
                    var displayName = ResolveAttributeName(attrKey, fieldLabels);
                    payload.Attributes![displayName] = ResolveOptionDisplayValue(attrKey, value, fieldOptions);
                }
                continue;
            }

            var name = ResolveAttributeName(key, fieldLabels);
            payload.Attributes![name] = ResolveOptionDisplayValue(key, value, fieldOptions);
            payload.RawFields![key] = value;
        }

        if (data.TryGetValue("attributes", out var attrsObj))
        {
            foreach (var (k, v) in ToStringDictionary(attrsObj))
            {
                var name = ResolveAttributeName(k, fieldLabels);
                payload.Attributes![name] = ResolveOptionDisplayValue(k, v, fieldOptions);
            }
        }

        return payload;
    }

    private static List<MedusaSpecDto>? ExtractShopSpecs(Dictionary<string, object> data)
    {
        if (!data.TryGetValue("_shopSpecs", out var raw) || raw is null)
            return null;

        var specs = new List<MedusaSpecDto>();
        foreach (var item in EnumerateObjects(raw))
        {
            var label = GetDictString(item, "label") ?? GetDictString(item, "Label");
            var value = GetDictString(item, "value") ?? GetDictString(item, "Value");
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                continue;
            specs.Add(new MedusaSpecDto
            {
                Key = GetDictString(item, "key") ?? GetDictString(item, "Key") ?? "",
                Label = label,
                Value = value
            });
        }

        return specs;
    }

    private static Dictionary<string, string> ExtractFieldLabels(Dictionary<string, object> data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!data.TryGetValue("_fieldLabels", out var raw) || raw is null)
            return result;
        foreach (var (k, v) in ToStringDictionary(raw))
        {
            if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                result[k] = v;
        }
        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> ExtractFieldOptions(Dictionary<string, object> data)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!data.TryGetValue("_fieldOptions", out var raw) || raw is null)
            return result;

        if (raw is Dictionary<string, Dictionary<string, string>> typed)
        {
            foreach (var (fieldKey, map) in typed)
            {
                if (string.IsNullOrWhiteSpace(fieldKey) || map == null || map.Count == 0)
                    continue;
                result[fieldKey] = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
            }
            return result;
        }

        if (raw is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                var fieldKey = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(fieldKey) || entry.Value is null)
                    continue;
                var map = ToStringDictionary(entry.Value);
                if (map.Count > 0)
                    result[fieldKey] = map;
            }
        }

        return result;
    }

    private static string ResolveAttributeName(string key, Dictionary<string, string> fieldLabels)
    {
        if (fieldLabels.TryGetValue(key, out var label) && !string.IsNullOrWhiteSpace(label))
            return label.Trim();
        return key;
    }

    private static string ResolveOptionDisplayValue(
        string fieldKey,
        object? value,
        Dictionary<string, Dictionary<string, string>> fieldOptions)
    {
        if (value is null)
            return "";

        fieldOptions.TryGetValue(fieldKey, out var options);

        string ResolveOne(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;
            if (options != null && options.TryGetValue(raw, out var label) && !string.IsNullOrWhiteSpace(label))
                return label;
            foreach (var map in fieldOptions.Values)
            {
                if (map.TryGetValue(raw, out var fallback) && !string.IsNullOrWhiteSpace(fallback))
                    return fallback;
            }
            return raw;
        }

        var parts = ToStringList(value);
        if (parts.Count == 0)
        {
            var single = Stringify(value);
            return string.IsNullOrWhiteSpace(single) ? "" : ResolveOne(single);
        }

        return string.Join(", ", parts.Select(ResolveOne));
    }

    private static List<string>? ExtractImageUrls(Dictionary<string, object> data)
    {
        if (data.TryGetValue("_imageUrls", out var urlsObj))
        {
            var list = ToStringList(urlsObj);
            if (list.Count > 0) return list;
        }

        if (data.TryGetValue("image_urls", out var alt) || data.TryGetValue("images", out alt))
        {
            var list = ToStringList(alt);
            if (list.Count > 0) return list;
        }

        return null;
    }

    private static List<string>? ExtractImageKeys(Dictionary<string, object> data)
    {
        if (!data.TryGetValue("_imageStoragePaths", out var raw) || raw is null)
            return null;
        var list = ToStringList(raw);
        return list.Count > 0 ? list : null;
    }

    private static List<MedusaVariantPayload>? ExtractVariants(Dictionary<string, object> data)
    {
        if (!data.TryGetValue("_variants", out var variantsObj) && !data.TryGetValue("variants", out variantsObj))
            return null;

        var result = new List<MedusaVariantPayload>();

        foreach (var item in EnumerateObjects(variantsObj))
        {
            var localId = GetDictString(item, "id")
                ?? GetDictString(item, "localVariantId")
                ?? GetDictString(item, "local_variant_id");
            var marketplaceId = GetDictString(item, "variantId")
                ?? GetDictString(item, "VariantId")
                ?? GetDictString(item, "variant_id");
            var variantId = localId ?? marketplaceId;
            if (string.IsNullOrWhiteSpace(variantId))
                continue;

            var techSize = GetDictString(item, "techSize")
                ?? GetDictString(item, "TechSize")
                ?? GetDictString(item, "tech_size");
            var marketplaceSize = GetDictString(item, "marketplaceSize")
                ?? GetDictString(item, "MarketplaceSize")
                ?? GetDictString(item, "marketplace_size");

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetValue("attributes", out var nestedAttrs) ||
                item.TryGetValue("Attributes", out nestedAttrs) ||
                item.TryGetValue("Extra", out nestedAttrs) ||
                item.TryGetValue("extra", out nestedAttrs))
            {
                foreach (var (k, v) in ToStringDictionary(nestedAttrs))
                    attributes[k] = v;
            }

            if (!attributes.ContainsKey("Size"))
            {
                var sizeLabel = marketplaceSize ?? techSize;
                if (!string.IsNullOrWhiteSpace(sizeLabel))
                    attributes["Size"] = sizeLabel!;
            }

            result.Add(new MedusaVariantPayload
            {
                VariantId = variantId!,
                Sku = GetDictString(item, "sku") ?? GetDictString(item, "Sku"),
                TechSize = techSize,
                MarketplaceSize = marketplaceSize,
                Barcode = GetDictString(item, "barcode") ?? FirstBarcode(item),
                Price = GetDictDecimal(item, "price") ?? GetDictDecimal(item, "Price"),
                SalePrice = GetDictDecimal(item, "sale_price") ?? GetDictDecimal(item, "SalePrice"),
                Quantity = GetDictInt(item, "quantity") ?? GetDictInt(item, "Quantity"),
                Attributes = attributes.Count > 0 ? attributes : null
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static string? FirstBarcode(Dictionary<string, object?> item)
    {
        if (!item.TryGetValue("barcodes", out var barcodes) && !item.TryGetValue("Barcodes", out barcodes))
            return null;
        return ToStringList(barcodes).FirstOrDefault();
    }

    private static IEnumerable<Dictionary<string, object?>> EnumerateObjects(object? value)
    {
        if (value is null) yield break;

        if (value is JsonElement { ValueKind: JsonValueKind.Array } je)
        {
            foreach (var el in je.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                yield return JsonElementToDict(el);
            }
            yield break;
        }

        if (value is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                if (item is Dictionary<string, object> d)
                {
                    yield return d.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                    continue;
                }
                if (item is Dictionary<string, object?> dn)
                {
                    yield return dn;
                    continue;
                }
                if (item is Dictionary<string, string> ds)
                {
                    yield return ds.ToDictionary(
                        kv => kv.Key,
                        kv => (object?)kv.Value,
                        StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                if (item is JsonElement { ValueKind: JsonValueKind.Object } jel)
                {
                    yield return JsonElementToDict(jel);
                    continue;
                }

                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in item.GetType().GetProperties())
                {
                    if (!prop.CanRead) continue;
                    dict[prop.Name] = prop.GetValue(item);
                }
                if (dict.Count > 0)
                    yield return dict;
            }
        }
    }

    private static Dictionary<string, object?> JsonElementToDict(JsonElement el)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = JsonElementToObject(prop.Value);
        return dict;
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number when el.TryGetInt64(out var l) => l,
        JsonValueKind.Number => el.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
        JsonValueKind.Object => JsonElementToDict(el),
        _ => el.ToString()
    };

    private static List<ExternalField> GetDefaultFields() =>
    [
        new("sku", "SKU", true, "string", Group: "Basic", SortOrder: 1, MaxLength: 255),
        new("name", "Name", true, "string", Group: "Basic", SortOrder: 2, MaxLength: 255),
        new("description", "Description", false, "string", Group: "Basic", SortOrder: 3),
        new("quantity", "Stock quantity", false, "integer", Group: "Inventory", SortOrder: 4, MinValue: 0),
        new("barcode", "Barcode", false, "string", Group: "Basic", SortOrder: 5, MaxLength: 13),
        new("brand", "Brand", false, "string", Group: "Basic", SortOrder: 6, MaxLength: 255),
        new("images", "Images", false, "multiselect",
            Description: "Product image URLs", Group: "Media", SortOrder: 7)
    ];

    private static List<ExternalField> GetPriceFields() =>
    [
        new("price", "Price", true, "number",
            Description: "Regular price", Group: "Prices", SortOrder: 0),
        new("sale_price", "Sale price", false, "number",
            Description: "Sale / discounted price", Group: "Prices", SortOrder: 1)
    ];

    private static bool IsPriceFieldKey(string? key)
        => !string.IsNullOrWhiteSpace(key) && PriceFieldKeys.Contains(key);

    private static List<ExternalField> EnsureDefaultCardFields(List<ExternalField> fields)
    {
        var keys = new HashSet<string>(fields.Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
        var nextSort = fields.Count == 0 ? 1 : fields.Max(f => f.SortOrder) + 1;
        foreach (var d in GetDefaultFields())
        {
            if (!keys.Add(d.Key))
                continue;
            fields.Add(d with { SortOrder = nextSort++ });
        }
        return fields;
    }

    private static List<ExternalField> MergeCategoryFields(IEnumerable<CreateCategoryField> requestFields)
    {
        var fields = GetDefaultFields();
        var existingKeys = new HashSet<string>(fields.Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
        var nextSort = fields.Count == 0 ? 1 : fields.Max(f => f.SortOrder) + 1;

        foreach (var f in requestFields.OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(f.Key))
                continue;
            var key = f.Key.Trim();
            if (!existingKeys.Add(key))
                continue;
            fields.Add(new ExternalField(
                key,
                string.IsNullOrWhiteSpace(f.Name) ? key : f.Name.Trim(),
                false,
                NormalizeFieldType(f.Type),
                f.Options,
                f.Description,
                Group: string.IsNullOrWhiteSpace(f.Group) ? "Attributes" : f.Group,
                SortOrder: f.SortOrder > 0 ? f.SortOrder : nextSort++));
        }

        return fields;
    }

    private static string NormalizeFieldType(string? type)
    {
        return (type ?? "string").Trim().ToLowerInvariant() switch
        {
            "int" or "integer" => "integer",
            "float" or "double" or "decimal" or "number" => "number",
            "bool" or "boolean" => "boolean",
            "select" => "select",
            "multiselect" or "multi_select" => "multiselect",
            "barcode" => "barcode",
            _ => "string"
        };
    }

    private static string? FormatBridgeError(object? error)
    {
        if (error is null) return null;
        if (error is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return MarketplaceApiErrorText.Format("Medusa", 0, s, s);
        }
        if (error is JsonElement json)
        {
            var raw = json.ValueKind == JsonValueKind.String ? json.GetString() : json.GetRawText();
            return MarketplaceApiErrorText.Format("Medusa", 0, raw, raw);
        }
        var text = error.ToString();
        return string.IsNullOrWhiteSpace(text) || text == error.GetType().ToString()
            ? error.GetType().Name
            : text;
    }

    private static Dictionary<string, object>? ExportIdentityDetails(
        string? existingExternalId,
        string? productId,
        Dictionary<string, string>? variantIds)
    {
        Dictionary<string, object>? details = null;
        void Set(string key, object value)
        {
            details ??= new Dictionary<string, object>(StringComparer.Ordinal);
            details[key] = value;
        }
        if (variantIds is { Count: > 0 })
            Set("variantIds", variantIds);
        if (!string.IsNullOrWhiteSpace(existingExternalId)
            && !string.IsNullOrWhiteSpace(productId)
            && !string.Equals(existingExternalId, productId, StringComparison.Ordinal))
            Set("resolvedFrom", existingExternalId);
        return details;
    }

    public override async Task<PagedResult<ExternalOrder>> GetOrdersAsync(
        ConnectionContext context,
        OrderQuery? query = null,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);
        query ??= new OrderQuery();
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var response = await client.ListOrdersAsync(page, pageSize, query.Status, query.Search, ct);
        var items = (response.Items ?? []).Select(MapOrder).ToList();
        return new PagedResult<ExternalOrder>(items, response.HasMore, response.HasMore ? (page + 1).ToString(CultureInfo.InvariantCulture) : null);
    }

    public override async Task<ExternalOrder?> GetOrderAsync(
        ConnectionContext context,
        string externalOrderId,
        CancellationToken ct = default)
    {
        using var client = CreateClient(context);
        try
        {
            var response = await client.GetOrderAsync(externalOrderId, ct);
            return response.Order == null ? null : MapOrder(response.Order);
        }
        catch (MedusaApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public override async Task<OrderStatusUpdateResult> UpdateOrderStatusAsync(
        ConnectionContext context,
        string externalOrderId,
        OrderStatusUpdate update,
        CancellationToken ct = default)
    {
        var action = string.IsNullOrWhiteSpace(update.Action)
            ? update.Status
            : update.Action;
        if (string.IsNullOrWhiteSpace(action))
        {
            return new OrderStatusUpdateResult
            {
                Success = false,
                ErrorMessage = "Medusa adapter needs an order action."
            };
        }
        using var client = CreateClient(context);
        try
        {
            var response = string.Equals(action, MarketplaceOrderActions.Cancel, StringComparison.OrdinalIgnoreCase)
                ? await client.CancelOrderAsync(externalOrderId, refund: true, ct)
                : await client.UpdateOrderStatusAsync(externalOrderId, action, ct);
            return new OrderStatusUpdateResult
            {
                Success = response.Success,
                ErrorMessage = response.Error,
                Order = response.Order == null ? null : MapOrder(response.Order)
            };
        }
        catch (MedusaApiException ex)
        {
            return new OrderStatusUpdateResult
            {
                Success = false,
                ErrorMessage = MarketplaceApiErrorText.Format("Medusa", ex.StatusCode, ex.Message, ex.ResponseBody)
            };
        }
    }

    private static ExternalOrder MapOrder(MedusaOrderDto order)
        => new()
        {
            ExternalId = order.ExternalId ?? "",
            DisplayId = order.DisplayId,
            Status = order.Status ?? MarketplaceOrderStatuses.Unknown,
            RawStatus = order.RawStatus,
            Channel = order.Channel ?? "shop",
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            Currency = order.Currency ?? "RUB",
            Total = order.Total,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            CustomerEmail = order.CustomerEmail,
            ShippingAddress = order.ShippingAddress,
            Comment = order.Comment,
            Items = (order.Items ?? []).Select(i => new ExternalOrderItem
            {
                ExternalProductId = i.ExternalProductId,
                Sku = i.Sku,
                Name = i.Name,
                Quantity = i.Quantity,
                Price = i.Price,
                ImageUrl = OrderItemImageUrl.NeedsReplacement(i.ImageUrl) ? null : i.ImageUrl
            }).ToList()
        };

    protected virtual MedusaApiClient CreateClient(ConnectionContext context, HttpClient? httpClient = null)
    {
        var baseUrl = context.GetCredential("apiBaseUrl")?.Trim();
        var apiKey = context.GetCredential("apiKey")?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Credential apiBaseUrl is required");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Credential apiKey is required");
        return new MedusaApiClient(baseUrl, apiKey, httpClient, context);
    }

    private static int GetBulkChunkSize(ConnectionContext context)
    {
        var raw = context.GetCredential("bulkChunkSize");
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
            return DefaultBulkChunkSize;
        return Math.Clamp(size, 1, MaxBulkChunkSize);
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            var count = Math.Min(size, source.Count - i);
            var chunk = new List<T>(count);
            for (var j = 0; j < count; j++)
                chunk.Add(source[i + j]);
            yield return chunk;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    private static string? GetString(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return Stringify(value);
    }

    private static string? GetDictString(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return Stringify(value);
    }

    private static decimal? GetDecimal(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return ToDecimal(value);
    }

    private static decimal? GetDictDecimal(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return ToDecimal(value);
    }

    private static int? GetInt(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return ToInt(value);
    }

    private static int? GetDictInt(Dictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return ToInt(value);
    }

    private static decimal? ToDecimal(object value) => value switch
    {
        decimal d => d,
        double db => (decimal)db,
        float f => (decimal)f,
        int i => i,
        long l => l,
        JsonElement { ValueKind: JsonValueKind.Number } je => je.GetDecimal(),
        JsonElement { ValueKind: JsonValueKind.String } je when
            decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
        string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
        _ => null
    };

    private static int? ToInt(object value) => value switch
    {
        int i => i,
        long l => (int)l,
        decimal d => (int)d,
        double db => (int)db,
        JsonElement { ValueKind: JsonValueKind.Number } je => je.TryGetInt32(out var i) ? i : (int)je.GetDecimal(),
        JsonElement { ValueKind: JsonValueKind.String } je when
            int.TryParse(je.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
        string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
        _ => null
    };

    private static string Stringify(object? value) => value switch
    {
        null => "",
        string s => s,
        JsonElement { ValueKind: JsonValueKind.Array } je =>
            string.Join(", ", ToStringList(je)),
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.ToString(),
        IEnumerable enumerable and not string and not IDictionary =>
            string.Join(", ", ToStringList(enumerable)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    private static List<string> ToStringList(object? value)
    {
        var list = new List<string>();
        if (value is null) return list;
        if (value is string s)
        {
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            return list;
        }
        if (value is JsonElement { ValueKind: JsonValueKind.Array } je)
        {
            foreach (var el in je.EnumerateArray())
            {
                var item = Stringify(el);
                if (!string.IsNullOrWhiteSpace(item)) list.Add(item);
            }
            return list;
        }
        if (value is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                var str = Stringify(item);
                if (!string.IsNullOrWhiteSpace(str)) list.Add(str);
            }
        }
        return list;
    }

    private static Dictionary<string, string> ToStringDictionary(object? value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (value is null) return result;
        if (value is JsonElement { ValueKind: JsonValueKind.Object } je)
        {
            foreach (var prop in je.EnumerateObject())
                result[prop.Name] = Stringify(prop.Value);
            return result;
        }
        if (value is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                var key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key)) continue;
                result[key] = Stringify(entry.Value);
            }
            return result;
        }
        if (value is IEnumerable<KeyValuePair<string, object>> kvpObj)
        {
            foreach (var (k, v) in kvpObj)
                result[k] = Stringify(v);
            return result;
        }
        if (value is IEnumerable<KeyValuePair<string, string>> kvpStr)
        {
            foreach (var (k, v) in kvpStr)
                result[k] = v;
        }
        return result;
    }
}
