using System.Text.Json;
using System.Text.Json.Serialization;
using Delegat.Services.Marketplace.Modifiers;

namespace Delegat.Services.Marketplace.Graph;

/// <summary>
/// Serialized whole-category node graph (the "Blueprint" mapping editor).
/// Stored as <see cref="Delegat.Models.Marketplace.CategoryMapping.graphJson"/>.
///
/// The frontend (Vue Flow) is the schema owner: it stores the full node objects
/// (including layout/position) and the backend deserializes only the subset it needs
/// to evaluate. Frontend-only node.data fields round-trip via
/// <see cref="GraphNodeData.ExtensionData"/> through Sync* serialize paths; raw
/// PUT storage keeps the string untouched.
/// </summary>
public class MappingGraph
{
    public List<GraphNode> nodes { get; set; } = new();
    public List<GraphEdge> edges { get; set; } = new();
}

/// <summary>
/// Vue Flow node coordinates - persisted by the editor, ignored by the evaluator.
/// </summary>
public class GraphPosition
{
    public double x { get; set; }
    public double y { get; set; }
}

/// <summary>
/// A single node. <see cref="GraphNode.type"/> selects behaviour:
/// "attribute" | "storeAttribute" | "productField" | "static" | "groupName"
/// | "modifier" | "formatString" | "join" | "output" | "adapterFields" | "adapterPriceFields".
/// </summary>
public class GraphNode
{
    public string id { get; set; } = "";
    public string type { get; set; } = "";
    /// <summary>Editor layout; round-tripped through graphJson so sync operations preserve user positions.</summary>
    public GraphPosition? position { get; set; }
    /// <summary>Vue Flow deletable flag (adapter sinks are false).</summary>
    public bool? deletable { get; set; }
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
    /// <summary>Marks a static node as a "Static value" block - its value is one of the
    /// adapter field's predefined options (frontend renders a select instead of free text).</summary>
    public bool? isAdapterValue { get; set; }
    /// <summary>Predefined options for "isAdapterValue" static nodes (copied from the adapter field).</summary>
    public List<GraphNodeOption>? options { get; set; }
    /// <summary>Shop field that supplied <see cref="options"/>. Fixed after the block is created.</summary>
    public string? optionSourceKey { get; set; }
    /// <summary>Display name of <see cref="optionSourceKey"/>.</summary>
    public string? optionSourceLabel { get; set; }
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
    /// <summary>
    /// For productField / attribute / storeAttribute sources: false = export-only lane
    /// (import from marketplace skips this source). Null or true = importable when the chain allows.
    /// </summary>
    public bool? importable { get; set; }
    /// <summary>
    /// Attribute / store-attribute sources that store a dictionary key. True emits the option
    /// label (the name). Null or false keeps the stored key.
    /// </summary>
    public bool? emitLabel { get; set; }

    // Modifier node
    /// <summary>Modifier type id (e.g. "multiply", "toText", "forceType").</summary>
    public string? modifierType { get; set; }
    /// <summary>Modifier parameters, same shape as a modifiersJson entry's "params".</summary>
    public Dictionary<string, object>? @params { get; set; }
    /// <summary>Adapter field key this map modifier feeds (persisted from graph wiring).</summary>
    public string? targetFieldKey { get; set; }
    /// <summary>Allowed values of the target adapter field (optionMap right-hand select).</summary>
    public List<GraphNodeOption>? targetFieldOptions { get; set; }
    /// <summary>Output type of the target adapter field.</summary>
    public string? targetValueType { get; set; }
    /// <summary>Extra input pins for llmGenerate nodes (wired via handle "field:&lt;id&gt;").</summary>
    public List<GraphFieldPin>? fieldPins { get; set; }

    // Combine / array op nodes
    /// <summary>Positional template for "formatString" (e.g. "{0}x{1}x{2}").</summary>
    public string? template { get; set; }
    /// <summary>Delimiter for "join" / "splitToArray".</summary>
    public string? delimiter { get; set; }
    /// <summary>How many left pins on a multi-input op (formatString, join, …).</summary>
    public int? inputCount { get; set; }
    /// <summary>Element index for "arrayIndex".</summary>
    public int? index { get; set; }

