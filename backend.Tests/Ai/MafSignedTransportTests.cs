using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Delegat.Infrastructure.Configuration;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.PlatformLink;
using ScriptedHttpMessageHandler = Delegat.AiRuntime.Tests.Support.ScriptedHttpMessageHandler;

namespace Delegat.Tests;

/// <summary>
/// M3 signed transport boundary: the same real SDK adapters drive the real
/// instance-side <see cref="PlatformLinkHostedAiSigningHandler"/> over the shared fake
/// HTTP handler. Coverage asserts credential stripping, independent HMAC verification,
/// request_id injection, hosted-error code preservation and wrong-origin rejection -
/// not Platform Link enrollment, production DI/startup, a real Control Plane or a live
/// model. All secrets here are dummy unit values, not environment credentials.
/// </summary>
public partial class MafEngineParityTests
{
    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task SignedTransport_SdkRequest_ReplacesStaleSignaturesAndRemovesCredentials(
        AiModelProvider provider)
    {
        var turn = await SignedRunAsync(provider, SignedApiUrl(provider),
            [(HttpStatusCode.OK, ProviderMixedBatch(provider), "text/event-stream")],
            addStaleHeaders: true);

        Assert.Equal(1, turn.HttpCalls);
        SignedAssertSnapshot(Assert.Single(turn.Sent), provider);
        Assert.Single(turn.Events, e => e.Type == SseEventType.Plan);
        Assert.Single(turn.Events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain(turn.Events,
            e => e.Type == SseEventType.Error || e.Type == SseEventType.Tool);
        SignedAssertPrivate(turn.Events);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task SignedTransport_QuotaError_PreservesCodeWithoutProviderRetry(
        AiModelProvider provider)
    {
        var turn = await SignedRunAsync(provider, SignedApiUrl(provider),
            [(HttpStatusCode.TooManyRequests, SignedQuotaBody, "application/json")]);

        Assert.Equal(1, turn.HttpCalls);
        SignedAssertSnapshot(Assert.Single(turn.Sent), provider);
        SignedAssertError(turn, "hosted_ai_quota_exceeded", 429);
        Assert.DoesNotContain(turn.Events, e => e.Type == SseEventType.Tool);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task SignedTransport_BusyError_PreservesCodeWithoutProviderRetry(
        AiModelProvider provider)
    {
        var turn = await SignedRunAsync(provider, SignedApiUrl(provider),
            [(HttpStatusCode.TooManyRequests, SignedBusyBody, "application/json")]);

        Assert.Equal(1, turn.HttpCalls);
        SignedAssertSnapshot(Assert.Single(turn.Sent), provider);
        SignedAssertError(turn, "hosted_ai_request_in_progress", 429);
        Assert.DoesNotContain(turn.Events, e => e.Type == SseEventType.Tool);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task SignedTransport_QuotaAfterRead_DoesNotRedispatchCompletedTool(
        AiModelProvider provider)
    {
        var turn = await SignedRunAsync(provider, SignedApiUrl(provider),
            [
                (HttpStatusCode.OK,
                    ProviderReadPrefix(provider) + ProviderFinish(provider),
                    "text/event-stream"),
                (HttpStatusCode.TooManyRequests, SignedQuotaBody, "application/json"),
            ], invocations: 1);

        Assert.Equal(2, turn.HttpCalls);
        Assert.Equal(2, turn.Sent.Count);
        Assert.All(turn.Sent, request => SignedAssertSnapshot(request, provider));
        Assert.NotEqual(turn.Sent[0].Uri.Query, turn.Sent[1].Uri.Query);
        Assert.Equal("/api/products/1", turn.LastPath);
        var tools = ProviderEventsOf(turn.Events, SseEventType.Tool);
        Assert.Equal(2, tools.Count);
        Assert.All(tools,
            e => Assert.Equal("sdk-read", e.Data.GetProperty("id").GetString()));
        Assert.Equal("running", tools[0].Data.GetProperty("status").GetString());
        Assert.Equal("done", tools[1].Data.GetProperty("status").GetString());
        SignedAssertError(turn, "hosted_ai_quota_exceeded", 429);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task SignedTransport_UntrustedGatewayError_UsesSanitizedFallbackCode(
        AiModelProvider provider)
    {
        var turn = await SignedRunAsync(provider, SignedApiUrl(provider),
            [(HttpStatusCode.BadGateway,
                "{\"code\":\"untrusted_code\",\"message\":\"wire-secret\"}",
                "application/json")]);

        Assert.Equal(1, turn.HttpCalls);
        SignedAssertSnapshot(Assert.Single(turn.Sent), provider);
        SignedAssertError(turn, "hosted_ai_provider_unavailable", 502);
        Assert.DoesNotContain(turn.Events, e => e.Type == SseEventType.Tool);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task SignedTransport_ForeignOrigin_IsRejectedBeforeInnerHttp(
        AiModelProvider provider)
    {
        var turn = await SignedRunAsync(
            provider, SignedApiUrl(provider, "https://foreign.invalid"), []);

        Assert.Equal(0, turn.HttpCalls);
        Assert.Empty(turn.Sent);
        SignedAssertError(turn, "hosted_ai_unavailable", 503);
        Assert.DoesNotContain(turn.Events, e => e.Type == SseEventType.Tool);
    }

    // ------------------------------------------------------------------
    // Signed transport fixtures: the real signing handler between the real
    // SDK adapter and the shared no-network HTTP fake.
    // ------------------------------------------------------------------

    private const string SignedInstanceId = "inst_0123456789abcdef0123456789abcdef";
    private const string SignedSecret = "signed-link-test-secret";
    private const string SignedQuotaBody =
        "{\"code\":\"hosted_ai_quota_exceeded\",\"message\":\"wire-secret\"}";
    private const string SignedBusyBody =
        "{\"code\":\"hosted_ai_request_in_progress\",\"message\":\"wire-secret\"}";

    private sealed record SignedSnapshot(
        string Method, Uri Uri, Dictionary<string, string[]> Headers, byte[] Body);

    private sealed record SignedTurn(
        List<AiTurnEvent> Events, List<SignedSnapshot> Sent, int HttpCalls,
        string? LastPath);

    private static string SignedApiUrl(AiModelProvider provider,
        string origin = "https://cp.test") =>
        origin + "/platform/v1/ai/"
            + (provider == AiModelProvider.Anthropic ? "anthropic" : "openai");

    private static async Task<SignedTurn> SignedRunAsync(AiModelProvider provider,
        string apiUrl, (HttpStatusCode Status, string Body, string MediaType)[] replies,
        int invocations = 0, bool addStaleHeaders = false)
    {
        using var key = SigningKey();
        using var wire = new ScriptedHttpMessageHandler();
        var sent = new List<SignedSnapshot>();
        foreach (var reply in replies)
        {
            wire.Enqueue(async (request, ct) =>
            {
                sent.Add(new SignedSnapshot(request.Method.Method, request.RequestUri!,
                    request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(),
                        StringComparer.OrdinalIgnoreCase),
                    await request.Content!.ReadAsByteArrayAsync(ct)));
                return new HttpResponseMessage(reply.Status)
                {
                    RequestMessage = request,
                    Content = new StringContent(reply.Body, Encoding.UTF8, reply.MediaType),
                };
            });
        }
        using var http = new HttpClient(new PlatformLinkHostedAiSigningHandler(
            new PlatformLinkOptions
            {
                ControlPlaneUrl = "https://cp.test",
                InstanceId = SignedInstanceId,
                InstanceLinkSecret = SignedSecret,
            })
        { InnerHandler = wire });
        if (addStaleHeaders)
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                PlatformLinkHmac.HeaderInstanceId, "stale-instance");
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                PlatformLinkHmac.HeaderTimestamp, "0");
            http.DefaultRequestHeaders.TryAddWithoutValidation(
                PlatformLinkHmac.HeaderSignature, "stale-signature");
        }
        using var sdk = ProviderSdkClient(provider, http, apiUrl);
        await using var fixture = await ProviderFixtureAsync(provider, sdk);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: "sdk-signed-transport", locale: "en", ct: deadline.Token));
        ProviderAssertBoundary(fixture, invocations);
        return new SignedTurn(events, sent, wire.RequestCount, fixture.Invoker.LastPath);
    }

