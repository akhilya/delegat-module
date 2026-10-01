using System.Text.RegularExpressions;
using Delegat.Infrastructure.Auth;

namespace Delegat.Services.Ai.Agent;

public enum AgentSqlOp
{
    Eq,
    Neq,
    Like,
    NotLike,
    Lt,
    Gt,
    Lte,
    Gte,
    In,
    NotIn,
    InSelect,
    NotInSelect,
    IsNull,
    IsNotNull,
}

public sealed class AgentSqlPredicate
{
    public required string Column { get; init; }
    public required AgentSqlOp Op { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<string>? Values { get; init; }
    public string? SubTable { get; init; }
    public string? SubColumn { get; init; }
    public IReadOnlyList<AgentSqlPredicate>? SubWhere { get; init; }
}

public sealed class AgentSqlSelectItem
{
    public required AgentSqlSelectKind Kind { get; init; }
    public required string OutputName { get; init; }
    public string? Column { get; init; }
}

public enum AgentSqlSelectKind
{
    Column,
    DistinctColumn,
    CountStar,
    CountDistinct,
    Min,
    Max,
}

public sealed class AgentSqlQuery
{
    public string? Error { get; init; }
    public string Table { get; init; } = "";
    public bool IsSchema { get; init; }
    /// <summary>When set, schema rows are columns of this allowlisted table.</summary>
    public string? SchemaFilterTable { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<AgentSqlSelectItem> Select { get; init; } = [];
    public IReadOnlyList<AgentSqlPredicate>? Where { get; init; }
    public string? OrderByColumn { get; init; }
    public bool OrderDescending { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }
    public bool IsAggregate => Select.Count > 0 && Select.All(s =>
        s.Kind is AgentSqlSelectKind.CountStar or AgentSqlSelectKind.CountDistinct
            or AgentSqlSelectKind.Min or AgentSqlSelectKind.Max);
    public bool IsDistinct => Select.Count > 0 && Select.All(s => s.Kind == AgentSqlSelectKind.DistinctColumn);
}

public static class AgentSqlGuard
{
    public const int MaxLimit = 200;

    private static readonly HashSet<string> ProductIdSubqueryTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "files",
        "product_images",
        "product_attribute_values",
        "store_product_attribute_values",
    };
    public const int MaxInValues = 200;

    public const string SchemaDescription = """
        Allowlisted store tables (SHOW COLUMNS / DESCRIBE for fields). storeId is forced.
        products, files, product_images, categories, category_attributes, product_attribute_values,
        store_attributes, store_product_attribute_values, groups, group_members, notes, variants,
        product_links, connections, mappings, orders, store_files, store_folders, operations,
        pending_imports, adapter_http_logs.
        Never: credentials, tokens, payloadJson, graphJson, HTTP bodies/headers, users, API keys.
        WHERE: = != <> < > <= >= LIKE NOT LIKE IN NOT IN IS NULL IS NOT NULL. AND only. No OR. No JOIN.
        IN or NOT IN (SELECT productId FROM product_images|files|product_attribute_values|store_product_attribute_values). SELECT DISTINCT col. LIMIT/OFFSET.
        Aggregates: COUNT(*), COUNT(DISTINCT col), MIN(col), MAX(col) — not mixed with row columns.
        SHOW TABLES / SHOW COLUMNS FROM t / SELECT table_name, column_name FROM schema.
        Not information_schema. Not SELECT *.
        """;

