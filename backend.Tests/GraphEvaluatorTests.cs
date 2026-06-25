using System.Globalization;
using System.Text.Json;
using PrintO.Services.Marketplace.Graph;
using PrintO.Services.Marketplace.Modifiers;
using Xunit;

namespace PrintO.Tests;

/// <summary>
/// Tests for the visual node-graph evaluator. The evaluator is delegate-driven (no DB),
/// so sources are stubbed. Modifier params are passed as real JsonElements (parsed from JSON)
/// so the modifiers receive exactly what production deserialization produces.
/// </summary>
public class GraphEvaluatorTests
{
    // ---- The motivating case: width/length/height in mm → "ШxВxД" in cm ----

    [Fact]
    public void Dimensions_ThreeMmAttributes_DividedBy10_FormatString_ProducesCentimeters()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Attr("w", 1), Attr("l", 2), Attr("h", 3),
                Mod("dw", "divide", "{\"divisor\":10}"),
                Mod("dl", "divide", "{\"divisor\":10}"),
                Mod("dh", "divide", "{\"divisor\":10}"),
                new GraphNode { id = "fmt", type = "formatString", data = new() { template = "{0}x{1}x{2}" } },
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "Габариты", outputType = "string" } },
            },
            edges = new()
            {
                Edge("w", "dw"), Edge("l", "dl"), Edge("h", "dh"),
                Edge("dw", "fmt", "in-0"), Edge("dl", "fmt", "in-1"), Edge("dh", "fmt", "in-2"),
                Edge("fmt", "out"),
            }
        };

        var ctx = Context(catAttr: id => id switch { 1 => "300", 2 => "200", 3 => "100", _ => null });

        var result = GraphEvaluator.Evaluate(graph, ctx);

        Assert.Equal("30x20x10", result.Outputs["Габариты"]);
    }

    // ---- Legacy parity: a single-line graph == the equivalent modifiersJson chain ----

    [Fact]
    public void LinearChain_MatchesModifierPipeline()
    {
        const string modifiersJson =
            "[{\"type\":\"multiply\",\"params\":{\"factor\":1000}},{\"type\":\"round\",\"params\":{\"decimals\":0}}]";

        object? source = "2.5";
        var legacy = ModifierPipeline.ApplyModifiers(source, modifiersJson, null);

        var graph = new MappingGraph
        {
            nodes = new()
            {
                new GraphNode { id = "s", type = "static", data = new() { staticValue = "2.5" } },
                Mod("m", "multiply", "{\"factor\":1000}"),
                Mod("r", "round", "{\"decimals\":0}"),
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "x" } },
            },
            edges = new() { Edge("s", "m"), Edge("m", "r"), Edge("r", "out") }
        };

        // Identity coercion so we compare the raw modifier composition.
        var ctx = Context(coerce: (v, _) => v);
        var result = GraphEvaluator.Evaluate(graph, ctx);

        Assert.Equal(Convert.ToDecimal(legacy), Convert.ToDecimal(result.Outputs["x"]));
    }

    [Fact]
    public void ForceTypeNode_OverridesOutputType_AndDrivesCoercion()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                new GraphNode { id = "s", type = "static", data = new() { staticValue = "123" } },
                Mod("f", "forceType", "{\"type\":\"integer\"}"),
                // Output has no explicit type — the upstream forceType must win.
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "x" } },
            },
            edges = new() { Edge("s", "f"), Edge("f", "out") }
        };

        var result = GraphEvaluator.Evaluate(graph, Context());

        Assert.Equal("integer", result.FieldTypes["x"]);
        Assert.Equal(123L, Assert.IsType<long>(result.Outputs["x"]));
    }

    [Fact]
    public void ToTextNode_UsesOriginatingAttributeOptions()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Attr("a", 1),
                Mod("t", "toText", "{\"useLabel\":true}"),
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "color", outputType = "string" } },
            },
            edges = new() { Edge("a", "t"), Edge("t", "out") }
        };

        var ctx = Context(
            catAttr: id => id == 1 ? "1" : null,
            catAttrOptions: id => id == 1
                ? new List<SourceOption> { new() { value = "1", label = "Красный" }, new() { value = "2", label = "Синий" } }
                : null);

        var result = GraphEvaluator.Evaluate(graph, ctx);

        Assert.Equal("Красный", result.Outputs["color"]);
    }

    [Fact]
    public void Cycle_DoesNotThrow_AndProducesNoOutput()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Mod("a", "trim", "{}"),
                Mod("b", "trim", "{}"),
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "x", outputType = "string" } },
            },
            edges = new() { Edge("a", "b"), Edge("b", "a"), Edge("b", "out") }
        };

        var result = GraphEvaluator.Evaluate(graph, Context());

        // Nodes in the cycle are dropped; the output (fed from the cycle) stays unwritten.
        Assert.False(result.Outputs.ContainsKey("x"));
    }

    // ---- Array collection ops + single adapterFields sink ----

    [Fact]
    public void AdapterFields_ImagesArray_PassesThrough()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                new GraphNode { id = "img", type = "productField", data = new() { field = "images", valueType = "array" } },
                Adapter("adapter", ("images", "multiselect")),
            },
            edges = new() { FEdge("img", "adapter", "images") },
        };
        var imgs = new List<string> { "a.jpg", "b.jpg" };

        var result = GraphEvaluator.Evaluate(graph, Context(productField: f => f == "images" ? imgs : null));

        Assert.Equal(imgs, result.Outputs["images"]);
    }

    [Fact]
    public void MakeArray_CollectsOrderedInputs()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                new GraphNode { id = "a", type = "static", data = new() { staticValue = "x" } },
                new GraphNode { id = "b", type = "static", data = new() { staticValue = "y" } },
                new GraphNode { id = "arr", type = "makeArray", data = new() },
                Adapter("adapter", ("imgs", "multiselect")),
            },
            edges = new()
            {
                new GraphEdge { id = "a-arr", source = "a", target = "arr", targetHandle = "in-0" },
                new GraphEdge { id = "b-arr", source = "b", target = "arr", targetHandle = "in-1" },
                FEdge("arr", "adapter", "imgs"),
            },
        };

        var result = GraphEvaluator.Evaluate(graph, Context());

        Assert.Equal(new List<object?> { "x", "y" }, result.Outputs["imgs"]);
    }

    [Fact]
    public void SplitToArray_Length_And_Index()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                new GraphNode { id = "s", type = "static", data = new() { staticValue = "a,b,c" } },
                new GraphNode { id = "sp", type = "splitToArray", data = new() { delimiter = "," } },
                new GraphNode { id = "len", type = "arrayLength", data = new() },
                new GraphNode { id = "idx", type = "arrayIndex", data = new() { index = 1 } },
                Adapter("adapter", ("len", "integer"), ("item", "string")),
            },
            edges = new()
            {
                Edge("s", "sp"),
                Edge("sp", "len"),
                Edge("sp", "idx"),
                FEdge("len", "adapter", "len"),
                FEdge("idx", "adapter", "item"),
            },
        };

        var result = GraphEvaluator.Evaluate(graph, Context());

        Assert.Equal(3L, Assert.IsType<long>(result.Outputs["len"]));
        Assert.Equal("b", result.Outputs["item"]);
    }

    // ---- Round 3: logic blocks, literal coercion, product files ----

    private static GraphNode Lit(string id, string v, string t) =>
        new() { id = id, type = "static", data = new() { staticValue = v, valueType = t } };

    [Fact]
    public void IfElse_PicksBranch_ByCondition()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Lit("c", "true", "boolean"), Lit("a", "ДА", "string"), Lit("b", "НЕТ", "string"),
                new GraphNode { id = "if", type = "ifElse" },
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "x", outputType = "string" } },
            },
            edges = new()
            {
                Edge("c", "if", "in-0"), Edge("a", "if", "in-1"), Edge("b", "if", "in-2"), Edge("if", "out"),
            }
        };

        var result = GraphEvaluator.Evaluate(graph, Context());
        Assert.Equal("ДА", result.Outputs["x"]);
    }

    [Fact]
    public void Predicates_IsEmpty_IsValid_Not()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Lit("empty", "", "string"), Lit("full", "hi", "string"),
                new GraphNode { id = "e", type = "isEmpty" },
                new GraphNode { id = "v", type = "isValid" },
                new GraphNode { id = "n", type = "not" },
                new GraphNode { id = "oe", type = "output", data = new() { fieldKey = "e" } },
                new GraphNode { id = "ov", type = "output", data = new() { fieldKey = "v" } },
                new GraphNode { id = "on", type = "output", data = new() { fieldKey = "n" } },
            },
            edges = new()
            {
                Edge("empty", "e"), Edge("e", "oe"),
                Edge("full", "v"), Edge("v", "ov"),
                Edge("v", "n"), Edge("n", "on"),
            }
        };

        var result = GraphEvaluator.Evaluate(graph, Context(coerce: (x, _) => x));
        Assert.Equal(true, result.Outputs["e"]);
        Assert.Equal(true, result.Outputs["v"]);
        Assert.Equal(false, result.Outputs["n"]);
    }

    [Fact]
    public void Comparisons_Equals_And_GreaterThan_OnNumbers()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Lit("a", "5", "number"), Lit("b", "5", "number"), Lit("c", "3", "number"),
                new GraphNode { id = "eq", type = "equals" },
                new GraphNode { id = "gt", type = "greaterThan" },
                new GraphNode { id = "oeq", type = "output", data = new() { fieldKey = "eq" } },
                new GraphNode { id = "ogt", type = "output", data = new() { fieldKey = "gt" } },
            },
            edges = new()
            {
                Edge("a", "eq", "in-0"), Edge("b", "eq", "in-1"), Edge("eq", "oeq"),
                Edge("a", "gt", "in-0"), Edge("c", "gt", "in-1"), Edge("gt", "ogt"),
            }
        };

        var result = GraphEvaluator.Evaluate(graph, Context(coerce: (x, _) => x));
        Assert.Equal(true, result.Outputs["eq"]);
        Assert.Equal(true, result.Outputs["gt"]);
    }

    [Fact]
    public void StaticLiteral_CoercedByValueType()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Lit("n", "5", "number"),
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "x", outputType = "number" } },
            },
            edges = new() { Edge("n", "out") }
        };

        var result = GraphEvaluator.Evaluate(graph, Context());
        Assert.Equal(5m, Assert.IsType<decimal>(result.Outputs["x"]));
    }

    [Fact]
    public void ProductFile_ByName_And_ByExtension_Resolve()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                new GraphNode { id = "u", type = "productFileByName", data = new() { fileName = "spec.pdf", valueType = "string" } },
                new GraphNode { id = "x", type = "productFilesByExtension", data = new() { fileExtension = "pdf", valueType = "array" } },
                new GraphNode { id = "ou", type = "output", data = new() { fieldKey = "u" } },
                new GraphNode { id = "ox", type = "output", data = new() { fieldKey = "x" } },
            },
            edges = new() { Edge("u", "ou"), Edge("x", "ox") }
        };

        var result = GraphEvaluator.Evaluate(graph, Context(coerce: (v, _) => v));
        Assert.Equal("https://files/spec.pdf", result.Outputs["u"]);
        var list = Assert.IsType<List<object?>>(result.Outputs["x"]);
        Assert.Equal(new object?[] { "a.pdf", "b.pdf" }, list);
    }

    [Fact]
    public void Reroute_PassesValueThrough_Unchanged()
    {
        var graph = new MappingGraph
        {
            nodes = new()
            {
                Attr("a", 1),
                new GraphNode { id = "rr", type = "reroute" },
                new GraphNode { id = "out", type = "output", data = new() { fieldKey = "x", outputType = "string" } },
            },
            edges = new() { Edge("a", "rr"), Edge("rr", "out") }
        };

        var result = GraphEvaluator.Evaluate(graph, Context(catAttr: _ => "hello"));
        Assert.Equal("hello", result.Outputs["x"]);
    }

    // ---- Helpers ----

    private static GraphNode Attr(string id, int attributeId) =>
        new() { id = id, type = "attribute", data = new() { attributeId = attributeId } };

    private static GraphNode Mod(string id, string modifierType, string paramsJson) =>
        new() { id = id, type = "modifier", data = new() { modifierType = modifierType, @params = ParseParams(paramsJson) } };

    private static GraphEdge Edge(string source, string target, string? targetHandle = null) =>
        new() { id = $"{source}->{target}", source = source, target = target, targetHandle = targetHandle };

    private static Dictionary<string, object> ParseParams(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static GraphNode Adapter(string id, params (string key, string type)[] fields) =>
        new()
        {
            id = id,
            type = "adapterFields",
            data = new() { fields = fields.Select(f => new AdapterField { fieldKey = f.key, outputType = f.type }).ToList() },
        };

    private static GraphEdge FEdge(string source, string target, string fieldKey) =>
        new() { id = $"{source}->{target}:{fieldKey}", source = source, target = target, targetHandle = $"f:{fieldKey}" };

    private static GraphEvaluationContext Context(
        Func<int, object?>? catAttr = null,
        Func<int, List<SourceOption>?>? catAttrOptions = null,
        Func<object?, string?, object?>? coerce = null,
        Func<string, object?>? productField = null) =>
        new()
        {
            ResolveCategoryAttribute = catAttr ?? (_ => null),
            ResolveStoreAttribute = _ => null,
            ResolveProductField = productField ?? (_ => null),
            ResolveGroupName = () => null,
            ResolveStoreFileUrl = _ => null,
            ResolveStoreFileContent = _ => null,
            ResolveProductFileUrlByName = (name) => name == null ? null : $"https://files/{name}",
            ResolveProductFileContentByName = (name) => name == null ? null : $"content:{name}",
            ResolveProductFilesByExtension = (ext) => new List<object?> { $"a.{ext}", $"b.{ext}" },
            ResolveCategoryAttributeOptions = catAttrOptions ?? (_ => null),
            ResolveStoreAttributeOptions = _ => null,
            Coerce = coerce ?? DefaultCoerce,
            ModifierContext = new ModifierContext { ResolveReference = _ => null }
        };

    /// <summary>Faithful-enough mirror of MappingService.CoerceToFieldType for tests.</summary>
    private static object? DefaultCoerce(object? value, string? type)
    {
        if (value == null || string.IsNullOrEmpty(type))
            return value;

        switch (type.ToLowerInvariant())
        {
            case "string":
            case "text":
                return value is System.Collections.IEnumerable && value is not string ? value : value.ToString();
            case "number":
                return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : value;
            case "integer":
                if (long.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
                    return l;
                return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dd)
                    ? (long)Math.Truncate(dd)
                    : value;
            default:
                return value;
        }
    }
}
