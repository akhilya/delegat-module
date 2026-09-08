using System.Text.RegularExpressions;

namespace Delegat.Services.Ai.Agent;

public enum AgentSqlOp
{
    Eq,
    Neq,
    Like,
    Lt,
    Gt,
    In,
}

public sealed class AgentSqlPredicate
{
    public required string Column { get; init; }
    public required AgentSqlOp Op { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<string>? Values { get; init; }
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
    CountStar,
    CountDistinct,
}

public sealed class AgentSqlQuery
{
    public string? Error { get; init; }
    public string Table { get; init; } = "";
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<AgentSqlSelectItem> Select { get; init; } = [];
    public IReadOnlyList<AgentSqlPredicate>? Where { get; init; }
    public string? OrderByColumn { get; init; }
    public bool OrderDescending { get; init; }
    public int Limit { get; init; } = 50;
    public bool IsAggregate => Select.Count > 0 && Select.All(s => s.Kind != AgentSqlSelectKind.Column);
}

public static class AgentSqlGuard
{
    public const int MaxLimit = 200;

    public const string SchemaDescription = """
        products(id, sku, barcode, name, price, storageQuantity, categoryId, description)
        files(id, productId, displayName, filePath, contentType, length)
        categories(id, name, description, hasSizes) — flat list, no parent_id
        category_attributes(id, categoryId, name, key, type, isRequired, isUnique, sortOrder, group, defaultValue, description, allowCustomValues)
        product_attribute_values(id, productId, attributeId, value)
        store_attributes(id, name, key, type, isRequired, sortOrder, description)
        store_product_attribute_values(id, productId, storeAttributeId, value)
        groups(id, name)
        connections(id, name, adapterKey, isActive)
        mappings(id, name, connectionId)
        orders(id, marketplaceOrderId, displayId, status, channel, total, orderCreatedAt, connectionId)
        WHERE: = != <> < > LIKE IN (...). AND only. No OR. No JOIN. No table aliases. storeId is forced. No SELECT *.
        Aggregates (not mixed with row columns): COUNT(*) AS n, COUNT(DISTINCT col) AS n.
        product_attribute_values.attributeId (not categoryAttributeId).
        """;

    private static readonly Dictionary<string, HashSet<string>> Tables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["products"] = ["id", "sku", "barcode", "name", "price", "storagequantity", "categoryid", "description"],
        ["files"] = ["id", "productid", "displayname", "filepath", "contenttype", "length"],
        ["categories"] = ["id", "name", "description", "hassizes"],
        ["category_attributes"] = ["id", "categoryid", "name", "key", "type", "isrequired", "isunique", "sortorder", "group", "defaultvalue", "description", "allowcustomvalues"],
        ["product_attribute_values"] = ["id", "productid", "attributeid", "value"],
        ["store_attributes"] = ["id", "name", "key", "type", "isrequired", "sortorder", "description"],
        ["store_product_attribute_values"] = ["id", "productid", "storeattributeid", "value"],
        ["groups"] = ["id", "name"],
        ["connections"] = ["id", "name", "adapterkey", "isactive"],
        ["mappings"] = ["id", "name", "connectionid"],
        ["orders"] = ["id", "marketplaceorderid", "displayid", "status", "channel", "total", "ordercreatedat", "connectionid"],
    };

    private static readonly Dictionary<string, string> ColumnHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["categories.parent_id"] = "categories are a flat list — there is no parent_id. Select id, name, description, hasSizes.",
        ["categories.parentid"] = "categories are a flat list — there is no parent_id. Select id, name, description, hasSizes.",
        ["products.quantity"] = "Use storageQuantity, not quantity.",
        ["product_attribute_values.categoryattributeid"] = "Use attributeId, not categoryAttributeId.",
        ["product_attribute_values.category_attribute_id"] = "Use attributeId, not categoryAttributeId.",
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

        if (!lower.StartsWith("select", StringComparison.Ordinal))
            return Fail("Only SELECT is allowed");

        var m = SelectRx.Match(trimmed);
        if (!m.Success)
            return Fail("Expected SELECT columns FROM table");

        var tableRaw = m.Groups["table"].Value;
        var table = CanonicalTable(tableRaw);
        if (!Tables.TryGetValue(table, out var allowedCols))
            return Fail($"Table '{tableRaw}' is not allowlisted. Allowed: products, files, categories, category_attributes, product_attribute_values, store_attributes, store_product_attribute_values, groups, connections, mappings, orders.");
        var colPart = m.Groups["cols"].Value.Trim();
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

