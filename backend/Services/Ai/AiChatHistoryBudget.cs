using System.Text.Json;

namespace Delegat.Services.Ai;

internal sealed record AiChatHistoryTrimResult(
    IReadOnlyList<ChatMessage> Messages,
    int RemovedTurns,
    long RemovedCharacters,
    long TotalCharacters,
    int MaxCharacters)
{
    public bool Trimmed => RemovedTurns > 0;
    public bool ExceedsBudget => Messages.Sum(AiChatHistoryBudget.EstimateMessageCharacters) > MaxCharacters;
}

/// <summary>
/// Bounds prior chat history by dropping oldest complete user turns. It never clips a
/// message, and tool-result user messages stay attached to the turn that produced them.
/// </summary>
internal static class AiChatHistoryBudget
{
    public const int DefaultMaxCharacters = 32_000;
    public const string MaxCharactersEnvironmentVariable = "AI_CHAT_HISTORY_MAX_CHARACTERS";
    public const string OmittedHistoryNotice =
        "Some older conversation turns were omitted to limit context size. Their contents are unavailable; do not guess or claim to remember them. If the current request depends on a missing detail, ask the user to repeat it.";

    public static int GetMaxCharacters()
    {
        var raw = Environment.GetEnvironmentVariable(MaxCharactersEnvironmentVariable);
        return int.TryParse(raw, out var maxCharacters) && maxCharacters > 0
            ? maxCharacters
            : DefaultMaxCharacters;
    }

    public static AiChatHistoryTrimResult Trim(
        IReadOnlyList<ChatMessage> messages,
        int maxCharacters = DefaultMaxCharacters)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (maxCharacters < 0) throw new ArgumentOutOfRangeException(nameof(maxCharacters));

        var groups = GroupTurns(messages);
        var totalCharacters = groups.Sum(g => g.Characters);
        if (groups.Count == 0 || totalCharacters <= maxCharacters)
        {
            return new AiChatHistoryTrimResult(
                messages.ToList(), 0, 0, totalCharacters, maxCharacters);
        }

        // Keep a contiguous suffix of history. The newest turn is always preserved whole,
        // even if it alone exceeds the target; truncating the active request would be unsafe.
        var firstKept = groups.Count - 1;
        long keptCharacters = groups[firstKept].Characters;
        for (var i = firstKept - 1; i >= 0; i--)
        {
            if (keptCharacters + groups[i].Characters > maxCharacters)
                break;
            firstKept = i;
            keptCharacters += groups[i].Characters;
        }

        var kept = groups.Skip(firstKept).SelectMany(g => g.Messages).ToList();
        return new AiChatHistoryTrimResult(
            kept,
            firstKept,
            Math.Max(0, totalCharacters - keptCharacters),
            totalCharacters,
            maxCharacters);
    }

    public static long EstimateMessageCharacters(ChatMessage message)
    {
        long count = message.role?.Length ?? 0;
        var content = message.content;
        if (content.ValueKind == JsonValueKind.String)
            return count + (content.GetString()?.Length ?? 0);
        if (content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return count;

        try
        {
            return count + content.GetRawText().Length;
        }
        catch (InvalidOperationException)
        {
            return count;
        }
    }

    private static List<TurnGroup> GroupTurns(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return [];

        var groups = new List<TurnGroup>();
        var start = 0;
        for (var i = 1; i < messages.Count; i++)
        {
            if (!StartsUserTurn(messages[i])) continue;
            groups.Add(CreateGroup(messages, start, i));
            start = i;
        }
        groups.Add(CreateGroup(messages, start, messages.Count));
        return groups;
    }

    private static TurnGroup CreateGroup(IReadOnlyList<ChatMessage> messages, int start, int end)
    {
        var groupMessages = new List<ChatMessage>(end - start);
        long characters = 0;
        for (var i = start; i < end; i++)
        {
            groupMessages.Add(messages[i]);
            characters += EstimateMessageCharacters(messages[i]);
        }
        return new TurnGroup(groupMessages, characters);
    }

    private static bool StartsUserTurn(ChatMessage message)
    {
        if (!string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase))
            return false;

        var content = message.content;
        if (content.ValueKind != JsonValueKind.Array)
            return !IsToolResult(content);

        var sawBlock = false;
        foreach (var block in content.EnumerateArray())
        {
            sawBlock = true;
            if (!IsToolResult(block)) return true;
        }

        return !sawBlock;
    }

    private static bool IsToolResult(JsonElement content) =>
        content.ValueKind == JsonValueKind.Object
        && content.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
        && string.Equals(type.GetString(), "tool_result", StringComparison.Ordinal);

    private sealed record TurnGroup(IReadOnlyList<ChatMessage> Messages, long Characters);
}
