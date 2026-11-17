using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Delegat.Models.Ai;
using Delegat.Services.PlatformLink;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Delegat.Services.Ai.Agent.Maf;

/// <summary>
/// Microsoft Agent Framework runtime for the read/proposal/review chat path, selected
/// per StreamChatAsync call by the Ai:AgentRuntime flag ("legacy" default, "maf" here).
///
/// One ChatClientAgent is built per turn over
///   TurnGateChatClient -> DeepSeekDsmlChatClient -> provider IChatClient
/// wrapped by FunctionInvokingChatClient (the only tool-dispatch loop; no second
/// orchestration loop exists around it). Application policy lives in the gate
/// decorator and the AIFunction adapters, never in middleware that bypasses the
/// framework's invocation path. The shared preamble (context building, history trim,
/// attachment inject) runs in AiChatService before this engine is invoked, exactly as
/// for the legacy loop.
/// </summary>
public sealed class MafChatEngine(
    IAiRuntimeClientFactory clientFactory,
    AgentToolHost toolHost,
    IConfiguration configuration,
    ILogger<MafChatEngine> logger)
{
    /// <summary>Temporary engine selection flag; "legacy" remains the default.</summary>
    public const string RuntimeFlagKey = "Ai:AgentRuntime";
    public const string EngineName = "maf";

    /// <summary>Resolved once per StreamChatAsync call and pinned for that run.</summary>
    public bool IsSelected =>
        string.Equals(configuration[RuntimeFlagKey], EngineName, StringComparison.OrdinalIgnoreCase);

    internal async IAsyncEnumerable<string> RunAsync(
        ChatTurnPreparation turn,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = Channel.CreateUnbounded<string>();
        var state = new MafTurnState
        {
            StoreId = turn.StoreId,
            UserId = turn.UserId,
            SessionId = turn.SessionId,
            LocaleCode = turn.LocaleCode,
            ModelConfigId = turn.ModelConfig.Id,
            ReviewMode = turn.PlanResultReview,
            AnswerNow = turn.AnswerNow,
            ToolHost = toolHost,
            Telemetry = turn.Telemetry,
            Logger = logger,
            Events = events,
        };

        var provider = clientFactory.CreateChatClient(turn.Model);
        IChatClient pipeline = new MafTurnGateChatClient(
            new DeepSeekDsmlChatClient(provider, state), state);
        var invoker = new FunctionInvokingChatClient(pipeline)
        {
            // The gate's own cap must fire first so a runaway turn ends cleanly instead
            // of surfacing the framework's iteration-limit exception.
            MaximumIterationsPerRequest = AgentChatTurnGate.RunawayToolRounds + 3,
            // The legacy loop has no consecutive-error fuse; tool failures are results.
            MaximumConsecutiveErrorsPerRequest = int.MaxValue,
            // Batch dispatch order must match the model's batch (legacy sequential loop).
            AllowConcurrentInvocation = false,
        };
        var (advertised, additional) = BuildTools(turn, state);
        // Always the same list instance as MafTurnState.AdditionalToolSink: the gate
        // late-registers policy adapters for uncatalogued call names into it, and FICC
        // consults AdditionalTools at dispatch time - never advertised to the provider.
        invoker.AdditionalTools = additional;

        var agent = new ChatClientAgent(invoker, new ChatClientAgentOptions
        {
            // The pipeline above is already fully decorated - the agent must use it
            // as-is so the turn gate stays between FICC and the provider.
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = turn.SystemPrompt,
                MaxOutputTokens = turn.Model.maxTokens,
                Tools = advertised,
            },
        });
        var session = await agent.CreateSessionAsync(cancellationToken);
        var input = (IEnumerable<FrameworkChatMessage>)MafChatHistoryMapper.Map(turn.Messages);

        var autoContinued = false;
        var nudgedSilence = false;
        Exception? providerFailure = null;

        while (true)
        {
            var enumerator = agent
                .RunStreamingAsync(input, session, null, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            try
            {
                while (true)
                {
                    AgentResponseUpdate? update;
                    try
                    {
                        update = await enumerator.MoveNextAsync() ? enumerator.Current : null;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        providerFailure = ex;
                        break;
                    }
                    if (update == null) break;

                    foreach (var chunk in MapUpdate(update, turn.PlanResultReview, state))
                        yield return chunk;
                    while (events.Reader.TryRead(out var ev))
                        yield return ev;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }
            while (events.Reader.TryRead(out var ev))
                yield return ev;

            if (providerFailure != null)
            {
                yield return providerFailure is PlatformHostedAiRequestException hosted
                    ? Sse(SseEventType.Error, new { code = hosted.Code, status = hosted.StatusCode })
                    : Sse(SseEventType.Error, new
                    {
                        message = state.RequestsSent > 1
                            ? AgentToolResult.TurnInterruptedMessage
                            : AgentToolResult.ProviderErrorMessage,
                    });
                break;
            }

            if (state.SawPlan || state.TerminalByCap || state.LastRoundStrippedCalls)
                break;

            var lastText = state.LastRoundText;
            // Review prose is provisional until the model round ends; emit it once.
            if (turn.PlanResultReview && AiChatService.HasVisibleReply(lastText))
            {
                state.Spoke = true;
                yield return Sse(SseEventType.Text, new { delta = lastText });
                break;
            }
            if (!turn.PlanResultReview && !state.LastRequestSpeakOnly && !autoContinued
                && AiChatService.EndsWithUnfulfilledIntent(lastText))
            {
                autoContinued = true;
                input = [new FrameworkChatMessage(ChatRole.User, AgentChatTurnGate.ContinueAfterIntentNudge)];
                continue;
            }
            if (!state.Spoke && !state.SawPlan && !nudgedSilence)
            {
                nudgedSilence = true;
                input = [new FrameworkChatMessage(ChatRole.User, AgentChatTurnGate.SilentTurnNudge)];
                continue;
            }
            break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        turn.Telemetry.CompleteIfUnset(state.SawPlan ? "plan_proposed" : "completed");

        if (!state.Spoke && !state.SawPlan)
        {
            yield return Sse(SseEventType.Text, new
            {
                delta = AgentChatTurnGate.SilentTurnFallback(turn.LocaleCode),
            });
        }

        yield return Sse(SseEventType.Done, new { modelName = turn.Model.modelId });
    }

    private static IEnumerable<string> MapUpdate(
        AgentResponseUpdate update, bool reviewMode, MafTurnState state)
    {
        // Review mode suppresses streamed text; the terminal round's reply is emitted
        // once when the run ends (provisional prose must not flash then vanish).
        if (reviewMode) yield break;
        var text = update.Text;
        if (string.IsNullOrEmpty(text)) yield break;
        if (AiChatService.HasVisibleReply(text)) state.Spoke = true;
        yield return Sse(SseEventType.Text, new { delta = text });
    }

    private static (List<AITool> Advertised, List<AITool> Additional) BuildTools(
        ChatTurnPreparation turn, MafTurnState state)
    {
        var adapters = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in turn.AllTools)
        {
            adapters[tool.name] = AgentChatTurnGate.IsProposePlan(tool.name)
                ? new MafProposePlanAdapter(tool, state)
                : new MafToolAdapter(tool, state);
        }

        var advertised = turn.Tools
            .Where(t => adapters.ContainsKey(t.name))
            .Select(t => (AITool)adapters[t.name])
            .ToList();
        // Review mode advertises propose_plan only; the rest of the catalog still
        // resolves so an off-script call gets the deterministic "Not run" result
        // (the same text the legacy loop synthesizes for any non-plan call).
        var additional = adapters.Values
            .Where(a => turn.Tools.All(t =>
                !string.Equals(t.name, a.Name, StringComparison.OrdinalIgnoreCase)))
            .Cast<AITool>()
            .ToList();
        // Every call name the model emits must reach the policy adapters - including
        // names outside the catalog (unconfigured run_python, stale MCP ids,
        // hallucinations). The gate late-registers an adapter for each such name in
        // this same sink before the batch dispatches.
        state.KnownToolNames.UnionWith(adapters.Keys);
        state.AdditionalToolSink = additional;
        return (advertised, additional);
    }

    private static string Sse(string type, object data) => AiSseTurnRecorder.Format(type, data);
}
