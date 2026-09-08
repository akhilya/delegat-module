using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class McpToolNamesTests
{
    [Fact]
    public void ForLlm_PrefixesServerIdAndSanitizesRemoteName()
    {
        var name = McpToolNames.ForLlm(12, "get weather!");
        Assert.Equal("mcp12_get_weather", name);
        Assert.True(name.Length <= 64);
    }

    [Fact]
    public void TryParse_RoundTripsServerIdAndRemoteName()
    {
        var llm = McpToolNames.ForLlm(3, "search-docs");
        Assert.True(McpToolNames.TryParse(llm, out var serverId, out var remote));
        Assert.Equal(3, serverId);
        Assert.Equal("search-docs", remote);
    }

    [Theory]
    [InlineData("search_catalog")]
    [InlineData("mcp_x_foo")]
    [InlineData("mcp0_tool")]
    [InlineData("")]
    public void TryParse_RejectsBuiltInAndMalformedNames(string name)
    {
        Assert.False(McpToolNames.TryParse(name, out _, out _));
    }
}
