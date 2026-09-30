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
        Assert.False(AgentChatTurnGate.IsMutatingChatTool(
            "platform_request", """{"method":"GET","path":"/marketplace/connections/10/mappings/28/graph"}"""));
    }

    [Fact]
    public void PostGroupTitles_IsAChatWrite()
    {
        Assert.True(AgentChatTurnGate.IsMutatingChatTool(
            "platform_request",
            """{"method":"POST","path":"/marketplace/connections/10/mappings/28/graph/group-titles"}"""));
    }

    [Fact]
    public void SearchActions_IsNotAChatWrite()
    {
        Assert.False(AgentChatTurnGate.IsMutatingChatTool("search_actions", """{"query":"group-titles"}"""));
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
    [InlineData("сколько брендов у меня на вб?")]
    [InlineData("ответь сколько брендов")]
    public void AsksToAnswerNow_LeavesARealQuestionAlone(string? text)
    {
        Assert.False(AgentChatTurnGate.AsksToAnswerNow(text));
    }

    [Fact]
    public void NextPass_OneScriptThenPlanOrAnswer()
    {
        Assert.Equal((false, false), AgentChatTurnGate.NextPass(false, 0, 0));
        Assert.Equal((false, true), AgentChatTurnGate.NextPass(false, 1, 1));
        Assert.Equal((false, false), AgentChatTurnGate.NextPass(false, 2, 0));
        Assert.Equal((true, false), AgentChatTurnGate.NextPass(false, 3, 0));
        Assert.Equal((true, false), AgentChatTurnGate.NextPass(true, 0, 0));
    }
}
