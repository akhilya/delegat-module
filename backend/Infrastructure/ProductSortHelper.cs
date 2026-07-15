using Delegat.Models.Products;

namespace Delegat.Infrastructure;

public readonly record struct ProductSortSpec(string Column, bool Desc);

public static class ProductSortHelper
{
    private static readonly char[] SortPartSeparators = [',', ';', '|'];

    public static List<ProductSortSpec> ParseSortSpecs(string? sort, string? legacySortBy, bool legacySortDesc)
    {
        var specs = new List<ProductSortSpec>();

        if (!string.IsNullOrWhiteSpace(sort))
        {
            foreach (var part in sort.Split(SortPartSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                TryAddSortPart(specs, part);
        }
        else if (!string.IsNullOrWhiteSpace(legacySortBy))
        {
            specs.Add(new ProductSortSpec(legacySortBy.Trim().ToLowerInvariant(), legacySortDesc));
        }

        return specs
            .Where(s => !string.IsNullOrEmpty(s.Column))
            .Take(5)
            .ToList();
    }

    public static Func<IQueryable<Product>, IOrderedQueryable<Product>> BuildOrderBy(
        IReadOnlyList<ProductSortSpec> specs,
        bool hasSearch,
        string? searchQuery)
    {
        return q =>
        {
            IOrderedQueryable<Product> ordered;

            if (hasSearch)
            {
                ordered = q.OrderByDescending(p => p.name == searchQuery || p.sku == searchQuery)
                    .ThenByDescending(p => p.name.StartsWith(searchQuery!) || p.sku.StartsWith(searchQuery!));
                foreach (var spec in specs)
                    ordered = ApplyThenSort(ordered, spec);
            }
            else if (specs.Count > 0)
            {
                ordered = ApplyFirstSort(q, specs[0]);
                for (var i = 1; i < specs.Count; i++)
                    ordered = ApplyThenSort(ordered, specs[i]);
            }
            else
            {
                ordered = q.OrderByDescending(p => p.Id);
            }

            return ordered.ThenBy(p => p.Id);
        };
    }

    private static IOrderedQueryable<Product> ApplyFirstSort(IQueryable<Product> query, ProductSortSpec spec) =>
        TryGetStoreAttrId(spec.Column, out var attrId)
            ? (spec.Desc
                ? query.OrderByDescending(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault())
                : query.OrderBy(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault()))
            : spec.Column switch
            {
                "name" => spec.Desc ? query.OrderByDescending(p => p.name) : query.OrderBy(p => p.name),
                "sku" => spec.Desc ? query.OrderByDescending(p => p.sku) : query.OrderBy(p => p.sku),
                "price" => spec.Desc ? query.OrderByDescending(p => p.price) : query.OrderBy(p => p.price),
                "storagequantity" => spec.Desc ? query.OrderByDescending(p => p.storageQuantity) : query.OrderBy(p => p.storageQuantity),
                "category" => spec.Desc ? query.OrderByDescending(p => p.category!.name) : query.OrderBy(p => p.category!.name),
                "createdat" => spec.Desc ? query.OrderByDescending(p => p.createdAt) : query.OrderBy(p => p.createdAt),
                "updatedat" => spec.Desc ? query.OrderByDescending(p => p.updatedAt) : query.OrderBy(p => p.updatedAt),
                _ => spec.Desc ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
            };

    private static IOrderedQueryable<Product> ApplyThenSort(IOrderedQueryable<Product> ordered, ProductSortSpec spec) =>
        TryGetStoreAttrId(spec.Column, out var attrId)
            ? (spec.Desc
                ? ordered.ThenByDescending(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault())
                : ordered.ThenBy(p => p.storeAttributeValues.Where(v => v.storeAttributeId == attrId).Select(v => v.value).FirstOrDefault()))
            : spec.Column switch
            {
                "name" => spec.Desc ? ordered.ThenByDescending(p => p.name) : ordered.ThenBy(p => p.name),
                "sku" => spec.Desc ? ordered.ThenByDescending(p => p.sku) : ordered.ThenBy(p => p.sku),
                "price" => spec.Desc ? ordered.ThenByDescending(p => p.price) : ordered.ThenBy(p => p.price),
                "storagequantity" => spec.Desc ? ordered.ThenByDescending(p => p.storageQuantity) : ordered.ThenBy(p => p.storageQuantity),
                "category" => spec.Desc ? ordered.ThenByDescending(p => p.category!.name) : ordered.ThenBy(p => p.category!.name),
                "createdat" => spec.Desc ? ordered.ThenByDescending(p => p.createdAt) : ordered.ThenBy(p => p.createdAt),
                "updatedat" => spec.Desc ? ordered.ThenByDescending(p => p.updatedAt) : ordered.ThenBy(p => p.updatedAt),
                _ => spec.Desc ? ordered.ThenByDescending(p => p.Id) : ordered.ThenBy(p => p.Id),
            };

    private static void TryAddSortPart(List<ProductSortSpec> specs, string part)
    {
        var colon = part.IndexOf(':');
        if (colon <= 0) return;

        var column = part[..colon].Trim().ToLowerInvariant();
        var dir = part[(colon + 1)..].Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(column)) return;

        specs.Add(new ProductSortSpec(column, dir != "asc"));
    }

    private static bool TryGetStoreAttrId(string column, out int attrId)
    {
        attrId = 0;
        if (!column.StartsWith("storeattr_", StringComparison.Ordinal))
            return false;
        return int.TryParse(column["storeattr_".Length..], out attrId);
    }
}