    private static readonly Dictionary<string, string[]> Tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["products"] = ["id", "sku", "barcode", "name", "price", "oldPrice", "minPrice", "netPrice", "discountedPrice", "discount", "currency", "storageQuantity", "categoryId", "description", "preferredExportAllMode", "createdAt", "updatedAt", "deletedAt"],
        ["files"] = ["id", "productId", "displayName", "filePath", "contentType", "length", "folderId", "isPublic", "sourceUrl", "uploadDateTime"],
        ["product_images"] = ["id", "productId", "fileId", "index"],
        ["categories"] = ["id", "name", "description", "hasSizes", "createdAt"],
        ["category_attributes"] = ["id", "categoryId", "name", "key", "type", "isRequired", "isUnique", "sortOrder", "group", "defaultValue", "description", "allowCustomValues"],
        ["product_attribute_values"] = ["id", "productId", "attributeId", "value"],
        ["store_attributes"] = ["id", "name", "key", "type", "isRequired", "sortOrder", "description"],
        ["store_product_attribute_values"] = ["id", "productId", "storeAttributeId", "value"],
        ["groups"] = ["id", "name", "description", "createdAt", "updatedAt"],
        ["group_members"] = ["id", "groupId", "productId", "sortOrder"],
        ["notes"] = ["id", "productId", "text", "importance", "createdAt", "updatedAt"],
        ["variants"] = ["id", "productId", "sku", "barcode", "size", "price", "quantity", "sortOrder", "createdAt", "updatedAt"],
        ["product_links"] = ["id", "productId", "connectionId", "externalProductId", "externalSku", "status", "lastErrorMessage", "lastSyncAt", "externalPrice", "externalStock", "categoryMappingId", "preferredExportMode", "createdAt"],
        ["connections"] = ["id", "name", "adapterKey", "isActive", "lastSyncAt", "createdAt"],
        ["mappings"] = ["id", "name", "connectionId", "localCategoryId", "externalCategoryId", "externalCategoryName", "createdAt"],
        ["orders"] = ["id", "marketplaceOrderId", "displayId", "status", "channel", "currency", "total", "orderCreatedAt", "orderUpdatedAt", "syncedAt", "connectionId"],
        ["store_files"] = ["id", "displayName", "filePath", "contentType", "length", "folderId", "isPublic", "uploadDateTime"],
        ["store_folders"] = ["id", "name", "parentFolderId", "createdAt"],
        ["operations"] = ["id", "operationId", "type", "title", "status", "connectionId", "connectionName", "batchType", "totalItems", "completedItems", "failedItems", "summary", "createdAt", "finishedAt"],
        ["pending_imports"] = ["id", "connectionId", "externalProductId", "targetCategoryId", "externalCategoryId", "status", "importedProductId", "lastError", "retryCount", "productName", "batchId", "createdAt", "completedAt"],
        ["adapter_http_logs"] = ["id", "createdAt", "connectionId", "adapterKey", "method", "path", "statusCode", "durationMs", "operationId"],
    };

    public static string? RequiredPermission(string table) => CanonicalTable(table) switch
    {
        "orders" => Permissions.OrdersManage,
        "connections" or "mappings" or "product_links" or "adapter_http_logs" => Permissions.MarketplaceManage,
        "files" or "product_images" or "store_files" or "store_folders" => Permissions.FilesManage,
        "operations" or "pending_imports" => Permissions.OperationsManage,
        _ => null,
    };

    public static IReadOnlyList<string> AllowlistedTables =>
        Tables.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<string>? ColumnsOf(string table) =>
        Tables.TryGetValue(CanonicalTable(table), out var cols) ? cols : null;

