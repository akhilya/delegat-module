using System.Text.Json;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests;

public class ModifierPipelineTests
{
    [Fact]
    public void ApplyModifiers_NullOrBlankJson_ReturnsOriginal()
    {
        Assert.Equal("keep", ModifierPipeline.ApplyModifiers("keep", null));
        Assert.Equal("keep", ModifierPipeline.ApplyModifiers("keep", "   "));
    }

    [Fact]
    public void ApplyModifiers_InvalidJson_ReturnsOriginal()
    {
        Assert.Equal("keep", ModifierPipeline.ApplyModifiers("keep", "{not-json"));
    }

    [Fact]
    public void ApplyModifiers_AppliesChainInOrder()
    {
        var json = """
            [
              {"type":"trim","params":{}},
              {"type":"uppercase","params":{}}
            ]
            """;

        Assert.Equal("HELLO", ModifierPipeline.ApplyModifiers("  hello  ", json));
    }

    [Fact]
    public void ApplyModifiers_DisabledModifier_IsSkipped()
    {
        var json = """
            [
              {"type":"uppercase","enabled":false,"params":{}},
              {"type":"trim","params":{}}
            ]
            """;

        Assert.Equal("hello", ModifierPipeline.ApplyModifiers("  hello  ", json));
    }

    [Fact]
    public void ApplyModifiers_ValueMap_MapsAndUsesDefault()
    {
        var json = """
            [
              {
                "type":"valueMap",
                "params":{
                  "mappings":[{"from":"1","to":"red"},{"from":"2","to":"blue"}],
                  "default":"other"
                }
              }
            ]
            """;

        Assert.Equal("red", ModifierPipeline.ApplyModifiers("1", json));
        Assert.Equal("other", ModifierPipeline.ApplyModifiers("9", json));
    }

    [Fact]
    public void ApplyModifiersReverse_ValueMap_ReversesMapping()
    {
        var json = """
            [
              {
                "type":"valueMap",
                "params":{
                  "mappings":[{"from":"1","to":"red"},{"from":"2","to":"blue"}]
                }
              }
            ]
            """;

        Assert.Equal("1", ModifierPipeline.ApplyModifiersReverse("red", json));
        Assert.Equal("2", ModifierPipeline.ApplyModifiersReverse("blue", json));
    }

    [Fact]
    public void ApplyModifiersReverse_ReversesChainOrder()
    {
        // Forward: "10" -> multiply 2 -> "20" -> append "x" -> "20x"
        // Reverse: strip append then divide
        var json = """
            [
              {"type":"multiply","params":{"factor":2}},
              {"type":"append","params":{"suffix":"x"}}
            ]
            """;

        var forward = ModifierPipeline.ApplyModifiers(10, json);
        Assert.Equal("20x", forward?.ToString());

        var reversed = ModifierPipeline.ApplyModifiersReverse("20x", json);
        Assert.Equal(10m, Assert.IsType<decimal>(reversed));
    }

    [Fact]
    public void HasIrreversibleModifiers_DetectsEnabledIrreversible()
    {
        var withRandom = """[{"type":"randomInt","params":{"min":1,"max":10}}]""";
        var withValueMap = """[{"type":"valueMap","params":{"mappings":[]}}]""";
        var disabledRandom = """[{"type":"randomInt","enabled":false,"params":{"min":1,"max":10}}]""";

        Assert.True(ModifierPipeline.HasIrreversibleModifiers(withRandom));
        Assert.False(ModifierPipeline.HasIrreversibleModifiers(withValueMap));
        Assert.False(ModifierPipeline.HasIrreversibleModifiers(disabledRandom));
        Assert.False(ModifierPipeline.HasIrreversibleModifiers(null));
    }

    [Fact]
    public void IsKnownModifier_And_IsReversible_ReflectRegistry()
    {
        Assert.True(ModifierPipeline.IsKnownModifier("valueMap"));
        Assert.True(ModifierPipeline.IsReversible("valueMap"));
        Assert.True(ModifierPipeline.IsKnownModifier("randomInt"));
        Assert.False(ModifierPipeline.IsReversible("randomInt"));
        Assert.False(ModifierPipeline.IsKnownModifier("notARealModifier"));
        Assert.False(ModifierPipeline.IsReversible(null));
    }

    [Fact]
    public void ApplySingle_UnknownType_PassesThrough()
    {
        Assert.Equal("x", ModifierPipeline.ApplySingle("nope", "x", null));
    }

    [Fact]
    public void ApplyModifiers_ValueMap_MapsListElements()
    {
        var json = """
            [
              {
                "type":"valueMap",
                "params":{
                  "mappings":[{"from":"1","to":"red"},{"from":"2","to":"blue"}]
                }
              }
            ]
            """;

        var result = ModifierPipeline.ApplyModifiers(new[] { "1", "2" }, json)?.ToString();
        Assert.NotNull(result);
        var mapped = JsonSerializer.Deserialize<List<string>>(result!);
        Assert.Equal(new[] { "red", "blue" }, mapped);
    }
}
