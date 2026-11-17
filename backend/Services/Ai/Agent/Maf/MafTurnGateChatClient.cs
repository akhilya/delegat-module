using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Delegat.Services.Ai.Agent.Maf;

/// <summary>
/// Per-turn gate decorator between the provider chain (optionally the DeepSeek DSML
/// decorator) and FunctionInvokingChatClient. Owns the request-side policy the legacy
/// loop applies (speak-only tool stripping, iteration cap, runaway close-task message,
/// blank-line round separators) and the response-side batch policy (tool-round
/// counting, batch flags the adapters consult, speak-only call stripping). Deferred
/// calls stay in the forwarded response: FICC hands every call to the adapters, which
/// return the exact legacy synthetic results instead of dispatching - so provider
/// history always carries a matching FunctionResultContent for every call id.
/// </summary>
internal sealed class MafTurnGateChatClient(IChatClient inner, MafTurnState state)
    : Microsoft.Extensions.AI.DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<FrameworkChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options, CancellationToken cancellationToken)
    {
        var (msgs, opts, forward) = PrepareRequest(messages, options);
        if (!forward)
            return TerminalResponse();

        var timer = Stopwatch.StartNew();
        using var span = state.Telemetry.StartModelRound();
        try
        {
            var response = await base.GetResponseAsync(msgs, opts, cancellationToken);
            var processed = ApplyResponse(response);
            timer.Stop();
            span.SetOutcome("completed");
            state.Telemetry.RecordModelRound(timer.ElapsedMilliseconds,
                state.CurrentRequestInputChars, OutputChars(processed), ToUsage(response.Usage));
            return processed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.SetOutcome("cancelled");
            throw;
        }
        catch
        {
            FailModelRound(span, timer);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<FrameworkChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (msgs, opts, forward) = PrepareRequest(messages, options);
        if (!forward)
        {
            yield return TerminalUpdate();
            yield break;
        }

        var timer = Stopwatch.StartNew();
        using var span = state.Telemetry.StartModelRound();
        var buffered = new List<ChatResponseUpdate>();
        try
        {
            // The gate must see the complete model round before deciding which calls
            // dispatch, so the provider stream is buffered per round here. Text is
            // re-emitted after each round instead of token-by-token - the SSE contract
            // is unchanged, only its chunking granularity differs from the legacy loop.
            await foreach (var update in base.GetStreamingResponseAsync(msgs, opts, cancellationToken))
                buffered.Add(update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.SetOutcome("cancelled");
            throw;
        }
        catch
        {
            FailModelRound(span, timer);
            throw;
        }

        var response = buffered.ToChatResponse();
        var processed = ApplyResponse(response);
        timer.Stop();
        span.SetOutcome("completed");
        state.Telemetry.RecordModelRound(timer.ElapsedMilliseconds,
            state.CurrentRequestInputChars, OutputChars(processed), ToUsage(response.Usage));
        foreach (var update in processed.ToChatResponseUpdates())
            yield return update;
    }

    private void FailModelRound(AiTurnSpan span, Stopwatch timer)
    {
        timer.Stop();
        span.SetOutcome("provider_error");
        state.Telemetry.RecordModelRound(timer.ElapsedMilliseconds,
            state.CurrentRequestInputChars, 0, new AiStreamUsage());
        state.Telemetry.Complete("provider_error");
        state.ProviderError = true;
    }

    private (IEnumerable<FrameworkChatMessage> Messages, ChatOptions? Options, bool Forward) PrepareRequest(
        IEnumerable<FrameworkChatMessage> messages, ChatOptions? options)
    {
        if (state.SawPlan)
        {
            // The legacy loop breaks right after presenting a plan; FICC's next
            // request becomes an empty terminal response instead of a provider call.
            return (messages, options, false);
        }

        if (state.CapReached)
        {
            // Same end state as the legacy for-loop exhausting its iteration budget:
            // the turn stops instead of erroring. The FICC iteration limit is set one
            // higher so this gate always wins and the turn ends cleanly.
            state.TerminalByCap = true;
            return (messages, options, false);
        }

        state.CurrentRequestIndex = state.RequestsSent;
        state.LastRoundStrippedCalls = false;

        // SpeakOnly consults RequestsSent as the 0-based request index (legacy
        // `iteration`), so it must be evaluated before the counter increments.
        var speakOnly = state.SpeakOnly;
        state.RequestsSent++;
        state.LastRequestSpeakOnly = speakOnly;
        var opts = options;
        if (speakOnly && opts != null)
        {
            opts = opts.Clone();
            opts.Tools = null;
        }

        var msgs = messages;
        if (state.ToolRounds >= AgentChatTurnGate.RunawayToolRounds)
            msgs = msgs.Concat([new FrameworkChatMessage(ChatRole.User, AgentChatTurnGate.CloseTaskNowMessage)]);

        if (state.CurrentRequestIndex > 0 && !state.ReviewMode)
            state.Events.Writer.TryWrite(AiTurnEvent.Create(
                SseEventType.Text, new { delta = "\n\n" }));

        state.CurrentRequestInputChars = EstimateInputCharacters(msgs, opts);
        return (msgs, opts, true);
    }

    private ChatResponse ApplyResponse(ChatResponse response)
    {
        response = NormalizeCallNames(response);
        var text = new StringBuilder();
        var calls = new List<FunctionCallContent>();
        foreach (var message in response.Messages)
        foreach (var content in message.Contents)
        {
            if (content is TextContent textContent)
                text.Append(textContent.Text);
            else if (content is FunctionCallContent call)
                calls.Add(call);
        }

        state.LastRoundText = text.ToString();
        state.WaitForPlan = false;
        state.AttachmentReadInBatch = false;

        if (calls.Count == 0)
            return response;

        // Any call name outside the catalog (unconfigured run_python, stale MCP ids,
        // hallucinations) gets a late-bound policy adapter so FICC dispatches it into
        // the same application-owned results as the legacy loop (review not-run text,
        // plan-batch deferral, or the "Unknown tool" dispatch error) instead of the
        // framework's own "function not found" fallback. The adapter is added to the
        // shared AdditionalTools sink before this batch reaches FICC's dispatch.
        foreach (var call in calls)
        {
            if (call.Name is { Length: > 0 } name && state.KnownToolNames.Add(name))
            {
                state.AdditionalToolSink.Add(new MafToolAdapter(
                    new AiTool { name = name, input_schema = new AiToolInputSchema() }, state));
            }
        }

        state.ToolRounds++;
        state.WaitForPlan = calls.Any(c => AgentChatTurnGate.IsProposePlan(c.Name));
        state.AttachmentReadInBatch = calls.Any(c =>
            string.Equals(c.Name, AgentChatTurnGate.ReadChatAttachmentTool, StringComparison.OrdinalIgnoreCase));
        state.Telemetry.RecordModelToolCalls(calls.Select(c => (c.Name, ArgsJson(c))));

        if (!state.LastRequestSpeakOnly)
            return response;

        // Speak-only rounds keep the calls out of dispatch entirely - same as the
        // legacy `if (passTools == null) break;` that still records the assistant turn.
        state.LastRoundStrippedCalls = true;
        var stripped = new List<FrameworkChatMessage>();
        foreach (var message in response.Messages)
        {
            var kept = message.Contents.Where(c => c is not FunctionCallContent).ToList();
            stripped.Add(new FrameworkChatMessage(message.Role, kept)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                RawRepresentation = message.RawRepresentation,
                AdditionalProperties = message.AdditionalProperties,
            });
        }

        return new ChatResponse(stripped)
        {
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            RawRepresentation = response.RawRepresentation,
            AdditionalProperties = response.AdditionalProperties,
        };
    }

    /// <summary>
    /// Some OpenAI-compatible servers emit legacy `functions.` tool names; the
    /// legacy stream path strips the prefix before policy and dispatch, so the
    /// framework response is rewritten the same way here - call ids and
    /// arguments preserved, batch inspection and adapters see normalized names.
    /// </summary>
    private static ChatResponse NormalizeCallNames(ChatResponse response)
    {
        List<FrameworkChatMessage>? changed = null;
        for (var i = 0; i < response.Messages.Count; i++)
        {
            var message = response.Messages[i];
            List<AIContent>? contents = null;
            for (var j = 0; j < message.Contents.Count; j++)
            {
                if (message.Contents[j] is not FunctionCallContent call)
                    continue;
                var normalized = AiApiClient.NormalizeToolName(call.Name);
                if (normalized == call.Name)
                    continue;
                contents ??= new List<AIContent>(message.Contents);
                contents[j] = new FunctionCallContent(call.CallId, normalized, call.Arguments);
            }
            if (contents == null)
                continue;
            changed ??= new List<FrameworkChatMessage>(response.Messages);
            changed[i] = new FrameworkChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                RawRepresentation = message.RawRepresentation,
                AdditionalProperties = message.AdditionalProperties,
            };
        }
        return changed == null
            ? response
            : new ChatResponse(changed)
            {
                FinishReason = response.FinishReason,
                Usage = response.Usage,
                ResponseId = response.ResponseId,
                ModelId = response.ModelId,
                RawRepresentation = response.RawRepresentation,
                AdditionalProperties = response.AdditionalProperties,
            };
    }

    private static ChatResponse TerminalResponse() =>
        new(new FrameworkChatMessage(ChatRole.Assistant, "")) { FinishReason = ChatFinishReason.Stop };

    private static ChatResponseUpdate TerminalUpdate() =>
        new(ChatRole.Assistant, "") { FinishReason = ChatFinishReason.Stop };

    private static string ArgsJson(FunctionCallContent call)
    {
        if (call.Arguments == null) return "{}";
        try { return JsonSerializer.Serialize(call.Arguments); }
        catch { return "{}"; }
    }

    private static long OutputChars(ChatResponse response)
    {
        long count = 0;
        foreach (var message in response.Messages)
        foreach (var content in message.Contents)
        {
            if (content is TextContent textContent)
                count += textContent.Text?.Length ?? 0;
            else if (content is FunctionCallContent call)
                count += ArgsJson(call).Length;
        }
        return count;
    }

    private static long EstimateInputCharacters(IEnumerable<FrameworkChatMessage> messages, ChatOptions? options)
    {
        long count = options?.Instructions?.Length ?? 0;
        foreach (var message in messages)
        {
            count += message.Role.Value?.Length ?? 0;
            foreach (var content in message.Contents)
                count += content switch
                {
                    TextContent textContent => textContent.Text?.Length ?? 0,
                    FunctionCallContent call => (call.Name?.Length ?? 0) + ArgsJson(call).Length,
                    FunctionResultContent result => SerializedLength(result.Result),
                    _ => 0,
                };
        }

        if (options?.Tools != null)
        {
            foreach (var tool in options.Tools)
                count += (tool.Name?.Length ?? 0) + (tool.Description?.Length ?? 0);
        }

        return count;
    }

    private static long SerializedLength(object? value)
    {
        try { return JsonSerializer.Serialize(value).Length; }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return 0;
        }
    }

    private static AiStreamUsage ToUsage(UsageDetails? usage)
    {
        var result = new AiStreamUsage();
        if (usage == null) return result;
        result.inputTokens = usage.InputTokenCount;
        result.outputTokens = usage.OutputTokenCount;
        result.cacheReadInputTokens = usage.CachedInputTokenCount;
        if (usage.AdditionalCounts != null)
        {
            foreach (var (key, value) in usage.AdditionalCounts)
            {
                if (key.Contains("cacheWrite", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("cacheCreation", StringComparison.OrdinalIgnoreCase))
                    result.cacheCreationInputTokens = (result.cacheCreationInputTokens ?? 0) + value;
                else if (result.cacheReadInputTokens == null
                    && (key.Contains("cacheRead", StringComparison.OrdinalIgnoreCase)
                        || key.Contains("cachedInput", StringComparison.OrdinalIgnoreCase)))
                    result.cacheReadInputTokens = value;
            }
        }
        return result;
    }
}