    /// <summary>
    /// Independent verifier: recomputes the expected signature from the captured
    /// request with a plain HMAC-SHA256 - deliberately not the production helper, so
    /// the same code is not verifying itself.
    /// </summary>
    private static void SignedAssertSnapshot(SignedSnapshot sent, AiModelProvider provider)
    {
        Assert.Equal("POST", sent.Method);
        Assert.Equal("cp.test", sent.Uri.Host);
        Assert.Equal(provider == AiModelProvider.Anthropic
            ? "/platform/v1/ai/anthropic/v1/messages"
            : "/platform/v1/ai/openai/v1/chat/completions", sent.Uri.AbsolutePath);
        Assert.False(sent.Headers.ContainsKey("Authorization"));
        Assert.False(sent.Headers.ContainsKey("x-api-key"));
        Assert.Equal(SignedInstanceId,
            Assert.Single(sent.Headers[PlatformLinkHmac.HeaderInstanceId]));
        var timestamp = Assert.Single(sent.Headers[PlatformLinkHmac.HeaderTimestamp]);
        Assert.True(long.TryParse(timestamp, NumberStyles.None,
            CultureInfo.InvariantCulture, out var seconds));
        Assert.True(seconds > 0);
        var requestId = Assert.Single(sent.Uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)),
            pair => pair[0] == "request_id");
        Assert.Equal(2, requestId.Length);
        Assert.True(Guid.TryParseExact(requestId[1], "D", out _));
        var bodyHash = Convert.ToHexString(SHA256.HashData(sent.Body)).ToLowerInvariant();
        var canonical = $"POST\n{sent.Uri.PathAndQuery}\n{timestamp}\n{bodyHash}";
        var expected = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SignedSecret),
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Assert.Equal(expected,
            Assert.Single(sent.Headers[PlatformLinkHmac.HeaderSignature]));
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal("scripted-model", body.RootElement.GetProperty("model").GetString());
    }

    private static void SignedAssertPrivate(IReadOnlyList<AiTurnEvent> events)
    {
        var wire = string.Concat(events.Select(e => e.ToSse()));
        foreach (var secret in new[]
        {
            "wire-secret", "test-key", SignedSecret, "stale-signature", "cp.test",
            "foreign.invalid",
        })
            Assert.DoesNotContain(secret, wire, StringComparison.Ordinal);
    }

    private static void SignedAssertError(SignedTurn turn, string code, int status)
    {
        var error = Assert.Single(turn.Events, e => e.Type == SseEventType.Error);
        Assert.Equal(code, error.Data.GetProperty("code").GetString());
        Assert.Equal(status, error.Data.GetProperty("status").GetInt32());
        Assert.Single(turn.Events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain(turn.Events, e => e.Type == SseEventType.Plan);
        SignedAssertPrivate(turn.Events);
    }
}
