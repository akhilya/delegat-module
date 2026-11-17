using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Provider IChatClient construction for the M1 prototype. Both providers are wired through
/// the SDKs' custom-transport seams — the same seams the hosted/proxy path uses
/// (HttpClientPipelineTransport for OpenAI, ClientOptions.HttpClient for Anthropic) —
/// which is exactly the BYOK/hosted injection point §9.4 needs proven.
/// </summary>
internal static class ProviderClients
{
    public const string OpenAiModelId = "gpt-m1-test";
    public const string AnthropicModelId = "claude-m1-test";
    public const string FakeBaseUrl = "http://m1-fake.invalid";
    public const string FakeApiKey = "m1-fake-key";

    /// <summary>OpenAI.Chat.ChatClient over the scripted transport, adapted via M.E.AI.OpenAI.</summary>
    public static IChatClient OpenAi(HttpMessageHandler handler)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri($"{FakeBaseUrl}/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler, disposeHandler: false)),
        };
        var chatClient = new ChatClient(OpenAiModelId, new ApiKeyCredential(FakeApiKey), options);
        return chatClient.AsIChatClient();
    }

    /// <summary>
    /// Anthropic SDK over the scripted transport, adapted via the SDK's own
    /// Microsoft.Extensions.AI.AnthropicClientExtensions.AsIChatClient — no
    /// Microsoft.Agents.AI.Anthropic package needed.
    /// </summary>
    public static IChatClient Anthropic(HttpMessageHandler handler, int maxRetries = 0)
    {
        var client = new Anthropic.AnthropicClient
        {
            ApiKey = FakeApiKey,
            BaseUrl = FakeBaseUrl,
            HttpClient = new HttpClient(handler, disposeHandler: false),
            MaxRetries = maxRetries,
        };
        return client.AsIChatClient(AnthropicModelId, 1024);
    }
}
