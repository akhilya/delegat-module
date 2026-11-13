using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests;

public class MappingGraphVersionTests
{
    [Fact]
    public void Compute_IsStableAndContentSensitive()
    {
        const string graph = "{\"nodes\":[],\"edges\":[]}";
        Assert.Equal(MappingGraphVersion.Compute(graph), MappingGraphVersion.Compute(graph));
        Assert.NotEqual(MappingGraphVersion.Compute(graph), MappingGraphVersion.Compute(graph + " "));
        Assert.Equal(MappingGraphVersion.Empty, MappingGraphVersion.Compute(null));
        Assert.Equal(MappingGraphVersion.Empty, MappingGraphVersion.Compute(""));
    }

    [Fact]
    public void Matches_AllowsMissingExpectationAndRejectsStaleVersion()
    {
        const string stored = "{\"nodes\":[{\"id\":\"a\"}],\"edges\":[]}";
        var current = MappingGraphVersion.Compute(stored);

        Assert.True(MappingGraphVersion.Matches(stored, null));
        Assert.True(MappingGraphVersion.Matches(stored, current));
        Assert.True(MappingGraphVersion.Matches(stored, current.ToUpperInvariant()));
        Assert.False(MappingGraphVersion.Matches(stored, MappingGraphVersion.Compute("{}")));
        Assert.False(MappingGraphVersion.Matches(null, current));
    }
}
