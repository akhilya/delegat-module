using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Delegat.Services.Ai.Agent.Maf;

/// <summary>
/// Provider-scoped decorator below the turn gate: feeds response text through
/// <see cref="DeepSeekDsmlGate"/> so DeepSeek-style <c>&lt;｜DSML｜invoke&gt;</c> markup
/// becomes real <see cref="FunctionCallContent"/> calls while cleaned text stays
/// visible. Applied unconditionally for the MAF path - the gate passes plain text
/// through untouched, matching the legacy loop which runs the same gate for every
/// provider. Streaming responses are buffered per model round (the gate's batch
/// inspection buffers anyway, so no extra latency is introduced).
/// </summary>
internal sealed class DeepSeekDsmlChatClient(IChatClient inner, MafTurnState state)
    : Microsoft.Extensions.AI.DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<FrameworkChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options, CancellationToken cancellationToken)
        => Transform(await base.GetResponseAsync(messages, options, cancellationToken));

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<FrameworkChatMessage> messages,
        Microsoft.Extensions.AI.ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffered = new List<ChatResponseUpdate>();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            buffered.Add(update);
        foreach (var update in Transform(buffered.ToChatResponse()).ToChatResponseUpdates())
            yield return update;
    }

    private ChatResponse Transform(ChatResponse response)
    {
        var gate = new DeepSeekDsmlGate();
        var invokeSeq = 0;
        var hasText = false;
        var outMessages = new List<FrameworkChatMessage>(response.Messages.Count);
        foreach (var message in response.Messages)
        {
            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
            {
                if (content is TextContent { Text: { Length: > 0 } text })
                {
                    hasText = true;
                    var drain = gate.Push(text);
                    if (drain.Visible.Length > 0)
                        contents.Add(new TextContent(drain.Visible));
                    // Invokes keep their stream position relative to real tool calls,
                    // mirroring the legacy loop which appends them as text arrives.
                    foreach (var invoke in drain.Invokes)
                        contents.Add(ToCall(invoke, ref invokeSeq));
                }
                else
                {
                    contents.Add(content);
                }
            }
            outMessages.Add(Clone(message, contents));
        }

        var tail = gate.Flush();
        if (tail.Visible.Length > 0 || tail.Invokes.Count > 0)
        {
            hasText = true;
            var last = outMessages.Count > 0
                ? outMessages[^1]
                : AddMessage(outMessages, new FrameworkChatMessage(ChatRole.Assistant, []));
            if (tail.Visible.Length > 0)
                last.Contents.Add(new TextContent(tail.Visible));
            foreach (var invoke in tail.Invokes)
                last.Contents.Add(ToCall(invoke, ref invokeSeq));
        }

        if (!hasText)
            return response;

        return new ChatResponse(outMessages)
        {
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            RawRepresentation = response.RawRepresentation,
            AdditionalProperties = response.AdditionalProperties,
        };
    }

    private static FrameworkChatMessage AddMessage(List<FrameworkChatMessage> messages, FrameworkChatMessage message)
    {
        messages.Add(message);
        return message;
    }

    private FunctionCallContent ToCall(DeepSeekDsmlGate.Invoke invoke, ref int seq) =>
        new($"dsml-{state.CurrentRequestIndex}-{seq++}", invoke.Name, ToArgs(invoke.ArgumentsJson));

    private static IDictionary<string, object?> ToArgs(string argsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static FrameworkChatMessage Clone(FrameworkChatMessage message, IList<AIContent> contents) =>
        new(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = message.AdditionalProperties,
        };
}
