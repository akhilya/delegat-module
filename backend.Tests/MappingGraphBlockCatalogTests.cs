using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests;

public class MappingGraphBlockCatalogTests
{
    [Fact]
    public void Describe_HasGroupNameAndSinkHandles()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(MappingGraphBlockCatalog.Describe());
        Assert.Contains("groupName", json, StringComparison.Ordinal);
        Assert.Contains("f:{key}", json, StringComparison.Ordinal);
        Assert.Contains("f:__add__", json, StringComparison.Ordinal);
        Assert.Contains("\"out\"", json, StringComparison.Ordinal);
        Assert.Contains("valueMap", json, StringComparison.Ordinal);
        Assert.Contains("adapterFields", json, StringComparison.Ordinal);
        Assert.Contains("rankSep", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimate_MatchesCanvasConstants()
    {
        var sink = new GraphNode
        {
            type = "adapterFields",
            data = new GraphNodeData
            {
                fields = [new AdapterField { fieldKey = "group_name" }],
            },
        };
        var (sw, sh) = MappingGraphBlockCatalog.Estimate(sink);
        Assert.Equal(380, sw);
        Assert.Equal(34 + 2 * 28 + 6, sh);

        var src = new GraphNode
        {
            type = "groupName",
            data = new GraphNodeData { label = "Product group name" },
        };
        var (cw, ch) = MappingGraphBlockCatalog.Estimate(src);
        Assert.Equal(196, cw);
        Assert.Equal(64, ch);
    }

    [Fact]
    public void PlaceWestOf_PutsSourceLeftOfSinkWithoutOverlap()
    {
        var sink = new GraphNode
        {
            id = "adapter-fields",
            type = "adapterFields",
            position = new GraphPosition { x = 400, y = 0 },
            data = new GraphNodeData
            {
                fields = [new AdapterField { fieldKey = "group_name" }],
            },
        };
        var src = new GraphNode
        {
            id = "src",
            type = "groupName",
            data = new GraphNodeData { label = "Product group name" },
        };
        var mod = new GraphNode
        {
            id = "mod",
            type = "modifier",
            data = new GraphNodeData { modifierType = "valueMap" },
        };
        var graph = new MappingGraph { nodes = [sink, src, mod] };

        MappingGraphBlockCatalog.PlaceWestOf(graph, [src, mod], sink, "f:group_name");

        Assert.True(src.position!.x + MappingGraphBlockCatalog.Estimate(src).Width
            <= mod.position!.x);
        Assert.True(mod.position.x + MappingGraphBlockCatalog.Estimate(mod).Width
            <= sink.position.x);
        Assert.False(MappingGraphBlockCatalog.BoxesOverlap(
            src.position.x, src.position.y,
            MappingGraphBlockCatalog.Estimate(src).Width,
            MappingGraphBlockCatalog.Estimate(src).Height,
            mod.position.x, mod.position.y,
            MappingGraphBlockCatalog.Estimate(mod).Width,
            MappingGraphBlockCatalog.Estimate(mod).Height));
    }
}
