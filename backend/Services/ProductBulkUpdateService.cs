using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Controllers;
using Delegat.Infrastructure;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>Fields that can be set during a bulk product update. Null values are skipped.</summary>
public class ProductBulkUpdateFields
{
    public string? name { get; set; }
    public string? description { get; set; }
    public int? storageQuantity { get; set; }
    public decimal? price { get; set; }
    public decimal? oldPrice { get; set; }
    public decimal? minPrice { get; set; }
    public decimal? netPrice { get; set; }
    public decimal? discountedPrice { get; set; }
    public int? discount { get; set; }
    public string? currency { get; set; }
    public List<ProductAttributeValue.UserValueForm>? attributeValues { get; set; }
    public List<StoreProductAttributeValue.UserValueForm>? storeAttributeValues { get; set; }
    public string? barcode { get; set; }
    public bool? generateBarcodes { get; set; }
}

public sealed class ProductBulkUpdateResult
{
    public int updatedCount { get; set; }
    public int failedCount { get; set; }
    public List<string> errors { get; set; } = [];
}

/// <summary>
/// Applies bulk field/attribute updates to products, with optional OperationLogger progress.
/// Used by the background worker (and tests).
/// </summary>
public class ProductBulkUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly DataContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AttributeUniquenessService _attributeUniqueness;

    public ProductBulkUpdateService(
        DataContext db,
        IUnitOfWork unitOfWork,
        AttributeUniquenessService attributeUniqueness)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _attributeUniqueness = attributeUniqueness;
    }

    public static string SerializeUpdates(ProductBulkUpdateFields updates) =>
        JsonSerializer.Serialize(updates, JsonOptions);

    public static ProductBulkUpdateFields DeserializeUpdates(string json) =>
        JsonSerializer.Deserialize<ProductBulkUpdateFields>(json, JsonOptions)
        ?? new ProductBulkUpdateFields();

    public static string SerializeProductIds(IEnumerable<int> productIds) =>
        JsonSerializer.Serialize(productIds.ToList(), JsonOptions);

    public static List<int> DeserializeProductIds(string json) =>
        JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? [];

    /// <summary>
    /// Fail-fast checks before enqueue (missing products, unique attrs on multi-select).
    /// </summary>
    public async Task ValidateBeforeEnqueueAsync(
        int storeId,
        List<int> productIds,
        ProductBulkUpdateFields updates,
        CancellationToken ct = default)
    {
        if (productIds.Count == 0)
            throw ApiException.BadRequest("No products specified", code: "required_fields_missing");

        if (!HasAnyUpdate(updates))
            throw ApiException.BadRequest("No fields to update", code: "required_fields_missing");

        var foundIds = await _db.products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.storeId == storeId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var notFound = productIds.Except(foundIds).ToList();
        if (notFound.Count > 0)
            throw ApiException.NotFound($"Products not found: {string.Join(", ", notFound)}", code: "product_not_found");

        if (productIds.Count > 1)
            await EnsureBulkUniqueAttributesAllowedAsync(storeId, updates, ct);
    }

    public async Task<ProductBulkUpdateResult> ExecuteAsync(
        int storeId,
        List<int> productIds,
        ProductBulkUpdateFields updates,
        CancellationToken ct = default,
        IOperationLogger? operationLogger = null,
        string? operationId = null)
    {
        var result = new ProductBulkUpdateResult();
        var total = productIds.Count;

        if (operationLogger != null && operationId != null)
        {
            await operationLogger.LogAsync(operationId, storeId, "info",
                $"Starting bulk update for {total} product(s)");
            await operationLogger.ProgressAsync(operationId, storeId, total, 0, 0);
            await LogUpdateSummaryAsync(operationLogger, operationId, storeId, updates);
        }

        // Pre-check unique attrs across the whole batch once (same rule as enqueue).
        if (productIds.Count > 1)
            await EnsureBulkUniqueAttributesAllowedAsync(storeId, updates, ct);

        var products = await _db.products
            .Include(p => p.attributeValues)
            .Include(p => p.storeAttributeValues)
            .Where(p => productIds.Contains(p.Id) && p.storeId == storeId)
            .ToListAsync(ct);

        var byId = products.ToDictionary(p => p.Id);
        var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
        var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();

        foreach (var productId in productIds)
        {
            ct.ThrowIfCancellationRequested();

            if (!byId.TryGetValue(productId, out var product))
            {
                result.failedCount++;
                var msg = $"Product #{productId} not found in store";
                result.errors.Add(msg);
                if (operationLogger != null && operationId != null)
                    await operationLogger.LogAsync(operationId, storeId, "error", msg);
                await ReportProgressAsync(operationLogger, operationId, storeId, total, result);
                continue;
            }

            try
            {
                if (updates.attributeValues != null || updates.storeAttributeValues != null)
                {
                    await _attributeUniqueness.EnsureUniqueAsync(
                        storeId,
                        product.categoryId,
                        updates.attributeValues,
                        updates.storeAttributeValues,
                        excludeProductIds: productIds);
                }

                ApplyProductFields(product, updates);

                if (updates.attributeValues != null)
                {
                    foreach (var av in updates.attributeValues)
                    {
                        var existing = product.attributeValues
                            .FirstOrDefault(a => a.attributeId == av.attributeId);
                        if (existing != null)
                        {
                            existing.value = av.value;
                        }
                        else
                        {
                            var attrValue = new ProductAttributeValue();
                            attrValue.AddFill(new ProductAttributeValue.AddForm
                            {
                                productId = product.Id,
                                attributeId = av.attributeId,
                                value = av.value
                            });
                            await attrValueRepo.AddWithoutSaveAsync(attrValue);
                        }
                    }
                }

                if (updates.storeAttributeValues != null)
                {
                    foreach (var sav in updates.storeAttributeValues)
                    {
                        var existing = product.storeAttributeValues
                            .FirstOrDefault(a => a.storeAttributeId == sav.storeAttributeId);
                        if (existing != null)
                        {
                            existing.value = sav.value;
                        }
                        else
                        {
                            var storeAttrValue = new StoreProductAttributeValue();
                            storeAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                            {
                                productId = product.Id,
                                storeAttributeId = sav.storeAttributeId,
                                value = sav.value
                            });
                            await storeAttrValueRepo.AddWithoutSaveAsync(storeAttrValue);
                        }
                    }
                }

                await _unitOfWork.SaveChangesAsync();
                result.updatedCount++;

                if (operationLogger != null && operationId != null)
                {
                    await operationLogger.LogAsync(operationId, storeId, "info",
                        $"Updated product #{product.Id} ({product.sku})");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ApiException ex)
            {
                result.failedCount++;
                var msg = $"Product #{product.Id} ({product.sku}): {ex.Message}";
                result.errors.Add(msg);
                if (operationLogger != null && operationId != null)
                    await operationLogger.LogAsync(operationId, storeId, "error", msg);
            }
            catch (Exception ex)
            {
                result.failedCount++;
                var msg = $"Product #{product.Id} ({product.sku}): {ex.Message}";
                result.errors.Add(msg);
                if (operationLogger != null && operationId != null)
                    await operationLogger.LogAsync(operationId, storeId, "error", msg);
            }

            await ReportProgressAsync(operationLogger, operationId, storeId, total, result);
        }

        if (operationLogger != null && operationId != null)
        {
            await operationLogger.LogAsync(operationId, storeId, "info",
                $"Finished: updated {result.updatedCount}, failed {result.failedCount}");
        }

        return result;
    }

    private static async Task ReportProgressAsync(
        IOperationLogger? logger,
        string? operationId,
        int storeId,
        int total,
        ProductBulkUpdateResult result)
    {
        if (logger == null || operationId == null) return;
        await logger.ProgressAsync(operationId, storeId, total, result.updatedCount, result.failedCount);
    }

    private static async Task LogUpdateSummaryAsync(
        IOperationLogger logger,
        string operationId,
        int storeId,
        ProductBulkUpdateFields updates)
    {
        var parts = new List<string>();
        if (updates.name != null) parts.Add("name");
        if (updates.description != null) parts.Add("description");
        if (updates.storageQuantity.HasValue) parts.Add("storageQuantity");
        if (updates.price.HasValue) parts.Add("price");
        if (updates.oldPrice.HasValue) parts.Add("oldPrice");
        if (updates.minPrice.HasValue) parts.Add("minPrice");
        if (updates.netPrice.HasValue) parts.Add("netPrice");
        if (updates.currency != null) parts.Add("currency");
        if (updates.barcode != null) parts.Add("barcode");
        if (updates.generateBarcodes == true) parts.Add("generateBarcodes");
        if (updates.attributeValues is { Count: > 0 } ca)
            parts.Add($"{ca.Count} category attribute(s)");
        if (updates.storeAttributeValues is { Count: > 0 } sa)
            parts.Add($"{sa.Count} store attribute(s)");

        await logger.LogAsync(operationId, storeId, "info",
            parts.Count == 0
                ? "Update payload is empty"
                : $"Fields to apply: {string.Join(", ", parts)}");
    }

    public static bool HasAnyUpdate(ProductBulkUpdateFields updates) =>
        updates.name != null
        || updates.description != null
        || updates.storageQuantity.HasValue
        || updates.price.HasValue
        || updates.oldPrice.HasValue
        || updates.minPrice.HasValue
        || updates.netPrice.HasValue
        || updates.currency != null
        || updates.barcode != null
        || updates.generateBarcodes == true
        || (updates.attributeValues != null && updates.attributeValues.Count > 0)
        || (updates.storeAttributeValues != null && updates.storeAttributeValues.Count > 0);

    private static void ApplyProductFields(Product product, ProductBulkUpdateFields updates)
    {
        if (updates.name != null)
            product.name = updates.name;

        if (updates.description != null)
            product.description = updates.description;

        if (updates.storageQuantity.HasValue)
            product.storageQuantity = updates.storageQuantity.Value;

        if (updates.price.HasValue)
            product.price = updates.price.Value;

        if (updates.oldPrice.HasValue) product.oldPrice = updates.oldPrice;
        if (updates.minPrice.HasValue) product.minPrice = updates.minPrice;
        if (updates.netPrice.HasValue) product.netPrice = updates.netPrice;
        if (updates.currency != null)
            product.currency = updates.currency == "" ? null : updates.currency;

        if (updates.price.HasValue || updates.oldPrice.HasValue)
        {
            if (product.price.HasValue)
                product.discountedPrice = product.price;
            product.discount = ProductPriceHelper.ComputeDiscountPercent(
                product.oldPrice, product.price);
        }

        if (updates.barcode != null)
        {
            var newBarcode = updates.barcode == "" ? null : updates.barcode;
            if (newBarcode != null && !ProductController.IsValidEanBarcode(newBarcode))
                throw ApiException.BadRequest($"Invalid barcode: {newBarcode}");
            product.barcode = newBarcode;
        }

        if (updates.generateBarcodes == true && string.IsNullOrEmpty(product.barcode))
            product.barcode = ProductController.GenerateEan13();
    }

    private async Task EnsureBulkUniqueAttributesAllowedAsync(
        int storeId,
        ProductBulkUpdateFields updates,
        CancellationToken ct)
    {
        if (updates.attributeValues != null)
        {
            var candidates = updates.attributeValues
                .Where(v => !string.IsNullOrWhiteSpace(v.value))
                .Select(v => v.attributeId)
                .Distinct()
                .ToList();
            if (candidates.Count > 0)
            {
                var uniqueIds = await _db.categoryAttributes
                    .AsNoTracking()
                    .Where(a => candidates.Contains(a.Id) && a.isUnique)
                    .Select(a => a.Id)
                    .ToListAsync(ct);
                if (uniqueIds.Count > 0)
                {
                    throw ApiException.BadRequest(
                        "Unique attribute values cannot be applied to multiple products at once",
                        data: new { attributeId = uniqueIds[0] },
                        code: "attribute_value_unique_bulk_forbidden");
                }
            }
        }

        if (updates.storeAttributeValues != null)
        {
            var candidates = updates.storeAttributeValues
                .Where(v => !string.IsNullOrWhiteSpace(v.value))
                .Select(v => v.storeAttributeId)
                .Distinct()
                .ToList();
            if (candidates.Count > 0)
            {
                var uniqueIds = await _db.storeAttributes
                    .AsNoTracking()
                    .Where(a => candidates.Contains(a.Id) && a.isUnique && a.storeId == storeId)
                    .Select(a => a.Id)
                    .ToListAsync(ct);
                if (uniqueIds.Count > 0)
                {
                    throw ApiException.BadRequest(
                        "Unique attribute values cannot be applied to multiple products at once",
                        data: new { storeAttributeId = uniqueIds[0] },
                        code: "attribute_value_unique_bulk_forbidden");
                }
            }
        }
    }
}
