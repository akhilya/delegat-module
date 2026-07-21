using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services.Ai.Tools;

namespace Delegat.Services.Ai.Tools.Handlers;

public class ProductToolHandlers : ToolHandlerBase
{
    private readonly DataContext _context;
    private readonly AiOperationService _operationService;
    private readonly ILogger<ProductToolHandlers> _logger;

    public ProductToolHandlers(
        DataContext context,
        AiOperationService operationService,
        ILogger<ProductToolHandlers> logger)
    {
        _context = context;
        _operationService = operationService;
        _logger = logger;
    }

    internal async Task<(object, string)> ListProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ListProductsInput>(json);
        var q = _context.products.AsNoTracking()
            .Where(x => x.storeId == storeId);

        if (p.CategoryId.HasValue) q = q.Where(x => x.categoryId == p.CategoryId);
        if (!string.IsNullOrEmpty(p.Search))
            q = q.Where(x => x.name.Contains(p.Search) || (x.sku != null && x.sku.Contains(p.Search)));
        if (p.PriceMin.HasValue) q = q.Where(x => x.price >= p.PriceMin);
        if (p.PriceMax.HasValue) q = q.Where(x => x.price <= p.PriceMax);
        if (p.QuantityMin.HasValue) q = q.Where(x => x.storageQuantity >= p.QuantityMin);
        if (p.QuantityMax.HasValue) q = q.Where(x => x.storageQuantity <= p.QuantityMax);
        if (p.HasImages == true)
            q = q.Where(x => _context.images.Any(i => i.productId == x.Id));
        if (p.HasImages == false)
            q = q.Where(x => !_context.images.Any(i => i.productId == x.Id));
        if (!string.IsNullOrEmpty(p.MissingField))
        {
            var attrId = await _context.categoryAttributes.AsNoTracking()
                .Where(a => EF.Functions.Like(a.name, p.MissingField))
                .Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
            if (attrId.HasValue)
                q = q.Where(x => !_context.productAttributeValues.Any(v => v.productId == x.Id && v.attributeId == attrId.Value && v.value != null && v.value != ""));
        }

        var limit = Math.Min(p.PageSize ?? 50, 200);
        var offset = p.StartIndex ?? 0;
        var desc = string.Equals(p.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Models.Products.Product> sorted = p.SortBy?.ToLower() switch
        {
            "price"    => desc ? q.OrderByDescending(x => x.price)    : q.OrderBy(x => x.price),
            "name"     => desc ? q.OrderByDescending(x => x.name)     : q.OrderBy(x => x.name),
            "quantity" => desc ? q.OrderByDescending(x => x.storageQuantity) : q.OrderBy(x => x.storageQuantity),
            _          => desc ? q.OrderByDescending(x => x.Id)       : q.OrderBy(x => x.Id),
        };
        var products = await sorted
            .Skip(offset).Take(limit)
            .Select(x => new { x.Id, x.name, x.sku, x.price, x.barcode, x.storageQuantity, x.categoryId })
            .ToListAsync(ct);

        // Zero hits on a text search: nudge the model to retry with spelling variants -
        // catalogs often mix Cyrillic and Latin transliterations of the same brand name
        if (products.Count == 0 && !string.IsNullOrEmpty(p.Search))
        {
            return (new
            {
                items = products,
                count = 0,
                hint = $"0 products for query '{p.Search}'. Try other spelling variants: " +
                       "Latin instead of Cyrillic (or vice versa), the English brand name, or a word stem without endings.",
            }, $"0 products for '{p.Search}' - try a different spelling (Latin/Cyrillic)");
        }

        return (new { items = products, count = products.Count }, $"Found {products.Count} products");
    }

