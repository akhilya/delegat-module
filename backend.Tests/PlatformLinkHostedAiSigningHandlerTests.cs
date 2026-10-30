using System.Net;
using System.Text;
using Delegat.Infrastructure.Configuration;
using Delegat.Services.PlatformLink;
using Microsoft.AspNetCore.Http;

namespace Delegat.Tests;

public sealed class PlatformLinkHostedAiSigningHandlerTests
{
    private static readonly PlatformLinkOptions EnabledOptions = new()
    {
        ControlPlaneUrl = "https://cp.test",
        InstanceId = "inst_0123456789abcdef0123456789abcdef",
        InstanceLinkSecret = "test-link-secret",
    };

    [Fact]
    public async Task SendAsync_AddsRequestIdSignsBodyAndRemovesProviderCredentials()
    {
        const string requestBody = "{\"model\":\"hosted-model\",\"messages\":[]}";
        byte[]? sentBody = null;
        HttpRequestMessage? sentRequest = null;
        var inner = new StubHandler(async (request, ct) =>
        {
            sentRequest = request;
            sentBody = await request.Content!.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        using var client = new HttpClient(new PlatformLinkHostedAiSigningHandler(EnabledOptions)
        {
            InnerHandler = inner,
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://cp.test/platform/v1/ai/openai/v1/chat/completions?trace=1");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "provider-key");
        request.Headers.TryAddWithoutValidation("x-api-key", "provider-key");
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(sentRequest);
        Assert.NotNull(sentBody);
        Assert.Equal(requestBody, Encoding.UTF8.GetString(sentBody));
        Assert.Contains("trace=1", sentRequest!.RequestUri!.Query);
        Assert.Contains("request_id=", sentRequest.RequestUri.Query);
        Assert.False(sentRequest.Headers.Contains("Authorization"));
        Assert.False(sentRequest.Headers.Contains("x-api-key"));
        Assert.Equal(EnabledOptions.InstanceId,
            sentRequest.Headers.GetValues(PlatformLinkHmac.HeaderInstanceId).Single());

        var timestamp = sentRequest.Headers.GetValues(PlatformLinkHmac.HeaderTimestamp).Single();
        var signature = sentRequest.Headers.GetValues(PlatformLinkHmac.HeaderSignature).Single();
        var bodyHash = PlatformLinkHmac.Sha256Hex(sentBody);
        var canonical = PlatformLinkHmac.BuildCanonical(
            "POST", sentRequest.RequestUri.PathAndQuery, long.Parse(timestamp), bodyHash);
        Assert.Equal(PlatformLinkHmac.Sign(EnabledOptions.InstanceLinkSecret!, canonical), signature);
    }

    [Fact]
    public async Task SendAsync_MapsQuotaErrorToStableHostedException()
    {
        var inner = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"code\":\"hosted_ai_quota_exceeded\"}", Encoding.UTF8, "application/json"),
        }));
        using var client = new HttpClient(new PlatformLinkHostedAiSigningHandler(EnabledOptions)
        {
            InnerHandler = inner,
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://cp.test/platform/v1/ai/openai/v1/chat/completions");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var exception = await Assert.ThrowsAsync<PlatformHostedAiRequestException>(
            () => client.SendAsync(request));

        Assert.Equal("hosted_ai_quota_exceeded", exception.Code);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, exception.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenPlatformLinkIsDisabled_DoesNotSendRequest()
    {
        var inner = new StubHandler((_, _) => throw new InvalidOperationException("Unexpected network call."));
        using var client = new HttpClient(new PlatformLinkHostedAiSigningHandler(new PlatformLinkOptions())
        {
            InnerHandler = inner,
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://cp.test/platform/v1/ai/openai/v1/chat/completions");

        var exception = await Assert.ThrowsAsync<PlatformHostedAiRequestException>(
            () => client.SendAsync(request));

        Assert.Equal("hosted_ai_unavailable", exception.Code);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