    // Output node(s)
    /// <summary>Target marketplace field key for the legacy single-field "output" node.</summary>
    public string? fieldKey { get; set; }
    /// <summary>Effective coercion type for the legacy single-field "output" node.</summary>
    public string? outputType { get; set; }
    /// <summary>Field list for the single "adapterFields" sink node (one input pin per field).</summary>
    public List<AdapterField>? fields { get; set; }

    /// <summary>Editor pill. Assistant-created blocks start collapsed so the canvas stays readable.</summary>
    public bool? collapsed { get; set; }

    /// <summary>
    /// Frontend-only editor fields that must survive SyncAdapterFields /
    /// SyncPriceFields deserialize→serialize round-trips. Evaluator ignores these.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Option entry for "isAdapterValue" static nodes - same shape the frontend editor uses.</summary>
public class GraphNodeOption
{
    public string? value { get; set; }
    public string? label { get; set; }
}

/// <summary>Named extra input on an llmGenerate modifier node.</summary>
public class GraphFieldPin
{
    public string? id { get; set; }
    public string? label { get; set; }
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
    /// <summary>Marketplace schema group, used to visually organize the price-field sink.</summary>
    public string? group { get; set; }
    /// <summary>Marketplace-provided field hint.</summary>
    public string? description { get; set; }
    /// <summary>Marketplace schema order. User bindings are keyed by fieldKey and survive reordering.</summary>
    public int? sortOrder { get; set; }
    public bool isRequired { get; set; }
    /// <summary>Optional direct source binding (set via the adapter dropdown) - when present the field
    /// has no input pin and its value is resolved straight from this source instead of an edge.</summary>
    public AdapterFieldSource? source { get; set; }
    /// <summary>Merged-duplicate marker: this field is an alias of another field (by fieldKey).
    /// Export copies the primary's evaluated value into this key; import reverses only the primary.
    /// Alias fields have no input pin and own connections are ignored.</summary>
    public string? aliasOf { get; set; }
    /// <summary>When true the field is excluded from export outputs and import projections.
    /// Mutually exclusive with <see cref="source"/> / <see cref="aliasOf"/> and graph wires.</summary>
    public bool omit { get; set; }
    /// <summary>Mapping-local field the operator added on the canvas (not from the adapter schema).</summary>
    public bool isCustom { get; set; }
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
    /// <summary>(value, type) → coerced value. Reuses <see cref="MappingFieldCoercion.Coerce"/>.</summary>
    public required Func<object?, string?, object?> Coerce { get; init; }
    /// <summary>Context for modifier nodes (template {{attr:N}} resolution). SourceAttributeOptions
    /// is overridden per-node from the branch's originating attribute.</summary>
    public ModifierContext? ModifierContext { get; init; }

    /// <summary>Pre-resolved llmGenerate outputs keyed by node id.</summary>
    public IReadOnlyDictionary<string, string>? GenerativeOutputs { get; init; }

    /// <summary>When true, llmGenerate nodes pass input through (used to resolve upstream values).</summary>
    public bool GenerativePassThrough { get; init; }

    public int? MappingId { get; init; }
    public int? ProductId { get; init; }
}

/// <summary>
/// Output of a graph evaluation: the field-key → value map plus the effective
/// type per field (mirrors the legacy BuildExportDataAsync result shape).
/// </summary>
public class GraphEvalResult
{
    public Dictionary<string, object?> Outputs { get; } = new();
    public Dictionary<string, string> FieldTypes { get; } = new();

    /// <summary>Value produced at each node (by node id) - for per-node preview in the editor.</summary>
    public Dictionary<string, object?> NodeValues { get; } = new();

    /// <summary>Status per llmGenerate node (ready / pending / failed).</summary>
    public Dictionary<string, GenerativeNodeStatus> GenerativeStatuses { get; set; } = new();

    /// <summary>Values wired into llmGenerate field pins (node id → pin id → value).</summary>
    public Dictionary<string, Dictionary<string, object?>> FieldPinValues { get; } = new(StringComparer.Ordinal);
}

public class GenerativeNodeStatus
{
    public string Status { get; set; } = "pending";
    public string? Output { get; set; }
    public string? OperationId { get; set; }
    public string? ErrorMessage { get; set; }
}
