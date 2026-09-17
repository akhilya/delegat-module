using Delegat.Services.Ai;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests;

public class MappingGroupTitleMapTests
{
    [Fact]
    public void ApplyGroupTitleMaps_WiresMergeIntoCardNotValueId()
    {
        var graph = new MappingGraph();
        GraphMutationService.ApplyGroupTitleMaps(graph,
        [
            new GroupTitlePair { From = "Мифология___Славянская", To = "Славянская мифология" },
            new GroupTitlePair { From = "фигурки Star Wars", To = "Star Wars" },
        ]);

        var sink = graph.nodes.Single(n => n.type == "adapterFields");
        Assert.Contains(sink.data!.fields!, f => f.fieldKey == "__group__");
        Assert.DoesNotContain(graph.nodes, n => n.data?.fields?.Any(f => f.fieldKey == "valueId") == true);

        var src = graph.nodes.Single(n => n.type == "groupName");
        var mod = graph.nodes.Single(n => n.type == "modifier");
        Assert.Equal("valueMap", mod.data!.modifierType);
        Assert.True(mod.data.@params!.TryGetValue("mappings", out var raw));
        var mapped = new ValueMapModifier().Apply("Мифология___Славянская",
            new Dictionary<string, object> { ["mappings"] = raw });
        Assert.Equal("Славянская мифология", mapped);
        Assert.True(src.position!.x < mod.position!.x);
        Assert.True(mod.position.x < sink.position!.x);
    }
}
