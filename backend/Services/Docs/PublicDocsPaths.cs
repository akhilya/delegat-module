using System.Text.RegularExpressions;
using Delegat.Infrastructure.Configuration;

namespace Delegat.Services.Docs;

public static class PublicDocsPaths
{
    private static readonly Regex SlugRx = new(
        @"^[a-z0-9-]+(/[a-z0-9-]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeLocale(string? locale) =>
        !string.IsNullOrWhiteSpace(locale) && locale.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "ru";

    public static bool IsAllowedSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return true;
        if (slug.Contains("..", StringComparison.Ordinal)
            || slug.Contains('\\', StringComparison.Ordinal)
            || slug.Contains(':', StringComparison.Ordinal)
            || slug.Contains('?', StringComparison.Ordinal)
            || slug.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        return SlugRx.IsMatch(slug);
    }

    public static Uri RequireDocsBase(string? baseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(baseUrl)
            ? PublicDocsOptions.DefaultBaseUrl
            : baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new DocsClientException("docs_unavailable", StatusCodes.Status502BadGateway, "Invalid docs base URL");
        }

        return uri;
    }

    public static Uri Combine(Uri docsBase, string relative)
    {
        var baseStr = docsBase.ToString().TrimEnd('/') + "/";
        return new Uri(baseStr + relative.TrimStart('/'), UriKind.Absolute);
    }

    public static Uri CatalogUri(Uri docsBase, string locale)
    {
        var loc = NormalizeLocale(locale);
        return Combine(docsBase, loc == "en" ? "en/catalog.json" : "catalog.json");
    }

    public static Uri LlmsTxtUri(Uri docsBase) => Combine(docsBase, "llms.txt");

    /// <summary>
    /// Build a sidebar catalog from public <c>/docs/llms.txt</c> when catalog.json
    /// is still the SPA HTML fallback (undeployed nginx pin).
    /// </summary>
    public static DocsCatalog CatalogFromLlmsTxt(string text, string locale)
    {
        var loc = NormalizeLocale(locale);
        var section = ExtractLlmsLocaleSection(text ?? "", loc);
        var groups = new List<DocsCatalogGroup>();
        var byPrefix = new Dictionary<string, DocsCatalogGroup>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(section, @"\[([^\]]+)\]\(([^)\s]+)\)"))
        {
            if (!TryDocsSlugFromHref(match.Groups[2].Value, loc, out var slug))
                continue;
            var prefix = GroupPrefix(slug);
            if (!byPrefix.TryGetValue(prefix, out var group))
            {
                group = new DocsCatalogGroup { Text = GroupTitle(prefix, loc), Items = [] };
                byPrefix[prefix] = group;
                groups.Add(group);
            }

            group.Items.Add(new DocsCatalogItem
            {
                Text = match.Groups[1].Value.Trim(),
                Slug = slug,
            });
        }

        return new DocsCatalog { Locale = loc, Groups = groups };
    }

    public static Uri PageUri(Uri docsBase, string locale, string? slug)
    {
        if (!IsAllowedSlug(slug))
            throw new DocsClientException("docs_slug_invalid", StatusCodes.Status400BadRequest, "Invalid docs slug");
        var loc = NormalizeLocale(locale);
        var file = string.IsNullOrEmpty(slug) ? "index.md" : $"{slug}.md";
        return Combine(docsBase, loc == "en" ? $"en/{file}" : file);
    }

    public static bool IsAllowedFetchUri(Uri request, Uri docsBase)
    {
        if (!string.Equals(request.Host, docsBase.Host, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(request.Scheme, docsBase.Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var basePath = docsBase.AbsolutePath.TrimEnd('/');
        var reqPath = request.AbsolutePath;
        if (basePath.Length > 0
            && !reqPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = reqPath[basePath.Length..].TrimStart('/');
        var locRest = rest.StartsWith("en/", StringComparison.OrdinalIgnoreCase) ? rest[3..] : rest;
        if (string.Equals(locRest, "catalog.json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(locRest, "llms.txt", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!locRest.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return false;
        var slugPart = locRest[..^3];
        if (string.Equals(slugPart, "index", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsAllowedSlug(slugPart);
    }

    public static string StripFrontmatter(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("---", StringComparison.Ordinal))
            return text;
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return text;
        var after = end + 4;
        if (after < text.Length && text[after] == '\r') after++;
        if (after < text.Length && text[after] == '\n') after++;
        return text[after..];
    }

    public static string TitleFromMarkdown(string markdown, string? slug)
    {
        var m = Regex.Match(markdown, @"^#\s+(.+)$", RegexOptions.Multiline);
        if (m.Success) return m.Groups[1].Value.Trim();
        return string.IsNullOrEmpty(slug) ? "Documentation" : slug;
    }

    private static string ExtractLlmsLocaleSection(string text, string loc)
    {
        var heading = loc == "en" ? "## English" : "## Russian";
        var start = text.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return text;
        start += heading.Length;
        var next = text.IndexOf("\n## ", start, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    private static bool TryDocsSlugFromHref(string href, string loc, out string slug)
    {
        slug = "";
        var raw = (href ?? "").Trim();
        if (raw.Length == 0) return false;
        var path = raw.Split('?', 2)[0].Split('#', 2)[0].Trim('/');
        const string docsSeg = "/docs/";
        var idx = ("/" + path).IndexOf(docsSeg, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;
        var rest = ("/" + path)[(idx + docsSeg.Length)..];
        var isEn = rest.StartsWith("en/", StringComparison.OrdinalIgnoreCase);
        if (loc == "en" != isEn) return false;
        if (isEn) rest = rest[3..];
        if (!rest.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;
        var file = rest[..^3];
        if (string.Equals(file, "index", StringComparison.OrdinalIgnoreCase))
        {
            slug = "";
            return true;
        }

        if (!IsAllowedSlug(file)) return false;
        slug = file;
        return true;
    }

    private static string GroupPrefix(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return "start";
        var slash = slug.IndexOf('/');
        return slash < 0 ? slug : slug[..slash];
    }

    private static string GroupTitle(string prefix, string loc) => prefix switch
    {
        "start" => loc == "en" ? "Start" : "Начало",
        "account" => loc == "en" ? "Account" : "Аккаунт",
        "workspace" => loc == "en" ? "Workspace" : "Кабинет",
        "guides" => loc == "en" ? "Guides" : "Руководства",
        "adapters" => loc == "en" ? "Adapters" : "Адаптеры",
        "help" => loc == "en" ? "Help" : "Помощь",
        "updates" => loc == "en" ? "Updates" : "Развитие",
        _ => prefix,
    };
}
