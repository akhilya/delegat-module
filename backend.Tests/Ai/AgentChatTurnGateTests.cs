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
}
