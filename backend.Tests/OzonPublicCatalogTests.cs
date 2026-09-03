using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Delegat.Marketplace.Ozon;
using Delegat.Marketplace.SDK;

namespace Delegat.Tests;

public class OzonPublicCatalogTests
{
    [Fact]
    public void Capabilities_IncludeSearchPublicCatalog()
    {
        var plugin = new OzonPlugin();
        Assert.True(plugin.Capabilities.HasFlag(MarketplaceCapabilities.SearchPublicCatalog));
    }

    [Fact]
    public void SharedClient_UsesCurrentDesktopChromeHeaders()
    {
        var headers = OzonPublicCatalogClient.Shared.DefaultRequestHeaders;
        Assert.True(headers.TryGetValues("Sec-CH-UA", out var ch));
        Assert.Contains("Chromium", string.Join(" ", ch), StringComparison.Ordinal);
        Assert.Contains("149", string.Join(" ", ch), StringComparison.Ordinal);
        Assert.True(headers.TryGetValues("Sec-CH-UA-Platform", out var platform));
        Assert.Contains("Windows", string.Join(" ", platform), StringComparison.Ordinal);
        Assert.Equal("https://www.ozon.ru/", headers.Referrer?.ToString());
    }

    [Fact]
    public async Task Search_MapsComposerTiles()
    {
        var tile = """
            {"items":[{"skuId":165204773,"action":{"link":"/product/airpods-165204773/"},"cellTrackingInfo":{"id":165204773,"title":"AirPods","brand":"Apple","price":14990,"finalPrice":12990,"seller":"Tech Shop"}}]}
            """;
        var json = "{\"widgetStates\":{\"searchResultsV2-1\":" + JsonSerializer.Serialize(tile) + "}}";
        var http = Scripted("ozon.ru", json);
        var client = new OzonPublicCatalogClient(http);

        var result = await client.SearchAsync("airpods", 50, null, CancellationToken.None);

        var product = Assert.Single(result.Items);
        Assert.Equal("165204773", product.ExternalId);
        Assert.Equal("AirPods", product.Name);
        Assert.Equal("Apple", product.Brand);
        Assert.Equal(12990m, product.Price);
        Assert.Equal("Tech Shop", product.RawData?["seller"]?.ToString());
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void MapProductPage_ReadsHeadingPriceGalleryAndCharacteristics()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "widgetStates": {
                "webProductHeading-1": "{\"title\":\"AirPods Pro\"}",
                "webPrice-1": "{\"cardPrice\":\"12 500\",\"originalPrice\":\"14990\"}",
                "webGallery-1": "{\"images\":[{\"src\":\"https://cdn.ozon.ru/a.jpg\"}]}",
                "webCharacteristics-1": "{\"characteristics\":[{\"title\":\"Цвет\",\"values\":[{\"text\":\"White\"}]}]}",
                "webBrand-1": "{\"name\":\"Apple\"}",
                "webSeller-1": "{\"sellerName\":\"Tech Shop\"}"
              }
            }
            """);
        var product = OzonPublicCatalogClient.MapProductPage(doc.RootElement, "165204773");

        Assert.NotNull(product);
        Assert.Equal("AirPods Pro", product!.Name);
        Assert.Equal("Apple", product.Brand);
        Assert.Equal(12500m, product.Price);
        Assert.Equal("White", product.Attributes!["Цвет"]);
        Assert.Equal("https://cdn.ozon.ru/a.jpg", Assert.Single(product.ImageUrls!));
        Assert.Equal("Tech Shop", product.RawData?["seller"]?.ToString());
    }

    [Fact]
    public void MapHtmlProducts_ReadsJsonLd()
    {
        var html = """
            <html><script type="application/ld+json">{"@type":"Product","name":"From LD","sku":"42","brand":{"name":"Acme"},"offers":{"price":"199"},"image":"https://cdn.ozon.ru/b.jpg"}</script></html>
            """;
        var product = Assert.Single(OzonPublicCatalogClient.MapHtmlProducts(html, 10));
        Assert.Equal("42", product.ExternalId);
        Assert.Equal("From LD", product.Name);
        Assert.Equal("Acme", product.Brand);
        Assert.Equal(199m, product.Price);
    }

    [Fact]
    public async Task Search_HtmlCaptcha_Throws403()
    {
        var html = "<html><body>captcha challenge-platform</body></html>";
        var http = Scripted("ozon.ru", html, HttpStatusCode.OK);
        var client = new OzonPublicCatalogClient(http);

        var ex = await Assert.ThrowsAsync<OzonApiException>(() =>
            client.SearchAsync("nike", 50, null, CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
    }

    [Fact]
    public async Task Search_HtmlCaptcha_UsesPageLoaderJson()
    {
        var html = "<html><body>captcha challenge-platform</body></html>";
        var http = Scripted("ozon.ru", html, HttpStatusCode.OK);
        var tile = """{"items":[{"skuId":9,"cellTrackingInfo":{"id":9,"title":"From Chromium","finalPrice":10}}]}""";
        var json = "{\"widgetStates\":{\"searchResultsV2-1\":" + JsonSerializer.Serialize(tile) + "}}";
        var loader = new FakePageLoader { CapturedJson = json, Html = "<html>ok</html>" };
        var client = new OzonPublicCatalogClient(http, new ConnectionContext { PageLoader = loader });

        var result = await client.SearchAsync("nike", 50, null, CancellationToken.None);

        Assert.Equal("9", Assert.Single(result.Items).ExternalId);
        Assert.Contains("/search/", loader.LastUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ozon.ru", loader.LastUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, loader.Calls);
    }

    [Fact]
    public async Task Search_HtmlCaptcha_UsesPageLoaderHtmlJsonLd()
    {
        var blocked = "<html><body>captcha</body></html>";
        var http = Scripted("ozon.ru", blocked, HttpStatusCode.OK);
        var pageHtml = """
            <html><script type="application/ld+json">{"@type":"Product","name":"From Chromium LD","sku":"77","offers":{"price":"5"}}</script></html>
            """;
        var loader = new FakePageLoader { Html = pageHtml };
        var client = new OzonPublicCatalogClient(http, new ConnectionContext { PageLoader = loader });

        var result = await client.SearchAsync("nike", 50, null, CancellationToken.None);

        var product = Assert.Single(result.Items);
        Assert.Equal("77", product.ExternalId);
        Assert.Equal("From Chromium LD", product.Name);
    }

    [Fact]
    public async Task Search_HtmlCaptcha_PageLoaderStillCaptcha_Throws403()
    {
        var html = "<html><body>captcha</body></html>";
        var http = Scripted("ozon.ru", html, HttpStatusCode.OK);
        var loader = new FakePageLoader { Html = "<html>captcha challenge-platform</html>" };
        var client = new OzonPublicCatalogClient(http, new ConnectionContext { PageLoader = loader });

        var ex = await Assert.ThrowsAsync<OzonApiException>(() =>
            client.SearchAsync("nike", 50, null, CancellationToken.None));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal(1, loader.Calls);
    }

    [Fact]
    public async Task Search_RetriesOnceOn429_ThenSucceeds()
    {
        var tile = """{"items":[{"skuId":1,"cellTrackingInfo":{"id":1,"title":"Ok","finalPrice":100}}]}""";
        var json = "{\"widgetStates\":{\"searchResultsV2-1\":" + JsonSerializer.Serialize(tile) + "}}";
        var handler = new SequenceHandler(
            (HttpStatusCode.TooManyRequests, null),
            (HttpStatusCode.OK, json));
        var client = new OzonPublicCatalogClient(new HttpClient(handler));

        var result = await client.SearchAsync("nike", 50, null, CancellationToken.None);

        Assert.Equal("1", Assert.Single(result.Items).ExternalId);
        Assert.Equal(2, handler.Calls);
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

    private static HttpClient Scripted(string hostContains, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpClient(new ScriptedHandler(hostContains, body, status), disposeHandler: true);
    }

    private sealed class ScriptedHandler(string hostContains, string body, HttpStatusCode status) : HttpMessageHandler
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
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, media)
            });
        }
    }

    private sealed class SequenceHandler(params (HttpStatusCode Status, string? Json)[] steps)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var step = steps[Math.Min(Calls, steps.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(step.Status)
            {
                Content = new StringContent(step.Json ?? "{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
