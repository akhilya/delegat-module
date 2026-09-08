using Delegat.Infrastructure.Auth;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Models.Products;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

public sealed class SqlReadonlyService
{
    private readonly DataContext _db;

    public SqlReadonlyService(DataContext db) => _db = db;

    public async Task<(object result, string summary)> QueryAsync(
        int storeId,
        string sql,
        IReadOnlySet<string> permissions,
        CancellationToken ct)
    {
        var parsed = AgentSqlGuard.Parse(sql);
        if (parsed.Error != null)
            return (new { error = parsed.Error }, parsed.Error);

        if (parsed.Table is "orders" && !AgentAccess.CanRead(permissions, Permissions.OrdersManage))
            return (new { error = "orders.manage required" }, "orders.manage required");
        if (parsed.Table is "connections" or "mappings"
            && !AgentAccess.CanRead(permissions, Permissions.MarketplaceManage))
            return (new { error = "marketplace.manage required" }, "marketplace.manage required");
        if (parsed.Table == "files" && !AgentAccess.CanRead(permissions, Permissions.FilesManage))
            return (new { error = "files.manage required" }, "files.manage required");

        if (parsed.IsAggregate)
        {
            var row = await AggregateRowAsync(storeId, parsed, ct);
            return (new { table = parsed.Table, columns = parsed.Columns, rows = new[] { row } }, "1 rows");
        }

        var rows = parsed.Table switch
        {
            "products" => await QueryProductsAsync(storeId, parsed, ct),
            "files" => await QueryFilesAsync(storeId, parsed, ct),
            "categories" => await QueryCategoriesAsync(storeId, parsed, ct),
            "category_attributes" => await QueryCategoryAttributesAsync(storeId, parsed, ct),
            "product_attribute_values" => await QueryProductAttributeValuesAsync(storeId, parsed, ct),
            "store_attributes" => await QueryStoreAttributesAsync(storeId, parsed, ct),
            "store_product_attribute_values" => await QueryStoreProductAttributeValuesAsync(storeId, parsed, ct),
            "groups" => await QueryGroupsAsync(storeId, parsed, ct),
            "connections" => await QueryConnectionsAsync(storeId, parsed, ct),
            "mappings" => await QueryMappingsAsync(storeId, parsed, ct),
            "orders" => await QueryOrdersAsync(storeId, parsed, ct),
            _ => [],
        };

        return (new { table = parsed.Table, columns = parsed.Columns, rows }, $"{rows.Count} rows");
    }

