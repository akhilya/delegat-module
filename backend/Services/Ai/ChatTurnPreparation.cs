using Delegat.Models.Ai;

namespace Delegat.Services.Ai;

/// <summary>
/// Everything the chat turn loop needs after the shared StreamChatAsync preamble ran:
/// resolved+decrypted model, system prompt, trimmed history, tool surface, and per-turn
/// policy flags. Produced once per StreamChatAsync call; consumed by the legacy loop or
/// the MAF engine depending on the Ai:AgentRuntime flag.
/// </summary>
internal sealed class ChatTurnPreparation
{
    /// <summary>Enabled model row from the database (un-decrypted; used for modelConfigId).</summary>
    public required AiModelConfig ModelConfig { get; init; }

    /// <summary>Decrypted copy for provider calls.</summary>
    public required AiModelConfig Model { get; init; }

    public required string LocaleCode { get; init; }
    public required string SystemPrompt { get; init; }

    /// <summary>Trimmed history + attachment inject message, in provider-neutral AiMessage form.</summary>
    public required List<AiMessage> Messages { get; init; }

    /// <summary>Tools the model may see this turn (propose_plan only in review mode).</summary>
    public required List<AiTool> Tools { get; init; }

    /// <summary>Full catalog + MCP surface (for review-mode AdditionalTools fallbacks).</summary>
    public required List<AiTool> AllTools { get; init; }

    public required bool AnswerNow { get; init; }
    public required int StoreId { get; init; }
    public required int UserId { get; init; }
    public required string? SessionId { get; init; }
    public required bool PlanResultReview { get; init; }
    public required AiTurnTelemetry Telemetry { get; init; }
}
