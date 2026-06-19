using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PrintO.Marketplace.OpenCart.Models;
using PrintO.Marketplace.SDK;
using PrintO.Marketplace.SDK.DTOs;

namespace PrintO.Marketplace.OpenCart;

public sealed class OpenCartPlugin : MarketplacePluginBase
{
    private const int DefaultBulkChunkSize = 100;
    private const int MaxBulkChunkSize = 500;

    private static readonly HashSet<string> InternalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "_categoryId",
        "_fieldTypes",
        "_variants",
        "_imageUrls"
    };

    private static readonly HashSet<string> KnownProductKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "product_id",
        "productId",
        "sku",
        "model",
        "name",
        "description",
        "meta_title",
        "metaTitle",
        "meta_description",
        "metaDescription",
        "meta_keyword",
        "metaKeyword",
        "tag",
        "tags",
        "manufacturer",
        "manufacturer_id",
        "barcode",
        "status",
        "price",
        "special_price",
        "specialPrice",
        "quantity",
        "stock",
        "storageQuantity",
        "minimum",
        "sort_order",
        "sortOrder",
        "stock_status_id",
        "stockStatusId",
        "tax_class_id",
        "taxClassId",
        "shipping",
        "length",
        "width",
        "height",
        "weight",
        "length_class_id",
        "lengthClassId",
        "weight_class_id",
        "weightClassId",
        "category_id",
        "category_ids",
        "categories",
        "images"
    };

    public override string Key => "opencart";
    public override string DisplayName => "OpenCart";
    public override string Version => "1.0.0";
    public override bool IsSystem => true;
    public override string? Description => "Integration with an OpenCart PrintO bridge for exporting categories, fields, products, images, prices, and stocks";
    public override string? LogoUrl => "https://www.opencart.com/application/view/image/icon/opencart-logo.png";

    public override MarketplaceCapabilities Capabilities =>
        MarketplaceCapabilities.GetCategories |
        MarketplaceCapabilities.GetCategoryFields |
        MarketplaceCapabilities.ImportAllProducts |
        MarketplaceCapabilities.CreateCategory |
        MarketplaceCapabilities.ExportProduct |
        MarketplaceCapabilities.ExportProductBulk |
        MarketplaceCapabilities.ExportPrices |
        MarketplaceCapabilities.ExportStocks;

    public override CredentialsSchema GetCredentialsSchema()
    {
        return new CredentialsSchema
        {
            Fields = new List<CredentialField>
            {
                new()
                {
                    Key = "apiBaseUrl",
                    DisplayName = "PrintO OpenCart bridge URL",
                    Description = "Full URL of the OpenCart bridge endpoint. Query-route URLs are supported.",
                    Type = "text",
                    IsRequired = true,
                    Placeholder = "https://shop.example.com/index.php?route=extension/printo/api"
                },
                new()
                {
                    Key = "apiKey",
                    DisplayName = "API Key",
                    Description = "Secret key configured in the OpenCart PrintO bridge",
                    Type = "password",
                    IsRequired = true,
                    IsSecret = true
                },
                new()
                {
                    Key = "storeId",
                    DisplayName = "Store ID",
                    Description = "Optional OpenCart store_id. Defaults to the bridge configuration.",
                    Type = "number",
                    IsRequired = false,
                    DefaultValue = "0"
                },
                new()
                {
                    Key = "languageId",
                    DisplayName = "Language ID",
                    Description = "Optional OpenCart language_id for descriptions. Defaults to the bridge configuration.",
                    Type = "number",
                    IsRequired = false
                },
                new()
                {
                    Key = "bulkChunkSize",
                    DisplayName = "Bulk chunk size",
                    Description = "Maximum products per request to the OpenCart bridge.",
                    Type = "number",
                    IsRequired = false,
                    DefaultValue = DefaultBulkChunkSize.ToString(CultureInfo.InvariantCulture)
                }
            }
        };
    }

    public override async Task<ConnectionTestResult> TestConnectionAsync(
        ConnectionContext context,
        CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient(context);
            var info = await client.GetInfoAsync(ct);

            return new ConnectionTestResult(
                true,
                "OpenCart bridge connection established",
                new Dictionary<string, object>
                {
                    ["storeName"] = info.StoreName ?? "",
                    ["opencartVersion"] = info.Version ?? "",
                    ["apiVersion"] = info.ApiVersion ?? "",
                    ["storeId"] = info.StoreId ?? 0,
                    ["languageId"] = info.LanguageId ?? 0,
                    ["capabilities"] = info.Capabilities ?? new List<string>()
                });
        }
        catch (OpenCartApiException ex)
        {
            return new ConnectionTestResult(
                false,
                $"OpenCart bridge error: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["statusCode"] = ex.StatusCode,
                    ["response"] = ex.ResponseBody ?? ""
                });
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
        var categories = await CreateClient(context).GetCategoriesAsync(parentId, searchQuery, ct);
        var result = new List<ExternalCategory>();
        foreach (var category in categories)
            FlattenCategory(category, result, category.ParentId, category.ParentName, category.FullPath);

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            return result
                .Where(c => c.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                            (c.FullPath?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        if (parentId == null)
            return result.Where(c => c.ParentId == null).ToList();

        return result.Where(c => c.ParentId == parentId).ToList();

    }

    public override async Task<List<ExternalField>> GetCategoryFieldsAsync(
        ConnectionContext context,
        string externalCategoryId,
        CancellationToken ct = default)
    {
        var fields = await CreateClient(context).GetCategoryFieldsAsync(externalCategoryId, ct);
        if (fields.Count == 0)
            return GetDefaultFields();

        return fields
            .OrderBy(f => f.SortOrder)
            .Select(f => new ExternalField(
                f.Key,
                f.Name,
                f.IsRequired,
                NormalizeFieldType(f.Type),
                f.Options?.Select(o => new ExternalFieldOption(o.Value, o.Label)).ToList(),
                f.Description,
                f.Unit,
                f.MaxLength,
                f.MinValue,
                f.MaxValue,
                f.ValidationPattern,
                f.Group,
                f.SortOrder))
            .ToList();
    }

    public override async Task<CreateCategoryResult> CreateCategoryAsync(
        ConnectionContext context,
        CreateCategoryRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new OpenCartCreateCategoryRequest
            {
                Name = request.Name,
                Description = request.Description,
                ParentId = request.ParentId,
                StoreId = request.StoreId ?? GetCredentialInt(context, "storeId"),
                LanguageId = request.LanguageId ?? GetCredentialInt(context, "languageId"),
                SortOrder = request.SortOrder,
                Fields = request.Fields.Select(f => new OpenCartCreateCategoryField
                {
                    Key = f.Key,
                    Name = f.Name,
                    Type = NormalizeFieldType(f.Type),
                    IsRequired = f.IsRequired,
                    Description = f.Description,
                    Group = f.Group,
                    SortOrder = f.SortOrder,
                    Options = f.Options?.Select(o => new OpenCartFieldOptionDto
                    {
                        Value = o.Value,
                        Label = o.Label
                    }).ToList()
                }).ToList()
            };

            var response = await CreateClient(context).CreateCategoryAsync(payload, ct);
            if (!response.Success)
                return new CreateCategoryResult(
                    false,
                    ErrorMessage: response.Message ?? "OpenCart rejected category creation",
                    Details: response.Details);

            var fields = response.Fields != null && response.Fields.Count > 0
                ? response.Fields
                    .OrderBy(f => f.SortOrder)
                    .Select(f => new ExternalField(
                        f.Key,
                        f.Name,
                        f.IsRequired,
                        NormalizeFieldType(f.Type),
                        f.Options?.Select(o => new ExternalFieldOption(o.Value, o.Label)).ToList(),
                        f.Description,
                        f.Unit,
                        f.MaxLength,
                        f.MinValue,
                        f.MaxValue,
                        f.ValidationPattern,
                        f.Group,
                        f.SortOrder))
                    .ToList()
                : await GetCategoryFieldsAsync(context, response.CategoryId ?? "", ct);

            return new CreateCategoryResult(
                true,
                response.CategoryId,
                response.Name ?? request.Name,
                fields,
                response.Message,
                response.Details);
        }
        catch (OpenCartApiException ex)
        {
            return new CreateCategoryResult(false, ErrorMessage: ex.Message, Details: BuildErrorDetails(ex));
        }
        catch (Exception ex)
        {
            return new CreateCategoryResult(false, ErrorMessage: ex.Message);
        }
    }

    public override async Task<ExternalProduct?> ImportProductAsync(
        ConnectionContext context,
        string externalProductId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(externalProductId))
            return null;

        var page = await ImportAllProductsAsync(context, 50, null, externalProductId, ct);
        return page.Items.FirstOrDefault(p =>
            p.ExternalId.Equals(externalProductId, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(p.Sku) && p.Sku.Equals(externalProductId, StringComparison.OrdinalIgnoreCase)));
    }

    public override async Task<List<ExternalProduct>> ImportProductsBatchAsync(
        ConnectionContext context,
        List<string> externalProductIds,
        CancellationToken ct = default)
    {
        var result = new List<ExternalProduct>();
        foreach (var externalId in externalProductIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var product = await ImportProductAsync(context, externalId, ct);
            if (product != null)
                result.Add(product);
        }

        return result;
    }

    public override async Task<PagedResult<ExternalProduct>> ImportAllProductsAsync(
        ConnectionContext context,
        int pageSize = 100,
        string? cursor = null,
        string? offerIdFilter = null,
        CancellationToken ct = default)
    {
        var response = await CreateClient(context).GetProductsAsync(
            Math.Clamp(pageSize, 1, 200),
            cursor,
            offerIdFilter,
            ct);

        var products = response.Items.Select(MapProduct).ToList();
        return new PagedResult<ExternalProduct>(
            products,
            response.HasMore,
            response.NextCursor);
    }

    public override async Task<ExportResult> ExportProductAsync(
        ConnectionContext context,
        Dictionary<string, object> productData,
        string? existingExternalId = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = BuildProductPayload(context, productData, existingExternalId);
            var response = await CreateClient(context).UpsertProductAsync(payload, ct);

            if (!response.Success)
                return new ExportResult(false, ErrorMessage: response.Message ?? "OpenCart rejected product export");

            return new ExportResult(
                true,
                response.ProductId ?? existingExternalId,
                response.Message,
                response.Details);
        }
        catch (OpenCartApiException ex)
        {
            return new ExportResult(false, ErrorMessage: ex.Message, Details: BuildErrorDetails(ex));
        }
        catch (Exception ex)
        {
            return new ExportResult(false, ErrorMessage: ex.Message);
        }
    }

    public override async Task<BulkExportResult> ExportProductsBulkAsync(
        ConnectionContext context,
        List<BulkExportItem> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return new BulkExportResult(0, 0, 0, new List<ExportItemResult>());

        var client = CreateClient(context);
        var chunkSize = GetBulkChunkSize(context);
        var results = new List<ExportItemResult>();

        foreach (var chunk in items.Chunk(chunkSize))
        {
            var request = new OpenCartBulkProductRequest
            {
                Items = chunk.Select(item => new OpenCartBulkProductItem
                {
                    LocalProductId = item.LocalProductId,
                    ExistingExternalId = item.ExistingExternalId,
                    Product = BuildProductPayload(context, item.ProductData, item.ExistingExternalId)
                }).ToList()
            };

            try
            {
                var response = await client.UpsertProductsBulkAsync(request, ct);
                results.AddRange(MapBulkResponse(response, chunk));
            }
            catch (OpenCartApiException ex) when (ex.StatusCode is 404 or 405)
            {
                context.Logger?.LogWarning("OpenCart bulk endpoint is unavailable, falling back to sequential export");
                foreach (var item in chunk)
                {
                    var singleResult = await ExportProductAsync(context, item.ProductData, item.ExistingExternalId, ct);
                    results.Add(new ExportItemResult(
                        item.LocalProductId,
                        singleResult.Success,
                        singleResult.ExternalProductId,
                        singleResult.ErrorMessage,
                        singleResult.Details));
                }
            }
            catch (OpenCartApiException ex)
            {
                results.AddRange(chunk.Select(item => new ExportItemResult(
                    item.LocalProductId,
                    false,
                    ErrorMessage: ex.Message)));
            }
            catch (Exception ex)
            {
                results.AddRange(chunk.Select(item => new ExportItemResult(
                    item.LocalProductId,
                    false,
                    ErrorMessage: ex.Message)));
            }
        }

        var successCount = results.Count(r => r.Success);
        return new BulkExportResult(items.Count, successCount, items.Count - successCount, results);
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
            var request = new OpenCartBulkOperationRequest<OpenCartPricePayload>
            {
                Items = prices.Select(p => new OpenCartPricePayload
                {
                    ExternalProductId = p.ExternalProductId,
                    Price = p.Price,
                    SpecialPrice = p.DiscountedPrice,
                    Currency = p.Currency
                }).ToList()
            };

            var response = await CreateClient(context).ExportPricesAsync(request, ct);
            return MapOperationResponse(prices.Count, response);
        }
        catch (OpenCartApiException ex) when (ex.StatusCode is 404 or 405)
        {
            return await ExportPricesViaProductUpsertAsync(context, prices, ct);
        }
        catch (OpenCartApiException ex)
        {
            return new ImportResult(prices.Count, 0, prices.Count, prices.Select(p =>
                new ImportError(p.ExternalProductId, ex.Message, ex.ResponseBody)).ToList());
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
            var request = new OpenCartBulkOperationRequest<OpenCartStockPayload>
            {
                Items = stocks.Select(s => new OpenCartStockPayload
                {
                    ExternalProductId = s.ExternalProductId,
                    Quantity = s.Quantity,
                    WarehouseId = s.WarehouseId
                }).ToList()
            };

            var response = await CreateClient(context).ExportStocksAsync(request, ct);
            return MapOperationResponse(stocks.Count, response);
        }
        catch (OpenCartApiException ex) when (ex.StatusCode is 404 or 405)
        {
            return await ExportStocksViaProductUpsertAsync(context, stocks, ct);
        }
        catch (OpenCartApiException ex)
        {
            return new ImportResult(stocks.Count, 0, stocks.Count, stocks.Select(s =>
                new ImportError(s.ExternalProductId, ex.Message, ex.ResponseBody)).ToList());
        }
    }

    private static OpenCartApiClient CreateClient(ConnectionContext context)
    {
        var apiBaseUrl = context.GetCredential("apiBaseUrl");
        var apiKey = context.GetCredential("apiKey");

        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new InvalidOperationException("apiBaseUrl is required for OpenCart connection");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("apiKey is required for OpenCart connection");

        return new OpenCartApiClient(apiBaseUrl, apiKey, context.Logger);
    }

    private static OpenCartProductPayload BuildProductPayload(
        ConnectionContext context,
        Dictionary<string, object> data,
        string? existingExternalId)
    {
        var payload = new OpenCartProductPayload
        {
            ProductId = GetString(data, "product_id") ?? GetString(data, "productId") ?? existingExternalId,
            Sku = GetString(data, "sku"),
            Model = FirstNonEmpty(GetString(data, "model"), GetString(data, "sku"), GetString(data, "barcode")),
            Name = GetString(data, "name"),
            Description = GetString(data, "description"),
            MetaTitle = GetString(data, "meta_title") ?? GetString(data, "metaTitle") ?? GetString(data, "name"),
            MetaDescription = GetString(data, "meta_description") ?? GetString(data, "metaDescription"),
            MetaKeyword = GetString(data, "meta_keyword") ?? GetString(data, "metaKeyword"),
            Tag = GetString(data, "tag") ?? GetString(data, "tags"),
            Manufacturer = GetString(data, "manufacturer") ?? GetString(data, "manufacturer_id"),
            Barcode = GetString(data, "barcode"),
            Status = GetBool(data, "status") ?? true,
            Price = GetDecimal(data, "price"),
            SpecialPrice = GetDecimal(data, "special_price") ?? GetDecimal(data, "specialPrice"),
            Quantity = GetInt(data, "quantity") ?? GetInt(data, "stock") ?? GetInt(data, "storageQuantity"),
            Minimum = GetInt(data, "minimum"),
            SortOrder = GetInt(data, "sort_order") ?? GetInt(data, "sortOrder"),
            StockStatusId = GetInt(data, "stock_status_id") ?? GetInt(data, "stockStatusId"),
            TaxClassId = GetInt(data, "tax_class_id") ?? GetInt(data, "taxClassId"),
            Shipping = GetBool(data, "shipping"),
            Length = GetDecimal(data, "length"),
            Width = GetDecimal(data, "width"),
            Height = GetDecimal(data, "height"),
            Weight = GetDecimal(data, "weight"),
            LengthClassId = GetString(data, "length_class_id") ?? GetString(data, "lengthClassId"),
            WeightClassId = GetString(data, "weight_class_id") ?? GetString(data, "weightClassId"),
            StoreId = GetCredentialInt(context, "storeId"),
            LanguageId = GetCredentialInt(context, "languageId"),
            CategoryIds = GetCategoryIds(data),
            ImageUrls = GetFirstStringList(data, "images", "_imageUrls"),
            Variants = GetVariants(data)
        };

        foreach (var (key, value) in data)
        {
            if (InternalKeys.Contains(key) || KnownProductKeys.Contains(key))
                continue;

            if (key.StartsWith("attr:", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("attribute:", StringComparison.OrdinalIgnoreCase))
            {
                payload.Attributes[NormalizePrefixedKey(key)] = value;
            }
            else if (key.StartsWith("option:", StringComparison.OrdinalIgnoreCase))
            {
                payload.Options[NormalizePrefixedKey(key)] = value;
            }
            else
            {
                payload.RawFields[key] = value;
            }
        }

        return payload;
    }

    private static IEnumerable<ExportItemResult> MapBulkResponse(
        OpenCartBulkProductResponse response,
        IReadOnlyCollection<BulkExportItem> requestedItems)
    {
        var responseByLocalId = response.Items.ToDictionary(i => i.LocalProductId);

        foreach (var item in requestedItems)
        {
            if (!responseByLocalId.TryGetValue(item.LocalProductId, out var result))
            {
                yield return new ExportItemResult(
                    item.LocalProductId,
                    false,
                    ErrorMessage: "OpenCart bulk response does not contain this product");
                continue;
            }

            yield return new ExportItemResult(
                item.LocalProductId,
                result.Success,
                result.ProductId ?? item.ExistingExternalId,
                result.ErrorMessage,
                result.Details);
        }
    }

    private static ExternalProduct MapProduct(OpenCartProductDto product)
    {
        var raw = new Dictionary<string, object>
        {
            ["model"] = product.Model ?? "",
        };

        return new ExternalProduct(
            ExternalId: product.ExternalId,
            Sku: FirstNonEmpty(product.Sku, product.Model),
            Name: string.IsNullOrWhiteSpace(product.Name) ? FirstNonEmpty(product.Sku, product.Model, product.ExternalId)! : product.Name,
            Description: product.Description,
            Price: product.Price,
            Quantity: product.Quantity,
            CategoryId: product.CategoryId,
            CategoryName: product.CategoryName,
            ImageUrls: product.ImageUrls?.Count > 0 ? product.ImageUrls : null,
            RawData: raw
        );
    }

    private static async Task<ImportResult> ExportPricesViaProductUpsertAsync(
        ConnectionContext context,
        List<PriceInfo> prices,
        CancellationToken ct)
    {
        var errors = new List<ImportError>();
        var success = 0;

        foreach (var price in prices)
        {
            var data = new Dictionary<string, object>
            {
                ["price"] = price.Price ?? 0
            };
            if (price.DiscountedPrice.HasValue)
                data["special_price"] = price.DiscountedPrice.Value;

            var result = await new OpenCartPlugin().ExportProductAsync(context, data, price.ExternalProductId, ct);
            if (result.Success) success++;
            else errors.Add(new ImportError(price.ExternalProductId, result.ErrorMessage ?? "Price export failed"));
        }

        return new ImportResult(prices.Count, success, prices.Count - success, errors);
    }

    private static async Task<ImportResult> ExportStocksViaProductUpsertAsync(
        ConnectionContext context,
        List<StockInfo> stocks,
        CancellationToken ct)
    {
        var errors = new List<ImportError>();
        var success = 0;

        foreach (var stock in stocks)
        {
            var data = new Dictionary<string, object>
            {
                ["quantity"] = stock.Quantity
            };

            var result = await new OpenCartPlugin().ExportProductAsync(context, data, stock.ExternalProductId, ct);
            if (result.Success) success++;
            else errors.Add(new ImportError(stock.ExternalProductId, result.ErrorMessage ?? "Stock export failed"));
        }

        return new ImportResult(stocks.Count, success, stocks.Count - success, errors);
    }

    private static ImportResult MapOperationResponse(
        int fallbackTotal,
        OpenCartBulkOperationResponse response)
    {
        var total = response.TotalCount > 0 ? response.TotalCount : fallbackTotal;
        var failed = response.FailedCount > 0
            ? response.FailedCount
            : response.Errors?.Count ?? Math.Max(0, total - response.SuccessCount);
        var success = response.SuccessCount > 0 ? response.SuccessCount : total - failed;

        return new ImportResult(
            total,
            success,
            failed,
            response.Errors?.Select(e => new ImportError(e.ExternalId, e.Message, e.Details)).ToList());
    }

    private static Dictionary<string, object> BuildErrorDetails(OpenCartApiException ex)
        => new()
        {
            ["statusCode"] = ex.StatusCode,
            ["response"] = ex.ResponseBody ?? ""
        };

    private static void FlattenCategory(
        OpenCartCategoryDto category,
        List<ExternalCategory> result,
        string? inheritedParentId,
        string? inheritedParentName,
        string? inheritedPath)
    {
        var parentId = category.ParentId ?? inheritedParentId;
        var parentName = category.ParentName ?? inheritedParentName;
        var fullPath = category.FullPath ?? (string.IsNullOrWhiteSpace(inheritedPath)
            ? category.Name
            : $"{inheritedPath} / {category.Name}");
        var children = category.Children ?? new List<OpenCartCategoryDto>();

        result.Add(new ExternalCategory(
            category.Id,
            category.Name,
            parentId,
            parentName,
            fullPath,
            category.HasChildren || children.Count > 0,
            category.ChildrenCount ?? children.Count));

        foreach (var child in children)
            FlattenCategory(child, result, category.Id, category.Name, fullPath);
    }

    private static List<ExternalField> GetDefaultFields()
        => new()
        {
            new ExternalField("sku", "SKU", true, "string", Group: "Basic", SortOrder: 10),
            new ExternalField("model", "Model", true, "string", Group: "Basic", SortOrder: 20),
            new ExternalField("name", "Name", true, "string", Group: "Basic", SortOrder: 30),
            new ExternalField("description", "Description", false, "string", Group: "Basic", SortOrder: 40),
            new ExternalField("price", "Price", true, "number", Group: "Commerce", SortOrder: 50),
            new ExternalField("quantity", "Quantity", false, "integer", Group: "Commerce", SortOrder: 60),
            new ExternalField("images", "Images", false, "multiselect", Description: "Product image URLs", Group: "Media", SortOrder: 70),
            new ExternalField("manufacturer", "Manufacturer", false, "string", Group: "Basic", SortOrder: 80),
            new ExternalField("meta_title", "Meta title", false, "string", Group: "SEO", SortOrder: 90),
            new ExternalField("meta_description", "Meta description", false, "string", Group: "SEO", SortOrder: 100),
            new ExternalField("tag", "Tags", false, "string", Group: "SEO", SortOrder: 110),
            new ExternalField("weight", "Weight", false, "number", Group: "Shipping", SortOrder: 120),
            new ExternalField("length", "Length", false, "number", Group: "Shipping", SortOrder: 130),
            new ExternalField("width", "Width", false, "number", Group: "Shipping", SortOrder: 140),
            new ExternalField("height", "Height", false, "number", Group: "Shipping", SortOrder: 150)
        };

    private static string NormalizeFieldType(string? type)
        => type?.Trim().ToLowerInvariant() switch
        {
            "int" or "integer" => "integer",
            "decimal" or "float" or "double" => "number",
            "bool" => "boolean",
            "textarea" or "text" => "string",
            "multi_select" or "multi-select" or "array" => "multiselect",
            "dropdown" => "select",
            "select" or "multiselect" or "number" or "boolean" or "string" => type!.Trim().ToLowerInvariant(),
            _ => "string"
        };

    private static int GetBulkChunkSize(ConnectionContext context)
    {
        var raw = context.GetCredential("bulkChunkSize") ?? context.GetSetting("bulkChunkSize");
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return DefaultBulkChunkSize;

        return Math.Clamp(value, 1, MaxBulkChunkSize);
    }

    private static int? GetCredentialInt(ConnectionContext context, string key)
    {
        var raw = context.GetCredential(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static List<string> GetCategoryIds(Dictionary<string, object> data)
    {
        foreach (var key in new[] { "_categoryId", "category_id", "category_ids", "categories" })
        {
            var values = GetStringList(data, key);
            if (values.Count > 0)
                return values;
        }

        return new List<string>();
    }

    private static List<OpenCartVariantPayload> GetVariants(Dictionary<string, object> data)
    {
        if (!data.TryGetValue("_variants", out var value) || value is not IEnumerable enumerable || value is string)
            return new List<OpenCartVariantPayload>();

        var variants = new List<OpenCartVariantPayload>();
        foreach (var item in enumerable)
        {
            if (item is not IDictionary dict)
                continue;

            variants.Add(new OpenCartVariantPayload
            {
                VariantId = GetDictionaryString(dict, "variantId"),
                TechSize = GetDictionaryString(dict, "techSize"),
                MarketplaceSize = GetDictionaryString(dict, "marketplaceSize"),
                Barcode = GetDictionaryString(dict, "barcode"),
                Price = GetDictionaryDecimal(dict, "price"),
                Quantity = GetDictionaryInt(dict, "quantity")
            });
        }

        return variants;
    }

    private static string? GetString(Dictionary<string, object> data, string key)
        => data.TryGetValue(key, out var value) && value != null ? value.ToString() : null;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static decimal? GetDecimal(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is decimal dec) return dec;
        if (value is double dbl) return (decimal)dbl;
        if (value is float flt) return (decimal)flt;
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.GetCultureInfo("ru-RU"), out parsed))
            return parsed;

        return null;
    }

    private static int? GetInt(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
            return (int)Math.Truncate(dec);

        return null;
    }

    private static bool? GetBool(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is bool b) return b;
        return value.ToString()?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "да" => true,
            "0" or "false" or "no" or "нет" => false,
            _ => null
        };
    }

    private static List<string> GetStringList(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value == null)
            return new List<string>();

        if (value is string str)
        {
            return str
                .Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        if (value is IEnumerable enumerable)
        {
            return enumerable
                .Cast<object?>()
                .Select(x => x?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToList();
        }

        return new List<string> { value.ToString()! };
    }

    private static List<string> GetFirstStringList(Dictionary<string, object> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            var values = GetStringList(data, key);
            if (values.Count > 0)
                return values;
        }

        return new List<string>();
    }

    private static string NormalizePrefixedKey(string key)
    {
        var idx = key.IndexOf(':', StringComparison.Ordinal);
        return idx >= 0 ? key[(idx + 1)..] : key;
    }

    private static string? GetDictionaryString(IDictionary dict, string key)
        => dict.Contains(key) ? dict[key]?.ToString() : null;

    private static decimal? GetDictionaryDecimal(IDictionary dict, string key)
    {
        var value = dict.Contains(key) ? dict[key] : null;
        if (value == null) return null;
        if (value is decimal dec) return dec;
        if (decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static int? GetDictionaryInt(IDictionary dict, string key)
    {
        var value = dict.Contains(key) ? dict[key] : null;
        if (value == null) return null;
        if (value is int i) return i;
        if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }
}
