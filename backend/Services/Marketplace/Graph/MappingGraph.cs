using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>
/// Serialized whole-category node graph (the "Blueprint" mapping editor).
/// Stored as <see cref="Delegat.Models.Marketplace.CategoryMapping.graphJson"/>.
///
/// The frontend (Vue Flow) is the schema owner: it stores the full node objects
/// (including layout/position) and the backend deserializes only the subset it needs
/// to evaluate. Unknown JSON properties are ignored, so frontend-only fields round-trip
/// untouched through the stored string.
/// </summary>
public class MappingGraph
{
    public List<GraphNode> nodes { get; set; } = new();
    public List<GraphEdge> edges { get; set; } = new();
}

/// <summary>
/// A single node. <see cref="type"/> selects behaviour:
/// "attribute" | "storeAttribute" | "productField" | "static" | "groupName"
/// | "modifier" | "formatString" | "join" | "output".
/// </summary>
public class GraphNode
{
    public string id { get; set; } = "";
    public string type { get; set; } = "";
    public GraphNodeData? data { get; set; }
}

/// <summary>
/// Node-type-specific configuration. Only the fields relevant to a node's
/// <see cref="GraphNode.type"/> are populated.
/// </summary>
public class GraphNodeData
{
    // Source nodes
    /// <summary>Attribute id for "attribute" / "storeAttribute" sources.</summary>
    public int? attributeId { get; set; }
    /// <summary>Product field name for "productField" sources (sku, name, price, …).</summary>
    public string? field { get; set; }
    /// <summary>Literal value for "static" sources.</summary>
    public string? staticValue { get; set; }
    /// <summary>Frontend-only display label for source nodes (ignored by the evaluator).</summary>
    public string? label { get; set; }
    /// <summary>Store shared-storage file id for "storeFileUrl" / "storeFileContent" sources.</summary>
    public int? storeFileId { get; set; }
    /// <summary>Product file name for "productFileByName" / "productFileContentByName" sources.</summary>
    public string? fileName { get; set; }
    /// <summary>File extension (no dot) for "productFilesByExtension" sources (e.g. "pdf").</summary>
    public string? fileExtension { get; set; }
    /// <summary>
    /// Declared source pin type (e.g. "multiselect"). When "multiselect"/"array",
    /// the source value is deserialized from CSV/JSON into a list before flowing downstream,
    /// matching the legacy export pipeline.
    /// </summary>
    public string? valueType { get; set; }

    // Modifier node
    /// <summary>Modifier type id (e.g. "multiply", "toText", "forceType").</summary>
    public string? modifierType { get; set; }
    /// <summary>Modifier parameters, same shape as a modifiersJson entry's "params".</summary>
    public Dictionary<string, object>? @params { get; set; }

    // Combine / array op nodes
    /// <summary>Positional template for "formatString" (e.g. "{0}x{1}x{2}").</summary>
    public string? template { get; set; }
    /// <summary>Delimiter for "join" / "splitToArray".</summary>
    public string? delimiter { get; set; }
    /// <summary>Element index for "arrayIndex".</summary>
    public int? index { get; set; }

    // Output node(s)
    /// <summary>Target marketplace field key for the legacy single-field "output" node.</summary>
    public string? fieldKey { get; set; }
    /// <summary>Effective coercion type for the legacy single-field "output" node.</summary>
    public string? outputType { get; set; }
    /// <summary>Field list for the single "adapterFields" sink node (one input pin per field).</summary>
    public List<AdapterField>? fields { get; set; }
}

/// <summary>
/// One marketplace field exposed as an input pin on the single "adapterFields" sink node.
/// Edges feed it via the handle id "f:&lt;fieldKey&gt;".
/// </summary>
public class AdapterField
{
    public string? fieldKey { get; set; }
    public string? label { get; set; }
    public string? outputType { get; set; }
    public bool isRequired { get; set; }
    /// <summary>Optional direct source binding (set via the adapter dropdown) — when present the field
    /// has no input pin and its value is resolved straight from this source instead of an edge.</summary>
    public AdapterFieldSource? source { get; set; }
}

/// <summary>A standard source bound directly to an adapter field (no node/wire).</summary>
public class AdapterFieldSource
{
    /// <summary>"attribute" | "storeAttribute" | "productField" | "groupName".</summary>
    public string? type { get; set; }
    public int? attributeId { get; set; }
    public string? field { get; set; }
}

/// <summary>
/// Edge between two node pins. Matches Vue Flow's edge shape.
/// <see cref="targetHandle"/> may encode an input index for multi-input nodes (e.g. "in-0", "in-1").
/// </summary>
public class GraphEdge
{
    public string id { get; set; } = "";
    public string source { get; set; } = "";
    public string target { get; set; } = "";
    public string? sourceHandle { get; set; }
    public string? targetHandle { get; set; }
}

/// <summary>
/// Delegates the evaluator needs to resolve sources and coerce output values.
/// Built by <see cref="MappingService"/> from a concrete product so the evaluator
/// itself stays free of EF / data-access concerns and is unit-testable.
/// </summary>
public class GraphEvaluationContext
{
    public required Func<int, object?> ResolveCategoryAttribute { get; init; }
    public required Func<int, object?> ResolveStoreAttribute { get; init; }
    public required Func<string, object?> ResolveProductField { get; init; }
    public required Func<object?> ResolveGroupName { get; init; }
    /// <summary>Store shared-storage file id → public URL (for "storeFileUrl" sources).</summary>
    public required Func<int, object?> ResolveStoreFileUrl { get; init; }
    /// <summary>Store shared-storage file id → text content (for "storeFileContent" sources).</summary>
    public required Func<int, object?> ResolveStoreFileContent { get; init; }
    /// <summary>Product file name → public URL (for "productFileByName" sources).</summary>
    public required Func<string?, object?> ResolveProductFileUrlByName { get; init; }
    /// <summary>Product file name → text content (for "productFileContentByName" sources).</summary>
    public required Func<string?, object?> ResolveProductFileContentByName { get; init; }
    /// <summary>File extension → URLs of all product files with it (for "productFilesByExtension").</summary>
    public required Func<string?, object?> ResolveProductFilesByExtension { get; init; }
    public required Func<int, List<SourceOption>?> ResolveCategoryAttributeOptions { get; init; }
    public required Func<int, List<SourceOption>?> ResolveStoreAttributeOptions { get; init; }
    /// <summary>(value, type) → coerced value. Reuses MappingService.CoerceToFieldType.</summary>
    public required Func<object?, string?, object?> Coerce { get; init; }
    /// <summary>Context for modifier nodes (template {{attr:N}} resolution). SourceAttributeOptions
    /// is overridden per-node from the branch's originating attribute.</summary>
    public ModifierContext? ModifierContext { get; init; }
}

/// <summary>
/// Output of a graph evaluation: the field-key → value map plus the effective
/// type per field (mirrors the legacy BuildExportDataAsync result shape).
/// </summary>
public class GraphEvalResult
{
    public Dictionary<string, object?> Outputs { get; } = new();
    public Dictionary<string, string> FieldTypes { get; } = new();

    /// <summary>Value produced at each node (by node id) — for per-node preview in the editor.</summary>
    public Dictionary<string, object?> NodeValues { get; } = new();
}
