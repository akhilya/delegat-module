using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Delegat.AiRuntime.Tests.Support;

/// <summary>
/// Scripted IChatClient (no HTTP) returning canned ChatResponse objects. Used to exercise
/// the gate directly — including the non-streaming GetResponseAsync path — and to observe
/// exactly which messages FICC sends on subsequent rounds.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();

    /// <summary>Every message list the client was called with (round boundaries preserved).</summary>
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = new();

    public void Enqueue(ChatResponse response) => _responses.Enqueue(response);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        if (_responses.Count == 0)
            throw new InvalidOperationException("ScriptedChatClient ran out of queued responses.");
        return Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(messages.ToList());
        await Task.Yield();
        if (_responses.Count == 0)
            throw new InvalidOperationException("ScriptedChatClient ran out of queued responses.");
        foreach (var update in _responses.Dequeue().ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