    private async Task<Dictionary<string, object?>> AggregateRowAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in q.Select)
        {
            row[item.OutputName] = item.Kind switch
            {
                AgentSqlSelectKind.CountStar => await CountRowsAsync(storeId, q, ct),
                AgentSqlSelectKind.CountDistinct => await CountDistinctAsync(storeId, q, item.Column!, ct),
                _ => null,
            };
        }

        return row;
    }

    private Task<int> CountRowsAsync(int storeId, AgentSqlQuery q, CancellationToken ct) =>
        q.Table switch
        {
            "products" => ProductSource(storeId, q).CountAsync(ct),
            "files" => FileSource(storeId, q).CountAsync(ct),
            "categories" => CategorySource(storeId, q).CountAsync(ct),
            "category_attributes" => CategoryAttributeSource(storeId, q).CountAsync(ct),
            "product_attribute_values" => ProductAttributeValueSource(storeId, q).CountAsync(ct),
            "store_attributes" => StoreAttributeSource(storeId).CountAsync(ct),
            "store_product_attribute_values" => StoreProductAttributeValueSource(storeId, q).CountAsync(ct),
            "groups" => GroupSource(storeId).CountAsync(ct),
            "connections" => ConnectionSource(storeId).CountAsync(ct),
            "mappings" => MappingSource(storeId).CountAsync(ct),
            "orders" => OrderSource(storeId).CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private Task<int> CountDistinctAsync(int storeId, AgentSqlQuery q, string column, CancellationToken ct)
    {
        var col = column.ToLowerInvariant();
        return q.Table switch
        {
            "products" => CountDistinctProductsAsync(ProductSource(storeId, q), col, ct),
            "files" => CountDistinctFilesAsync(FileSource(storeId, q), col, ct),
            "categories" => CountDistinctCategoriesAsync(CategorySource(storeId, q), col, ct),
            "category_attributes" => CountDistinctCategoryAttributesAsync(CategoryAttributeSource(storeId, q), col, ct),
            "product_attribute_values" => CountDistinctProductAttributeValuesAsync(
                ProductAttributeValueSource(storeId, q), col, ct),
            "store_attributes" => CountDistinctStoreAttributesAsync(StoreAttributeSource(storeId), col, ct),
            "store_product_attribute_values" => CountDistinctStoreProductAttributeValuesAsync(
                StoreProductAttributeValueSource(storeId, q), col, ct),
            "groups" => CountDistinctGroupsAsync(GroupSource(storeId), col, ct),
            "connections" => CountDistinctConnectionsAsync(ConnectionSource(storeId), col, ct),
            "mappings" => CountDistinctMappingsAsync(MappingSource(storeId), col, ct),
            "orders" => CountDistinctOrdersAsync(OrderSource(storeId), col, ct),
            _ => Task.FromResult(0),
        };
    }

    private static Task<int> CountDistinctProductsAsync(IQueryable<Product> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(p => p.Id).Distinct().CountAsync(ct),
            "sku" => src.Select(p => p.sku).Distinct().CountAsync(ct),
            "barcode" => src.Select(p => p.barcode).Distinct().CountAsync(ct),
            "name" => src.Select(p => p.name).Distinct().CountAsync(ct),
            "price" => src.Select(p => p.price).Distinct().CountAsync(ct),
            "storagequantity" => src.Select(p => p.storageQuantity).Distinct().CountAsync(ct),
            "categoryid" => src.Select(p => p.categoryId).Distinct().CountAsync(ct),
            "description" => src.Select(p => p.description).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctFilesAsync(IQueryable<Delegat.Models.File> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(f => f.Id).Distinct().CountAsync(ct),
            "productid" => src.Select(f => f.productId).Distinct().CountAsync(ct),
            "displayname" => src.Select(f => f.displayName).Distinct().CountAsync(ct),
            "filepath" => src.Select(f => f.filePath).Distinct().CountAsync(ct),
            "contenttype" => src.Select(f => f.contentType).Distinct().CountAsync(ct),
            "length" => src.Select(f => f.length).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctCategoriesAsync(IQueryable<Category> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(c => c.Id).Distinct().CountAsync(ct),
            "name" => src.Select(c => c.name).Distinct().CountAsync(ct),
            "description" => src.Select(c => c.description).Distinct().CountAsync(ct),
            "hassizes" => src.Select(c => c.hasSizes).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctCategoryAttributesAsync(
        IQueryable<CategoryAttribute> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(a => a.Id).Distinct().CountAsync(ct),
            "categoryid" => src.Select(a => a.categoryId).Distinct().CountAsync(ct),
            "name" => src.Select(a => a.name).Distinct().CountAsync(ct),
            "key" => src.Select(a => a.key).Distinct().CountAsync(ct),
            "type" => src.Select(a => a.type).Distinct().CountAsync(ct),
            "isrequired" => src.Select(a => a.isRequired).Distinct().CountAsync(ct),
            "isunique" => src.Select(a => a.isUnique).Distinct().CountAsync(ct),
            "sortorder" => src.Select(a => a.sortOrder).Distinct().CountAsync(ct),
            "group" => src.Select(a => a.group).Distinct().CountAsync(ct),
            "defaultvalue" => src.Select(a => a.defaultValue).Distinct().CountAsync(ct),
            "description" => src.Select(a => a.description).Distinct().CountAsync(ct),
            "allowcustomvalues" => src.Select(a => a.allowCustomValues).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctProductAttributeValuesAsync(
        IQueryable<ProductAttributeValue> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(v => v.Id).Distinct().CountAsync(ct),
            "productid" => src.Select(v => v.productId).Distinct().CountAsync(ct),
            "attributeid" => src.Select(v => v.attributeId).Distinct().CountAsync(ct),
            "value" => src.Select(v => v.value).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctStoreAttributesAsync(
        IQueryable<StoreAttribute> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(a => a.Id).Distinct().CountAsync(ct),
            "name" => src.Select(a => a.name).Distinct().CountAsync(ct),
            "key" => src.Select(a => a.key).Distinct().CountAsync(ct),
            "type" => src.Select(a => a.type).Distinct().CountAsync(ct),
            "isrequired" => src.Select(a => a.isRequired).Distinct().CountAsync(ct),
            "sortorder" => src.Select(a => a.sortOrder).Distinct().CountAsync(ct),
            "description" => src.Select(a => a.description).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctStoreProductAttributeValuesAsync(
        IQueryable<StoreProductAttributeValue> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(v => v.Id).Distinct().CountAsync(ct),
            "productid" => src.Select(v => v.productId).Distinct().CountAsync(ct),
            "storeattributeid" => src.Select(v => v.storeAttributeId).Distinct().CountAsync(ct),
            "value" => src.Select(v => v.value).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctGroupsAsync(IQueryable<ProductGroup> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(g => g.Id).Distinct().CountAsync(ct),
            "name" => src.Select(g => g.name).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctConnectionsAsync(
        IQueryable<MarketplaceConnection> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(c => c.Id).Distinct().CountAsync(ct),
            "name" => src.Select(c => c.name).Distinct().CountAsync(ct),
            "adapterkey" => src.Select(c => c.adapterKey).Distinct().CountAsync(ct),
            "isactive" => src.Select(c => c.isActive).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctMappingsAsync(IQueryable<CategoryMapping> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(m => m.Id).Distinct().CountAsync(ct),
            "name" => src.Select(m => m.name).Distinct().CountAsync(ct),
            "connectionid" => src.Select(m => m.connectionId).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private static Task<int> CountDistinctOrdersAsync(IQueryable<MarketplaceOrder> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(o => o.Id).Distinct().CountAsync(ct),
            "marketplaceorderid" => src.Select(o => o.marketplaceOrderId).Distinct().CountAsync(ct),
            "displayid" => src.Select(o => o.displayId).Distinct().CountAsync(ct),
            "status" => src.Select(o => o.status).Distinct().CountAsync(ct),
            "channel" => src.Select(o => o.channel).Distinct().CountAsync(ct),
            "total" => src.Select(o => o.total).Distinct().CountAsync(ct),
            "ordercreatedat" => src.Select(o => o.orderCreatedAt).Distinct().CountAsync(ct),
            "connectionid" => src.Select(o => o.connectionId).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private IQueryable<Product> ProductSource(int storeId, AgentSqlQuery q)
    {
        var src = _db.products.AsNoTracking().Where(p => p.storeId == storeId && p.deletedAt == null);
        return ApplyProductWhere(src, q);
    }

    private async Task<List<Dictionary<string, object?>>> QueryProductsAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var src = ProductSource(storeId, q);
        src = q.OrderByColumn?.ToLowerInvariant() switch
        {
            "name" => q.OrderDescending ? src.OrderByDescending(p => p.name) : src.OrderBy(p => p.name),
            "sku" => q.OrderDescending ? src.OrderByDescending(p => p.sku) : src.OrderBy(p => p.sku),
            "price" => q.OrderDescending ? src.OrderByDescending(p => p.price) : src.OrderBy(p => p.price),
            "storagequantity" => q.OrderDescending ? src.OrderByDescending(p => p.storageQuantity) : src.OrderBy(p => p.storageQuantity),
            "id" => q.OrderDescending ? src.OrderByDescending(p => p.Id) : src.OrderBy(p => p.Id),
            _ => src.OrderBy(p => p.Id),
        };
        var list = await src.Take(q.Limit).ToListAsync(ct);
        return list.Select(p => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = p.Id,
            ["sku"] = p.sku,
            ["barcode"] = p.barcode,
            ["name"] = p.name,
            ["price"] = p.price,
            ["storagequantity"] = p.storageQuantity,
            ["categoryid"] = p.categoryId,
            ["description"] = p.description,
        })).ToList();
    }

    private static IQueryable<Product> ApplyProductWhere(IQueryable<Product> src, AgentSqlQuery q)
    {
        if (q.Where == null) return src;
        foreach (var pred in q.Where)
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("sku", AgentSqlOp.Like) => src.Where(p => p.sku.Contains(val.Trim('%'))),
                ("sku", AgentSqlOp.Eq) => src.Where(p => p.sku == val),
                ("name", AgentSqlOp.Like) => src.Where(p => p.name.Contains(val.Trim('%'))),
                ("name", AgentSqlOp.Eq) => src.Where(p => p.name == val),
                ("description", AgentSqlOp.Like) => src.Where(p => p.description.Contains(val.Trim('%'))),
                ("barcode", AgentSqlOp.Like) => src.Where(p => p.barcode != null && p.barcode.Contains(val.Trim('%'))),
                ("barcode", AgentSqlOp.Eq) => src.Where(p => p.barcode == val),
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(p => p.Id == id),
                ("id", AgentSqlOp.In) when ids.Count > 0 => src.Where(p => ids.Contains(p.Id)),
                ("categoryid", AgentSqlOp.Eq) when TryInt(val, out var cid) => src.Where(p => p.categoryId == cid),
                ("categoryid", AgentSqlOp.In) when ids.Count > 0 =>
                    src.Where(p => p.categoryId != null && ids.Contains(p.categoryId.Value)),
                ("price", AgentSqlOp.Eq) when decimal.TryParse(val, out var price) => src.Where(p => p.price == price),
                ("price", AgentSqlOp.Gt) when decimal.TryParse(val, out var priceGt) => src.Where(p => p.price > priceGt),
                ("price", AgentSqlOp.Lt) when decimal.TryParse(val, out var priceLt) => src.Where(p => p.price < priceLt),
                ("storagequantity", AgentSqlOp.Eq) when TryInt(val, out var qty) => src.Where(p => p.storageQuantity == qty),
                ("storagequantity", AgentSqlOp.Gt) when TryInt(val, out var qtyGt) => src.Where(p => p.storageQuantity > qtyGt),
                ("storagequantity", AgentSqlOp.Lt) when TryInt(val, out var qtyLt) => src.Where(p => p.storageQuantity < qtyLt),
                _ => src,
            };
        }

        return src;
    }

    private IQueryable<Delegat.Models.File> FileSource(int storeId, AgentSqlQuery q)
    {
        var src = _db.files.AsNoTracking().Where(f => f.product.storeId == storeId);
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("productid", AgentSqlOp.Eq) when TryInt(val, out var pid) => src.Where(f => f.productId == pid),
                ("productid", AgentSqlOp.In) when ids.Count > 0 => src.Where(f => ids.Contains(f.productId)),
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(f => f.Id == id),
                ("id", AgentSqlOp.In) when ids.Count > 0 => src.Where(f => ids.Contains(f.Id)),
                _ => src,
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryFilesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await FileSource(storeId, q).Take(q.Limit).ToListAsync(ct);
        return list.Select(f => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = f.Id,
            ["productid"] = f.productId,
            ["displayname"] = f.displayName,
            ["filepath"] = f.filePath,
            ["contenttype"] = f.contentType,
            ["length"] = f.length,
        })).ToList();
    }

    private IQueryable<Category> CategorySource(int storeId, AgentSqlQuery q)
    {
        var src = _db.categories.AsNoTracking().Where(c => c.storeId == storeId);
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(c => c.Id == id),
                ("id", AgentSqlOp.In) when ids.Count > 0 => src.Where(c => ids.Contains(c.Id)),
                ("name", AgentSqlOp.Like) => src.Where(c => c.name.Contains(val.Trim('%'))),
                ("name", AgentSqlOp.Eq) => src.Where(c => c.name == val),
                _ => src,
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryCategoriesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var src = CategorySource(storeId, q);

        src = q.OrderByColumn?.ToLowerInvariant() switch
        {
            "name" => q.OrderDescending ? src.OrderByDescending(c => c.name) : src.OrderBy(c => c.name),
            _ => src.OrderBy(c => c.Id),
        };

        var list = await src.Take(q.Limit).ToListAsync(ct);
        return list.Select(c => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = c.Id,
            ["name"] = c.name,
            ["description"] = c.description,
            ["hassizes"] = c.hasSizes,
        })).ToList();
    }

    private IQueryable<CategoryAttribute> CategoryAttributeSource(int storeId, AgentSqlQuery q)
    {
        var categoryIds = _db.categories.AsNoTracking().Where(c => c.storeId == storeId).Select(c => c.Id);
        var src = _db.categoryAttributes.AsNoTracking().Where(a => categoryIds.Contains(a.categoryId));
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(a => a.Id == id),
                ("id", AgentSqlOp.In) when ids.Count > 0 => src.Where(a => ids.Contains(a.Id)),
                ("categoryid", AgentSqlOp.Eq) when TryInt(val, out var cid) => src.Where(a => a.categoryId == cid),
                ("categoryid", AgentSqlOp.In) when ids.Count > 0 => src.Where(a => ids.Contains(a.categoryId)),
                ("name", AgentSqlOp.Like) => src.Where(a => a.name.Contains(val.Trim('%'))),
                ("key", AgentSqlOp.Eq) => src.Where(a => a.key == val),
                _ => src,
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryCategoryAttributesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await CategoryAttributeSource(storeId, q).OrderBy(a => a.sortOrder).Take(q.Limit).ToListAsync(ct);
        return list.Select(a => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = a.Id,
            ["categoryid"] = a.categoryId,
            ["name"] = a.name,
            ["key"] = a.key,
            ["type"] = a.type.ToString(),
            ["isrequired"] = a.isRequired,
            ["isunique"] = a.isUnique,
            ["sortorder"] = a.sortOrder,
            ["group"] = a.group,
            ["defaultvalue"] = a.defaultValue,
            ["description"] = a.description,
            ["allowcustomvalues"] = a.allowCustomValues,
        })).ToList();
    }

    private IQueryable<ProductAttributeValue> ProductAttributeValueSource(int storeId, AgentSqlQuery q)
    {
        var productIds = _db.products.AsNoTracking().Where(p => p.storeId == storeId).Select(p => p.Id);
        var src = _db.productAttributeValues.AsNoTracking().Where(v => productIds.Contains(v.productId));
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("productid", AgentSqlOp.Eq) when TryInt(val, out var pid) => src.Where(v => v.productId == pid),
                ("productid", AgentSqlOp.In) when ids.Count > 0 => src.Where(v => ids.Contains(v.productId)),
                ("attributeid", AgentSqlOp.Eq) when TryInt(val, out var aid) => src.Where(v => v.attributeId == aid),
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(v => v.Id == id),
                _ => src,
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryProductAttributeValuesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await ProductAttributeValueSource(storeId, q).Take(q.Limit).ToListAsync(ct);
        return list.Select(v => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = v.Id,
            ["productid"] = v.productId,
            ["attributeid"] = v.attributeId,
            ["value"] = Truncate(v.value, 2000),
        })).ToList();
    }

    private IQueryable<StoreAttribute> StoreAttributeSource(int storeId) =>
        _db.storeAttributes.AsNoTracking().Where(a => a.storeId == storeId);

    private async Task<List<Dictionary<string, object?>>> QueryStoreAttributesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var src = StoreAttributeSource(storeId);
        var list = await src.OrderBy(a => a.sortOrder).Take(q.Limit).ToListAsync(ct);
        return list.Select(a => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = a.Id,
            ["name"] = a.name,
            ["key"] = a.key,
            ["type"] = a.type.ToString(),
            ["isrequired"] = a.isRequired,
            ["sortorder"] = a.sortOrder,
            ["description"] = a.description,
        })).ToList();
    }

    private IQueryable<StoreProductAttributeValue> StoreProductAttributeValueSource(int storeId, AgentSqlQuery q)
    {
        var productIds = _db.products.AsNoTracking().Where(p => p.storeId == storeId).Select(p => p.Id);
        var src = _db.storeProductAttributeValues.AsNoTracking().Where(v => productIds.Contains(v.productId));
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("productid", AgentSqlOp.Eq) when TryInt(val, out var pid) => src.Where(v => v.productId == pid),
                ("productid", AgentSqlOp.In) when ids.Count > 0 => src.Where(v => ids.Contains(v.productId)),
                ("storeattributeid", AgentSqlOp.Eq) when TryInt(val, out var aid) => src.Where(v => v.storeAttributeId == aid),
                _ => src,
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryStoreProductAttributeValuesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await StoreProductAttributeValueSource(storeId, q).Take(q.Limit).ToListAsync(ct);
        return list.Select(v => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = v.Id,
            ["productid"] = v.productId,
            ["storeattributeid"] = v.storeAttributeId,
            ["value"] = Truncate(v.value, 2000),
        })).ToList();
    }

    private IQueryable<ProductGroup> GroupSource(int storeId) =>
        _db.productGroups.AsNoTracking().Where(g => g.storeId == storeId);

    private async Task<List<Dictionary<string, object?>>> QueryGroupsAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await GroupSource(storeId).Take(q.Limit).ToListAsync(ct);
        return list.Select(g => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = g.Id,
            ["name"] = g.name,
        })).ToList();
    }

    private IQueryable<MarketplaceConnection> ConnectionSource(int storeId) =>
        _db.marketplaceConnections.AsNoTracking().Where(c => c.storeId == storeId);

    private async Task<List<Dictionary<string, object?>>> QueryConnectionsAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await ConnectionSource(storeId).Take(q.Limit).ToListAsync(ct);
        return list.Select(c => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = c.Id,
            ["name"] = c.name,
            ["adapterkey"] = c.adapterKey,
            ["isactive"] = c.isActive,
        })).ToList();
    }

    private IQueryable<CategoryMapping> MappingSource(int storeId) =>
        _db.categoryMappings.AsNoTracking().Where(m => m.connection.storeId == storeId);

    private async Task<List<Dictionary<string, object?>>> QueryMappingsAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await MappingSource(storeId).Take(q.Limit).ToListAsync(ct);
        return list.Select(m => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = m.Id,
            ["name"] = m.name,
            ["connectionid"] = m.connectionId,
        })).ToList();
    }

    private IQueryable<MarketplaceOrder> OrderSource(int storeId) =>
        _db.marketplaceOrders.AsNoTracking().Where(o => o.storeId == storeId);

    private async Task<List<Dictionary<string, object?>>> QueryOrdersAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await OrderSource(storeId).Take(q.Limit).ToListAsync(ct);
        return list.Select(o => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = o.Id,
            ["marketplaceorderid"] = o.marketplaceOrderId,
            ["displayid"] = o.displayId,
            ["status"] = o.status,
            ["channel"] = o.channel,
            ["total"] = o.total,
            ["ordercreatedat"] = o.orderCreatedAt,
            ["connectionid"] = o.connectionId,
        })).ToList();
    }

    private static Dictionary<string, object?> Project(
        IReadOnlyList<string> columns, Dictionary<string, object?> row)
    {
        var projected = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            row.TryGetValue(col, out var value);
            projected[col] = value;
        }

        return projected;
    }

    private static List<int> IntValues(AgentSqlPredicate pred)
    {
        var raw = pred.Values ?? (pred.Value != null ? [pred.Value] : Array.Empty<string>());
        var ids = new List<int>();
        foreach (var s in raw)
        {
            if (int.TryParse(s, out var n))
                ids.Add(n);
        }

        return ids;
    }

    private static bool TryInt(string val, out int n) => int.TryParse(val, out n);

    private static string? Truncate(string? value, int max) =>
        value != null && value.Length > max ? value[..max] + "…" : value;
}
