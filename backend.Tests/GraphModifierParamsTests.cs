using System.Text.Json;
using Delegat.Services;

namespace Delegat.Tests;

public class GraphModifierParamsTests
{
    [Fact]
    public void Merge_CopiesFlatPatternAndReplacementIntoParams()
    {
        var merged = GraphModifierParams.Merge(
            "regexReplace",
            fromParams: null,
            pattern: "warhammer",
            replacement: "ВХ40",
            ignoreCase: true,
            group: null);

        Assert.NotNull(merged);
        Assert.Equal("warhammer", merged!["pattern"]);
        Assert.Equal("ВХ40", merged["replacement"]);
        Assert.Equal(true, merged["ignoreCase"]);
    }

    [Fact]
    public void Merge_KeepsPatternAlreadyInsideParams()
    {
        var fromParams = new Dictionary<string, object>
        {
            ["pattern"] = JsonSerializer.SerializeToElement("inside"),
            ["replacement"] = JsonSerializer.SerializeToElement(""),
        };

        var merged = GraphModifierParams.Merge(
            "regexReplace", fromParams, "flat", "ignored", ignoreCase: null, group: null);

        Assert.Equal("inside", ((JsonElement)merged!["pattern"]).GetString());
        Assert.Equal("", ((JsonElement)merged["replacement"]).GetString());
    }

    [Fact]
    public void Merge_RejectsRegexWithoutAPattern()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GraphModifierParams.Merge("regexReplace", null, pattern: "  ", replacement: "x", ignoreCase: null, group: null));
        Assert.Contains("params.pattern", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_AllowsANonRegexModifierWithoutAPattern()
    {
        var merged = GraphModifierParams.Merge("divide", null, pattern: null, replacement: null, ignoreCase: null, group: null);
        Assert.Null(merged);
    }
}
