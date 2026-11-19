using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Delegat.Services.PlatformLink;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

/// <summary>
/// M3 BYOK/proxy transport boundary: the production AiApiClient builds the real SDK
/// adapters with isPlatformHosted=false, so bytes cross real loopback HTTP into owned
/// fixture servers. Coverage proves direct endpoints keep provider credentials and
/// custom path prefixes, an explicit HTTP proxy receives the request instead of the
/// origin (the origin trap catches any direct bypass), a 407 challenge is answered
/// with the decoded proxy userinfo, and a rejected proxy credential surfaces a
/// sanitized provider error. This does not exercise TLS/HTTPS CONNECT, SOCKS5 or
/// system proxy configuration, Platform Link signing, live providers or model
/// adherence; all secrets are dummy unit values.
/// </summary>
public partial class MafEngineParityTests
{
    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ByokTransport_DirectFullEndpoint_PreservesProviderCredentialsAndPrefix(
        AiModelProvider provider)
    {
        await using var origin = await ByokServer.StartAsync(_ => ByokSuccess(provider));
        var endpoint = ByokEndpoint(provider, origin.Address);
        var events = await ByokRunAsync(provider, endpoint);

        var request = Assert.Single(origin.Requests);
        ByokAssertRequest(request, provider, endpoint);
        Assert.Equal(ByokPath(provider), request.RawTarget);
        Assert.False(request.Headers.ContainsKey("Proxy-Authorization"));
        ByokAssertPlan(events);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ByokTransport_HttpProxy_RoutesOnlyToProxy(AiModelProvider provider)
    {
        await using var origin = await ByokServer.StartAsync(_ => ByokTrap());
        await using var proxy = await ByokServer.StartAsync(_ => ByokSuccess(provider));
        var endpoint = ByokEndpoint(provider, origin.Address);
        var events = await ByokRunAsync(provider, endpoint, proxy.Address.AbsoluteUri);

        Assert.Empty(origin.Requests);
        var request = Assert.Single(proxy.Requests);
        Assert.Equal(endpoint, request.RawTarget);
        Assert.False(request.Headers.ContainsKey("Proxy-Authorization"));
        ByokAssertRequest(request, provider, endpoint);
        ByokAssertPlan(events);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ByokTransport_ProxyCredentials_AnswersChallengeWithDecodedUserInfo(
        AiModelProvider provider)
    {
        var expected = ByokBasic(ByokProxyPassword);
        await using var origin = await ByokServer.StartAsync(_ => ByokTrap());
        await using var proxy = await ByokServer.StartAsync(request =>
            request.Headers.TryGetValue("Proxy-Authorization", out var values)
            && values.SequenceEqual(new[] { expected })
                ? ByokSuccess(provider) : ByokChallenge());
        var endpoint = ByokEndpoint(provider, origin.Address);
        var events = await ByokRunAsync(provider, endpoint,
            ByokProxyUrl(proxy.Address, ByokProxyPassword));

        Assert.Empty(origin.Requests);
        var requests = proxy.Requests;
        Assert.Equal(2, requests.Length);
        Assert.False(requests[0].Headers.ContainsKey("Proxy-Authorization"));
        Assert.Equal(new[] { expected }, requests[1].Headers["Proxy-Authorization"]);
        Assert.All(requests, request =>
        {
            Assert.Equal(endpoint, request.RawTarget);
            ByokAssertRequest(request, provider, endpoint);
        });
        ByokAssertPlan(events);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ByokTransport_ProxyRejectsCredentials_IsSanitizedWithoutToolDispatch(
        AiModelProvider provider)
    {
        await using var origin = await ByokServer.StartAsync(_ => ByokTrap());
        await using var proxy = await ByokServer.StartAsync(_ => ByokChallenge());
        var endpoint = ByokEndpoint(provider, origin.Address);
        var events = await ByokRunAsync(provider, endpoint,
            ByokProxyUrl(proxy.Address, ByokBadPassword));

        Assert.Empty(origin.Requests);
        var requests = proxy.Requests;
        Assert.Equal(2, requests.Length);
        Assert.False(requests[0].Headers.ContainsKey("Proxy-Authorization"));
        Assert.Equal(new[] { ByokBasic(ByokBadPassword) },
            requests[1].Headers["Proxy-Authorization"]);
        Assert.All(requests, request =>
        {
            Assert.Equal(endpoint, request.RawTarget);
            ByokAssertRequest(request, provider, endpoint);
        });
        ByokAssertError(events);
    }

    // ------------------------------------------------------------------
    // Owned loopback fixtures: Kestrel on 127.0.0.1 port 0 serves as the
    // fake origin and fake proxy; every listener is disposed by the test.
    // ------------------------------------------------------------------

    private const string ByokApiSecret = "byok-api-secret";
    private const string ByokProxyUser = "proxy user";
    private const string ByokProxyPassword = "p@ss:word";
    private const string ByokBadPassword = "wrong-pass";

    private sealed record ByokRequest(string Method, string RawTarget, string Path,
        string Host, Dictionary<string, string[]> Headers, string Body);

    private sealed record ByokReply(int Status, string Body,
        string ContentType = "application/json", bool Challenge = false);

    private static string ByokPath(AiModelProvider p) => p == AiModelProvider.Anthropic
        ? "/fixture/anthropic/v1/messages" : "/fixture/openai/v1/chat/completions";

    private static string ByokEndpoint(AiModelProvider p, Uri origin) =>
        origin.GetLeftPart(UriPartial.Authority) + ByokPath(p);

    private static string ByokProxyUrl(Uri proxy, string password) =>
        $"http://{Uri.EscapeDataString(ByokProxyUser)}:{Uri.EscapeDataString(password)}@{proxy.Authority}";

    private static string ByokBasic(string password) => "Basic "
        + Convert.ToBase64String(Encoding.UTF8.GetBytes(ByokProxyUser + ":" + password));

    private static ByokReply ByokTrap() =>
        new(418, "{\"error\":{\"message\":\"origin-trap\"}}");

    private static ByokReply ByokChallenge() =>
        new(407, "{\"error\":{\"message\":\"proxy-wire-secret\"}}", Challenge: true);

    private static ByokReply ByokSuccess(AiModelProvider p) =>
        new(200, ProviderMixedBatch(p), "text/event-stream");

    private sealed class ByokServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<ByokRequest> _requests;

        public Uri Address { get; }
        public ByokRequest[] Requests => _requests.ToArray();

        private ByokServer(WebApplication app, ConcurrentQueue<ByokRequest> requests,
            Uri address)
        {
            _app = app;
            _requests = requests;
            Address = address;
        }

        public static async Task<ByokServer> StartAsync(Func<ByokRequest, ByokReply> respond)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing",
                ApplicationName = typeof(MafEngineParityTests).Assembly.GetName().Name,
            });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0,
                    listen => listen.Protocols = HttpProtocols.Http1);
                options.Limits.MaxRequestBodySize = 1_048_576;
            });
            var app = builder.Build();
            var requests = new ConcurrentQueue<ByokRequest>();
            app.Run(async context =>
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false, bufferSize: 1024,
                    leaveOpen: true);
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                var captured = new ByokRequest(context.Request.Method,
                    context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty,
                    context.Request.Path.Value ?? string.Empty,
                    context.Request.Host.Value,
                    context.Request.Headers.ToDictionary(h => h.Key,
                        h => h.Value.Select(v => v ?? string.Empty).ToArray(),
                        StringComparer.OrdinalIgnoreCase), body);
                requests.Enqueue(captured);
                var reply = respond(captured);
                context.Response.StatusCode = reply.Status;
                context.Response.ContentType = reply.ContentType;
                context.Response.ContentLength = Encoding.UTF8.GetByteCount(reply.Body);
                if (reply.Challenge)
                    context.Response.Headers["Proxy-Authenticate"] = "Basic realm=\"fixture\"";
                await context.Response.WriteAsync(reply.Body, context.RequestAborted);
            });
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await app.StartAsync(deadline.Token);
                var address = new Uri(app.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.Single());
                return new ByokServer(app, requests, address);
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _app.StopAsync(deadline.Token);
            }
            finally
            {
                await _app.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Clears the per-process default proxy for the scoped run so no ambient
    /// system proxy can intercept loopback; EnvVars excludes concurrent tests.
    /// </summary>
    private sealed class ByokNoSystemProxy : IDisposable
    {
        private readonly IWebProxy _previous = HttpClient.DefaultProxy;
        public ByokNoSystemProxy() => HttpClient.DefaultProxy = new WebProxy();
        public void Dispose() => HttpClient.DefaultProxy = _previous;
    }

    private sealed class ByokHostedFactory : IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Calls++;
            throw new InvalidOperationException(
                "BYOK must not resolve hosted transport: " + name);
        }
    }

    private sealed class ByokTypedHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException(
                "BYOK must not use the typed DI transport");
        }
    }

    private static async Task<List<AiTurnEvent>> ByokRunAsync(
        AiModelProvider provider, string endpoint, string? proxyUrl = null)
    {
        Assert.True(new Uri(endpoint).IsLoopback);
        Assert.Equal("http", new Uri(endpoint).Scheme);
        if (proxyUrl != null)
        {
            Assert.True(new Uri(proxyUrl).IsLoopback);
            Assert.Equal("http", new Uri(proxyUrl).Scheme);
        }
        // Only this owned test process changes; the EnvVars collection excludes
        // other tests.
        using var noSystemProxy = new ByokNoSystemProxy();
        using var key = SigningKey();
        using var typed = new ByokTypedHandler();
        using var http = new HttpClient(typed, disposeHandler: false);
        var hosted = new ByokHostedFactory();
        var api = new AiApiClient(http, NullLogger<AiApiClient>.Instance, hosted);
        using var sdk = api.CreateChatClient(new AiModelConfig
        {
            Id = 1,
            name = "scripted",
            provider = provider,
            modelId = "scripted-model",
            apiUrl = endpoint,
            apiKeyEncrypted = ByokApiSecret,
            maxTokens = 1024,
            isEnabled = true,
            isDefault = true,
            isPlatformHosted = false,
            proxyUrl = proxyUrl,
        });
        await using var fixture = await ProviderFixtureAsync(provider, sdk);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: "sdk-byok-transport", locale: "en", ct: deadline.Token));
        ProviderAssertBoundary(fixture);
        Assert.Equal(0, hosted.Calls);
        Assert.Equal(0, typed.Calls);
        return events;
    }

    private static void ByokAssertRequest(
        ByokRequest request, AiModelProvider provider, string endpoint)
    {
        Assert.Equal("POST", request.Method);
        Assert.Equal(ByokPath(provider), request.Path);
        Assert.Equal(new Uri(endpoint).Authority, request.Host);
        var authorization = request.Headers.GetValueOrDefault("Authorization") ?? [];
        var apiKey = request.Headers.GetValueOrDefault("x-api-key") ?? [];
        if (provider == AiModelProvider.OpenAiCompatible)
            Assert.Equal(new[] { "Bearer " + ByokApiSecret }, authorization);
        else
            Assert.True(apiKey.SequenceEqual(new[] { ByokApiSecret })
                || authorization.SequenceEqual(new[] { "Bearer " + ByokApiSecret }));
        Assert.False(request.Headers.ContainsKey(PlatformLinkHmac.HeaderInstanceId));
        Assert.False(request.Headers.ContainsKey(PlatformLinkHmac.HeaderTimestamp));
        Assert.False(request.Headers.ContainsKey(PlatformLinkHmac.HeaderSignature));
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("scripted-model",
            body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(body.RootElement.GetProperty("tools").GetArrayLength() > 0);
    }

    private static void ByokAssertPrivate(IReadOnlyList<AiTurnEvent> events)
    {
        var wire = string.Concat(events.Select(e => e.ToSse()));
        foreach (var secret in new[]
        {
            ByokApiSecret, ByokProxyUser, ByokProxyPassword, ByokBadPassword,
            "proxy-wire-secret", "127.0.0.1",
        })
            Assert.DoesNotContain(secret, wire, StringComparison.Ordinal);
    }

    private static void ByokAssertPlan(List<AiTurnEvent> events)
    {
        var plan = Assert.Single(events, e => e.Type == SseEventType.Plan);
        Assert.Equal("Update one fixture product",
            plan.Data.GetProperty("summary").GetString());
        Assert.False(string.IsNullOrEmpty(plan.Data.GetProperty("proposalId").GetString()));
        Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain(events,
            e => e.Type == SseEventType.Error || e.Type == SseEventType.Tool);
        ByokAssertPrivate(events);
    }

    private static void ByokAssertError(List<AiTurnEvent> events)
    {
        var error = Assert.Single(events, e => e.Type == SseEventType.Error);
        Assert.Equal(AgentToolResult.ProviderErrorMessage,
            error.Data.GetProperty("message").GetString());
        Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain(events,
            e => e.Type == SseEventType.Plan || e.Type == SseEventType.Tool);
        ByokAssertPrivate(events);
    }
}
