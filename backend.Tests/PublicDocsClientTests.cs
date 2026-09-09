using System.Net;
using System.Text;
using Delegat.Infrastructure.Configuration;
using Delegat.Services.Docs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

public class PublicDocsClientTests
{
    private const string DocsBase = "https://docs.test/docs";

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("workspace/products", true)]
    [InlineData("adapters/ozon", true)]
    [InlineData("../secret", false)]
    [InlineData("workspace/../etc", false)]
    [InlineData("http://evil.test", false)]
    [InlineData("Workspace/Products", false)]
    [InlineData("a b", false)]
    public void IsAllowedSlug_MatchesAllowlist(string? slug, bool allowed)
    {
        Assert.Equal(allowed, PublicDocsPaths.IsAllowedSlug(slug));
    }

    [Fact]
    public void PageUri_RefusesDotDot()
    {
        var baseUri = new Uri(DocsBase);
        var ex = Assert.Throws<DocsClientException>(() =>
            PublicDocsPaths.PageUri(baseUri, "ru", "../etc"));
        Assert.Equal("docs_slug_invalid", ex.Code);
    }

    [Fact]
    public void IsAllowedFetchUri_RefusesOffHost()
    {
        var baseUri = new Uri(DocsBase);
        Assert.False(PublicDocsPaths.IsAllowedFetchUri(new Uri("https://evil.test/docs/catalog.json"), baseUri));
        Assert.True(PublicDocsPaths.IsAllowedFetchUri(new Uri("https://docs.test/docs/catalog.json"), baseUri));
        Assert.True(PublicDocsPaths.IsAllowedFetchUri(new Uri("https://docs.test/docs/llms.txt"), baseUri));
        Assert.True(PublicDocsPaths.IsAllowedFetchUri(new Uri("https://docs.test/docs/workspace/products.md"), baseUri));
        Assert.False(PublicDocsPaths.IsAllowedFetchUri(new Uri("https://docs.test/secret.md"), baseUri));
        Assert.False(PublicDocsPaths.IsAllowedFetchUri(new Uri("https://docs.test/docs/secret.txt"), baseUri));
    }

    [Fact]
    public void StripFrontmatter_RemovesYamlBlock()
    {
        var raw = "---\ntitle: X\n---\n# Hello\n";
        Assert.Equal("# Hello\n", PublicDocsPaths.StripFrontmatter(raw));
        Assert.Equal("# Hello\n", PublicDocsPaths.StripFrontmatter("# Hello\n"));
    }

    [Fact]
    public async Task GetPage_ReturnsMarkdownAndTitle()
    {
        var handler = new ScriptedHandler(_ => OkText("# Товары\nHow to add SKUs.\n"));
        var client = CreateClient(handler);

        var page = await client.GetPageAsync("workspace/products", "ru");

        Assert.Equal("workspace/products", page.Slug);
        Assert.Equal("Товары", page.Title);
        Assert.Contains("How to add SKUs", page.Markdown);
        Assert.Equal("https://docs.test/docs/workspace/products.md", handler.LastUrl);
    }

    [Fact]
    public async Task GetPage_Origin404_IsDocsNotFound()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DocsClientException>(() =>
            client.GetPageAsync("workspace/missing", "ru"));
        Assert.Equal("docs_not_found", ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task GetPage_CachesSuccessfulFetch()
    {
        var handler = new ScriptedHandler(_ => OkText("# Cached\n"));
        var client = CreateClient(handler);

        await client.GetPageAsync("workspace/products", "ru");
        await client.GetPageAsync("workspace/products", "ru");

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetPage_OffHostRedirect_IsDenied()
    {
        var handler = new ScriptedHandler(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.Found);
            res.Headers.Location = new Uri("https://evil.test/steal");
            return res;
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DocsClientException>(() =>
            client.GetPageAsync("workspace/products", "ru"));
        Assert.Equal("docs_redirect_denied", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task GetPage_InvalidSlug_DoesNotCallHttp()
    {
        var handler = new ScriptedHandler(_ => OkText("nope"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DocsClientException>(() =>
            client.GetPageAsync("../passwd", "ru"));
        Assert.Equal("docs_slug_invalid", ex.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetCatalog_ParsesGroups()
    {
        var handler = new ScriptedHandler(_ => OkText(
            """{"locale":"ru","groups":[{"text":"Кабинет","items":[{"text":"Товары","slug":"workspace/products"}]}]}""",
            "application/json"));
        var client = CreateClient(handler);

        var catalog = await client.GetCatalogAsync("ru");

        Assert.Equal("ru", catalog.Locale);
        Assert.Equal("workspace/products", catalog.Groups[0].Items[0].Slug);
        Assert.Equal("https://docs.test/docs/catalog.json", handler.LastUrl);
    }

    [Fact]
    public void CatalogFromLlmsTxt_GroupsRussianLinks()
    {
        const string llms = """
            # Docs

            ## Russian

            - [Документация](https://docs.test/docs/index.md)
            - [Товары](https://docs.test/docs/workspace/products.md)
            - [Ozon](https://docs.test/docs/adapters/ozon.md)

            ## English

            - [Products](https://docs.test/docs/en/workspace/products.md)
            """;

        var catalog = PublicDocsPaths.CatalogFromLlmsTxt(llms, "ru");

        Assert.Equal("ru", catalog.Locale);
        Assert.Contains(catalog.Groups, g => g.Items.Any(i => i.Slug == "" && i.Text == "Документация"));
        Assert.Contains(
            catalog.Groups,
            g => g.Text == "Кабинет" && g.Items.Any(i => i.Slug == "workspace/products"));
        Assert.DoesNotContain(
            catalog.Groups.SelectMany(g => g.Items),
            i => i.Slug == "workspace/products" && i.Text == "Products");
    }

    [Fact]
    public async Task GetCatalog_HtmlSpaFallback_UsesLlmsTxt()
    {
        var handler = new ScriptedHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("catalog.json", StringComparison.Ordinal))
            {
                return OkText("<!doctype html><html lang=\"ru\"><title>Docs</title></html>", "text/html");
            }

            return OkText(
                """
                ## Russian
                - [Товары](https://docs.test/docs/workspace/products.md)
                """,
                "text/markdown");
        });
        var client = CreateClient(handler);

        var catalog = await client.GetCatalogAsync("ru");

        Assert.Equal("workspace/products", catalog.Groups.SelectMany(g => g.Items).First().Slug);
        Assert.Equal(2, handler.Calls);
        Assert.Equal("https://docs.test/docs/llms.txt", handler.LastUrl);
    }

    [Fact]
    public async Task GetCatalog_HtmlSpaFallback_IsUnavailable_WhenLlmsAlsoHtml()
    {
        var handler = new ScriptedHandler(_ => OkText(
            "<!doctype html><html lang=\"ru\"><title>Docs</title></html>",
            "text/html"));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DocsClientException>(() =>
            client.GetCatalogAsync("ru"));
        Assert.Equal("docs_unavailable", ex.Code);
        Assert.Equal(502, ex.StatusCode);
    }

    private static PublicDocsClient CreateClient(ScriptedHandler handler)
    {
        var opts = new PublicDocsOptions { BaseUrl = DocsBase, CacheDuration = TimeSpan.FromMinutes(15) };
        return new PublicDocsClient(
            new StubFactory(handler),
            opts,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<PublicDocsClient>.Instance);
    }

    private static HttpResponseMessage OkText(string body, string media = "text/markdown") =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, media),
        };

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int Calls { get; private set; }
        public string? LastUrl { get; private set; }

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(_respond(request));
        }
    }
}
