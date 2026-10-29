using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class AgentChatTurnGateTests
{
    [Fact]
    public void BatchHasProposePlan_IsTrueWhenPresent()
    {
        (string Id, string Name, string Json)[] tools =
        [
            ("1", "platform_request", """{"method":"GET","path":"/productgroups"}"""),
            ("2", "propose_plan", """{"summary":"x","steps":[]}"""),
        ];
        Assert.True(AgentChatTurnGate.BatchHasProposePlan(tools));
    }

    [Fact]
    public void GetPlatformRequest_IsNotAChatWrite()
    {
        Assert.False(AgentChatTurnGate.RequiresPlanConfirmation(
            "platform_request", """{"method":"GET","path":"/marketplace/connections/10/mappings/28/graph"}"""));
    }

    [Fact]
    public void PostGroupTitles_IsAChatWrite()
    {
        Assert.True(AgentChatTurnGate.RequiresPlanConfirmation(
            "platform_request",
            """{"method":"POST","path":"/marketplace/connections/10/mappings/28/graph/group-titles"}"""));
    }

    [Fact]
    public void SearchActions_IsNotAChatWrite()
    {
        Assert.False(AgentChatTurnGate.RequiresPlanConfirmation("search_actions", """{"query":"group-titles"}"""));
    }

    [Theory]
    [InlineData("run_python", "{\"code\":\"print(1)\"}")]
    [InlineData("mcp3_query_docs", "{}")]
    [InlineData("mcp21_update_product", "{\"id\":42}")]
    public void PythonAndConnectedMcp_RequirePlanConfirmation(string tool, string input)
    {
        Assert.True(AgentChatTurnGate.RequiresPlanConfirmation(tool, input));
    }

    [Fact]
    public void InvalidMcpLikeName_DoesNotAcquireConfirmationSemantics()
    {
        Assert.False(AgentChatTurnGate.RequiresPlanConfirmation("mcp_not_a_valid_name", "{}"));
    }

    [Theory]
    [InlineData("ответь")]
    [InlineData("Ответь!")]
    [InlineData("  хватит искать  ")]
    [InlineData("answer")]
    [InlineData("just answer")]
    public void AsksToAnswerNow_IsOnlyADemandToReply(string text)
    {
        Assert.True(AgentChatTurnGate.AsksToAnswerNow(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("и?")]
    [InlineData("ну")]
    [InlineData("дальше")]
    [InlineData("and")]
    [InlineData("so")]
    [InlineData("well")]
    [InlineData("сколько брендов у меня на вб?")]
    [InlineData("ответь сколько брендов")]
    public void AsksToAnswerNow_LeavesARealQuestionAlone(string? text)
    {
        Assert.False(AgentChatTurnGate.AsksToAnswerNow(text));
    }

    [Theory]
    [InlineData("и?")]
    [InlineData("дальше")]
    [InlineData("and")]
    public void ContinuationWords_DoNotDisableTools(string text)
    {
        Assert.False(AgentChatTurnGate.SpeakOnly(AgentChatTurnGate.AsksToAnswerNow(text), toolRounds: 0));
    }

    [Fact]
    public void SpeakOnly_WhenTheyAskToAnswer_NotAfterAFewTools()
    {
        Assert.False(AgentChatTurnGate.SpeakOnly(false, 0));
        Assert.False(AgentChatTurnGate.SpeakOnly(false, 3));
        Assert.False(AgentChatTurnGate.SpeakOnly(false, 12));
        Assert.True(AgentChatTurnGate.SpeakOnly(true, 0));
        Assert.True(AgentChatTurnGate.SpeakOnly(false, AgentChatTurnGate.RunawayToolRounds));
    }
}
