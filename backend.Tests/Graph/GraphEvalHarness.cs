using Delegat.Services.Marketplace.Graph;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Tests.Graph;

/// <summary>
/// Fluent in-memory graph + stub <see cref="GraphEvaluationContext"/> for evaluator tests.
/// </summary>
public sealed class GraphEvalHarness
{
    public MappingGraph Graph { get; } = new();

    public Dictionary<int, object?> CategoryAttributes { get; } = new();
    public Dictionary<int, object?> StoreAttributes { get; } = new();
    public Dictionary<string, object?> ProductFields { get; } = new(StringComparer.Ordinal);
    public Dictionary<int, List<SourceOption>?> CategoryAttributeOptions { get; } = new();
    public Dictionary<int, List<SourceOption>?> StoreAttributeOptions { get; } = new();
    public Dictionary<int, object?> StoreFileUrls { get; } = new();
    public Dictionary<int, object?> StoreFileContents { get; } = new();
    public Dictionary<string, object?> ProductFileUrls { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> ProductFileContents { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, object?> ProductFilesByExt { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object? GroupName { get; set; }
    public ModifierContext? ModifierContext { get; set; }
    public IReadOnlyDictionary<string, string>? GenerativeOutputs { get; set; }
    public bool GenerativePassThrough { get; set; }

    public GraphEvalHarness Node(string id, string type, Action<GraphNodeData>? configure = null)
    {
        var data = new GraphNodeData();
        configure?.Invoke(data);
        Graph.nodes.Add(new GraphNode { id = id, type = type, data = data });
        return this;
    }

    public GraphEvalHarness Static(string id, string? value, string? valueType = null)
        => Node(id, "static", d =>
        {
            d.staticValue = value;
            d.valueType = valueType;
        });

    public GraphEvalHarness ProductField(string id, string field, string? valueType = null)
        => Node(id, "productField", d =>
        {
            d.field = field;
            d.valueType = valueType;
        });

    public GraphEvalHarness Attribute(string id, int attributeId, string? valueType = null)
        => Node(id, "attribute", d =>
        {
            d.attributeId = attributeId;
            d.valueType = valueType;
        });

    public GraphEvalHarness StoreAttribute(string id, int attributeId, string? valueType = null)
        => Node(id, "storeAttribute", d =>
        {
            d.attributeId = attributeId;
            d.valueType = valueType;
        });

    public GraphEvalHarness GroupNameSource(string id)
        => Node(id, "groupName");

    public GraphEvalHarness Reroute(string id)
        => Node(id, "reroute");

    public GraphEvalHarness Modifier(
        string id,
        string modifierType,
        Dictionary<string, object>? parameters = null,
        Action<GraphNodeData>? extra = null)
        => Node(id, "modifier", d =>
        {
            d.modifierType = modifierType;
            d.@params = parameters;
            extra?.Invoke(d);
        });

    public GraphEvalHarness Op(string id, string type, Action<GraphNodeData>? configure = null)
        => Node(id, type, configure);

    public GraphEvalHarness Edge(
        string source,
        string target,
        string? targetHandle = "in",
        string? sourceHandle = "out")
    {
        Graph.edges.Add(new GraphEdge
        {
            id = $"e_{Graph.edges.Count + 1}",
            source = source,
            target = target,
            sourceHandle = sourceHandle,
            targetHandle = targetHandle,
        });
        return this;
    }

    public GraphEvalHarness Field(
        string fieldKey,
        string? outputType,
        string sinkId = "adapter-fields",
        string sinkType = "adapterFields",
        Action<AdapterField>? configure = null)
    {
        var node = Graph.nodes.FirstOrDefault(n => n.id == sinkId);
        if (node == null)
        {
            Node(sinkId, sinkType, d => d.fields = new List<AdapterField>());
            node = Graph.nodes.Last();
        }

        node.data!.fields ??= new List<AdapterField>();
        var field = new AdapterField { fieldKey = fieldKey, outputType = outputType };
        configure?.Invoke(field);
        node.data.fields.Add(field);
        return this;
    }

    public GraphEvalHarness WireToField(
        string sourceId,
        string fieldKey,
        string sinkId = "adapter-fields")
        => Edge(sourceId, sinkId, "f:" + fieldKey);

    public GraphEvaluationContext BuildContext() => new()
    {
        ResolveCategoryAttribute = id => CategoryAttributes.GetValueOrDefault(id),
        ResolveStoreAttribute = id => StoreAttributes.GetValueOrDefault(id),
        ResolveProductField = field => ProductFields.GetValueOrDefault(field),
        ResolveGroupName = () => GroupName,
        ResolveStoreFileUrl = id => StoreFileUrls.GetValueOrDefault(id),
        ResolveStoreFileContent = id => StoreFileContents.GetValueOrDefault(id),
        ResolveProductFileUrlByName = name => ProductFileUrls.GetValueOrDefault(name ?? ""),
        ResolveProductFileContentByName = name => ProductFileContents.GetValueOrDefault(name ?? ""),
        ResolveProductFilesByExtension = ext =>
            ProductFilesByExt.GetValueOrDefault((ext ?? "").TrimStart('.').ToLowerInvariant())
            ?? (object)new List<string>(),
        ResolveCategoryAttributeOptions = id => CategoryAttributeOptions.GetValueOrDefault(id),
        ResolveStoreAttributeOptions = id => StoreAttributeOptions.GetValueOrDefault(id),
        Coerce = MappingFieldCoercion.Coerce,
        ModifierContext = ModifierContext,
        GenerativeOutputs = GenerativeOutputs,
        GenerativePassThrough = GenerativePassThrough,
    };

    public GraphEvalResult Eval() => GraphEvaluator.Evaluate(Graph, BuildContext());

    public static GraphEvalHarness StaticToSink(
        string? staticValue,
        string? sourceType,
        string sinkType,
        string fieldKey = "out")
    {
        return new GraphEvalHarness()
            .Static("src", staticValue, sourceType)
            .Field(fieldKey, sinkType)
            .WireToField("src", fieldKey);
    }
}

internal static class GraphAssert
{
    public static void Equal(object? expected, object? actual)
    {
        if (expected is null && actual is null)
            return;

        if (expected is bool || actual is bool)
        {
            Assert.Equal(expected, actual);
            return;
        }

        if (IsSeq(expected) || IsSeq(actual))
        {
            Assert.Equal(Flatten(expected), Flatten(actual));
            return;
        }

        if (TryDecimal(expected, out var eDec) && TryDecimal(actual, out var aDec))
        {
            Assert.Equal(eDec, aDec);
            return;
        }

        Assert.Equal(expected, actual);
    }

    public static void Output(
        GraphEvalResult result,
        string key,
        object? expected,
        string? expectedType = null)
    {
        Assert.True(result.Outputs.ContainsKey(key), $"missing output '{key}'");
        Equal(expected, result.Outputs[key]);
        if (expectedType != null)
            Assert.Equal(expectedType, result.FieldTypes.GetValueOrDefault(key));
    }

    private static bool IsSeq(object? v) =>
        v is System.Collections.IEnumerable and not string;

    private static List<string> Flatten(object? v)
    {
        if (v is null)
            return new List<string> { "" };
        if (v is string s)
            return new List<string> { s };
        if (v is System.Collections.IEnumerable en)
            return en.Cast<object?>().SelectMany(Flatten).ToList();
        return new List<string> { v.ToString() ?? "" };
    }

    private static bool TryDecimal(object? v, out decimal d)
    {
        switch (v)
        {
            case decimal dec:
                d = dec;
                return true;
            case int i:
                d = i;
                return true;
            case long l:
                d = l;
                return true;
            case double db:
                d = (decimal)db;
                return true;
        }

        return decimal.TryParse(
            v?.ToString(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out d);
    }
}
