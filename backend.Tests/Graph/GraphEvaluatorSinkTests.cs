using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests.Graph;

public class GraphEvaluatorSinkTests
{
    [Fact]
    public void AdapterFields_AndPriceFields_AreSeparateSinks()
    {
        var result = new GraphEvalHarness()
            .Static("title", "Hat")
            .Static("price", "19.5", "number")
            .Field("name", "string")
            .Field("price", "number", "adapter-price-fields", "adapterPriceFields")
            .WireToField("title", "name")
            .WireToField("price", "price", "adapter-price-fields")
            .Eval();

        GraphAssert.Output(result, "name", "Hat", "string");
        GraphAssert.Output(result, "price", 19.5m, "number");
    }

    [Fact]
    public void Alias_CopiesPrimaryThenCoercesToAliasType()
    {
        var result = new GraphEvalHarness()
            .Static("src", "12")
            .Field("primary", "number")
            .Field("alias", "integer", configure: f => f.aliasOf = "primary")
            .WireToField("src", "primary")
            .Eval();

        GraphAssert.Output(result, "primary", 12m, "number");
        GraphAssert.Output(result, "alias", 12L, "integer");
    }

    [Fact]
    public void Omit_IgnoresWire()
    {
        var result = new GraphEvalHarness()
            .Static("src", "secret")
            .Field("hidden", "string", configure: f => f.omit = true)
            .Field("shown", "string")
            .WireToField("src", "hidden")
            .WireToField("src", "shown")
            .Eval();

        Assert.False(result.Outputs.ContainsKey("hidden"));
        GraphAssert.Output(result, "shown", "secret", "string");
    }

    [Fact]
    public void BoundSource_WinsOverWire()
    {
        var g = new GraphEvalHarness();
        g.ProductFields["name"] = "bound-name";
        var result = g
            .Static("src", "wired-name")
            .Field("title", "string", configure: f =>
            {
                f.source = new AdapterFieldSource { type = "productField", field = "name" };
            })
            .WireToField("src", "title")
            .Eval();

        GraphAssert.Output(result, "title", "bound-name", "string");
    }

    [Fact]
    public void BoundAttribute_AndGroupName()
    {
        var g = new GraphEvalHarness { GroupName = "Grp" };
        g.CategoryAttributes[4] = "attr-val";
        var result = g
            .Field("a", "string", configure: f =>
            {
                f.source = new AdapterFieldSource { type = "attribute", attributeId = 4 };
            })
            .Field("g", "string", configure: f =>
            {
                f.source = new AdapterFieldSource { type = "groupName" };
            })
            .Eval();

        GraphAssert.Output(result, "a", "attr-val", "string");
        GraphAssert.Output(result, "g", "Grp", "string");
    }

    [Fact]
    public void Cycle_DropsCyclicNodes_OtherOutputsSurvive()
    {
        var result = new GraphEvalHarness()
            .Op("a", "not")
            .Op("b", "not")
            .Edge("a", "b")
            .Edge("b", "a")
            .Static("src", "ok")
            .Field("out", "string")
            .WireToField("src", "out")
            .Eval();

        GraphAssert.Output(result, "out", "ok", "string");
        Assert.False(result.NodeValues.ContainsKey("a"));
        Assert.False(result.NodeValues.ContainsKey("b"));
    }

    [Fact]
    public void DanglingAndMissingNodeEdges_AreIgnored()
    {
        var g = new GraphEvalHarness()
            .Static("src", "ok")
            .Field("out", "string")
            .WireToField("src", "out");
        g.Graph.edges.Add(new GraphEdge
        {
            id = "ghost",
            source = "missing",
            target = "adapter-fields",
            targetHandle = "f:out",
        });
        g.Graph.edges.Add(new GraphEdge
        {
            id = "ghost2",
            source = "src",
            target = "nope",
            targetHandle = "in",
        });

        var result = g.Eval();
        GraphAssert.Output(result, "out", "ok", "string");
    }

    [Fact]
    public void LegacyOutputNode_WritesFieldKey()
    {
        var result = new GraphEvalHarness()
            .Static("src", "v")
            .Node("outn", "output", d =>
            {
                d.fieldKey = "legacy";
                d.outputType = "string";
            })
            .Edge("src", "outn")
            .Eval();

        GraphAssert.Output(result, "legacy", "v", "string");
    }
}
