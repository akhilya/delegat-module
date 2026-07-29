using Delegat;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services;

/// <summary>
/// Ensures unique attribute values within the configured scope (category or store).
/// Empty / whitespace-only values are skipped. Comparison is Trim + ordinal equality.
/// </summary>
public class AttributeUniquenessService
{
    private readonly DataContext _db;

    public AttributeUniquenessService(DataContext db)
    {
        _db = db;
    }

    public async Task EnsureUniqueAsync(
        int storeId,
        int? categoryId,
        IEnumerable<ProductAttributeValue.UserValueForm>? categoryValues,
        IEnumerable<StoreProductAttributeValue.UserValueForm>? storeValues,
        int? excludeProductId = null,
        IEnumerable<int>? excludeProductIds = null,
        CancellationToken ct = default)
    {
        var excluded = BuildExcludedIds(excludeProductId, excludeProductIds);

        if (categoryValues != null && categoryId.HasValue)
            await EnsureCategoryValuesUniqueAsync(storeId, categoryId.Value, categoryValues, excluded, ct);

        if (storeValues != null)
            await EnsureStoreValuesUniqueAsync(storeId, storeValues, excluded, ct);
    }

    private static HashSet<int> BuildExcludedIds(int? excludeProductId, IEnumerable<int>? excludeProductIds)
    {
        var excluded = new HashSet<int>();
        if (excludeProductId.HasValue)
            excluded.Add(excludeProductId.Value);
        if (excludeProductIds != null)
        {
            foreach (var id in excludeProductIds)
                excluded.Add(id);
        }
        return excluded;
    }

    private async Task EnsureCategoryValuesUniqueAsync(
        int storeId,
        int categoryId,
        IEnumerable<ProductAttributeValue.UserValueForm> values,
        HashSet<int> excludeProductIds,
        CancellationToken ct)
    {
        var candidates = values
            .Select(v => (v.attributeId, normalized: Normalize(v.value)))
            .Where(v => v.normalized != null)
            .GroupBy(v => v.attributeId)
            .Select(g => g.Last())
            .ToList();

        if (candidates.Count == 0)
            return;

        var attributeIds = candidates.Select(c => c.attributeId).Distinct().ToList();
        var uniqueAttrIds = await _db.categoryAttributes
            .AsNoTracking()
            .Where(a => attributeIds.Contains(a.Id) && a.isUnique && a.categoryId == categoryId)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (uniqueAttrIds.Count == 0)
            return;

        foreach (var (attributeId, normalized) in candidates.Where(c => uniqueAttrIds.Contains(c.attributeId)))
        {
            var query = _db.productAttributeValues
                .AsNoTracking()
                .Where(av =>
                    av.attributeId == attributeId &&
                    av.value != null &&
                    av.product.storeId == storeId &&
                    av.product.categoryId == categoryId &&
                    av.product.deletedAt == null);

            if (excludeProductIds.Count > 0)
                query = query.Where(av => !excludeProductIds.Contains(av.productId));

            var conflicting = await query
                .Select(av => new { av.productId, av.value })
                .ToListAsync(ct);

            var match = conflicting.FirstOrDefault(av =>
                string.Equals(Normalize(av.value), normalized, StringComparison.Ordinal));

            if (match != null)
            {
                throw ApiException.BadRequest(
                    "Attribute value is already used by another product",
                    data: new
                    {
                        attributeId,
                        value = normalized,
                        conflictingProductId = match.productId
                    },
                    code: "attribute_value_not_unique");
            }
        }
    }

    private async Task EnsureStoreValuesUniqueAsync(
        int storeId,
        IEnumerable<StoreProductAttributeValue.UserValueForm> values,
        HashSet<int> excludeProductIds,
        CancellationToken ct)
    {
        var candidates = values
            .Select(v => (v.storeAttributeId, normalized: Normalize(v.value)))
            .Where(v => v.normalized != null)
            .GroupBy(v => v.storeAttributeId)
            .Select(g => g.Last())
            .ToList();

        if (candidates.Count == 0)
            return;

        var attributeIds = candidates.Select(c => c.storeAttributeId).Distinct().ToList();
        var uniqueAttrIds = await _db.storeAttributes
            .AsNoTracking()
            .Where(a => attributeIds.Contains(a.Id) && a.isUnique && a.storeId == storeId)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (uniqueAttrIds.Count == 0)
            return;

        foreach (var (storeAttributeId, normalized) in candidates.Where(c => uniqueAttrIds.Contains(c.storeAttributeId)))
        {
            var query = _db.storeProductAttributeValues
                .AsNoTracking()
                .Where(av =>
                    av.storeAttributeId == storeAttributeId &&
                    av.value != null &&
                    av.product.storeId == storeId &&
                    av.product.deletedAt == null);

            if (excludeProductIds.Count > 0)
                query = query.Where(av => !excludeProductIds.Contains(av.productId));

            var conflicting = await query
                .Select(av => new { av.productId, av.value })
                .ToListAsync(ct);

            var match = conflicting.FirstOrDefault(av =>
                string.Equals(Normalize(av.value), normalized, StringComparison.Ordinal));

            if (match != null)
            {
                throw ApiException.BadRequest(
                    "Attribute value is already used by another product",
                    data: new
                    {
                        storeAttributeId,
                        value = normalized,
                        conflictingProductId = match.productId
                    },
                    code: "attribute_value_not_unique");
            }
        }
    }

    /// <summary>Returns trimmed value, or null when empty / whitespace-only.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }
}
