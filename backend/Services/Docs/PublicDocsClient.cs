using System.Net;
using System.Text.Json;
using Delegat.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace Delegat.Services.Docs;

public sealed class PublicDocsClient : IPublicDocsClient
{
    public const string HttpClientName = "PublicDocs";
    public const int MaxBodyChars = 32_000;
    private const int MaxRedirects = 2;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _http;
    private readonly PublicDocsOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PublicDocsClient> _logger;
    private readonly Uri _docsBase;

    public PublicDocsClient(
        IHttpClientFactory http,
        PublicDocsOptions options,
        IMemoryCache cache,
        ILogger<PublicDocsClient> logger)
    {
        _http = http;
        _options = options;
        _cache = cache;
        _logger = logger;
        _docsBase = PublicDocsPaths.RequireDocsBase(options.BaseUrl);
    }

    public async Task<DocsCatalog> GetCatalogAsync(string locale, CancellationToken ct = default)
    {
        var loc = PublicDocsPaths.NormalizeLocale(locale);
        var key = $"docs:catalog:{loc}";
        if (_cache.TryGetValue(key, out DocsCatalog? cached) && cached != null)
            return cached;

        DocsCatalog? parsed = null;
        try
        {
            var json = await FetchTextAsync(PublicDocsPaths.CatalogUri(_docsBase, loc), ct);
            parsed = JsonSerializer.Deserialize<DocsCatalog>(json, JsonOpts);
        }
        catch (DocsClientException ex) when (ex.Code == "docs_unavailable")
        {
            _logger.LogInformation(
                "Public docs catalog.json unavailable at {Base}; using llms.txt",
                _docsBase);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Public docs catalog JSON was invalid at {Base}; using llms.txt", _docsBase);
        }

        if (parsed?.Groups is { Count: > 0 })
        {
            NormalizeCatalog(parsed, loc);
            _cache.Set(key, parsed, _options.CacheDuration);
            return parsed;
        }

        var llms = await FetchTextAsync(PublicDocsPaths.LlmsTxtUri(_docsBase), ct);
        parsed = PublicDocsPaths.CatalogFromLlmsTxt(llms, loc);
        if (parsed.Groups.Count == 0)
        {
            throw new DocsClientException(
                "docs_unavailable",
                StatusCodes.Status502BadGateway,
                "Documentation catalog is unavailable");
        }

        NormalizeCatalog(parsed, loc);
        _cache.Set(key, parsed, _options.CacheDuration);
        return parsed;
    }

    public async Task<DocsPage> GetPageAsync(string slug, string locale, CancellationToken ct = default)
    {
        if (!PublicDocsPaths.IsAllowedSlug(slug))
            throw new DocsClientException("docs_slug_invalid", StatusCodes.Status400BadRequest, "Invalid docs slug");

        var loc = PublicDocsPaths.NormalizeLocale(locale);
        var key = $"docs:page:{loc}:{slug}";
        if (_cache.TryGetValue(key, out DocsPage? cached) && cached != null)
            return cached;

        var uri = PublicDocsPaths.PageUri(_docsBase, loc, slug);
        var text = await FetchTextAsync(uri, ct);
        var markdown = PublicDocsPaths.StripFrontmatter(text);
        var page = new DocsPage
        {
            Slug = slug ?? "",
            Locale = loc,
            Title = PublicDocsPaths.TitleFromMarkdown(markdown, slug),
            Markdown = markdown,
        };
        _cache.Set(key, page, _options.CacheDuration);
        return page;
    }

    private async Task<string> FetchTextAsync(Uri uri, CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        var current = uri;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!PublicDocsPaths.IsAllowedFetchUri(current, _docsBase))
            {
                throw new DocsClientException(
                    "docs_redirect_denied",
                    StatusCodes.Status400BadRequest,
                    "Documentation fetch left the allowlisted host");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400)
            {
                var location = response.Headers.Location
                    ?? throw new DocsClientException(
                        "docs_redirect_denied",
                        StatusCodes.Status400BadRequest,
                        "Documentation redirect had no Location");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new DocsClientException(
                    "docs_not_found",
                    StatusCodes.Status404NotFound,
                    "Documentation article not found");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Public docs fetch {Url} returned {Status}",
                    current,
                    code);
                throw new DocsClientException(
                    "docs_unavailable",
                    StatusCodes.Status502BadGateway,
                    "Documentation origin is unavailable");
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            var media = response.Content.Headers.ContentType?.MediaType;
            if (LooksLikeHtml(media, text))
            {
                _logger.LogWarning(
                    "Public docs fetch {Url} returned HTML instead of {Expected}",
                    current,
                    uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? "JSON"
                        : "markdown");
                throw new DocsClientException(
                    "docs_unavailable",
                    StatusCodes.Status502BadGateway,
                    uri.AbsolutePath.EndsWith("catalog.json", StringComparison.OrdinalIgnoreCase)
                        ? "Documentation catalog is unavailable"
                        : "Documentation origin is unavailable");
            }

            return text;
        }

        throw new DocsClientException(
            "docs_redirect_denied",
            StatusCodes.Status400BadRequest,
            "Documentation fetch used too many redirects");
    }

    private static void NormalizeCatalog(DocsCatalog parsed, string loc)
    {
        parsed.Locale = loc;
        parsed.Groups ??= [];
        foreach (var group in parsed.Groups)
            group.Items ??= [];
    }

    private static bool LooksLikeHtml(string? mediaType, string body)
    {
        if (mediaType is not null
            && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trim = body.AsSpan().TrimStart();
        return trim.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || trim.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }
}
