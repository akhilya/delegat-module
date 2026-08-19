using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests.Graph;

public class GraphEvaluatorSourceTests
{
    [Theory]
    [InlineData("hello", null, "hello")]
    [InlineData(null, "string", null)]
    public void Static_FlowsToSink(string? value, string? valueType, string? expected)
    {
        var result = GraphEvalHarness.StaticToSink(value, valueType, "string").Eval();
        GraphAssert.Output(result, "out", expected, "string");
    }

    [Fact]
    public void Static_NumberValueType_CoercesAtSourceThenAgainAtSink()
    {
        var result = GraphEvalHarness.StaticToSink("12", "number", "integer").Eval();
        GraphAssert.Output(result, "out", 12L, "integer");
    }

    [Theory]
    [InlineData("multiselect", "[\"a\",\"b\"]")]
    [InlineData("array", "[\"a\",\"b\"]")]
    public void Source_JsonArray_DeserializesForListTypes(string valueType, string json)
    {
        var g = new GraphEvalHarness();
        g.CategoryAttributes[1] = json;
        var result = g
            .Attribute("src", 1, valueType)
            .Field("out", "multiselect")
            .WireToField("src", "out")
            .Eval();

        GraphAssert.Output(result, "out", new[] { "a", "b" }, "multiselect");
    }

    [Theory]
    [InlineData("multiselect")]
    [InlineData("array")]
    public void Source_Csv_DeserializesWhenCommaPresent(string valueType)
    {
        var g = new GraphEvalHarness();
        g.CategoryAttributes[1] = "red, blue";
        var result = g
            .Attribute("src", 1, valueType)
            .Field("out", valueType)
            .WireToField("src", "out")
            .Eval();

        GraphAssert.Output(result, "out", new[] { "red", "blue" }, valueType);
    }

    [Fact]
    public void Source_CsvSingleToken_StaysString()
    {
        var g = new GraphEvalHarness();
        g.CategoryAttributes[1] = "only";
        var result = g
            .Attribute("src", 1, "multiselect")
            .Field("out", "multiselect")
            .WireToField("src", "out")
            .Eval();

        GraphAssert.Output(result, "out", "only", "multiselect");
    }

    [Fact]
    public void ProductField_AndStoreAttribute_AndGroupName_AndFiles()
    {
        var g = new GraphEvalHarness
        {
            GroupName = "Shoes",
        };
        g.ProductFields["sku"] = "SKU-1";
        g.StoreAttributes[9] = "store-val";
        g.StoreFileUrls[3] = "https://files/a.png";
        g.StoreFileContents[3] = "file-body";
        g.ProductFileUrls["spec.pdf"] = "https://p/spec.pdf";
        g.ProductFileContents["spec.pdf"] = "pdf-text";
        g.ProductFilesByExt["pdf"] = new List<string> { "https://p/a.pdf", "https://p/b.pdf" };

        var result = g
            .ProductField("sku", "sku")
            .StoreAttribute("sa", 9)
            .GroupNameSource("gn")
            .Node("sfu", "storeFileUrl", d => d.storeFileId = 3)
            .Node("sfc", "storeFileContent", d => d.storeFileId = 3)
            .Node("pfn", "productFileByName", d => d.fileName = "spec.pdf")
            .Node("pfc", "productFileContentByName", d => d.fileName = "spec.pdf")
            .Node("pfe", "productFilesByExtension", d => d.fileExtension = "pdf")
            .Field("sku", "string")
            .Field("sa", "string")
            .Field("gn", "string")
            .Field("sfu", "string")
            .Field("sfc", "string")
            .Field("pfn", "string")
            .Field("pfc", "string")
            .Field("pfe", "array")
            .WireToField("sku", "sku")
            .WireToField("sa", "sa")
            .WireToField("gn", "gn")
            .WireToField("sfu", "sfu")
            .WireToField("sfc", "sfc")
            .WireToField("pfn", "pfn")
            .WireToField("pfc", "pfc")
            .WireToField("pfe", "pfe")
            .Eval();

        GraphAssert.Output(result, "sku", "SKU-1", "string");
        GraphAssert.Output(result, "sa", "store-val", "string");
        GraphAssert.Output(result, "gn", "Shoes", "string");
        GraphAssert.Output(result, "sfu", "https://files/a.png", "string");
        GraphAssert.Output(result, "sfc", "file-body", "string");
        GraphAssert.Output(result, "pfn", "https://p/spec.pdf", "string");
        GraphAssert.Output(result, "pfc", "pdf-text", "string");
        GraphAssert.Output(result, "pfe", new[] { "https://p/a.pdf", "https://p/b.pdf" }, "array");
    }

    [Fact]
    public void Reroute_IsIdentity()
    {
        var result = new GraphEvalHarness()
            .Static("src", "keep")
            .Reroute("rr")
            .Edge("src", "rr")
            .Field("out", "string")
            .WireToField("rr", "out")
            .Eval();

        GraphAssert.Output(result, "out", "keep", "string");
    }

    [Fact]
    public void MissingAttribute_YieldsNull()
    {
        var result = new GraphEvalHarness()
            .Attribute("src", 99)
            .Field("out", "string")
            .WireToField("src", "out")
            .Eval();

        Assert.Null(result.Outputs["out"]);
    }

    [Fact]
    public void UnknownNodeType_DoesNotAbortTheRest()
    {
        var result = new GraphEvalHarness()
            .Node("bad", "not-a-real-type")
            .Static("src", "ok")
            .Field("out", "string")
            .WireToField("src", "out")
            .Eval();

        GraphAssert.Output(result, "out", "ok", "string");
        Assert.False(result.NodeValues.ContainsKey("bad"));
    }

    [Fact]
    public void AttributeOptions_PropagateToToText()
    {
        var g = new GraphEvalHarness
        {
            ModifierContext = new ModifierContext(),
        };
        g.CategoryAttributes[1] = "v1";
        g.CategoryAttributeOptions[1] =
        [
            new SourceOption { value = "v1", label = "Label One" },
        ];
        var result = g
            .Attribute("src", 1, "select")
            .Modifier("txt", "toText", new Dictionary<string, object> { ["useLabel"] = true })
            .Edge("src", "txt")
            .Field("out", "string")
            .WireToField("txt", "out")
            .Eval();

        GraphAssert.Output(result, "out", "Label One", "string");
    }
}
