using System.Text.Json;
using Delegat.Services.Ai;

namespace Delegat.Tests.Ai;

public class AiChatHistoryBudgetTests
{
    [Fact]
    public void GetMaxCharacters_UsesPositiveEnvironmentOverrideAndDefaultsOtherwise()
    {
        var setting = AiChatHistoryBudget.MaxCharactersEnvironmentVariable;
        var original = Environment.GetEnvironmentVariable(setting);
        try
        {
            Environment.SetEnvironmentVariable(setting, null);
            Assert.Equal(AiChatHistoryBudget.DefaultMaxCharacters, AiChatHistoryBudget.GetMaxCharacters());

            Environment.SetEnvironmentVariable(setting, "12000");
            Assert.Equal(12000, AiChatHistoryBudget.GetMaxCharacters());

            Environment.SetEnvironmentVariable(setting, "0");
            Assert.Equal(AiChatHistoryBudget.DefaultMaxCharacters, AiChatHistoryBudget.GetMaxCharacters());

            Environment.SetEnvironmentVariable(setting, "invalid");
            Assert.Equal(AiChatHistoryBudget.DefaultMaxCharacters, AiChatHistoryBudget.GetMaxCharacters());
        }
        finally
        {
            Environment.SetEnvironmentVariable(setting, original);
        }
    }

    [Fact]
    public void Trim_DropsOldTurnsAsWholeUnitsAndKeepsRecentToolExchangeAndCurrentRequest()
    {
        var messages = new List<ChatMessage>
        {
            User(new string('x', 2_000)),
            Assistant("""[{"type":"tool_use","id":"old-call","name":"search","input":{"q":"old"}}]"""),
            UserBlocks("""[{"type":"tool_result","tool_use_id":"old-call","content":"old result"}]"""),
            Assistant("Old lookup completed."),
            User("Check the saved workflow."),
            Assistant("""[{"type":"tool_use","id":"recent-call","name":"memory_search","input":{"query":"workflow"}}]"""),
            UserBlocks("""[{"type":"tool_result","tool_use_id":"recent-call","content":"workflow: publish weekly"}]"""),
            Assistant("The saved workflow is to publish weekly."),
            User("Do that workflow now."),
        };
        var retained = messages.Skip(4).ToList();
        var budget = (int)retained.Sum(AiChatHistoryBudget.EstimateMessageCharacters);

        var result = AiChatHistoryBudget.Trim(messages, budget);

        Assert.Equal(1, result.RemovedTurns);
        Assert.True(result.Trimmed);
        Assert.False(result.ExceedsBudget);
        Assert.Equal(retained.Count, result.Messages.Count);
        Assert.Equal("Do that workflow now.", result.Messages[^1].content.GetString());
        Assert.Contains(result.Messages, m => m.content.ValueKind == JsonValueKind.Array
            && m.content.GetRawText().Contains("recent-call", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Messages, m => m.content.GetRawText().Contains("old-call", StringComparison.Ordinal));
    }

    [Fact]
    public void Trim_KeepsActiveRequestWholeEvenWhenItExceedsBudget()
    {
        var current = User(new string('y', 500));
        var result = AiChatHistoryBudget.Trim([User(new string('x', 500)), current], maxCharacters: 20);

        Assert.True(result.Trimmed);
        Assert.True(result.ExceedsBudget);
        Assert.Equal(1, result.RemovedTurns);
        Assert.Single(result.Messages);
        Assert.Equal(new string('y', 500), result.Messages[0].content.GetString());
    }

    [Fact]
    public void Trim_DoesNotModifyHistoryUnderBudget()
    {
        var messages = new List<ChatMessage> { User("first"), Assistant("second") };

        var result = AiChatHistoryBudget.Trim(messages, maxCharacters: 100);

        Assert.False(result.Trimmed);
        Assert.Equal(messages, result.Messages);
        Assert.Equal(0, result.RemovedCharacters);
    }

    private static ChatMessage User(string text) => Message("user", JsonSerializer.Serialize(text));
    private static ChatMessage UserBlocks(string json) => Message("user", json);
    private static ChatMessage Assistant(string text) => Message("assistant", text.StartsWith('[') ? text : JsonSerializer.Serialize(text));

    private static ChatMessage Message(string role, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new ChatMessage { role = role, content = document.RootElement.Clone() };
    }
}