    private static HashSet<string> AllowedColumnSet(string[] names) =>
        new(names.Select(n => n.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ColumnHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["categories.parent_id"] = "categories are a flat list — there is no parent_id. Select id, name, description, hasSizes.",
        ["categories.parentid"] = "categories are a flat list — there is no parent_id. Select id, name, description, hasSizes.",
        ["products.quantity"] = "Use storageQuantity, not quantity.",
        ["product_attribute_values.categoryattributeid"] = "Use attributeId, not categoryAttributeId.",
        ["product_attribute_values.category_attribute_id"] = "Use attributeId, not categoryAttributeId.",
        ["connections.credentialsjson"] = "Marketplace credentials are not readable via SQL.",
        ["connections.settingsjson"] = "Connection settings JSON is not readable via SQL.",
        ["orders.payloadjson"] = "Order payload JSON is not readable via SQL.",
        ["files.publictoken"] = "Public file tokens are not readable via SQL.",
        ["store_files.publictoken"] = "Public file tokens are not readable via SQL.",
        ["mappings.graphjson"] = "Mapping graph JSON is not readable via SQL.",
        ["adapter_http_logs.requestbody"] = "HTTP bodies and headers are not readable via SQL.",
        ["adapter_http_logs.responsebody"] = "HTTP bodies and headers are not readable via SQL.",
        ["adapter_http_logs.requestheadersjson"] = "HTTP bodies and headers are not readable via SQL.",
        ["adapter_http_logs.responseheadersjson"] = "HTTP bodies and headers are not readable via SQL.",
        ["adapter_http_logs.url"] = "Use path, not url (query strings can carry secrets).",
    };

    private static readonly Regex ForbiddenRx = new(
        @"\b(insert|update|delete|drop|alter|create|grant|truncate|outfile|load_file|information_schema|performance_schema|sqlite_master|pg_catalog|union|having)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SelectRx = new(
        @"^\s*SELECT\s+(?<cols>.+?)\s+FROM\s+(?<table>[a-zA-Z_][\w]*)\s*(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static AgentSqlQuery Parse(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return Fail("SQL is required");

        var trimmed = sql.Trim().TrimEnd(';');
        if (trimmed.Contains(';'))
            return Fail("Multiple statements are not allowed");

        var lower = trimmed.ToLowerInvariant();
        if (Regex.IsMatch(trimmed, @"\bjoin\b", RegexOptions.IgnoreCase))
            return Fail("JOIN is not allowed; query one table");

        if (ForbiddenRx.IsMatch(trimmed))
            return Fail("Statement contains a forbidden keyword");

        var schemaQuery = TryParseSchema(trimmed);
        if (schemaQuery != null)
            return schemaQuery;

        if (!lower.StartsWith("select", StringComparison.Ordinal))
            return Fail("Only SELECT, SHOW TABLES, SHOW COLUMNS, or DESCRIBE is allowed");

        var m = SelectRx.Match(trimmed);
        if (!m.Success)
            return Fail("Expected SELECT columns FROM table");

        var tableRaw = m.Groups["table"].Value;
        var table = CanonicalTable(tableRaw);
        if (!Tables.TryGetValue(table, out var tableCols))
            return Fail($"Table '{tableRaw}' is not allowlisted. SHOW TABLES lists allowed tables. Secrets, users, and payload blobs are never readable.");
        var allowedCols = AllowedColumnSet(tableCols);
        var colPart = m.Groups["cols"].Value.Trim();
        var distinctPrefix = Regex.Match(colPart, @"^DISTINCT\s+", RegexOptions.IgnoreCase);
        var distinct = distinctPrefix.Success;
        if (distinct)
            colPart = colPart[distinctPrefix.Length..].Trim();
        if (colPart == "*")
            return Fail("SELECT * is not allowed; name columns");

        var columns = colPart.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length == 0)
            return Fail("At least one column is required");

        var selectItems = new List<AgentSqlSelectItem>();
        var normalizedCols = new List<string>();
        foreach (var col in columns)
        {
            if (TryParseAggregate(col, allowedCols, table, out var agg, out var aggErr))
            {
                if (aggErr != null)
                    return Fail(aggErr);
                selectItems.Add(agg!);
                normalizedCols.Add(agg!.OutputName);
                continue;
            }

            if (!Regex.IsMatch(col, @"^[a-zA-Z_][\w]*$"))
                return Fail($"Invalid column '{col}'");
            if (!allowedCols.Contains(col.ToLowerInvariant()))
                return Fail(ColumnError(table, col));
            selectItems.Add(new AgentSqlSelectItem
            {
                Kind = AgentSqlSelectKind.Column,
                OutputName = col,
                Column = col,
            });
            normalizedCols.Add(col);
        }

        if (distinct)
        {
            if (selectItems.Any(i => i.Kind != AgentSqlSelectKind.Column))
                return Fail("DISTINCT cannot be mixed with aggregates");
            if (selectItems.Count != 1)
                return Fail("DISTINCT supports one column");
            selectItems[0] = new AgentSqlSelectItem
            {
                Kind = AgentSqlSelectKind.DistinctColumn,
                OutputName = selectItems[0].OutputName,
                Column = selectItems[0].Column,
            };
        }

        var aggregateKinds = selectItems.Any(i => i.Kind is AgentSqlSelectKind.CountStar
            or AgentSqlSelectKind.CountDistinct or AgentSqlSelectKind.Min or AgentSqlSelectKind.Max);
        var rowKinds = selectItems.Any(i => i.Kind is AgentSqlSelectKind.Column or AgentSqlSelectKind.DistinctColumn);
        if (aggregateKinds && rowKinds)
            return Fail("COUNT cannot be mixed with row columns");
        if (normalizedCols.Count != normalizedCols.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return Fail("Duplicate column aliases");

        var rest = m.Groups["rest"].Value.Trim();
        List<AgentSqlPredicate>? where = null;
        string? orderCol = null;
        var orderDesc = false;
        var limit = 50;
        var offset = 0;

        if (rest.Length > 0)
        {
            var orderIdx = IndexOfKeyword(rest, "ORDER BY");
            var limitIdx = IndexOfKeyword(rest, "LIMIT");
            var offsetIdx = IndexOfKeyword(rest, "OFFSET");
            var whereIdx = IndexOfKeyword(rest, "WHERE");

            if (whereIdx >= 0)
            {
                var whereEnd = NextClause(orderIdx, NextClause(limitIdx, offsetIdx, rest.Length), rest.Length);
                if (whereEnd <= whereIdx)
                    whereEnd = rest.Length;
                var whereSql = rest[(whereIdx + 5)..whereEnd].Trim();
                var pred = ParseWhere(whereSql, allowedCols, table);
                if (pred.Error != null)
                    return Fail(pred.Error);
                where = pred.Predicates;
            }

            if (orderIdx >= 0)
            {
                var orderEnd = NextClause(limitIdx, offsetIdx, rest.Length);
                var orderSql = rest[(orderIdx + 8)..orderEnd].Trim();
                var parts = orderSql.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0 || !allowedCols.Contains(parts[0].ToLowerInvariant()))
                    return Fail("ORDER BY column is not allowlisted");
                orderCol = parts[0];
                if (parts.Length > 1 && parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase))
                    orderDesc = true;
            }

            if (limitIdx >= 0)
            {
                var limitSql = rest[(limitIdx + 5)..].Trim();
                var innerOffset = IndexOfKeyword(limitSql, "OFFSET");
                var limitTok = innerOffset >= 0 ? limitSql[..innerOffset].Trim() : limitSql.Split(' ', 2)[0];
                if (!int.TryParse(limitTok, out limit) || limit < 1)
                    return Fail("LIMIT must be a positive integer");
            }

            if (offsetIdx >= 0)
            {
                var offsetSql = rest[(offsetIdx + 6)..].Trim();
                var offsetTok = offsetSql.Split(' ', 2)[0];
                if (!int.TryParse(offsetTok, out offset) || offset < 0)
                    return Fail("OFFSET must be a non-negative integer");
            }
        }