    internal async Task<(object, string)> GetProductsCountAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<ListProductsInput>(json);
        var q = _context.products.AsNoTracking().Where(x => x.storeId == storeId);
        if (p.CategoryId.HasValue) q = q.Where(x => x.categoryId == p.CategoryId);
        if (!string.IsNullOrEmpty(p.Search)) q = q.Where(x => x.name.Contains(p.Search));
        if (p.PriceMin.HasValue) q = q.Where(x => x.price >= p.PriceMin);
        if (p.PriceMax.HasValue) q = q.Where(x => x.price <= p.PriceMax);
        var count = await q.CountAsync(ct);
        if (count == 0 && !string.IsNullOrEmpty(p.Search))
        {
            return (new
            {
                count = 0,
                hint = $"0 products for query '{p.Search}'. Try other spelling variants: " +
                       "Latin instead of Cyrillic (or vice versa), the English brand name, or a word stem without endings.",
            }, $"0 products for '{p.Search}' - try a different spelling");
        }
        return (new { count }, $"{count} products");
    }

    internal async Task<(object, string)> GetProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdInput>(json);
        var product = await _context.products.AsNoTracking()
            .Include(x => x.category)
            .Include(x => x.attributeValues).ThenInclude(v => v.attribute)
            .FirstOrDefaultAsync(x => x.Id == p.Id && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Product not found" }, "Product not found");
        var result = new
        {
            product.Id,
            product.name,
            product.sku,
            product.price,
            product.barcode,
            product.description,
            product.storageQuantity,
            category = product.category == null ? null : new { product.category.Id, product.category.name },
            attributes = product.attributeValues.Select(v => new
            {
                attributeId = v.attributeId,
                name = v.attribute?.name,
                key = v.attribute?.key,
                type = v.attribute?.type.ToString(),
                value = v.value
            }).ToList()
        };
        return (result, product.name);
    }

    internal async Task<(object, string)> CreateProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateProductInput>(json);
        var product = new Product
        {
            name = p.Name,
            sku = p.Sku ?? "",
            price = p.Price ?? 0,
            barcode = p.Barcode ?? "",
            description = p.Description ?? "",
            storageQuantity = p.Quantity ?? 0,
            categoryId = p.CategoryId,
            storeId = storeId
        };
        _context.products.Add(product);
        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "create_product",
            $"Created product \"{p.Name}\"", new { productId = product.Id }, canRollback: false, ct: ct);
        return (new { id = product.Id }, $"Product \"{p.Name}\" created (id={product.Id})");
    }

    internal async Task<(object, string)> CreateProductsBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<CreateProductsBulkInput>(json);
        if (p.Products is not { Count: > 0 })
            return (new { error = "Product list is empty" }, "List is empty");

        var created = new List<object>();
        foreach (var spec in p.Products)
        {
            var product = new Product
            {
                name = spec.Name,
                sku = spec.Sku ?? "",
                price = spec.Price ?? 0,
                barcode = spec.Barcode ?? "",
                description = spec.Description ?? "",
                storageQuantity = spec.Quantity ?? 0,
                categoryId = spec.CategoryId,
                storeId = storeId
            };
            _context.products.Add(product);
            await _context.SaveChangesAsync(ct);
            created.Add(new { id = product.Id, name = product.name });
        }
        return (new { created }, $"Created {created.Count} products");
    }

    internal async Task<(object, string)> UpdateProductAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateProductInput>(json);
        var product = await _context.products.FirstOrDefaultAsync(x => x.Id == p.Id && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Product not found" }, "Product not found");

        var changes = new List<string>();
        if (p.Name != null) { changes.Add($"name: {product.name}->{p.Name}"); product.name = p.Name; }
        if (p.Price.HasValue) { changes.Add($"price: {product.price}->{p.Price}"); product.price = p.Price.Value; }
        if (p.Description != null) product.description = p.Description;
        if (p.Barcode != null) product.barcode = p.Barcode;
        if (p.Quantity.HasValue) product.storageQuantity = p.Quantity.Value;
        if (p.CategoryId.HasValue) product.categoryId = p.CategoryId.Value;

        await _context.SaveChangesAsync(ct);
        return (new { success = true }, $"Product updated: {string.Join(", ", changes)}");
    }

    internal async Task<(object, string)> UpdateProductSkuAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateSkuInput>(json);
        var product = await _context.products.FirstOrDefaultAsync(x => x.Id == p.ProductId && x.storeId == storeId, ct);
        if (product == null) return (new { error = "Product not found" }, "Product not found");
        var oldSku = product.sku;
        product.sku = p.NewSku;
        await _context.SaveChangesAsync(ct);
        return (new { success = true, oldSku, newSku = p.NewSku }, $"SKU changed: {oldSku} -> {p.NewSku}");
    }

    internal async Task<(object, string)> UpdateProductPricesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdatePricesInput>(json);
        var q = _context.products.Where(x => x.storeId == storeId);
        if (p.ProductIds?.Count > 0) q = q.Where(x => p.ProductIds.Contains(x.Id));
        if (p.CategoryId.HasValue) q = q.Where(x => x.categoryId == p.CategoryId);

        var products = await q.ToListAsync(ct);
        var rollback = products.Select(pr => (pr.Id, pr.price ?? 0)).ToList();

        foreach (var product in products)
        {
            product.price = p.Mode switch
            {
                "set" => p.Amount,
                "subtract" => Math.Max(0, (product.price ?? 0) - p.Amount),
                "multiply" => Math.Round((product.price ?? 0) * p.Amount, 2),
                _ => (product.price ?? 0) + p.Amount // add (default)
            };
        }

        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "update_prices",
            $"Changed price for {products.Count} products ({p.Mode} {p.Amount})",
            AiOperationService.BuildPriceRollback(rollback), ct: ct);

        return (new { affected = products.Count }, $"Changed price for {products.Count} products");
    }

    internal async Task<(object, string)> UpdateProductAttributeAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateAttributeInput>(json);
        var attr = await _context.categoryAttributes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.key == p.AttributeKey && a.category.storeId == storeId, ct);
        if (attr == null) return (new { error = "Attribute not found" }, "Attribute not found");

        var productIds = p.ProductIds ?? await _context.products.AsNoTracking()
            .Where(x => x.storeId == storeId && x.categoryId == attr.categoryId)
            .Select(x => x.Id).ToListAsync(ct);

        var existing = await _context.productAttributeValues
            .Where(v => productIds.Contains(v.productId) && v.attributeId == attr.Id)
            .ToListAsync(ct);

        var oldValues = existing.Select(v => new ProductAttributeSnapshot
        { ProductId = v.productId, AttributeId = v.attributeId, OldValue = v.value }).ToList();

        var existingIds = existing.Select(v => v.productId).ToHashSet();
        foreach (var ev in existing) ev.value = p.Value;

        foreach (var pid in productIds.Where(id => !existingIds.Contains(id)))
        {
            _context.productAttributeValues.Add(new ProductAttributeValue
            { productId = pid, attributeId = attr.Id, value = p.Value });
        }

        await _context.SaveChangesAsync(ct);
        await _operationService.RecordAsync(storeId, userId, "update_attribute",
            $"Attribute '{p.AttributeKey}' = '{p.Value}' for {productIds.Count} products",
            new ProductAttributeRollbackData { Products = oldValues }, ct: ct);

        return (new { affected = productIds.Count }, $"Attribute updated for {productIds.Count} products");
    }

    internal async Task<(object, string)> UpdateProductAttributesBulkAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<UpdateAttributesBulkInput>(json);
        if (p.Attributes is not { Count: > 0 })
            return (new { error = "Attribute list is empty" }, "List is empty");

        var applied = 0;
        var errors = new List<string>();
        var allOldValues = new List<ProductAttributeSnapshot>();

        foreach (var attr in p.Attributes)
        {
            var attrDef = await _context.categoryAttributes.AsNoTracking()
                .FirstOrDefaultAsync(a => a.key == attr.AttributeKey && a.category.storeId == storeId, ct);
            if (attrDef == null) { errors.Add($"Attribute '{attr.AttributeKey}' not found"); continue; }

            var productIds = p.ProductIds ?? await _context.products.AsNoTracking()
                .Where(x => x.storeId == storeId && x.categoryId == attrDef.categoryId)
                .Select(x => x.Id).ToListAsync(ct);

            var existing = await _context.productAttributeValues
                .Where(v => productIds.Contains(v.productId) && v.attributeId == attrDef.Id)
                .ToListAsync(ct);

            allOldValues.AddRange(existing.Select(v => new ProductAttributeSnapshot
            { ProductId = v.productId, AttributeId = v.attributeId, OldValue = v.value }));

            var existingIds = existing.Select(v => v.productId).ToHashSet();
            foreach (var ev in existing) ev.value = attr.Value;
            foreach (var pid in productIds.Where(id => !existingIds.Contains(id)))
                _context.productAttributeValues.Add(new ProductAttributeValue
                { productId = pid, attributeId = attrDef.Id, value = attr.Value });

            applied++;
        }

        await _context.SaveChangesAsync(ct);
        if (allOldValues.Count > 0)
            await _operationService.RecordAsync(storeId, userId, "update_attribute",
                $"Updated {applied} attributes on products",
                new ProductAttributeRollbackData { Products = allOldValues }, ct: ct);

        var summary = $"Attributes updated: {applied}";
        if (errors.Count > 0) summary += $", errors: {errors.Count} ({string.Join("; ", errors)})";
        return (new { applied, errors }, summary);
    }

    internal async Task<(object, string)> DeleteProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdsInput>(json);
        var products = await _context.products
            .Where(x => p.Ids.Contains(x.Id) && x.storeId == storeId)
            .ToListAsync(ct);
        foreach (var pr in products) pr.deletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return (new { deleted = products.Count }, $"Deleted {products.Count} products");
    }

    internal async Task<(object, string)> RestoreProductsAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<IdsInput>(json);
        await _context.products.IgnoreQueryFilters()
            .Where(x => p.Ids.Contains(x.Id) && x.storeId == storeId && x.deletedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.deletedAt, (DateTime?)null), ct);
        return (new { success = true }, $"Restored {p.Ids.Count} products");
    }

    internal async Task<(object, string)> ListTrashAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<SearchInput>(json);
        var q = _context.products.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.storeId == storeId && x.deletedAt != null);
        if (!string.IsNullOrEmpty(p.Search)) q = q.Where(x => x.name.Contains(p.Search));
        var items = await q.OrderByDescending(x => x.deletedAt).Take(50)
            .Select(x => new { x.Id, x.name, x.deletedAt }).ToListAsync(ct);
        return (new { items, count = items.Count }, $"{items.Count} products in trash");
    }

    internal async Task<(object, string)> GenerateBarcodesAsync(string json, int storeId, int userId, CancellationToken ct)
    {
        var p = ParseInput<GenerateBarcodesInput>(json);
        var q = _context.products.Where(x => x.storeId == storeId && (x.barcode == null || x.barcode == ""));
        if (p.ProductIds?.Count > 0) q = q.Where(x => p.ProductIds.Contains(x.Id));
        var products = await q.ToListAsync(ct);
        if (products.Count == 0) return (new { generated = 0 }, "No products without a barcode");

        // Uniqueness within the store: seed with all existing barcodes
        var existing = (await _context.products.AsNoTracking()
            .Where(x => x.storeId == storeId && x.barcode != null && x.barcode != "")
            .Select(x => x.barcode!).ToListAsync(ct)).ToHashSet();

        var snapshots = new List<BarcodeSnapshot>();
        foreach (var product in products)
        {
            string bc;
            do { bc = Controllers.ProductController.GenerateEan13(); } while (!existing.Add(bc));
            snapshots.Add(new BarcodeSnapshot { Id = product.Id, OldBarcode = product.barcode });
            product.barcode = bc;
        }
        await _context.SaveChangesAsync(ct);

        await _operationService.RecordAsync(storeId, userId, "generate_barcodes",
            $"Generated EAN-13 barcodes for {products.Count} products",
            new BarcodeRollbackData { Products = snapshots }, canRollback: true, ct);

        return (new { generated = products.Count },
            $"Generated barcodes for {products.Count} products");
    }
}

