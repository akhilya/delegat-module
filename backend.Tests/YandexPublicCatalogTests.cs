using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.SDK;
using Delegat.Marketplace.YandexMarket;

namespace Delegat.Tests;

public class YandexPublicCatalogTests
{
    [Fact]
    public void Capabilities_IncludeSearchPublicCatalog()
    {
        var plugin = new YandexMarketPlugin();
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog));
    }

    [Fact]
    public void SharedClient_UsesCurrentDesktopChromeHeaders()
    {
        var headers = YandexPublicCatalogClient.Shared.DefaultRequestHeaders;
        Assert.True(headers.TryGetValues("Sec-CH-UA", out var ch));
        Assert.Contains("149", string.Join(" ", ch), StringComparison.Ordinal);
        Assert.Equal("https://market.yandex.ru/", headers.Referrer?.ToString());
    }

    [Fact]
    public async Task Search_MapsApiResults()
    {
        var json = """
            {"search":{"total":1,"results":[{"id":123456,"entity":"product","titles":{"raw":"iPhone 15"},"prices":{"value":"79990"},"vendor":{"name":"Apple"},"shop":{"name":"MVideo"},"pictures":[{"original":{"url":"//avatars.mds.yandex.net/img.jpg"}}],"categories":[{"id":91491,"name":"Smartphones"}]}]}}
            """;
        var http = Scripted("market.yandex.ru", json);
        var client = new YandexPublicCatalogClient(http);

        var result = await client.SearchAsync("iphone", 50, null, CancellationToken.None);

        var product = Assert.Single(result.Items);
        Assert.Equal("123456", product.ExternalId);
        Assert.Equal("iPhone 15", product.Name);
        Assert.Equal("Apple", product.Brand);
        Assert.Equal(79990m, product.Price);
        Assert.Equal("Smartphones", product.CategoryName);
        Assert.Equal("91491", product.CategoryId);
        Assert.Equal("MVideo", product.RawData?["seller"]?.ToString());
        Assert.Equal("https://avatars.mds.yandex.net/img.jpg", Assert.Single(product.ImageUrls!));
        Assert.Equal(1, result.TotalCount);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void MapSearchHit_IgnoresVendorOnlyObjects()
    {
        using var doc = JsonDocument.Parse("""{"id":99,"name":"Apple"}""");
        Assert.Null(YandexPublicCatalogClient.MapSearchHit(doc.RootElement));
    }

    [Fact]
    public async Task GetProduct_UsesResolveThenMatchesId()
    {
        var json = """
            {"search":{"results":[{"id":555,"entity":"product","titles":{"raw":"Card"},"prices":{"value":"100"}}]}}
            """;
        var http = Scripted("market.yandex.ru", json);
        var client = new YandexPublicCatalogClient(http);

        var product = await client.GetProductAsync("555", CancellationToken.None);

        Assert.NotNull(product);
        Assert.Equal("555", product!.ExternalId);
        Assert.Equal("Card", product.Name);
        Assert.Equal(100m, product.Price);
    }

    [Fact]
    public void MapHtmlProducts_ReadsJsonLd()
    {
        var html = """
            <html><script type="application/ld+json">{"@type":"Product","name":"From LD","sku":"77","offers":{"price":"10"}}</script></html>
            """;
        var product = Assert.Single(YandexPublicCatalogClient.MapHtmlProducts(html, 10));
        Assert.Equal("77", product.ExternalId);
        Assert.Equal("From LD", product.Name);
        Assert.Equal(10m, product.Price);
    }

    [Fact]
    public async Task Search_HtmlCaptcha_Throws403()
    {
        var html = "<html>captcha</html>";
        var http = Scripted("market.yandex.ru", html);
        var client = new YandexPublicCatalogClient(http);

        var ex = await Assert.ThrowsAsync<YandexMarketApiException>(() =>
            client.SearchAsync("nike", 50, null, CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Search_HtmlCaptcha_UsesPageLoaderJson()
    {
        var html = "<html>captcha</html>";
        var http = Scripted("market.yandex.ru", html);
        var json = """
            {"search":{"total":1,"results":[{"id":42,"entity":"product","titles":{"raw":"From Chromium"},"prices":{"value":"10"}}]}}
            """;
        var loader = new FakePageLoader { CapturedJson = json, Html = "<html>ok</html>" };
        var client = new YandexPublicCatalogClient(http, new ConnectionContext { PageLoader = loader });

        var result = await client.SearchAsync("nike", 50, null, CancellationToken.None);

        Assert.Equal("42", Assert.Single(result.Items).ExternalId);
        Assert.Contains("market.yandex.ru/search", loader.LastUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, loader.Calls);
    }

    private sealed class FakePageLoader : IStorefrontPageLoader
    {
        public string Html { get; set; } = "";
        public string? CapturedJson { get; set; }
        public string LastUrl { get; private set; } = "";
        public int Calls { get; private set; }

        public Task<StorefrontPageLoad?> LoadAsync(
            string url,
            IReadOnlyList<string>? captureUrlContains = null,
            CancellationToken ct = default)
        {
            Calls++;
            LastUrl = url;
            return Task.FromResult<StorefrontPageLoad?>(new StorefrontPageLoad(Html, CapturedJson));
        }
    }

    private static HttpClient Scripted(string hostContains, string body)
    {
        return new HttpClient(new ScriptedHandler(hostContains, body), disposeHandler: true);
    }

    private sealed class ScriptedHandler(string hostContains, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            Assert.Null(request.Headers.Authorization);
            if (!url.Contains(hostContains, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }

            var media = body.TrimStart().StartsWith('<') ? "text/html" : "application/json";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, media)
            });
        }
    }
}