        if (limit > MaxLimit)
            limit = MaxLimit;

        return new AgentSqlQuery
        {
            Table = table,
            Columns = normalizedCols,
            Select = selectItems,
            Where = where,
            OrderByColumn = orderCol,
            OrderDescending = orderDesc,
            Limit = limit,
            Offset = offset,
        };
    }

    private static readonly Regex ShowTablesRx = new(
        @"^\s*SHOW\s+TABLES\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShowColumnsRx = new(
        @"^\s*(?:SHOW\s+COLUMNS\s+FROM|DESC(?:RIBE)?)\s+([a-zA-Z_][\w]*)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SchemaSelectRx = new(
        @"^\s*SELECT\s+(?<cols>.+?)\s+FROM\s+schema(?:\s+WHERE\s+table_name\s*=\s*(?:'(?<q1>[^']*)'|""(?<q2>[^""]*)""|(?<id>[a-zA-Z_][\w]*)))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static AgentSqlQuery? TryParseSchema(string sql)
    {
        if (ShowTablesRx.IsMatch(sql))
            return SchemaResult(["table_name"], filterTable: null);

        var showCols = ShowColumnsRx.Match(sql);
        if (showCols.Success)
        {
            var tableRaw = showCols.Groups[1].Value;
            var table = CanonicalTable(tableRaw);
            if (!Tables.ContainsKey(table))
                return Fail($"Table '{tableRaw}' is not allowlisted. SHOW TABLES lists allowed tables.");
            return SchemaResult(["column_name"], table);
        }

        var sel = SchemaSelectRx.Match(sql);
        if (!sel.Success)
            return null;

        var colPart = sel.Groups["cols"].Value.Trim();
        if (colPart == "*")
            return Fail("SELECT * is not allowed; name columns");

        var wanted = colPart.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (wanted.Length == 0)
            return Fail("At least one column is required");

        var columns = new List<string>();
        foreach (var col in wanted)
        {
            if (col.Equals("table_name", StringComparison.OrdinalIgnoreCase))
                columns.Add("table_name");
            else if (col.Equals("column_name", StringComparison.OrdinalIgnoreCase))
                columns.Add("column_name");
            else
                return Fail($"Invalid column '{col}' on schema (table_name, column_name)");
        }

        if (columns.Count != columns.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return Fail("Duplicate column aliases");

        string? filter = null;
        if (sel.Groups["q1"].Success)
            filter = sel.Groups["q1"].Value;
        else if (sel.Groups["q2"].Success)
            filter = sel.Groups["q2"].Value;
        else if (sel.Groups["id"].Success)
            filter = sel.Groups["id"].Value;

        if (filter != null)
        {
            var table = CanonicalTable(filter);
            if (!Tables.ContainsKey(table))
                return Fail($"Table '{filter}' is not allowlisted. SHOW TABLES lists allowed tables.");
            filter = table;
        }

        return SchemaResult(columns, filter);
    }

    private static AgentSqlQuery SchemaResult(IReadOnlyList<string> columns, string? filterTable) =>
        new()
        {
            IsSchema = true,
            Table = "schema",
            SchemaFilterTable = filterTable,
            Columns = columns,
            Select = columns.Select(c => new AgentSqlSelectItem
            {
                Kind = AgentSqlSelectKind.Column,
                OutputName = c,
                Column = c,
            }).ToArray(),
        };

    private static string CanonicalTable(string table)
    {
        var key = table.ToLowerInvariant();
        return key switch
        {
            "productimages" or "product_image" or "images" => "product_images",
            "categoryattributes" or "category_attribute" => "category_attributes",
            "productattributes" or "product_attributes" or "productattributevalues" => "product_attribute_values",
            "storeattributes" => "store_attributes",
            "storeproductattributevalues" or "store_product_attributes" => "store_product_attribute_values",
            "product_variants" or "productvariants" or "sizes" => "variants",
            "product_group_members" or "groupmembers" => "group_members",
            "product_marketplace_links" or "links" => "product_links",
            "httplogs" or "adapter_logs" or "adapterhttplogs" => "adapter_http_logs",
            "pendingimports" or "product_imports" => "pending_imports",
            _ => key,
        };
    }

    private static string ColumnError(string table, string col)
    {
        if (ColumnHints.TryGetValue($"{table}.{col}", out var hint))
            return hint;
        return $"Column '{col}' is not allowlisted on {table}";
    }

    private static (string? Error, List<AgentSqlPredicate>? Predicates) ParseWhere(
        string whereSql, HashSet<string> allowedCols, string table)
    {
        // Single AND-chain of col OP value (do not split AND inside a subquery)
        if (Regex.IsMatch(whereSql, @"\bOR\b", RegexOptions.IgnoreCase))
            return ("OR is not allowed; use AND or two queries", null);
        var parts = SplitAndOutsideParens(whereSql);
        var list = new List<AgentSqlPredicate>();
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            var isNull = Regex.Match(trimmedPart,
                @"^([a-zA-Z_][\w]*)\s+IS\s+(NOT\s+)?NULL$",
                RegexOptions.IgnoreCase);
            if (isNull.Success)
            {
                var nullCol = isNull.Groups[1].Value;
                if (!allowedCols.Contains(nullCol.ToLowerInvariant()))
                    return (ColumnError(table, nullCol), null);
                list.Add(new AgentSqlPredicate
                {
                    Column = nullCol,
                    Op = isNull.Groups[2].Success ? AgentSqlOp.IsNotNull : AgentSqlOp.IsNull,
                });
                continue;
            }

            var like = Regex.Match(trimmedPart,
                @"^([a-zA-Z_][\w]*)\s+(NOT\s+)?LIKE\s+('([^']*)'|""([^""]*)"")$",
                RegexOptions.IgnoreCase);
            if (like.Success)
            {
                var col = like.Groups[1].Value;
                if (!allowedCols.Contains(col.ToLowerInvariant()))
                    return (ColumnError(table, col), null);
                var val = like.Groups[4].Success ? like.Groups[4].Value : like.Groups[5].Value;
                var likeOp = like.Groups[2].Success ? AgentSqlOp.NotLike : AgentSqlOp.Like;
                list.Add(new AgentSqlPredicate { Column = col, Op = likeOp, Value = val });
                continue;
            }

            var inSelect = Regex.Match(trimmedPart,
                @"^([a-zA-Z_][\w]*)\s+(NOT\s+)?IN\s*\(\s*SELECT\s+([a-zA-Z_][\w]*)\s+FROM\s+([a-zA-Z_][\w]*)(?:\s+WHERE\s+(.+?))?\s*\)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (inSelect.Success)
            {
                var col = inSelect.Groups[1].Value;
                if (!allowedCols.Contains(col.ToLowerInvariant()))
                    return (ColumnError(table, col), null);
                var subCol = inSelect.Groups[3].Value;
                var subTableRaw = inSelect.Groups[4].Value;
                var subTable = CanonicalTable(subTableRaw);
                if (!Tables.TryGetValue(subTable, out var subColNames))
                    return ($"Table '{subTableRaw}' is not allowlisted", null);
                var subCols = AllowedColumnSet(subColNames);
                if (!subCols.Contains(subCol.ToLowerInvariant()))
                    return (ColumnError(subTable, subCol), null);
                if (!ProductIdSubqueryTables.Contains(subTable))
                    return ("IN subquery is only allowed against files, product_images, or attribute values", null);
                if (!subCol.Equals("productId", StringComparison.OrdinalIgnoreCase))
                    return ("IN subquery must select productId", null);
                IReadOnlyList<AgentSqlPredicate>? subWhere = null;
                if (inSelect.Groups[5].Success && inSelect.Groups[5].Value.Trim().Length > 0)
                {
                    var nested = ParseWhere(inSelect.Groups[5].Value.Trim(), subCols, subTable);
                    if (nested.Error != null)
                        return (nested.Error, null);
                    subWhere = nested.Predicates;
                }

                list.Add(new AgentSqlPredicate
                {
                    Column = col,
                    Op = inSelect.Groups[2].Success ? AgentSqlOp.NotInSelect : AgentSqlOp.InSelect,
                    SubTable = subTable,
                    SubColumn = subCol,
                    SubWhere = subWhere,
                });
                continue;
            }

            var inn = Regex.Match(trimmedPart,
                @"^([a-zA-Z_][\w]*)\s+(NOT\s+)?IN\s*\(\s*((?:'[^']*'|""[^""]*""|-?[0-9]+(?:\.[0-9]+)?)(?:\s*,\s*(?:'[^']*'|""[^""]*""|-?[0-9]+(?:\.[0-9]+)?))*)\s*\)$",
                RegexOptions.IgnoreCase);
            if (inn.Success)
            {
                var col = inn.Groups[1].Value;
                if (!allowedCols.Contains(col.ToLowerInvariant()))
                    return (ColumnError(table, col), null);
                var values = ParseInValues(inn.Groups[3].Value);
                if (values.Count == 0)
                    return ("IN list is empty", null);
                if (values.Count > MaxInValues)
                    return ($"IN list is limited to {MaxInValues} values", null);
                var inOp = inn.Groups[2].Success ? AgentSqlOp.NotIn : AgentSqlOp.In;
                list.Add(new AgentSqlPredicate { Column = col, Op = inOp, Values = values });
                continue;
            }

            var cmp = Regex.Match(trimmedPart,
                @"^([a-zA-Z_][\w]*)\s*(=|!=|<>|<=|>=|<|>)\s*('([^']*)'|""([^""]*)""|(-?[0-9]+(?:\.[0-9]+)?))$");
            if (!cmp.Success)
                return ("Unsupported WHERE clause", null);
            var column = cmp.Groups[1].Value;
            if (!allowedCols.Contains(column.ToLowerInvariant()))
                return (ColumnError(table, column), null);
            var op = cmp.Groups[2].Value switch
            {
                "=" => AgentSqlOp.Eq,
                "!=" or "<>" => AgentSqlOp.Neq,
                "<" => AgentSqlOp.Lt,
                ">" => AgentSqlOp.Gt,
                "<=" => AgentSqlOp.Lte,
                ">=" => AgentSqlOp.Gte,
                _ => AgentSqlOp.Eq,
            };
            var value = cmp.Groups[4].Success ? cmp.Groups[4].Value
                : cmp.Groups[5].Success ? cmp.Groups[5].Value
                : cmp.Groups[6].Value;
            list.Add(new AgentSqlPredicate { Column = column, Op = op, Value = value });
        }

        return (null, list);
    }

    /// <summary>
    /// Parses a products WHERE clause in the sql_readonly dialect (AND only, allowlisted columns).
    /// </summary>
    public static (string? Error, IReadOnlyList<AgentSqlPredicate>? Predicates) TryParseWhere(
        string table, string whereSql)
    {
        var canonical = CanonicalTable(table);
        if (!Tables.TryGetValue(canonical, out var cols))
            return ($"Table '{table}' is not allowlisted", null);
        var parsed = ParseWhere(whereSql, AllowedColumnSet(cols), canonical);
        return (parsed.Error, parsed.Predicates);
    }

    private static List<string> ParseInValues(string inner)
    {
        var values = new List<string>();
        foreach (var token in inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            if ((t.StartsWith('\'') && t.EndsWith('\'')) || (t.StartsWith('"') && t.EndsWith('"')))
                t = t[1..^1];
            values.Add(t);
        }

        return values;
    }

    private static List<string> SplitAndOutsideParens(string whereSql)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < whereSql.Length; i++)
        {
            var c = whereSql[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (depth == 0
                && i + 5 <= whereSql.Length
                && whereSql.AsSpan(i, 5).Equals(" AND ", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(whereSql[start..i].Trim());
                start = i + 5;
                i += 4;
            }
        }

        parts.Add(whereSql[start..].Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }

    private static int IndexOfKeyword(string rest, string keyword)
    {
        var depth = 0;
        for (var i = 0; i <= rest.Length - keyword.Length; i++)
        {
            var c = rest[i];
            if (c == '(') depth++;
            else if (c == ')' && depth > 0) depth--;
            else if (depth == 0
                && rest.AsSpan(i).StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var beforeOk = i == 0 || !char.IsLetterOrDigit(rest[i - 1]);
                var after = i + keyword.Length;
                var afterOk = after >= rest.Length || !char.IsLetterOrDigit(rest[after]);
                if (beforeOk && afterOk)
                    return i;
            }
        }

        return -1;
    }

    private static int NextClause(int a, int b, int fallback)
    {
        var candidates = new[] { a, b }.Where(i => i >= 0).ToArray();
        return candidates.Length == 0 ? fallback : candidates.Min();
    }

    private static readonly Regex CountStarRx = new(
        @"^COUNT\s*\(\s*\*\s*\)(?:\s+AS\s+([a-zA-Z_][\w]*))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CountDistinctRx = new(
        @"^COUNT\s*\(\s*DISTINCT\s+([a-zA-Z_][\w]*)\s*\)(?:\s+AS\s+([a-zA-Z_][\w]*))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MinMaxRx = new(
        @"^(MIN|MAX)\s*\(\s*([a-zA-Z_][\w]*)\s*\)(?:\s+AS\s+([a-zA-Z_][\w]*))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool TryParseAggregate(
        string col,
        HashSet<string> allowedCols,
        string table,
        out AgentSqlSelectItem? item,
        out string? error)
    {
        item = null;
        error = null;
        var star = CountStarRx.Match(col.Trim());
        if (star.Success)
        {
            item = new AgentSqlSelectItem
            {
                Kind = AgentSqlSelectKind.CountStar,
                OutputName = star.Groups[1].Success ? star.Groups[1].Value : "count",
            };
            return true;
        }

        var minMax = MinMaxRx.Match(col.Trim());
        if (minMax.Success)
        {
            var minCol = minMax.Groups[2].Value;
            if (!allowedCols.Contains(minCol.ToLowerInvariant()))
            {
                error = ColumnError(table, minCol);
                return true;
            }

            item = new AgentSqlSelectItem
            {
                Kind = minMax.Groups[1].Value.Equals("MIN", StringComparison.OrdinalIgnoreCase)
                    ? AgentSqlSelectKind.Min
                    : AgentSqlSelectKind.Max,
                OutputName = minMax.Groups[3].Success ? minMax.Groups[3].Value : minMax.Groups[1].Value.ToLowerInvariant(),
                Column = minCol,
            };
            return true;
        }

        var distinct = CountDistinctRx.Match(col.Trim());
        if (!distinct.Success)
            return false;

        var inner = distinct.Groups[1].Value;
        if (!allowedCols.Contains(inner.ToLowerInvariant()))
        {
            error = ColumnError(table, inner);
            return true;
        }

        item = new AgentSqlSelectItem
        {
            Kind = AgentSqlSelectKind.CountDistinct,
            OutputName = distinct.Groups[2].Success ? distinct.Groups[2].Value : $"count_distinct_{inner}",
            Column = inner,
        };
        return true;
    }

    private static AgentSqlQuery Fail(string error) => new() { Error = error };
}

public sealed class AgentSqlFilterException : InvalidOperationException
{
    public AgentSqlFilterException(string message) : base(message) { }
}