// ---- Product-specific DTOs ----

internal class ListProductsInput { public int? CategoryId { get; set; } public string? Search { get; set; } public decimal? PriceMin { get; set; } public decimal? PriceMax { get; set; } public int? QuantityMin { get; set; } public int? QuantityMax { get; set; } public bool? HasImages { get; set; } public string? MissingField { get; set; } public int? StartIndex { get; set; } public int? PageSize { get; set; } public string? SortBy { get; set; } public string? SortDir { get; set; } }
internal class CreateProductInput { public string Name { get; set; } = ""; public string? Sku { get; set; } public decimal? Price { get; set; } public int CategoryId { get; set; } public string? Barcode { get; set; } public string? Description { get; set; } public int? Quantity { get; set; } }
internal class CreateProductsBulkInput { public List<CreateProductInput> Products { get; set; } = []; }
internal class UpdateProductInput { public int Id { get; set; } public string? Name { get; set; } public decimal? Price { get; set; } public string? Description { get; set; } public string? Barcode { get; set; } public int? Quantity { get; set; } public int? CategoryId { get; set; } }
internal class UpdateSkuInput { public int ProductId { get; set; } public string NewSku { get; set; } = ""; }
internal class UpdatePricesInput { public List<int>? ProductIds { get; set; } public int? CategoryId { get; set; } public decimal Amount { get; set; } public string Mode { get; set; } = "add"; }
internal class UpdateAttributeInput { public List<int>? ProductIds { get; set; } public string AttributeKey { get; set; } = ""; public string Value { get; set; } = ""; }
internal class AttributeKeyValuePair { public string AttributeKey { get; set; } = ""; public string Value { get; set; } = ""; }
internal class UpdateAttributesBulkInput { public List<int>? ProductIds { get; set; } public List<AttributeKeyValuePair> Attributes { get; set; } = []; }
internal class GenerateBarcodesInput { public List<int>? ProductIds { get; set; } }
