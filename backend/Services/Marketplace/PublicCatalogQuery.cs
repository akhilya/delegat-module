using System.Text.RegularExpressions;

namespace Delegat.Services.Marketplace;

public enum PublicCatalogQueryKind
{
    Text,
    ProductIds
}

/// <summary>
/// Host-side parser for the Catalog scrape search field:
/// marketplace URL or digits → product id(s); otherwise storefront text search.
/// </summary>
public sealed class PublicCatalogQuery
{
    public PublicCatalogQueryKind Kind { get; init; }
    public string Text { get; init; } = "";
    public IReadOnlyList<string> ProductIds { get; init; } = [];

    public static PublicCatalogQuery Parse(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
            return new PublicCatalogQuery { Kind = PublicCatalogQueryKind.Text, Text = "" };

        var tokens = SplitTokens(text);
        if (tokens.Count == 0)
            return new PublicCatalogQuery { Kind = PublicCatalogQueryKind.Text, Text = text };

        var ids = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            var id = ExtractProductId(token);
            if (id == null)
            {
                return new PublicCatalogQuery { Kind = PublicCatalogQueryKind.Text, Text = text };
            }

            ids.Add(id);
        }

        return new PublicCatalogQuery
        {
            Kind = PublicCatalogQueryKind.ProductIds,
            ProductIds = ids,
            Text = text
        };
    }

    public static string? ExtractProductId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        token = token.Trim();
        if (IsDigits(token))
            return token;

        var catalog = Regex.Match(token, @"/catalog/(\d+)(?:/|$|\?|#)", RegexOptions.IgnoreCase);
        if (catalog.Success)
            return catalog.Groups[1].Value;

        var nm = Regex.Match(token, @"[?&]nm=(\d+)", RegexOptions.IgnoreCase);
        if (nm.Success)
            return nm.Groups[1].Value;

        return null;
    }

    private static List<string> SplitTokens(string text)
    {
        return text
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static bool IsDigits(string value)
    {
        foreach (var ch in value)
        {
            if (ch < '0' || ch > '9')
                return false;
        }

        return value.Length > 0;
    }
}
