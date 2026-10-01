using Delegat.Services.Ai;
using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests;

public class MappingModifierRemovalTests
{
    [Fact]
    public void RemoveModifiersOnField_DropsLiteralReplacesAndKeepsTheChain()
    {
        var graph = Chain();
        var removed = GraphMutationService.RemoveModifiersOnField(graph, "description", ["replace"]);

        Assert.Equal(3, removed);
        Assert.DoesNotContain(graph.nodes, n => n.data?.modifierType == "replace");
        Assert.DoesNotContain(graph.nodes, n => n.id == "literal");
        Assert.Contains(graph.nodes, n => n.id == "div");
        Assert.Contains(graph.nodes, n => n.id == "rx");
        Assert.Contains(graph.nodes, n => n.id == "src");

        var intoSink = graph.edges.Single(e => e.target == "sink" && e.targetHandle == "f:description");
        Assert.Equal("rx", intoSink.source);
        var intoRx = graph.edges.Single(e => e.target == "rx" && e.targetHandle == "in");
        Assert.Equal("div", intoRx.source);
        var intoDiv = graph.edges.Single(e => e.target == "div" && e.targetHandle == "in");
        Assert.Equal("src", intoDiv.source);
    }

    private static MappingGraph Chain()
    {
        GraphNode Mod(string id, string type) => new()
        {
            id = id,
            type = "modifier",
            data = new GraphNodeData { modifierType = type },
        };

        var graph = new MappingGraph
        {
            nodes =
            [
                new GraphNode
                {
                    id = "sink",
                    type = "adapterFields",
                    data = new GraphNodeData
                    {
                        fields = [new AdapterField { fieldKey = "description" }],
                    },
                },
                new GraphNode { id = "src", type = "productField", data = new GraphNodeData { field = "description" } },
                Mod("r1", "replace"),
                Mod("r2", "replace"),
                Mod("div", "divide"),
                Mod("r3", "replace"),
                Mod("rx", "regexReplace"),
                new GraphNode { id = "literal", type = "static", data = new GraphNodeData { staticValue = "🔥" } },
            ],
        };

        void Wire(string from, string to, string handle) => graph.edges.Add(new GraphEdge
        {
            id = $"e-{from}-{to}",
            source = from,
            target = to,
            sourceHandle = "out",
            targetHandle = handle,
        });

        Wire("src", "r1", "in");
        Wire("r1", "r2", "in");
        Wire("r2", "div", "in");
        Wire("div", "r3", "in");
        Wire("r3", "rx", "in");
        Wire("rx", "sink", "f:description");
        Wire("literal", "r1", "p:search");
        return graph;
    }
}