        if (selectItems.Any(i => i.Kind != AgentSqlSelectKind.Column)
            && selectItems.Any(i => i.Kind == AgentSqlSelectKind.Column))
            return Fail("COUNT cannot be mixed with row columns");
        if (normalizedCols.Count != normalizedCols.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            return Fail("Duplicate column aliases");

        var rest = m.Groups["rest"].Value.Trim();
        List<AgentSqlPredicate>? where = null;
        string? orderCol = null;
        var orderDesc = false;
        var limit = 50;

        if (rest.Length > 0)
        {
            var orderIdx = IndexOfKeyword(rest, "ORDER BY");
            var limitIdx = IndexOfKeyword(rest, "LIMIT");
            var whereIdx = IndexOfKeyword(rest, "WHERE");

            if (whereIdx >= 0)
            {
                var whereEnd = NextClause(orderIdx, limitIdx, rest.Length);
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
                var orderEnd = limitIdx >= 0 ? limitIdx : rest.Length;
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
                if (!int.TryParse(limitSql.Split(' ', 2)[0], out limit) || limit < 1)
                    return Fail("LIMIT must be a positive integer");
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
        };
    }

    private static string CanonicalTable(string table)
    {
        var key = table.ToLowerInvariant();
        return key switch
        {
            "categoryattributes" or "category_attribute" => "category_attributes",
            "productattributes" or "product_attributes" or "productattributevalues" => "product_attribute_values",
            "storeattributes" => "store_attributes",
            "storeproductattributevalues" or "store_product_attributes" => "store_product_attribute_values",
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
        // Single AND-chain of col OP value
        var parts = Regex.Split(whereSql, @"\s+AND\s+", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(whereSql, @"\bOR\b", RegexOptions.IgnoreCase))
            return ("OR is not allowed; use AND or two queries", null);
        var list = new List<AgentSqlPredicate>();
        foreach (var part in parts)
        {
            var like = Regex.Match(part.Trim(),
                @"^([a-zA-Z_][\w]*)\s+LIKE\s+('([^']*)'|""([^""]*)"")$",
                RegexOptions.IgnoreCase);
            if (like.Success)
            {
                var col = like.Groups[1].Value;
                if (!allowedCols.Contains(col.ToLowerInvariant()))
                    return (ColumnError(table, col), null);
                var val = like.Groups[3].Success ? like.Groups[3].Value : like.Groups[4].Value;
                list.Add(new AgentSqlPredicate { Column = col, Op = AgentSqlOp.Like, Value = val });
                continue;
            }

            var inn = Regex.Match(part.Trim(),
                @"^([a-zA-Z_][\w]*)\s+IN\s*\(\s*((?:'[^']*'|""[^""]*""|-?[0-9]+(?:\.[0-9]+)?)(?:\s*,\s*(?:'[^']*'|""[^""]*""|-?[0-9]+(?:\.[0-9]+)?))*)\s*\)$",
                RegexOptions.IgnoreCase);
            if (inn.Success)
            {
                var col = inn.Groups[1].Value;
                if (!allowedCols.Contains(col.ToLowerInvariant()))
                    return (ColumnError(table, col), null);
                var values = ParseInValues(inn.Groups[2].Value);
                if (values.Count == 0)
                    return ("IN list is empty", null);
                if (values.Count > 50)
                    return ("IN list is limited to 50 values", null);
                list.Add(new AgentSqlPredicate { Column = col, Op = AgentSqlOp.In, Values = values });
                continue;
            }

            var cmp = Regex.Match(part.Trim(),
                @"^([a-zA-Z_][\w]*)\s*(=|!=|<>|<|>)\s*('([^']*)'|""([^""]*)""|([0-9]+(?:\.[0-9]+)?))$");
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
                _ => AgentSqlOp.Eq,
            };
            var value = cmp.Groups[4].Success ? cmp.Groups[4].Value
                : cmp.Groups[5].Success ? cmp.Groups[5].Value
                : cmp.Groups[6].Value;
            list.Add(new AgentSqlPredicate { Column = column, Op = op, Value = value });
        }

        return (null, list);
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

    private static int IndexOfKeyword(string rest, string keyword)
    {
        var idx = rest.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        return idx;
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
