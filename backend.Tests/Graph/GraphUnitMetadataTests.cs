using System.Text.Json;
using Delegat.Services.Marketplace;
using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests.Graph;

public class GraphUnitMetadataTests
{
    private static GraphEvalHarness WeightGraph(bool manual = false)
    {
        var graph = new GraphEvalHarness();
        graph.CategoryAttributes[7] = "2";
        return graph.Attribute("source", 7, "number")
            .Modifier("convert", "unitConversion", new()
            {
                ["fromUnit"] = "g", ["toUnit"] = "kg", ["autoDetect"] = !manual
            })
            .Edge("source", "convert")
            .Reroute("bend").Edge("convert", "bend")
            .Field("weight", "number", configure: f => f.unitCode = "g")
            .WireToField("bend", "weight");
    }

    [Fact]
    public void AutomaticConversionUsesDefinitionAndSinkInsteadOfStaleParameters()
    {
        var graph = WeightGraph();
        GraphUnitMetadata.Reconcile(graph.Graph, (_, _) => "kg");
        GraphAssert.Output(graph.Eval(), "weight", 2000m, "number");
        var field = Assert.Single(MappingService.ProjectGraphForImport(JsonSerializer.Serialize(graph.Graph)));
        Assert.Equal(2m, ModifierPipeline.ApplyModifiersReverse("2000", field.modifiersJson));
    }

    [Fact]
    public void MissingAutoDetectFlagDefaultsToAutomatic()
    {
        var graph = WeightGraph();
        graph.Graph.nodes.Single(n => n.id == "convert").data!.@params!.Remove("autoDetect");
        GraphUnitMetadata.Reconcile(graph.Graph, (_, _) => "kg");
        GraphAssert.Output(graph.Eval(), "weight", 2000m, "number");
    }

    [Fact]
    public void ManualConversionKeepsBothSelectedUnits()
    {
        var graph = WeightGraph(manual: true);
        GraphUnitMetadata.Reconcile(graph.Graph, (_, _) => "kg");
        GraphAssert.Output(graph.Eval(), "weight", .002m, "number");
    }

    [Fact]
    public void RemovedUnitDoesNotReuseCachedSourceUnit()
    {
        var graph = WeightGraph();
        GraphUnitMetadata.Reconcile(graph.Graph, (_, _) => null);
        Assert.Equal("", graph.Graph.nodes.Single(n => n.id == "convert").data!.@params!["fromUnit"]);
    }

    [Fact]
    public void ConflictingDestinationsDoNotPickAnArbitraryUnit()
    {
        var graph = WeightGraph().Field("otherWeight", "number", configure: f => f.unitCode = "kg")
            .WireToField("convert", "otherWeight");
        GraphUnitMetadata.Reconcile(graph.Graph, (_, _) => "kg");
        Assert.Equal("", graph.Graph.nodes.Single(n => n.id == "convert").data!.@params!["toUnit"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackageDepthDividedByTenReachesRequiredLength(bool storeAttribute)
    {
        var graph = new GraphEvalHarness();
        if (storeAttribute) { graph.StoreAttributes[7] = "110"; graph.StoreAttribute("source", 7, "number"); }
        else { graph.CategoryAttributes[7] = "110"; graph.Attribute("source", 7, "number"); }
        graph.Modifier("divide", "divide", new() { ["divisor"] = 10 })
            .Edge("source", "divide", "in")
            .Field("length", "number", configure: f => f.isRequired = true)
            .WireToField("divide", "length");
        GraphAssert.Output(graph.Eval(), "length", 11m, "number");
    }
}
