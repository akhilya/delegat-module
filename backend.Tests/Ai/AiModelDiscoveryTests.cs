using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Delegat.Services.Ai;

namespace Delegat.Tests.Ai;

public class AiModelDiscoveryTests
{
    [Fact]
    public async Task DeepSeekAnthropicDiscovery_UsesRootModelsEndpointAndBearerAuth()
    {
        var handler = new CapturingHandler("""
            {"object":"list","data":[{"id":"deepseek-flash"},{"id":"deepseek-v4-pro"}]}
            """);
        using var http = new HttpClient(handler);

        var models = await AiModelConfigService.FetchAnthropicModelsAsync(
            "https://api.deepseek.com/anthropic", "test-key", http);

        Assert.Equal(["deepseek-flash", "deepseek-v4-pro"], models);
        Assert.Equal("https://api.deepseek.com/models", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("test-key", handler.Authorization?.Parameter);
        Assert.Null(handler.ApiKeyHeader);
    }

    [Fact]
    public async Task AnthropicDiscovery_UsesVersionedModelsEndpointAndAnthropicHeaders()
    {
        var handler = new CapturingHandler("""
            {"data":[{"id":"claude-test"}]}
            """);
        using var http = new HttpClient(handler);

        var models = await AiModelConfigService.FetchAnthropicModelsAsync(
            "https://anthropic.example.test/proxy", "test-key", http);

        Assert.Equal(["claude-test"], models);
        Assert.Equal("https://anthropic.example.test/proxy/v1/models", handler.RequestUri?.AbsoluteUri);
        Assert.Null(handler.Authorization);
        Assert.Equal("test-key", handler.ApiKeyHeader);
        Assert.Equal("2023-06-01", handler.AnthropicVersionHeader);
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? ApiKeyHeader { get; private set; }
        public string? AnthropicVersionHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            ApiKeyHeader = request.Headers.TryGetValues("x-api-key", out var apiKey)
                ? apiKey.Single()
                : null;
            AnthropicVersionHeader = request.Headers.TryGetValues("anthropic-version", out var version)
                ? version.Single()
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            });
        }
    }
}
