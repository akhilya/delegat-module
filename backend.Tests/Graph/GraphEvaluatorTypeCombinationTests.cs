using Delegat.Services.Marketplace.Graph;

namespace Delegat.Tests.Graph;

public class GraphEvaluatorTypeCombinationTests
{
    private static readonly string[] Types =
    [
        "string", "text", "select", "number", "integer", "boolean", "multiselect", "array",
    ];

    private static readonly string[] Values =
    [
        "12.5", "1", "true", "да", "[\"a\",\"b\"]", "a,b", "",
    ];

    public static IEnumerable<object?[]> SourceSinkValueCases()
    {
        foreach (var src in Types)
        foreach (var sink in Types)
        foreach (var val in Values)
            yield return new object?[] { src, sink, val };
    }

    public static IEnumerable<object?[]> ForceTypeCases()
    {
        foreach (var src in Types)
        foreach (var force in Types)
        foreach (var val in Values)
            yield return new object?[] { src, force, val };
    }

    [Theory]
    [MemberData(nameof(SourceSinkValueCases))]
    public void StaticToSink_CoercesPinToDeclaredSinkType(string sourceType, string sinkType, string value)
    {
        var result = GraphEvalHarness.StaticToSink(value, sourceType, sinkType).Eval();

        Assert.Equal(sinkType, result.FieldTypes["out"]);
        var pin = result.NodeValues["src"];
        GraphAssert.Equal(MappingFieldCoercion.Coerce(pin, sinkType), result.Outputs["out"]);
    }

    [Theory]
    [MemberData(nameof(ForceTypeCases))]
    public void ForceType_OverridesSinkDeclaredType(string sourceType, string forceType, string value)
    {
        var result = new GraphEvalHarness()
            .Static("src", value, sourceType)
            .Modifier("ft", "forceType", new Dictionary<string, object> { ["type"] = forceType })
            .Edge("src", "ft")
            .Field("out", "string")
            .WireToField("ft", "out")
            .Eval();

        Assert.Equal(forceType, result.FieldTypes["out"]);
        GraphAssert.Equal(result.NodeValues["src"], result.NodeValues["ft"]);
        GraphAssert.Equal(
            MappingFieldCoercion.Coerce(result.NodeValues["ft"], forceType),
            result.Outputs["out"]);
    }

    [Fact]
    public void JoinThenNumberSink_ParsesConcatenation()
    {
        var result = new GraphEvalHarness()
            .Static("a", "12")
            .Static("b", ".5")
            .Op("op", "join", d => d.delimiter = "")
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "number")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", 12.5m, "number");
    }

    [Fact]
    public void FormatStringThenIntegerSink_Truncates()
    {
        var result = new GraphEvalHarness()
            .Static("a", "12")
            .Static("b", "9")
            .Op("op", "formatString", d => d.template = "{0}.{1}")
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "integer")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", 12L, "integer");
    }

    [Fact]
    public void MakeArray_StringSink_KeepsEnumerable()
    {
        var result = new GraphEvalHarness()
            .Static("a", "x")
            .Static("b", "y")
            .Op("op", "makeArray")
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "string")
            .WireToField("op", "out")
            .Eval();

        Assert.Equal("string", result.FieldTypes["out"]);
        Assert.IsAssignableFrom<System.Collections.IEnumerable>(result.Outputs["out"]);
        Assert.IsNotType<string>(result.Outputs["out"]);
        GraphAssert.Equal(new[] { "x", "y" }, result.Outputs["out"]);
    }

    [Fact]
    public void BooleanSink_KeepsLogicBool()
    {
        var result = new GraphEvalHarness()
            .Static("a", "1")
            .Static("b", "1")
            .Op("op", "equals")
            .Edge("a", "op", "in-0")
            .Edge("b", "op", "in-1")
            .Field("out", "boolean")
            .WireToField("op", "out")
            .Eval();

        GraphAssert.Output(result, "out", true, "boolean");
    }
}
