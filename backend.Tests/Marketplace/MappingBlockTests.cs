using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests.Marketplace;

public class MappingBlockTests
{
    [Fact]
    public void RegexReplace_RewritesByPattern()
    {
        var mod = new RegexReplaceModifier();
        var result = mod.Apply("Цвет: 61576", new Dictionary<string, object>
        {
            ["pattern"] = @"\d+",
            ["replacement"] = "красный",
        });
        Assert.Equal("Цвет: красный", result);
    }

    [Fact]
    public void RegexReplace_InvalidPattern_KeepsTheOriginal()
    {
        var mod = new RegexReplaceModifier();
        var result = mod.Apply("красный", new Dictionary<string, object>
        {
            ["pattern"] = "(",
            ["replacement"] = "x",
        });
        Assert.Equal("красный", result);
    }

    [Fact]
    public void RegexExtract_ReturnsTheRequestedGroup()
    {
        var mod = new RegexExtractModifier();
        var result = mod.Apply("арт-61576", new Dictionary<string, object>
        {
            ["pattern"] = @"арт-(\d+)",
            ["group"] = 1,
        });
        Assert.Equal("61576", result);
    }

    [Fact]
    public void RegexExtract_NoMatch_ReturnsEmpty()
    {
        var mod = new RegexExtractModifier();
        var result = mod.Apply("красный", new Dictionary<string, object>
        {
            ["pattern"] = @"\d+",
        });
        Assert.Equal("", result);
    }

    [Fact]
    public void EmitDictionaryLabel_MapsKeyToName()
    {
        var options = new List<SourceOption>
        {
            new() { value = "61576", label = "красный" },
        };
        Assert.Equal("красный", GraphEvaluator.EmitDictionaryLabel("61576", options));
    }

    [Fact]
    public void EmitDictionaryLabel_OffPath_KeepsTheKeyWhenLabelIsMissing()
    {
        var options = new List<SourceOption>
        {
            new() { value = "61576", label = "красный" },
        };
        Assert.Equal("999", GraphEvaluator.EmitDictionaryLabel("999", options));
        var list = Assert.IsType<List<string>>(GraphEvaluator.EmitDictionaryLabel(new List<string> { "61576" }, options));
        Assert.Equal("красный", list[0]);
        var fromJson = Assert.IsType<List<string>>(GraphEvaluator.EmitDictionaryLabel("[\"61576\"]", options));
        Assert.Equal("красный", fromJson[0]);
    }
}
