using Delegat.Services.Ai;
using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class ConnectedMcpToolAllowlistTests
{
    [Fact]
    public void EmptyConfiguration_DeniesEveryConnectedTool()
    {
        Assert.False(ConnectedMcpToolAllowlist.IsAllowed(null, 1, "search_docs"));
        Assert.False(ConnectedMcpToolAllowlist.IsAllowed("", 1, "search_docs"));
    }

    [Fact]
    public void ExactEntries_AreScopedToServerAndRemoteName()
    {
        const string configured = "2:search_docs; 2:fetch_document,3:render_file";

        Assert.True(ConnectedMcpToolAllowlist.IsAllowed(configured, 2, "search_docs"));
        Assert.True(ConnectedMcpToolAllowlist.IsAllowed(configured, 3, "render_file"));
        Assert.False(ConnectedMcpToolAllowlist.IsAllowed(configured, 1, "search_docs"));
        Assert.False(ConnectedMcpToolAllowlist.IsAllowed(configured, 2, "delete_document"));
    }

    [Fact]
    public void FilterTools_HidesUnapprovedToolsFromModelDiscovery()
    {
        var tools = new[]
        {
            new AiTool { name = "mcp2_search_docs", McpRemoteName = "search_docs" },
            new AiTool { name = "mcp2_delete_all", McpRemoteName = "delete_all" },
        };

        var visible = ConnectedMcpToolAllowlist.FilterTools("2:search_docs", 2, tools);

        Assert.Single(visible);
        Assert.Equal("search_docs", visible[0].McpRemoteName);
        Assert.False(ConnectedMcpToolAllowlist.IsAllowed("2:search_docs", 2, "delete_all"));
    }

    [Fact]
    public void WildcardsAndMalformedEntries_AreIgnored()
    {
        var parsed = ConnectedMcpToolAllowlist.Parse("*:all;0:zero;bad;4:*;5:read_only");

        Assert.Single(parsed);
        Assert.Contains((5, "read_only"), parsed);
    }
}
