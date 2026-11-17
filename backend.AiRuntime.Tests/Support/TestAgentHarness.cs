using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Delegat.AiRuntime.Tests.Gating;

namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Composes the exact pipeline the M1 prototype must prove:
///   ChatClientAgent (UseProvidedChatClientAsIs = true, so the agent adds NOTHING)
///     → FunctionInvokingChatClient (the single maintained tool loop)
///       → BatchGateChatClient (pre-dispatch batch inspection, below FICC)
///         → provider IChatClient (OpenAI or Anthropic SDK adapter over a fake transport)
/// </summary>
internal sealed class TestAgentHarness
{
    private TestAgentHarness(IChatClient providerClient, RecordingTools tools)
    {
        Tools = tools;
        Gate = new BatchGateChatClient(providerClient, new ProposePlanSiblingDeferralPolicy(), tools.Names);
        Ficc = new FunctionInvokingChatClient(Gate)
        {
            // FICC iteration cap property as it exists in M.E.AI 10.11.0.
            MaximumIterationsPerRequest = 4,
        };
        Agent = new ChatClientAgent(Ficc, new ChatClientAgentOptions
        {
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Tools = [tools.ProposePlan, tools.Sibling],
            },
        });
    }

    public RecordingTools Tools { get; }
    public BatchGateChatClient Gate { get; }
    public FunctionInvokingChatClient Ficc { get; }
    public ChatClientAgent Agent { get; }

    public static TestAgentHarness ForOpenAi(ScriptedHttpMessageHandler handler) =>
        new(ProviderClients.OpenAi(handler), new RecordingTools());

    public static TestAgentHarness ForAnthropic(ScriptedHttpMessageHandler handler, int maxRetries = 0) =>
        new(ProviderClients.Anthropic(handler, maxRetries), new RecordingTools());

    public static TestAgentHarness ForClient(IChatClient providerClient) =>
        new(providerClient, new RecordingTools());

    /// <summary>Drains a streaming agent run into a list of updates.</summary>
    public static async Task<List<AgentResponseUpdate>> CollectAsync(
        ChatClientAgent agent, string prompt, CancellationToken cancellationToken = default)
    {
        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in agent
                           .RunStreamingAsync(prompt, cancellationToken: cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            updates.Add(update);
        }
        return updates;
    }
}
