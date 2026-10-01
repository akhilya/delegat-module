using System.Text;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Named catalog filters. <c>@name</c> in a where clause is replaced with a fixed predicate
/// before the clause is parsed. Values never become SQL text.
/// </summary>
public static class CatalogWhereTemplates
{
    public const int MaxLength = 2000;

    public sealed record Template(string Name, string Sql);

    public static readonly Template[] All =
    [
        new("withGallery", "id IN (SELECT productId FROM product_images)"),
        new("withoutGallery", "id NOT IN (SELECT productId FROM product_images)"),
        new("withFiles", "id IN (SELECT productId FROM files)"),
        new("withoutFiles", "id NOT IN (SELECT productId FROM files)"),
        new("inStock", "storageQuantity > 0"),
        new("outOfStock", "storageQuantity <= 0"),
        new("noPrice", "price IS NULL"),
        new("noBarcode", "barcode IS NULL"),
        new("noCategory", "categoryId IS NULL"),
    ];

    public static string PromptBlock()
    {
        var lines = string.Join("\n", All.Select(t => $"@{t.Name} = {t.Sql}"));
        return $"""
            === CATALOG WHERE (internal — never read this to the user) ===
            Filter GET /products, PATCH /products/bulk, and export-batch with where.
            Copy a template. Combine with AND. Do not invent a flag for photos or any other fact.
            {lines}
            Fill in numbers bare and text in single quotes:
            price >= N    price <= N    categoryId = N
            name LIKE '%text%'    sku LIKE '%text%'
            id IN (SELECT productId FROM store_product_attribute_values WHERE storeAttributeId = N AND value = 'text')
            id IN (SELECT productId FROM product_attribute_values WHERE attributeId = N AND value = 'text')
            Example: @withGallery AND price >= 1000 AND categoryId = 3
            """;
    }

    /// <summary>
    /// Replaces <c>@name</c> tokens outside quotes and rejects statement separators and comments.
    /// </summary>
    public static string Expand(string where)
    {
        if (string.IsNullOrWhiteSpace(where))
            throw new AgentSqlFilterException("WHERE is empty");
        if (where.Length > MaxLength)
            throw new AgentSqlFilterException("WHERE is too long");

        var map = All.ToDictionary(t => t.Name, t => t.Sql, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder(where.Length + 64);
        var quote = '\0';
        for (var i = 0; i < where.Length; i++)
        {
            var c = where[i];
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                sb.Append(c);
                continue;
            }

            if (c == ';')
                throw new AgentSqlFilterException("WHERE cannot contain a statement separator");
            if (c == '`')
                throw new AgentSqlFilterException("WHERE cannot contain an identifier quote");
            if (c == '-' && i + 1 < where.Length && where[i + 1] == '-')
                throw new AgentSqlFilterException("WHERE cannot contain a comment");
            if (c == '/' && i + 1 < where.Length && where[i + 1] == '*')
                throw new AgentSqlFilterException("WHERE cannot contain a comment");
            if (c == '#' )
                throw new AgentSqlFilterException("WHERE cannot contain a comment");

            if (c == '@')
            {
                var start = i + 1;
                var end = start;
                while (end < where.Length && (char.IsAsciiLetterOrDigit(where[end]) || where[end] == '_'))
                    end++;
                if (end == start)
                    throw new AgentSqlFilterException("Unknown where template");
                var name = where[start..end];
                if (!map.TryGetValue(name, out var sql))
                    throw new AgentSqlFilterException($"Unknown where template @{name}");
                sb.Append(sql);
                i = end - 1;
                continue;
            }

            sb.Append(c);
        }

        if (quote != '\0')
            throw new AgentSqlFilterException("WHERE has an unclosed quote");
        return sb.ToString();
    }
}
