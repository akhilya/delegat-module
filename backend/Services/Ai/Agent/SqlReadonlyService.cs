using System.Reflection;
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

        if (parsed.IsSchema)
            return SchemaCatalog(parsed, permissions);

        try
        {
            return await QueryCoreAsync(storeId, parsed, permissions, ct);
        }
        catch (AgentSqlFilterException ex)
        {
            return (new { error = ex.Message }, ex.Message);
        }
    }

    private async Task<(object result, string summary)> QueryCoreAsync(
        int storeId,
        AgentSqlQuery parsed,
        IReadOnlySet<string> permissions,
        CancellationToken ct)
    {
        var need = AgentSqlGuard.RequiredPermission(parsed.Table);
        if (need != null && !AgentAccess.CanRead(permissions, need))
        {
            var msg = PermissionError(parsed.Table);
            return (new { error = msg }, msg);
        }

        if (parsed.IsAggregate)
        {
            var row = await AggregateRowAsync(storeId, parsed, ct);
            return (new { table = parsed.Table, columns = parsed.Columns, rows = new[] { row } }, "1 rows");
        }

        var rows = parsed.Table switch
        {
            "products" => await QueryProductsAsync(storeId, parsed, ct),
            "files" => await QueryFilesAsync(storeId, parsed, ct),
            "product_images" => await QueryImagesAsync(storeId, parsed, ct),
            "categories" => await QueryCategoriesAsync(storeId, parsed, ct),
            "category_attributes" => await QueryCategoryAttributesAsync(storeId, parsed, ct),
            "product_attribute_values" => await QueryProductAttributeValuesAsync(storeId, parsed, ct),
            "store_attributes" => await QueryStoreAttributesAsync(storeId, parsed, ct),
            "store_product_attribute_values" => await QueryStoreProductAttributeValuesAsync(storeId, parsed, ct),
            "groups" => await QueryGroupsAsync(storeId, parsed, ct),
            "connections" => await QueryConnectionsAsync(storeId, parsed, ct),
            "mappings" => await QueryMappingsAsync(storeId, parsed, ct),
            "orders" => await QueryOrdersAsync(storeId, parsed, ct),
            "notes" => await QueryProjectedAsync(
                _db.notes.AsNoTracking().Where(n => n.product.storeId == storeId && n.product.deletedAt == null),
                parsed, ct),
            "variants" => await QueryProjectedAsync(
                _db.productVariants.AsNoTracking().Where(v => v.product.storeId == storeId && v.product.deletedAt == null),
                parsed, ct),
            "group_members" => await QueryProjectedAsync(
                _db.productGroupMembers.AsNoTracking().Where(m => m.group.storeId == storeId),
                parsed, ct),
            "product_links" => await QueryProjectedAsync(
                _db.productMarketplaceLinks.AsNoTracking()
                    .Where(l => l.product.storeId == storeId && l.product.deletedAt == null),
                parsed, ct),
            "store_files" => await QueryProjectedAsync(
                _db.storeFiles.AsNoTracking().Where(f => f.storeId == storeId), parsed, ct),
            "store_folders" => await QueryProjectedAsync(
                _db.storeFolders.AsNoTracking().Where(f => f.storeId == storeId), parsed, ct),
            "operations" => await QueryProjectedAsync(
                _db.operations.AsNoTracking().Where(o => o.storeId == storeId), parsed, ct),
            "pending_imports" => await QueryProjectedAsync(
                _db.pendingProductImports.AsNoTracking().Where(p => p.storeId == storeId), parsed, ct),
            "adapter_http_logs" => await QueryProjectedAsync(
                _db.adapterHttpLogs.AsNoTracking().Where(l => l.storeId == storeId), parsed, ct),
            _ => throw new AgentSqlFilterException($"Table '{parsed.Table}' is not queryable"),
        };

        return (new { table = parsed.Table, columns = parsed.Columns, rows }, $"{rows.Count} rows");
    }

    private static bool CanReadTable(string table, IReadOnlySet<string> permissions)
    {
        var need = AgentSqlGuard.RequiredPermission(table);
        return need == null || AgentAccess.CanRead(permissions, need);
    }

    private static string PermissionError(string table) =>
        AgentSqlGuard.RequiredPermission(table) switch
        {
            Permissions.OrdersManage => "orders.manage required",
            Permissions.MarketplaceManage => "marketplace.manage required",
            Permissions.FilesManage => "files.manage required",
            Permissions.OperationsManage => "operations.manage required",
            _ => "permission required",
        };

    private static (object result, string summary) SchemaCatalog(
        AgentSqlQuery parsed, IReadOnlySet<string> permissions)
    {
        if (parsed.SchemaFilterTable is { } one)
        {
            if (!CanReadTable(one, permissions))
            {
                var need = PermissionError(one);
                return (new { error = need }, need);
            }

            var cols = AgentSqlGuard.ColumnsOf(one) ?? [];
            var rows = cols.Select(c => ProjectSchemaRow(parsed.Columns, one, c)).ToList();
            return (new { table = "schema", columns = parsed.Columns, rows }, $"{rows.Count} rows");
        }

        var tables = AgentSqlGuard.AllowlistedTables.Where(t => CanReadTable(t, permissions)).ToList();
        var wantColumns = parsed.Columns.Any(c => c.Equals("column_name", StringComparison.OrdinalIgnoreCase));
        if (!wantColumns)
        {
            var tableRows = tables.Select(t => ProjectSchemaRow(parsed.Columns, t, column: null)).ToList();
            return (new { table = "schema", columns = parsed.Columns, rows = tableRows }, $"{tableRows.Count} rows");
        }

        var flat = new List<Dictionary<string, object?>>();
        foreach (var t in tables)
        {
            foreach (var c in AgentSqlGuard.ColumnsOf(t) ?? [])
                flat.Add(ProjectSchemaRow(parsed.Columns, t, c));
        }

        return (new { table = "schema", columns = parsed.Columns, rows = flat }, $"{flat.Count} rows");
    }

    private static Dictionary<string, object?> ProjectSchemaRow(
        IReadOnlyList<string> columns, string table, string? column)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            if (col.Equals("table_name", StringComparison.OrdinalIgnoreCase))
                row[col] = table;
            else if (col.Equals("column_name", StringComparison.OrdinalIgnoreCase))
                row[col] = column;
        }

        return row;
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
                AgentSqlSelectKind.Min => await MinMaxAsync(storeId, q, item.Column!, min: true, ct),
                AgentSqlSelectKind.Max => await MinMaxAsync(storeId, q, item.Column!, min: false, ct),
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
            "product_images" => ImageSource(storeId, q).CountAsync(ct),
            "categories" => CategorySource(storeId, q).CountAsync(ct),
            "category_attributes" => CategoryAttributeSource(storeId, q).CountAsync(ct),
            "product_attribute_values" => ProductAttributeValueSource(storeId, q).CountAsync(ct),
            "store_attributes" => StoreAttributeSource(storeId).CountAsync(ct),
            "store_product_attribute_values" => StoreProductAttributeValueSource(storeId, q).CountAsync(ct),
            "groups" => GroupSource(storeId).CountAsync(ct),
            "connections" => ConnectionSource(storeId).CountAsync(ct),
            "mappings" => MappingSource(storeId).CountAsync(ct),
            "orders" => OrderSource(storeId).CountAsync(ct),
            "notes" => CountProjectedAsync(
                _db.notes.AsNoTracking().Where(n => n.product.storeId == storeId && n.product.deletedAt == null), q, ct),
            "variants" => CountProjectedAsync(
                _db.productVariants.AsNoTracking().Where(v => v.product.storeId == storeId && v.product.deletedAt == null), q, ct),
            "group_members" => CountProjectedAsync(
                _db.productGroupMembers.AsNoTracking().Where(m => m.group.storeId == storeId), q, ct),
            "product_links" => CountProjectedAsync(
                _db.productMarketplaceLinks.AsNoTracking()
                    .Where(l => l.product.storeId == storeId && l.product.deletedAt == null), q, ct),
            "store_files" => CountProjectedAsync(
                _db.storeFiles.AsNoTracking().Where(f => f.storeId == storeId), q, ct),
            "store_folders" => CountProjectedAsync(
                _db.storeFolders.AsNoTracking().Where(f => f.storeId == storeId), q, ct),
            "operations" => CountProjectedAsync(
                _db.operations.AsNoTracking().Where(o => o.storeId == storeId), q, ct),
            "pending_imports" => CountProjectedAsync(
                _db.pendingProductImports.AsNoTracking().Where(p => p.storeId == storeId), q, ct),
            "adapter_http_logs" => CountProjectedAsync(
                _db.adapterHttpLogs.AsNoTracking().Where(l => l.storeId == storeId), q, ct),
            _ => Task.FromResult(0),
        };

    private Task<int> CountDistinctAsync(int storeId, AgentSqlQuery q, string column, CancellationToken ct)
    {
        var col = column.ToLowerInvariant();
        return q.Table switch
        {
            "products" => CountDistinctProductsAsync(ProductSource(storeId, q), col, ct),
            "files" => CountDistinctFilesAsync(FileSource(storeId, q), col, ct),
            "product_images" => CountDistinctImagesAsync(ImageSource(storeId, q), col, ct),
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

    private static Task<int> CountDistinctImagesAsync(IQueryable<ImageReference> src, string col, CancellationToken ct) =>
        col switch
        {
            "id" => src.Select(i => i.Id).Distinct().CountAsync(ct),
            "productid" => src.Select(i => i.productId).Distinct().CountAsync(ct),
            "fileid" => src.Select(i => i.fileId).Distinct().CountAsync(ct),
            "index" => src.Select(i => i.index).Distinct().CountAsync(ct),
            _ => Task.FromResult(0),
        };

    private Task<int?> MinMaxAsync(int storeId, AgentSqlQuery q, string column, bool min, CancellationToken ct)
    {
        var col = column.ToLowerInvariant();
        return q.Table switch
        {
            "products" => MinMaxProductsAsync(ProductSource(storeId, q), col, min, ct),
            "files" => MinMaxFilesAsync(FileSource(storeId, q), col, min, ct),
            "product_images" => MinMaxImagesAsync(ImageSource(storeId, q), col, min, ct),
            _ => Task.FromResult<int?>(null),
        };
    }

    private static Task<int?> MinMaxProductsAsync(IQueryable<Product> src, string col, bool min, CancellationToken ct) =>
        col switch
        {
            "id" => min ? src.MinAsync(p => (int?)p.Id, ct) : src.MaxAsync(p => (int?)p.Id, ct),
            "categoryid" => min ? src.MinAsync(p => p.categoryId, ct) : src.MaxAsync(p => p.categoryId, ct),
            "storagequantity" => min ? src.MinAsync(p => (int?)p.storageQuantity, ct) : src.MaxAsync(p => (int?)p.storageQuantity, ct),
            _ => Task.FromResult<int?>(null),
        };

    private static Task<int?> MinMaxFilesAsync(IQueryable<Delegat.Models.File> src, string col, bool min, CancellationToken ct) =>
        col switch
        {
            "id" => min ? src.MinAsync(f => (int?)f.Id, ct) : src.MaxAsync(f => (int?)f.Id, ct),
            "productid" => min ? src.MinAsync(f => (int?)f.productId, ct) : src.MaxAsync(f => (int?)f.productId, ct),
            _ => Task.FromResult<int?>(null),
        };

    private static Task<int?> MinMaxImagesAsync(IQueryable<ImageReference> src, string col, bool min, CancellationToken ct) =>
        col switch
        {
            "id" => min ? src.MinAsync(i => (int?)i.Id, ct) : src.MaxAsync(i => (int?)i.Id, ct),
            "productid" => min ? src.MinAsync(i => (int?)i.productId, ct) : src.MaxAsync(i => (int?)i.productId, ct),
            "fileid" => min ? src.MinAsync(i => (int?)i.fileId, ct) : src.MaxAsync(i => (int?)i.fileId, ct),
            "index" => min ? src.MinAsync(i => (int?)i.index, ct) : src.MaxAsync(i => (int?)i.index, ct),
            _ => Task.FromResult<int?>(null),
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
        return ApplyProductWhere(src, q, storeId);
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
        var list = await Page(src, q).ToListAsync(ct);
        var rows = list.Select(p => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = p.Id,
            ["sku"] = p.sku,
            ["barcode"] = p.barcode,
            ["name"] = p.name,
            ["price"] = p.price,
            ["oldprice"] = p.oldPrice,
            ["minprice"] = p.minPrice,
            ["netprice"] = p.netPrice,
            ["discountedprice"] = p.discountedPrice,
            ["discount"] = p.discount,
            ["currency"] = p.currency,
            ["storagequantity"] = p.storageQuantity,
            ["categoryid"] = p.categoryId,
            ["description"] = p.description,
            ["preferredexportallmode"] = p.preferredExportAllMode,
            ["createdat"] = p.createdAt,
            ["updatedat"] = p.updatedAt,
            ["deletedat"] = p.deletedAt,
        })).ToList();
        return q.IsDistinct ? DistinctRows(rows, q) : rows;
    }

    /// <summary>
    /// Applies a sql_readonly WHERE clause to a product query. Store scope stays on <paramref name="src"/>.
    /// </summary>
    public IQueryable<Product> ApplyProductWhereClause(IQueryable<Product> src, int storeId, string where)
    {
        var expanded = CatalogWhereTemplates.Expand(where);
        var parsed = AgentSqlGuard.TryParseWhere("products", expanded);
        if (parsed.Error != null)
            throw new AgentSqlFilterException(parsed.Error);
        return ApplyProductWhere(src, new AgentSqlQuery { Table = "products", Where = parsed.Predicates }, storeId);
    }

    private IQueryable<Product> ApplyProductWhere(IQueryable<Product> src, AgentSqlQuery q, int storeId)
    {
        if (q.Where == null) return src;
        foreach (var pred in q.Where)
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            IQueryable<Product>? next = (col, pred.Op) switch
            {
                ("sku", AgentSqlOp.Like) => src.Where(p => p.sku.Contains(val.Trim('%'))),
                ("sku", AgentSqlOp.NotLike) => src.Where(p => !p.sku.Contains(val.Trim('%'))),
                ("sku", AgentSqlOp.Eq) => src.Where(p => p.sku == val),
                ("name", AgentSqlOp.Like) => src.Where(p => p.name.Contains(val.Trim('%'))),
                ("name", AgentSqlOp.NotLike) => src.Where(p => !p.name.Contains(val.Trim('%'))),
                ("name", AgentSqlOp.Eq) => src.Where(p => p.name == val),
                ("description", AgentSqlOp.Like) => src.Where(p => p.description.Contains(val.Trim('%'))),
                ("barcode", AgentSqlOp.Like) => src.Where(p => p.barcode != null && p.barcode.Contains(val.Trim('%'))),
                ("barcode", AgentSqlOp.Eq) => src.Where(p => p.barcode == val),
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(p => p.Id == id),
                ("id", AgentSqlOp.Neq) when TryInt(val, out var idNeq) => src.Where(p => p.Id != idNeq),
                ("id", AgentSqlOp.Lt) when TryInt(val, out var idLt) => src.Where(p => p.Id < idLt),
                ("id", AgentSqlOp.Gt) when TryInt(val, out var idGt) => src.Where(p => p.Id > idGt),
                ("id", AgentSqlOp.Lte) when TryInt(val, out var idLte) => src.Where(p => p.Id <= idLte),
                ("id", AgentSqlOp.Gte) when TryInt(val, out var idGte) => src.Where(p => p.Id >= idGte),
                ("id", AgentSqlOp.In) when ids.Count > 0 => src.Where(p => ids.Contains(p.Id)),
                ("id", AgentSqlOp.NotIn) when ids.Count > 0 => src.Where(p => !ids.Contains(p.Id)),
                ("id", AgentSqlOp.InSelect) => src.Where(p => SubqueryProductIds(storeId, pred).Contains(p.Id)),
                ("id", AgentSqlOp.NotInSelect) => src.Where(p => !SubqueryProductIds(storeId, pred).Contains(p.Id)),
                ("barcode", AgentSqlOp.IsNull) => src.Where(p => p.barcode == null),
                ("barcode", AgentSqlOp.IsNotNull) => src.Where(p => p.barcode != null),
                ("categoryid", AgentSqlOp.IsNull) => src.Where(p => p.categoryId == null),
                ("categoryid", AgentSqlOp.IsNotNull) => src.Where(p => p.categoryId != null),
                ("categoryid", AgentSqlOp.Eq) when TryInt(val, out var cid) => src.Where(p => p.categoryId == cid),
                ("categoryid", AgentSqlOp.In) when ids.Count > 0 =>
                    src.Where(p => p.categoryId != null && ids.Contains(p.categoryId.Value)),
                ("price", AgentSqlOp.Eq) when decimal.TryParse(val, out var price) => src.Where(p => p.price == price),
                ("price", AgentSqlOp.Gt) when decimal.TryParse(val, out var priceGt) => src.Where(p => p.price > priceGt),
                ("price", AgentSqlOp.Lt) when decimal.TryParse(val, out var priceLt) => src.Where(p => p.price < priceLt),
                ("price", AgentSqlOp.Gte) when decimal.TryParse(val, out var priceGte) => src.Where(p => p.price >= priceGte),
                ("price", AgentSqlOp.Lte) when decimal.TryParse(val, out var priceLte) => src.Where(p => p.price <= priceLte),
                ("price", AgentSqlOp.IsNull) => src.Where(p => p.price == null),
                ("price", AgentSqlOp.IsNotNull) => src.Where(p => p.price != null),
                ("storagequantity", AgentSqlOp.Eq) when TryInt(val, out var qty) => src.Where(p => p.storageQuantity == qty),
                ("storagequantity", AgentSqlOp.Gt) when TryInt(val, out var qtyGt) => src.Where(p => p.storageQuantity > qtyGt),
                ("storagequantity", AgentSqlOp.Lt) when TryInt(val, out var qtyLt) => src.Where(p => p.storageQuantity < qtyLt),
                ("storagequantity", AgentSqlOp.Gte) when TryInt(val, out var qtyGte) => src.Where(p => p.storageQuantity >= qtyGte),
                ("storagequantity", AgentSqlOp.Lte) when TryInt(val, out var qtyLte) => src.Where(p => p.storageQuantity <= qtyLte),
                _ => null,
            };
            if (next == null)
                throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported on products");
            src = next;
        }

        return src;
    }

    private IQueryable<int> SubqueryProductIds(int storeId, AgentSqlPredicate pred)
    {
        var sub = new AgentSqlQuery { Table = pred.SubTable ?? "", Where = pred.SubWhere };
        return (pred.SubTable ?? "") switch
        {
            "product_images" => ImageSource(storeId, sub).Select(i => i.productId),
            "files" => FileSource(storeId, sub).Select(f => f.productId),
            "product_attribute_values" => ProductAttributeValueSource(storeId, sub).Select(v => v.productId),
            "store_product_attribute_values" => StoreProductAttributeValueSource(storeId, sub).Select(v => v.productId),
            _ => _db.products.Where(_ => false).Select(p => p.Id),
        };
    }

    private IQueryable<Delegat.Models.File> FileSource(int storeId, AgentSqlQuery q)
    {
        var src = _db.files.AsNoTracking().Where(f => f.product.storeId == storeId && f.product.deletedAt == null);
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("productid", AgentSqlOp.Eq) when TryInt(val, out var pid) => src.Where(f => f.productId == pid),
                ("productid", AgentSqlOp.Neq) when TryInt(val, out var pidNeq) => src.Where(f => f.productId != pidNeq),
                ("productid", AgentSqlOp.Lt) when TryInt(val, out var pidLt) => src.Where(f => f.productId < pidLt),
                ("productid", AgentSqlOp.Gt) when TryInt(val, out var pidGt) => src.Where(f => f.productId > pidGt),
                ("productid", AgentSqlOp.Lte) when TryInt(val, out var pidLte) => src.Where(f => f.productId <= pidLte),
                ("productid", AgentSqlOp.Gte) when TryInt(val, out var pidGte) => src.Where(f => f.productId >= pidGte),
                ("productid", AgentSqlOp.In) when ids.Count > 0 => src.Where(f => ids.Contains(f.productId)),
                ("productid", AgentSqlOp.NotIn) when ids.Count > 0 => src.Where(f => !ids.Contains(f.productId)),
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(f => f.Id == id),
                ("id", AgentSqlOp.In) when ids.Count > 0 => src.Where(f => ids.Contains(f.Id)),
                ("contenttype", AgentSqlOp.Eq) => src.Where(f => f.contentType == val),
                ("contenttype", AgentSqlOp.Neq) => src.Where(f => f.contentType != val),
                ("contenttype", AgentSqlOp.IsNull) => src.Where(f => f.contentType == null),
                ("contenttype", AgentSqlOp.IsNotNull) => src.Where(f => f.contentType != null),
                ("contenttype", AgentSqlOp.Like) => src.Where(f => f.contentType != null && f.contentType.Contains(val.Trim('%'))),
                ("contenttype", AgentSqlOp.NotLike) => src.Where(f => f.contentType == null || !f.contentType.Contains(val.Trim('%'))),
                ("displayname", AgentSqlOp.Like) => src.Where(f => f.displayName != null && f.displayName.Contains(val.Trim('%'))),
                _ => throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported on files"),
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryFilesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var src = FileSource(storeId, q);
        if (q.IsDistinct)
            return await DistinctFileColumnAsync(src, q, ct);

        var list = await Page(src.OrderBy(f => f.Id), q).ToListAsync(ct);
        return list.Select(f => Project(q.Columns, FileRow(f))).ToList();
    }

    private static Dictionary<string, object?> FileRow(Delegat.Models.File f) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = f.Id,
            ["productid"] = f.productId,
            ["displayname"] = f.displayName,
            ["filepath"] = f.filePath,
            ["contenttype"] = f.contentType,
            ["length"] = f.length,
            ["folderid"] = f.folderId,
            ["ispublic"] = f.isPublic,
            ["sourceurl"] = f.sourceUrl,
            ["uploaddatetime"] = f.uploadDateTime,
        };

    private async Task<List<Dictionary<string, object?>>> DistinctFileColumnAsync(
        IQueryable<Delegat.Models.File> src, AgentSqlQuery q, CancellationToken ct)
    {
        var col = q.Select[0].Column!.ToLowerInvariant();
        var name = q.Select[0].OutputName;
        List<object?> values = col switch
        {
            "contenttype" => (await Page(src.Select(f => f.contentType).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "productid" => (await Page(src.Select(f => f.productId).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "displayname" => (await Page(src.Select(f => f.displayName).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "filepath" => (await Page(src.Select(f => f.filePath).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "id" => (await Page(src.Select(f => f.Id).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "length" => (await Page(src.Select(f => f.length).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "folderid" => (await Page(src.Select(f => f.folderId).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "ispublic" => (await Page(src.Select(f => f.isPublic).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            "sourceurl" => (await Page(src.Select(f => f.sourceUrl).Distinct(), q).ToListAsync(ct))
                .Select(v => (object?)v).ToList(),
            _ => throw new AgentSqlFilterException($"SELECT DISTINCT {col} is not supported on files"),
        };
        return values.Select(v => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = v,
        }).ToList();
    }

    private IQueryable<ImageReference> ImageSource(int storeId, AgentSqlQuery q)
    {
        var src = _db.images.AsNoTracking().Where(i => i.product.storeId == storeId && i.product.deletedAt == null);
        foreach (var pred in q.Where ?? [])
        {
            var col = pred.Column.ToLowerInvariant();
            var val = pred.Value ?? "";
            var ids = IntValues(pred);
            src = (col, pred.Op) switch
            {
                ("productid", AgentSqlOp.Eq) when TryInt(val, out var pid) => src.Where(i => i.productId == pid),
                ("productid", AgentSqlOp.In) when ids.Count > 0 => src.Where(i => ids.Contains(i.productId)),
                ("id", AgentSqlOp.Eq) when TryInt(val, out var id) => src.Where(i => i.Id == id),
                ("fileid", AgentSqlOp.Eq) when TryInt(val, out var fid) => src.Where(i => i.fileId == fid),
                _ => throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported on product_images"),
            };
        }

        return src;
    }

    private async Task<List<Dictionary<string, object?>>> QueryImagesAsync(
        int storeId, AgentSqlQuery q, CancellationToken ct)
    {
        var list = await Page(ImageSource(storeId, q).OrderBy(i => i.index).ThenBy(i => i.Id), q).ToListAsync(ct);
        if (q.IsDistinct)
            return DistinctRows(list.Select(i => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = i.Id,
                ["productid"] = i.productId,
                ["fileid"] = i.fileId,
                ["index"] = i.index,
            }).ToList(), q);
        return list.Select(i => Project(q.Columns, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = i.Id,
            ["productid"] = i.productId,
            ["fileid"] = i.fileId,
            ["index"] = i.index,
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
                ("value", AgentSqlOp.Eq) => src.Where(v => v.value == val),
                ("value", AgentSqlOp.Like) => src.Where(v => v.value != null && v.value.Contains(val.Trim('%'))),
                _ => throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported on product_attribute_values"),
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
                ("value", AgentSqlOp.Eq) => src.Where(v => v.value == val),
                ("value", AgentSqlOp.Like) => src.Where(v => v.value != null && v.value.Contains(val.Trim('%'))),
                _ => throw new AgentSqlFilterException($"WHERE {pred.Column} is not supported on store_product_attribute_values"),
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

    private static Task<int> CountProjectedAsync<T>(IQueryable<T> scoped, AgentSqlQuery q, CancellationToken ct) =>
        AgentSqlWhere.Apply(scoped, q.Where).CountAsync(ct);

    private async Task<List<Dictionary<string, object?>>> QueryProjectedAsync<T>(
        IQueryable<T> scoped, AgentSqlQuery q, CancellationToken ct)
    {
        var src = AgentSqlWhere.Apply(scoped, q.Where);
        src = AgentSqlWhere.Order(src, q);
        if (string.IsNullOrEmpty(q.OrderByColumn))
        {
            var id = typeof(T).GetProperty("Id");
            if (id != null && id.PropertyType == typeof(int))
                src = src.OrderBy(e => EF.Property<int>(e, "Id"));
        }

        var list = await Page(src, q).ToListAsync(ct);
        var rows = list.Select(e => ProjectObject(e!, q.Columns)).ToList();
        return q.IsDistinct ? DistinctRows(rows, q) : rows;
    }

    private static Dictionary<string, object?> ProjectObject(object entity, IReadOnlyList<string> columns)
    {
        var type = entity.GetType();
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            var prop = col.Equals("id", StringComparison.OrdinalIgnoreCase)
                ? type.GetProperty("Id")
                : type.GetProperty(col, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var val = prop?.GetValue(entity);
            if (val is Enum en)
                val = en.ToString();
            map[col] = val;
        }

        return Project(columns, map);
    }

    private static IQueryable<T> Page<T>(IQueryable<T> src, AgentSqlQuery q) =>
        q.Offset > 0 ? src.Skip(q.Offset).Take(q.Limit) : src.Take(q.Limit);

    private static List<Dictionary<string, object?>> DistinctRows(
        List<Dictionary<string, object?>> rows, AgentSqlQuery q)
    {
        var col = q.Select[0].Column ?? q.Columns[0];
        return rows
            .GroupBy(r => r.TryGetValue(col, out var v) ? v : null)
            .Select(g => Project(q.Columns, g.First()))
            .ToList();
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
