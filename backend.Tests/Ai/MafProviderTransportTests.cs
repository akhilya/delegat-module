using System.Net;
using System.Text.Json;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ScriptedHttpMessageHandler = Delegat.AiRuntime.Tests.Support.ScriptedHttpMessageHandler;

namespace Delegat.Tests;

/// <summary>
/// M3 transport boundary: the production <see cref="AiApiClient.CreateChatClient"/> builds
/// real OpenAI/Anthropic SDK adapters over the hosted custom-HTTP branch, so every byte
/// terminates in the scripted handler while the resulting IChatClient drives the real MAF
/// engine, turn gate and tool adapters. This is not Platform Link enrollment/signing,
/// production DI, BYOK/proxy, a live provider or model-adherence evaluation.
/// </summary>
public partial class MafEngineParityTests
{
    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ProviderTransport_FragmentedMixedBatch_PresentsSignedPlanWithoutDispatch(
        AiModelProvider provider)
    {
        using var key = SigningKey();
        using var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueSse(ProviderMixedBatch(provider));
        using var http = new HttpClient(handler, disposeHandler: false);
        using var sdk = ProviderSdkClient(provider, http);
        await using var fixture = await ProviderFixtureAsync(provider, sdk);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: $"sdk-transport-mixed-{provider}", locale: "en"));

        ProviderAssertBoundary(fixture);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(ProviderExpectedUrl(provider), Assert.Single(handler.RequestUrls));

        var plan = Assert.Single(events, e => e.Type == SseEventType.Plan);
        Assert.Equal("Update one fixture product",
            plan.Data.GetProperty("summary").GetString());
        Assert.False(string.IsNullOrEmpty(plan.Data.GetProperty("proposalId").GetString()));
        Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Error);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Tool);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ProviderTransport_FirstHttpFailure_IsSanitizedAndDoesNotDispatch(
        AiModelProvider provider)
    {
        using var key = SigningKey();
        using var handler = new ScriptedHttpMessageHandler();
        // 401 is non-transient for both SDKs; no SDK retry-policy change is made.
        handler.EnqueueStatus(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"wire-secret\",\"type\":\"authentication_error\"}}");
        using var http = new HttpClient(handler, disposeHandler: false);
        using var sdk = ProviderSdkClient(provider, http);
        await using var fixture = await ProviderFixtureAsync(provider, sdk);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: $"sdk-transport-first-fail-{provider}", locale: "en",
            ct: deadline.Token));

        ProviderAssertBoundary(fixture);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Tool);
        var error = Assert.Single(events, e => e.Type == SseEventType.Error);
        Assert.Equal(AgentToolResult.ProviderErrorMessage,
            error.Data.GetProperty("message").GetString());
        Assert.Single(events, e => e.Type == SseEventType.Done);
        var wire = string.Concat(events.Select(e => e.ToSse()));
        Assert.DoesNotContain("wire-secret", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("api.example.test", wire, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ProviderTransport_LaterHttpFailure_PreservesCompletedReadAndToolResult(
        AiModelProvider provider)
    {
        using var key = SigningKey();
        using var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueSse(ProviderReadPrefix(provider) + ProviderFinish(provider));
        handler.EnqueueStatus(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"wire-secret\",\"type\":\"authentication_error\"}}");
        using var http = new HttpClient(handler, disposeHandler: false);
        using var sdk = ProviderSdkClient(provider, http);
        await using var fixture = await ProviderFixtureAsync(provider, sdk);

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: $"sdk-transport-later-fail-{provider}", locale: "en"));

        ProviderAssertBoundary(fixture, invocations: 1);
        Assert.Equal(2, handler.RequestCount);
        Assert.All(handler.RequestUrls,
            url => Assert.Equal(ProviderExpectedUrl(provider), url));
        Assert.Equal("/api/products/1", fixture.Invoker.LastPath);

        var tools = ProviderEventsOf(events, SseEventType.Tool);
        Assert.Equal(2, tools.Count);
        Assert.All(tools,
            e => Assert.Equal("sdk-read", e.Data.GetProperty("id").GetString()));
        Assert.Equal("running", tools[0].Data.GetProperty("status").GetString());
        Assert.Equal("done", tools[1].Data.GetProperty("status").GetString());
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        var error = Assert.Single(events, e => e.Type == SseEventType.Error);
        Assert.Equal(AgentToolResult.TurnInterruptedMessage,
            error.Data.GetProperty("message").GetString());
        Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain("wire-secret",
            string.Concat(events.Select(e => e.ToSse())), StringComparison.Ordinal);

        // The SDK's own follow-up request carries the paired call and result.
        using var wire = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = wire.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        if (provider == AiModelProvider.Anthropic)
        {
            var blocks = messages.SelectMany(ProviderContentBlocks).ToArray();
            var toolResult = Assert.Single(blocks,
                b => b.GetProperty("type").GetString() == "tool_result");
            Assert.Equal("sdk-read",
                toolResult.GetProperty("tool_use_id").GetString());
            Assert.Contains("ok", ProviderContentText(toolResult.GetProperty("content")),
                StringComparison.Ordinal);
            var toolUse = Assert.Single(blocks,
                b => b.GetProperty("type").GetString() == "tool_use");
            Assert.Equal("sdk-read", toolUse.GetProperty("id").GetString());
        }
        else
        {
            var toolMessage = Assert.Single(messages,
                m => m.GetProperty("role").GetString() == "tool"
                    && m.GetProperty("tool_call_id").GetString() == "sdk-read");
            Assert.Contains("ok", ProviderContentText(toolMessage.GetProperty("content")),
                StringComparison.Ordinal);
            var assistant = Assert.Single(messages,
                m => m.GetProperty("role").GetString() == "assistant"
                    && m.TryGetProperty("tool_calls", out _));
            var call = Assert.Single(
                assistant.GetProperty("tool_calls").EnumerateArray());
            Assert.Equal("sdk-read", call.GetProperty("id").GetString());
        }
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ProviderTransport_CancelMidRound_NeverDispatchesParsedCall(
        AiModelProvider provider)
    {
        using var key = SigningKey();
        using var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueHangingMidStream(
            ProviderReadPrefix(provider), TimeSpan.FromMinutes(1));
        using var http = new HttpClient(handler, disposeHandler: false);
        using var sdk = ProviderSdkClient(provider, http);
        await using var fixture = await ProviderFixtureAsync(provider, sdk);

        using var cts = new CancellationTokenSource();
        var observed = new List<AiTurnEvent>();
        var task = ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: $"sdk-transport-cancel-{provider}", locale: "en", ct: cts.Token),
            observed);
        try
        {
            // The SDK must be genuinely mid-body (whole prefix consumed, blocked
            // on the next read) before cancellation - no wall-clock guessing.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!handler.MidStreamPrefixFullyRead && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.True(handler.MidStreamPrefixFullyRead,
                "The SDK SSE parser never consumed the complete tool-call prefix.");
            Assert.Equal(0, fixture.Invoker.Calls);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            cts.Cancel();
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // The outcome was already asserted above; this drains the task.
            }
        }

        ProviderAssertBoundary(fixture);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(observed, e => e.Type == SseEventType.Plan);
        Assert.DoesNotContain(observed, e => e.Type == SseEventType.Tool);
        Assert.DoesNotContain(observed, e => e.Type == SseEventType.Error);
        Assert.DoesNotContain(observed, e => e.Type == SseEventType.Done);
    }

    [Theory]
    [InlineData(AiModelProvider.OpenAiCompatible)]
    [InlineData(AiModelProvider.Anthropic)]
    public async Task ProviderTransport_MalformedTail_DiscardsEarlierParsedCall(
        AiModelProvider provider)
    {
        using var key = SigningKey();
        using var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueSse(ProviderReadPrefix(provider) + ProviderMalformedTail(provider));
        using var http = new HttpClient(handler, disposeHandler: false);
        using var sdk = ProviderSdkClient(provider, http);
        await using var fixture = await ProviderFixtureAsync(provider, sdk);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var events = await ProviderCollectAsync(fixture.Chat.StreamChatEventsAsync(
            [UserMessage("Prepare a catalog update.")], fixture.StoreId, fixture.UserId,
            sessionId: $"sdk-transport-malformed-{provider}", locale: "en",
            ct: deadline.Token));

        ProviderAssertBoundary(fixture);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Plan);
        Assert.DoesNotContain(events, e => e.Type == SseEventType.Tool);
        var error = Assert.Single(events, e => e.Type == SseEventType.Error);
        Assert.Equal(AgentToolResult.ProviderErrorMessage,
            error.Data.GetProperty("message").GetString());
        Assert.Single(events, e => e.Type == SseEventType.Done);
        Assert.DoesNotContain("wire-secret",
            string.Concat(events.Select(e => e.ToSse())), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Provider transport fixtures: hand-authored wire bytes for both SDK
    // parsers. These are transport fixtures, not graders or live evals.
    // ------------------------------------------------------------------

    private const string ProviderReadArgs =
        "{\"method\":\"GET\",\"path\":\"/products/1\"}";
    private const string ProviderPlanArgs =
        "{\"summary\":\"Update one fixture product\",\"steps\":[{\"description\":\"Update one fixture product\",\"tool\":\"platform_request\",\"args\":{\"method\":\"PATCH\",\"path\":\"/products/1\",\"body\":{\"name\":\"Fixture update\"}}}]}";

    private static string ProviderOpenAiChunk(object delta, string? finish = null) =>
        "data: " + JsonSerializer.Serialize(new
        {
            id = "chatcmpl-sdk",
            @object = "chat.completion.chunk",
            created = 1730000000,
            model = "scripted-model",
            choices = new[] { new { index = 0, delta, finish_reason = finish } },
        }) + "\n\n";

    private static string ProviderAnthropicEvent(string type, object data) =>
        "event: " + type + "\ndata: " + JsonSerializer.Serialize(data) + "\n\n";

    private static string ProviderAnthropicStart() =>
        ProviderAnthropicEvent("message_start", new
        {
            type = "message_start",
            message = new
            {
                id = "msg_sdk",
                type = "message",
                role = "assistant",
                model = "scripted-model",
                content = Array.Empty<object>(),
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = new { input_tokens = 9, output_tokens = 1 },
            },
        });

    private static string[] ProviderFragments(string text) =>
        [text[..(text.Length / 3)], text[(text.Length / 3)..(2 * text.Length / 3)],
            text[(2 * text.Length / 3)..]];

    private static string ProviderOpenAiCallStart(int index, string id, string name) =>
        ProviderOpenAiChunk(new
        {
            tool_calls = new[]
            {
                new { index, id, type = "function", function = new { name, arguments = "" } },
            },
        });

    private static string ProviderOpenAiArgument(int index, string fragment) =>
        ProviderOpenAiChunk(new
        {
            tool_calls = new[] { new { index, function = new { arguments = fragment } } },
        });

    private static string ProviderAnthropicCall(int index, string id, string name, string arguments)
    {
        var wire = ProviderAnthropicEvent("content_block_start", new
        {
            type = "content_block_start",
            index,
            content_block = new { type = "tool_use", id, name, input = new { } },
        });
        foreach (var fragment in ProviderFragments(arguments))
            wire += ProviderAnthropicEvent("content_block_delta", new
            {
                type = "content_block_delta",
                index,
                delta = new { type = "input_json_delta", partial_json = fragment },
            });
        return wire + ProviderAnthropicEvent("content_block_stop",
            new { type = "content_block_stop", index });
    }

    private static string ProviderReadPrefix(AiModelProvider provider) =>
        provider == AiModelProvider.Anthropic
            ? ProviderAnthropicStart()
                + ProviderAnthropicCall(0, "sdk-read", "platform_request", ProviderReadArgs)
            : ProviderOpenAiChunk(new { role = "assistant" })
                + ProviderOpenAiCallStart(0, "sdk-read", "platform_request")
                + ProviderOpenAiArgument(0, ProviderReadArgs);

    private static string ProviderFinish(AiModelProvider provider) =>
        provider == AiModelProvider.Anthropic
            ? ProviderAnthropicEvent("message_delta", new
            {
                type = "message_delta",
                delta = new { stop_reason = "tool_use", stop_sequence = (string?)null },
                usage = new { output_tokens = 5 },
            }) + ProviderAnthropicEvent("message_stop", new { type = "message_stop" })
            : ProviderOpenAiChunk(new { }, "tool_calls") + "data: [DONE]\n\n";

    private static string ProviderMixedBatch(AiModelProvider provider)
    {
        if (provider == AiModelProvider.Anthropic)
            return ProviderAnthropicStart()
                + ProviderAnthropicCall(0, "sdk-read", "platform_request", ProviderReadArgs)
                + ProviderAnthropicCall(1, "sdk-plan", "propose_plan", ProviderPlanArgs)
                + ProviderFinish(provider);
        var wire = ProviderOpenAiChunk(new { role = "assistant" })
            + ProviderOpenAiCallStart(0, "sdk-read", "platform_request")
            + ProviderOpenAiCallStart(1, "sdk-plan", "propose_plan");
        var read = ProviderFragments(ProviderReadArgs);
        var plan = ProviderFragments(ProviderPlanArgs);
        for (var i = 0; i < 3; i++)
            wire += ProviderOpenAiArgument(0, read[i]) + ProviderOpenAiArgument(1, plan[i]);
        return wire + ProviderFinish(provider);
    }

    private static string ProviderMalformedTail(AiModelProvider provider) =>
        provider == AiModelProvider.Anthropic
            ? "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":1,\"delta\":wire-secret}\n\n"
            : "data: {\"id\":\"sdk-bad\",\"choices\":[{\"index\":0,\"delta\":wire-secret}]}\n\n";

    // ------------------------------------------------------------------
    // Host seams: the real production adapter over the hosted custom-HTTP
    // branch; every byte still terminates in the scripted handler.
    // ------------------------------------------------------------------

    private static IChatClient ProviderSdkClient(AiModelProvider provider, HttpClient http)
    {
        var api = new AiApiClient(http, NullLogger<AiApiClient>.Instance,
            new ProviderHttpFactory(http));
        return api.CreateChatClient(new AiModelConfig
        {
            Id = 1,
            name = "scripted",
            provider = provider,
            modelId = "scripted-model",
            apiUrl = "https://api.example.test/v1",
            apiKeyEncrypted = "test-key",
            maxTokens = 1024,
            isEnabled = true,
            isDefault = true,
            isPlatformHosted = true,
        });
    }

    private sealed class ProviderHttpFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal("platform-link-hosted-ai", name);
            return http;
        }
    }

    private static Task<ChatFixture> ProviderFixtureAsync(
        AiModelProvider provider, IChatClient sdk) =>
        CreateFixtureCoreAsync(new EmptyMcpGateway(), null, null, null, [],
            providerClient: sdk, modelProvider: provider);

    private static async Task<List<AiTurnEvent>> ProviderCollectAsync(
        IAsyncEnumerable<AiTurnEvent> stream)
    {
        var events = new List<AiTurnEvent>();
        await ProviderCollectAsync(stream, events);
        return events;
    }

    private static async Task ProviderCollectAsync(
        IAsyncEnumerable<AiTurnEvent> stream, List<AiTurnEvent> into)
    {
        await foreach (var item in stream)
            into.Add(item);
    }

    /// <summary>
    /// One engine client resolution, the scripted IChatClient never invoked,
    /// and the recorded native side effect count - no catches or fallback values.
    /// </summary>
    private static void ProviderAssertBoundary(ChatFixture fixture, int invocations = 0)
    {
        Assert.Equal(1, fixture.Factory.Calls);
        Assert.Empty(fixture.Client.Requests);
        Assert.Equal(invocations, fixture.Invoker.Calls);
        Assert.Equal(0, fixture.Mcp.Calls);
    }

    private static List<AiTurnEvent> ProviderEventsOf(
        IEnumerable<AiTurnEvent> events, string type) =>
        events.Where(e => e.Type == type).ToList();

    private static string ProviderExpectedUrl(AiModelProvider provider) =>
        provider == AiModelProvider.Anthropic
            ? "https://api.example.test/v1/messages"
            : "https://api.example.test/v1/chat/completions";

    private static IEnumerable<JsonElement> ProviderContentBlocks(JsonElement message) =>
        message.TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string ProviderContentText(JsonElement content) =>
        content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : content.GetRawText();
}
