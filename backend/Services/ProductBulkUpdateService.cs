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
    /// <summary>Add this amount to each card (and sized variant that has its own price).</summary>
    public decimal? priceDelta { get; set; }
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
    /// <summary>How many products to persist per SaveChanges round-trip.</summary>
    private const int SaveChunkSize = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly DataContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AttributeUniquenessService _attributeUniqueness;
    private readonly Func<string> _barcodeGenerator;

    public ProductBulkUpdateService(
        DataContext db,
        IUnitOfWork unitOfWork,
        AttributeUniquenessService attributeUniqueness,
        Func<string>? barcodeGenerator = null)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _attributeUniqueness = attributeUniqueness;
        _barcodeGenerator = barcodeGenerator ?? ProductController.GenerateEan13;
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
            throw ApiException.BadRequest(code: "products_required");

        if (!HasAnyUpdate(updates))
            throw ApiException.BadRequest(code: "product_fields_required");

        if (updates.price.HasValue && updates.priceDelta.HasValue)
            throw ApiException.BadRequest(code: "price_delta_conflict");

        if (!string.IsNullOrEmpty(updates.barcode))
        {
            if (!ProductController.IsValidEanBarcode(updates.barcode))
                throw ApiException.BadRequest(code: "barcode_invalid");
            if (productIds.Distinct().Count() > 1 ||
                await _db.products.IgnoreQueryFilters().AnyAsync(p => p.storeId == storeId &&
                    p.barcode == updates.barcode && !productIds.Contains(p.Id), ct) ||
                await _db.productVariants.IgnoreQueryFilters().AnyAsync(v => v.product.storeId == storeId &&
                    v.barcode == updates.barcode, ct))
                throw ApiException.Conflict(code: "barcode_already_used");
        }

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
        string? operationId = null,
        PendingProductBulkUpdate? job = null)
    {
        productIds = productIds.Distinct().ToList();
        var result = new ProductBulkUpdateResult
        {
            updatedCount = job?.completedCount ?? 0,
            failedCount = job?.failedCount ?? 0
        };
        if (job != null && (_db.Entry(job).State == EntityState.Detached || job.storeId != storeId || job.processedCount < 0
            || job.processedCount > productIds.Count || job.completedCount < 0 || job.failedCount < 0
            || job.completedCount + job.failedCount != job.processedCount))
            throw new InvalidOperationException("Bulk update checkpoint does not match the product selection");
        if (job?.lastError is { Length: > 0 } resumedError)
            result.errors.Add(resumedError);
        var total = productIds.Count;

        // Queued jobs may outlive the catalog state checked at enqueue time.
        if (!string.IsNullOrEmpty(updates.barcode))
            await ValidateBeforeEnqueueAsync(storeId, productIds, updates, ct);

        if (operationLogger != null && operationId != null)
        {
            await operationLogger.LogAsync(operationId, storeId, "info",
                $"Starting bulk update for {total} product(s)");
            await ReportProgressAsync(operationLogger, operationId, storeId, total, result);
            await LogUpdateSummaryAsync(operationLogger, operationId, storeId, updates);
        }

        // Pre-check unique attrs across the whole batch once (same rule as enqueue).
        if (productIds.Count > 1)
            await EnsureBulkUniqueAttributesAllowedAsync(storeId, updates, ct);

        if (total == 1 && (updates.attributeValues != null || updates.storeAttributeValues != null))
        {
            var target = await _db.products.AsNoTracking()
                .Where(p => p.Id == productIds[0] && p.storeId == storeId)
                .Select(p => new { p.categoryId }).SingleOrDefaultAsync(ct);
            if (target != null)
                await _attributeUniqueness.EnsureUniqueAsync(storeId, target.categoryId,
                    updates.attributeValues, updates.storeAttributeValues, excludeProductIds: productIds, ct: ct);
        }

        HashSet<string>? occupiedBarcodes = null;
        if (updates.generateBarcodes == true)
        {
            occupiedBarcodes = (await _db.products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.storeId == storeId && p.barcode != null)
                .Select(p => p.barcode!).ToListAsync(ct))
                .Concat(await _db.productVariants.IgnoreQueryFilters().AsNoTracking()
                    .Where(v => v.product.storeId == storeId && v.barcode != null)
                    .Select(v => v.barcode!).ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
        }
        for (var offset = job?.processedCount ?? 0; offset < total; offset += SaveChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var ids = productIds.Skip(offset).Take(SaveChunkSize).ToList();
            var products = await _db.products.AsSplitQuery()
                .Include(p => p.attributeValues)
                .Include(p => p.storeAttributeValues)
                .Include(p => p.variants)
                .Where(p => ids.Contains(p.Id) && p.storeId == storeId).ToListAsync(ct);
            var byId = products.ToDictionary(p => p.Id);
            var errors = new List<string>();
            var applied = 0;
            var previousError = job?.lastError;
            try
            {
                foreach (var id in ids)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!byId.TryGetValue(id, out var product))
                    {
                        errors.Add($"Product #{id} not found in store");
                        continue;
                    }
                    try
                    {
                        ApplyProductFields(product, updates);
                        if (occupiedBarcodes != null && string.IsNullOrEmpty(product.barcode))
                        {
                            string candidate;
                            do
                            {
                                ct.ThrowIfCancellationRequested();
                                candidate = _barcodeGenerator();
                            } while (!occupiedBarcodes.Add(candidate));
                            product.barcode = candidate;
                        }
                        foreach (var av in updates.attributeValues ?? [])
                        {
                            var value = product.attributeValues.FirstOrDefault(v => v.attributeId == av.attributeId);
                            if (value == null)
                                product.attributeValues.Add(new ProductAttributeValue { productId = id, attributeId = av.attributeId, value = av.value });
                            else value.value = av.value;
                        }
                        foreach (var av in updates.storeAttributeValues ?? [])
                        {
                            var value = product.storeAttributeValues.FirstOrDefault(v => v.storeAttributeId == av.storeAttributeId);
                            if (value == null)
                                product.storeAttributeValues.Add(new StoreProductAttributeValue { productId = id, storeAttributeId = av.storeAttributeId, value = av.value });
                            else value.value = av.value;
                        }
                        applied++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        DiscardChunkChanges([product]);
                        errors.Add($"Product #{id}: {ex.Message}");
                    }
                }

                SetCheckpoint(job, offset + ids.Count, result.updatedCount + applied,
                    result.failedCount + errors.Count, errors.FirstOrDefault() ?? previousError);
                // EF commits the products, variants, attributes and durable job checkpoint together.
                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException)
            {
                SetCheckpoint(job, offset, result.updatedCount, result.failedCount, previousError);
                DiscardChunkChanges(products);
                throw;
            }
            catch (DbUpdateConcurrencyException)
            {
                SetCheckpoint(job, offset, result.updatedCount, result.failedCount, previousError);
                throw; // Another worker owns the committed prefix; do not mark its work as failed.
            }
            catch (Exception ex)
            {
                DiscardChunkChanges(products);
                applied = 0;
                errors = ids.Select(id => $"Product #{id}: batch was not saved ({ex.Message})").ToList();
                SetCheckpoint(job, offset + ids.Count, result.updatedCount, result.failedCount + ids.Count, errors[0]);
                // Do not let failed tracked changes leak into the next batch or the worker's status save.
                // If even the checkpoint cannot be saved, abort and resume at the previous committed prefix.
                if (job != null)
                {
                    try { await _unitOfWork.SaveChangesAsync(ct); }
                    catch
                    {
                        SetCheckpoint(job, offset, result.updatedCount, result.failedCount, previousError);
                        throw;
                    }
                }
            }
            finally { DiscardChunkChanges(products); }

            result.updatedCount += applied;
            result.failedCount += errors.Count;
            result.errors.AddRange(errors);
            if (operationLogger != null && operationId != null)
                foreach (var error in errors)
                    await operationLogger.LogAsync(operationId, storeId, "error", error);
            await ReportProgressAsync(operationLogger, operationId, storeId, total, result);
        }

        if (operationLogger != null && operationId != null)
        {
            await operationLogger.LogAsync(operationId, storeId, "info",
                $"Finished: updated {result.updatedCount}, failed {result.failedCount}");
        }

        return result;
    }

    private static void SetCheckpoint(PendingProductBulkUpdate? job, int processed, int completed, int failed, string? error)
    {
        if (job == null) return;
        job.processedCount = processed;
        job.completedCount = completed;
        job.failedCount = failed;
        job.lastError = error;
    }

    private void DiscardChunkChanges(IEnumerable<Product> products)
    {
        var ids = products.Select(p => p.Id).ToHashSet();
        foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.Entity switch
        {
            Product p => ids.Contains(p.Id),
            ProductVariant v => ids.Contains(v.productId),
            ProductAttributeValue v => ids.Contains(v.productId),
            StoreProductAttributeValue v => ids.Contains(v.productId),
            _ => false
        }).ToList())
        {
            if (entry.State == EntityState.Modified)
                entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Detached;
        }
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
        if (updates.priceDelta.HasValue) parts.Add($"priceDelta {updates.priceDelta.Value}");
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
        || updates.priceDelta.HasValue
        || updates.oldPrice.HasValue
        || updates.minPrice.HasValue
        || updates.netPrice.HasValue
        || updates.currency != null
        || updates.barcode != null
        || updates.generateBarcodes == true
        || (updates.attributeValues != null && updates.attributeValues.Count > 0)
        || (updates.storeAttributeValues != null && updates.storeAttributeValues.Count > 0);

    public async Task<List<int>> ListCatalogProductIdsAsync(int storeId, CancellationToken ct = default) =>
        await _db.products.AsNoTracking()
            .Where(p => p.storeId == storeId)
            .Select(p => p.Id)
            .ToListAsync(ct);

    private static decimal AddPriceDelta(decimal? current, decimal delta)
    {
        var next = (current ?? 0m) + delta;
        return next < 0m ? 0m : next;
    }

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

        if (updates.priceDelta.HasValue)
        {
            product.price = AddPriceDelta(product.price, updates.priceDelta.Value);
            foreach (var variant in product.variants)
            {
                if (variant.price.HasValue)
                    variant.price = AddPriceDelta(variant.price, updates.priceDelta.Value);
            }
        }

        if (updates.oldPrice.HasValue) product.oldPrice = updates.oldPrice;
        if (updates.minPrice.HasValue) product.minPrice = updates.minPrice;
        if (updates.netPrice.HasValue) product.netPrice = updates.netPrice;
        if (updates.currency != null)
            product.currency = updates.currency == "" ? null : updates.currency;

        if (updates.price.HasValue || updates.oldPrice.HasValue || updates.priceDelta.HasValue)
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
                throw ApiException.BadRequest(code: "barcode_invalid");
            product.barcode = newBarcode;
        }
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
