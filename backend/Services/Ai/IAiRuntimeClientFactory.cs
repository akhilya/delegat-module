using Delegat.Models.Ai;
using Microsoft.Extensions.AI;

namespace Delegat.Services.Ai;

/// <summary>
/// Builds a Microsoft.Extensions.AI <see cref="IChatClient"/> for one provider model.
/// Implemented by <see cref="AiApiClient"/> so the agent-runtime path reuses the same
/// endpoint, credential, proxy, and hosted-link transport construction as the legacy
/// transport. Tests substitute this factory to inject a scripted <see cref="IChatClient"/>.
/// </summary>
public interface IAiRuntimeClientFactory
{
    IChatClient CreateChatClient(AiModelConfig model);
}
