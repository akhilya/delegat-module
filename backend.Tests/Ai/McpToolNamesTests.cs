using Delegat.Services.Ai.Agent;

namespace Delegat.Tests;

public class McpToolNamesTests
{
    [Fact]
    public void ForLlm_PrefixesServerIdAndSanitizesRemoteName()
    {
        var name = McpToolNames.ForLlm(12, "get weather!");
        Assert.StartsWith("mcp12_get_weather_", name, StringComparison.Ordinal);
        Assert.True(name.Length <= 64);
    }

    [Fact]
    public void ForLlm_DistinguishesNamesThatSanitizeOrTruncateToTheSamePrefix()
    {
        Assert.NotEqual(
            McpToolNames.ForLlm(12, "get weather!"),
            McpToolNames.ForLlm(12, "get_weather"));

        var prefix = new string('x', 100);
        Assert.NotEqual(
            McpToolNames.ForLlm(12, prefix + "first"),
            McpToolNames.ForLlm(12, prefix + "second"));
    }

    [Fact]
    public void TryParse_ExtractsServerIdAndToolKey()
    {
        var llm = McpToolNames.ForLlm(3, "search-docs");
        Assert.True(McpToolNames.TryParse(llm, out var serverId, out var toolKey));
        Assert.Equal(3, serverId);
        Assert.Equal(llm["mcp3_".Length..], toolKey);
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
