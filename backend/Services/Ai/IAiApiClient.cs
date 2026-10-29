using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

/// <summary>Streaming boundary for the chat agent; permits deterministic loop tests without a provider.</summary>
public interface IAiApiClient
{
    Task<AiResponse> SendAsync(AiModelConfig model, AiRequest request, CancellationToken ct);

    IAsyncEnumerable<AiStreamEvent> StreamAsync(
        AiModelConfig model,
        AiRequest request,
        CancellationToken ct);
}
