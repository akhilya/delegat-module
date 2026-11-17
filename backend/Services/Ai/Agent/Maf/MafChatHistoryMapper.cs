using System.Text.Json;
using Microsoft.Extensions.AI;
using FrameworkChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Delegat.Services.Ai.Agent.Maf;

/// <summary>
/// Maps the application-owned persisted history (<see cref="AiMessage"/>: role plus a
/// string or a block array with text / tool_use / tool_result entries) onto
/// Microsoft.Extensions.AI <see cref="FrameworkChatMessage"/> objects. Tool calls become
/// <see cref="FunctionCallContent"/> and tool results become <see cref="ChatRole.Tool"/>
/// messages with <see cref="FunctionResultContent"/> - the same wire shapes the legacy
/// loop appends. The persisted rows themselves are not touched; framework session
/// persistence stays out of scope for M3 (owned by M4).
/// </summary>
internal static class MafChatHistoryMapper
{
    public static List<FrameworkChatMessage> Map(IReadOnlyList<AiMessage> messages)
    {
        var result = new List<FrameworkChatMessage>();
        foreach (var message in messages)
        {
            var assistant = string.Equals(message.role, "assistant", StringComparison.OrdinalIgnoreCase);
            var element = JsonSerializer.SerializeToElement(message.content);
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    result.Add(new FrameworkChatMessage(assistant ? ChatRole.Assistant : ChatRole.User,
                        element.GetString() ?? ""));
                    break;
                case JsonValueKind.Array:
                    MapBlocks(element, assistant, result);
                    break;
                case JsonValueKind.Undefined:
                case JsonValueKind.Null:
                    break;
                default:
                    result.Add(new FrameworkChatMessage(assistant ? ChatRole.Assistant : ChatRole.User,
                        element.ToString()));
                    break;
            }
        }
        return result;
    }

    private static void MapBlocks(JsonElement array, bool assistant, List<FrameworkChatMessage> result)
    {
        var contents = new List<AIContent>();
        var toolResults = new List<AIContent>();
        foreach (var block in array.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
                continue;
            switch (typeEl.GetString())
            {
                case "text":
                    if (block.TryGetProperty("text", out var textEl)
                        && textEl.ValueKind == JsonValueKind.String)
                        contents.Add(new TextContent(textEl.GetString() ?? ""));
                    break;
                case "tool_use":
                    contents.Add(new FunctionCallContent(
                        block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                        block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                        block.TryGetProperty("input", out var inputEl) ? ToArgs(inputEl) : null));
                    break;
                case "tool_result":
                    toolResults.Add(new FunctionResultContent(
                        block.TryGetProperty("tool_use_id", out var refEl) ? refEl.GetString() ?? "" : "",
                        block.TryGetProperty("content", out var contentEl)
                            ? (contentEl.ValueKind == JsonValueKind.String
                                ? contentEl.GetString()
                                : contentEl.ToString())
                            : ""));
                    break;
            }
        }

        // Persisted user rows carry tool_result blocks; providers need them as
        // ChatRole.Tool messages that directly follow the assistant call message.
        if (toolResults.Count > 0)
            result.Add(new FrameworkChatMessage(ChatRole.Tool, toolResults));
        if (contents.Count > 0)
            result.Add(new FrameworkChatMessage(assistant ? ChatRole.Assistant : ChatRole.User, contents));
    }

    private static IDictionary<string, object?> ToArgs(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>();
        var args = new Dictionary<string, object?>();
        foreach (var property in input.EnumerateObject())
            args[property.Name] = property.Value.Clone();
        return args;
    }
}
