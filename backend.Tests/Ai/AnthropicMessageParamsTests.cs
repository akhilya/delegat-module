using System.Net;
using System.Text;
using System.Text.Json;
using Delegat.Models.Ai;
using Delegat.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests.Ai;

[Collection("EnvVars")]
public class AnthropicMessageParamsTests
{
    [Fact]
    public void BuildAnthropicParams_PreservesUserAndAssistantRoles()
    {
        var client = new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance);
        var request = new AiRequest
        {
            model = "test-model",
            messages =
            [
                new AiMessage { role = "user", content = "Hello" },
                new AiMessage { role = "assistant", content = "Hi" },
            ],
        };

        var parameters = client.BuildAnthropicParams(request);

        Assert.True(parameters.Messages[0].TryPickUser(out var user));
        Assert.True(user.Content.TryPickString(out var userText));
        Assert.Equal("Hello", userText);
        Assert.True(parameters.Messages[1].TryPickAssistant(out var assistant));
        Assert.True(assistant.Content.TryPickString(out var assistantText));
        Assert.Equal("Hi", assistantText);
    }

    [Fact]
    public async Task BuildAnthropicClient_PreservesDeepSeekBasePathInMessagesRequest()
    {
        var handler = new CapturingHandler();
        var client = new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance);
        var anthropicClient = client.BuildAnthropicClient(new AiModelConfig
        {
            apiUrl = "https://api.deepseek.com/anthropic",
            apiKeyEncrypted = "test-key",
            modelId = "deepseek-flash",
        }, new HttpClient(handler));
        var parameters = client.BuildAnthropicParams(new AiRequest
        {
            model = "deepseek-flash",
            max_tokens = 16,
            messages = [new AiMessage { role = "user", content = "Hello" }],
        });

        await anthropicClient.Messages.Create(parameters);

        Assert.Equal("https://api.deepseek.com/anthropic/v1/messages", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task BuildAnthropicParams_OptInCachesStableToolPrefixForFiveMinutes()
    {
        const string setting = "AI_ANTHROPIC_PROMPT_CACHE";
        const string endpointSetting = "AI_ANTHROPIC_PROMPT_CACHE_ENDPOINT";
        var original = Environment.GetEnvironmentVariable(setting);
        var originalEndpoint = Environment.GetEnvironmentVariable(endpointSetting);
        try
        {
            var client = new AiApiClient(new HttpClient(), NullLogger<AiApiClient>.Instance);
            var model = new AiModelConfig
            {
                apiUrl = "https://anthropic.test/v1/messages",
                modelId = "test-model",
            };
            var request = new AiRequest
            {
                model = "test-model",
                system = "Stable system instructions",
                enable_prompt_cache = true,
                messages = [new AiMessage { role = "user", content = "current request" }],
                tools =
                [
                    new AiTool { name = "first", input_schema = new AiToolInputSchema() },
                    new AiTool { name = "last", input_schema = new AiToolInputSchema() },
                ],
            };

            Environment.SetEnvironmentVariable(setting, "false");
            Environment.SetEnvironmentVariable(endpointSetting, "https://anthropic.test");
            var disabled = client.BuildAnthropicParams(request, model);
            Assert.True(disabled.Tools![1].TryPickTool(out var uncachedTool));
            Assert.Null(uncachedTool.CacheControl);

            Environment.SetEnvironmentVariable(setting, "true");
            var enabled = client.BuildAnthropicParams(request, model);
            Assert.True(enabled.Tools![0].TryPickTool(out var firstTool));
            Assert.Null(firstTool.CacheControl);
            Assert.True(enabled.Tools[1].TryPickTool(out var cachedTool));
            Assert.Equal(Anthropic.Models.Messages.Ttl.Ttl5m, cachedTool.CacheControl?.Ttl?.Value());

            var handler = new CapturingHandler();
            var anthropicClient = new Anthropic.AnthropicClient
            {
                ApiKey = "test-key",
                BaseUrl = "https://anthropic.test",
                HttpClient = new HttpClient(handler),
            };
            await anthropicClient.Messages.Create(enabled);

            using var wireRequest = JsonDocument.Parse(handler.RequestBody!);
            var serializedCacheControl = wireRequest.RootElement
                .GetProperty("tools")[1]
                .GetProperty("cache_control");
            Assert.Equal("ephemeral", serializedCacheControl.GetProperty("type").GetString());
            Assert.Equal("5m", serializedCacheControl.GetProperty("ttl").GetString());

            var otherEndpoint = new AiModelConfig
            {
                apiUrl = "https://other-anthropic.test/v1/messages",
                modelId = "other-model",
            };
            var notAllowlisted = client.BuildAnthropicParams(request, otherEndpoint);
            Assert.True(notAllowlisted.Tools![1].TryPickTool(out var otherEndpointTool));
            Assert.Null(otherEndpointTool.CacheControl);

            Environment.SetEnvironmentVariable(endpointSetting, null);
            var missingAllowlist = client.BuildAnthropicParams(request, model);
            Assert.True(missingAllowlist.Tools![1].TryPickTool(out var noAllowlistTool));
            Assert.Null(noAllowlistTool.CacheControl);

            request.enable_prompt_cache = false;
            var notEligible = client.BuildAnthropicParams(request, model);
            Assert.True(notEligible.Tools![1].TryPickTool(out var nonChatTool));
            Assert.Null(nonChatTool.CacheControl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(setting, original);
            Environment.SetEnvironmentVariable(endpointSetting, originalEndpoint);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            const string response = """
                {"id":"msg_test","type":"message","role":"assistant","model":"test-model","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":1,"output_tokens":1}}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
