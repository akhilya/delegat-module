/**
 * graphModel.js - the framework-agnostic brain of the visual node mapping editor.
 *
 * Responsibilities:
 *  - Pin type system + colors (the colored output circles).
 *  - autoMigrate(): turn the existing per-field mappings into an equivalent node graph
 *    (each field => source -> modifier chain -> output), so nothing is lost when the
 *    table is replaced by the canvas.
 *  - Serialization helpers and light validation (cycles, unconnected outputs, type hints).
 *
 * The node `type` and `data` field names MUST stay aligned with the backend
 * GraphNode / GraphNodeData (Services/Marketplace/Graph/MappingGraph.cs).
 */
import { MODIFIER_CATALOG, getModifierLabel } from "../modifierCatalog.js";
import { t, tm } from "@/i18n";

// ---- Pin types & colors --------------------------------------------------

export const PIN_COLORS = {
  string: "#3b82f6", // blue
  select: "#3b82f6",
  number: "#10b981", // green
  integer: "#10b981",
  boolean: "#f59e0b", // amber
  multiselect: "#8b5cf6", // violet
  array: "#8b5cf6",
  any: "#9ca3af", // gray
};

const PIN_TYPE_LABEL_KEYS = {
  string: "text",
  select: "select",
  number: "number",
  integer: "integer",
  boolean: "boolean",
  multiselect: "list",
  array: "list",
  any: "any",
};

export const pinColor = (type) => PIN_COLORS[type] || PIN_COLORS.any;
export const pinTypeLabel = (type) =>
  t(`marketplace.graph.pinTypes.${PIN_TYPE_LABEL_KEYS[type] || PIN_TYPE_LABEL_KEYS.any}`);

/**
 * Output pin type of a modifier node given its input type.
 * forceType resolves to its `type` param (handled by the node component).
 */
export function modifierOutputType(modifierType, inputType) {
  switch (modifierType) {
    case "toText":
    case "join":
    case "template":
    case "takeFirst":
      return "string";
    case "forceType":
      return inputType || "any";
  }
  const category = MODIFIER_CATALOG[modifierType]?.category;
  if (category === "number") return "number";
  if (category === "text") return "string";
  if (category === "array") return "multiselect";
  if (category === "select") return inputType || "select";
  return inputType || "any";
}

/** Walk upstream from a node to find the value type feeding its input. */
export function upstreamOutputType(nodeId, nodes, edges) {
  if (!nodeId || !nodes?.length) return "any";
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const incoming = new Map();
  for (const e of edges || []) {
    const list = incoming.get(e.target) || [];
    list.push(e);
    incoming.set(e.target, list);
  }
  const seen = new Set();
  const stack = [nodeId];
  while (stack.length) {
    const id = stack.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    const ins = incoming.get(id) || [];
    if (!ins.length) {
      const n = byId.get(id);
      return n ? nodeOutputType(n, nodes, edges) : "any";
    }
    for (const e of ins) stack.push(e.source);
  }
  return "any";
}

// ---- Node taxonomy -------------------------------------------------------

export const SOURCE_NODE_TYPES = [
  "attribute",
  "storeAttribute",
  "productField",
  "static",
  "groupName",
  "storeFileUrl",
  "storeFileContent",
  "productFileByName",
  "productFileContentByName",
  "productFilesByExtension",
];
/**
 * Operation nodes that aren't sources/modifiers/output. Each spec drives the generic OpNode
 * component and the inspector. Param keys map to TOP-LEVEL node.data fields (template, delimiter,
 * index) - matching the backend GraphNodeData, not the modifier `params` bag.
 */
export const OP_SPECS = {
  formatString: {
    aliases: ["format", "sprintf", "template", "format string"],
    icon: "Braces",
    inputs: "multi",
    output: "string",
    accepts: null, // any
    params: [
      { key: "template", type: "text", default: "{0}x{1}" },
    ],
  },
  join: {
    aliases: ["join", "concat", "merge", "combine"],
    icon: "Braces",
    inputs: "multi",
    output: "string",
    accepts: null,
    params: [
      { key: "delimiter", type: "text", default: "x" },
    ],
  },
  makeArray: {
    aliases: ["array", "create", "new array", "make array", "collection"],
    icon: "List",
    inputs: "multi",
    output: "array",
    accepts: null,
    params: [],
  },
  arrayAppend: {
    aliases: ["push", "append", "add", "insert"],
    icon: "List",
    inputs: 2,
    output: "array",
    accepts: null,
    params: [],
  },
  splitToArray: {
    aliases: ["split", "explode", "parse", "separate"],
    icon: "List",
    inputs: "single",
    output: "array",
    accepts: ["string", "text", "select"],
    params: [
      { key: "delimiter", type: "text", default: "," },
    ],
  },
  arrayIndex: {
    aliases: ["index", "get", "nth", "at", "element"],
    icon: "List",
    inputs: "single",
    output: "string",
    accepts: ["array", "multiselect"],
    params: [{ key: "index", type: "number", default: 0 }],
  },
  arrayLength: {
    aliases: ["length", "count", "size", "len"],
    icon: "List",
    inputs: "single",
    output: "number",
    accepts: ["array", "multiselect"],
    params: [],
  },

  // ---- General-purpose logic / predicate blocks (pure functions) ----
  ifElse: {
    aliases: ["if", "condition", "ternary", "when"],
    icon: "GitBranch",
    category: "logic",
    inputs: 3,
    output: "any",
    accepts: null,
    params: [],
  },
  not: {
    aliases: ["not", "negate", "invert", "!"],
    icon: "Binary",
    category: "logic",
    inputs: "single",
    output: "boolean",
    accepts: null,
    params: [],
  },
  and: {
    aliases: ["and", "&&", "all", "both"],
    icon: "Binary",
    category: "logic",
    inputs: "multi",
    output: "boolean",
    accepts: null,
    params: [],
  },
  or: {
    aliases: ["or", "||", "any", "OR", "either"],
    icon: "Binary",
    category: "logic",
    inputs: "multi",
    output: "boolean",
    accepts: null,
    params: [],
  },
  equals: {
    aliases: ["equals", "equal", "==", "compare", "eq"],
    icon: "Equal",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: null,
    params: [],
  },
  notEquals: {
    aliases: ["not equal", "!=", "different", "neq"],
    icon: "Equal",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: null,
    params: [],
  },
  greaterThan: {
    aliases: ["greater", ">", "more than", "gt"],
    icon: "Binary",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: ["number", "integer"],
    params: [],
  },
  lessThan: {
    aliases: ["less", "<", "less than", "lt"],
    icon: "Binary",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: ["number", "integer"],
    params: [],
  },
  contains: {
    aliases: ["contains", "includes", "indexOf", "has", "search", "include"],
    icon: "Binary",
    category: "logic",
    inputs: 2,
    output: "boolean",
    accepts: null,
    params: [],
  },
  isEmpty: {
    aliases: ["empty", "null", "nil", "undefined", "is empty", "isnull"],
    icon: "Binary",
    category: "logic",
    inputs: "single",
    output: "boolean",
    accepts: null,
    params: [],
  },
  isValid: {
    aliases: ["filled", "not empty", "has value", "not null", "isnotnull"],
    icon: "Binary",
    category: "logic",
    inputs: "single",
    output: "boolean",
    accepts: null,
    params: [],
  },
};
export const OP_NODE_TYPES = Object.keys(OP_SPECS);

/** Translated display label for an op-node type (was OP_SPECS[type].label). */
export function opLabel(type) {
  return t(`marketplace.graph.opSpecs.${type}.label`);
}

/** Translated label for an op-node's own param (was param.label). */
export function opParamLabel(type, paramKey) {
  return t(`marketplace.graph.opSpecs.${type}.params.${paramKey}`);
}

/** Translated input pin labels for fixed/multi-input op nodes (was spec.inputLabels). */
export function opInputLabels(type) {
  const arr = tm(`marketplace.graph.opSpecs.${type}.inputLabels`);
  return Array.isArray(arr) ? arr : [];
}

let _seq = 0;
export function genId(prefix = "n") {
  _seq += 1;
  return `${prefix}_${Date.now().toString(36)}_${_seq}`;
}

function normalizeSourceType(sourceType) {
  if (typeof sourceType === "number") {
    return (
      [
        "ProductField",
        "CategoryAttribute",
        "StoreAttribute",
        "Static",
        "GroupName",
      ][sourceType] || "Static"
    );
  }
  return sourceType || "Static";
}

// ---- Auto-migration: per-field mappings -> graph -------------------------

const COL_W = 280;
const ROW_H = 150;

/** Fixed id of the single "adapter fields" sink node. */
export const ADAPTER_NODE_ID = "adapter-fields";
/** Fixed id of the dedicated price-API sink node. */
export const ADAPTER_PRICE_NODE_ID = "adapter-price-fields";

/** Vue Flow isNode() - used to filter payloads before setNodes (sparse arrays crash createGraphNodes). */
export function isVueFlowNode(element) {
  return (
    element &&
    typeof element === "object" &&
    "id" in element &&
    "position" in element &&
    !("source" in element && "target" in element)
  );
}

/**
 * Normalize nodes for Vue Flow setNodes/addNodes.
 * Drops invalid entries (edges in nodes[], missing id/position) - Vue Flow leaves
 * holes in its internal array and then throws on parentNode when iterating.
 */
export function sanitizeGraphNodes(rawNodes) {
  const nodes = [];
  const rejected = [];
  for (let i = 0; i < (rawNodes || []).length; i++) {
    const n = rawNodes[i];
    if (!n || typeof n !== "object") {
      rejected.push({ index: i, reason: "not-object", value: n });
      continue;
    }
    if ("source" in n && "target" in n) {
      rejected.push({ index: i, reason: "edge-in-nodes", id: n.id });
      continue;
    }
    if (n.id == null || n.id === "") {
      rejected.push({ index: i, reason: "missing-id", type: n.type });
      continue;
    }
    const pos = n.position;
    nodes.push({
      ...n,
      id: String(n.id),
      type: n.type ?? "default",
      position:
        pos && typeof pos === "object"
          ? { x: Number(pos.x) || 0, y: Number(pos.y) || 0 }
          : { x: 0, y: 0 },
    });
  }
  return { nodes, rejected };
}

/** Drop edges whose endpoints are missing after node sanitization. */
export function sanitizeGraphEdges(rawEdges, nodeIds) {
  const ids = nodeIds instanceof Set ? nodeIds : new Set(nodeIds);
  const edges = [];
  const rejected = [];
  for (const e of rawEdges || []) {
    if (!e?.id || !e?.source || !e?.target) {
      rejected.push({ reason: "incomplete", id: e?.id });
      continue;
    }
    const source = String(e.source);
    const target = String(e.target);
    if (!ids.has(source) || !ids.has(target)) {
      rejected.push({ reason: "dangling", id: e.id, source, target });
      continue;
    }
    edges.push({
      ...e,
      id: String(e.id),
      source,
      target,
      sourceHandle: e.sourceHandle ?? null,
      targetHandle: e.targetHandle ?? null,
    });
  }
  return { edges, rejected };
}

const MIGRATABLE_PRICE_KEYS = new Set([
  "price",
  "oldPrice",
  "minPrice",
  "netPrice",
  "autoActionEnabled",
  "priceStrategyEnabled",
  "minPriceForAutoActionsEnabled",
  "discountedPrice",
  "discount",
]);

/**
 * Build a graph from the page's field mappings. Each enabled field becomes a lane:
 *   [source] -> [modifier] -> ... -> [output]
 * Disabled modifiers are skipped (matching the legacy pipeline).
 *
 * @param {Array} fieldMappings  rows with externalFieldKey/sourceType/.../parsedModifiers
 * @param {Object} sources       availableSources { categoryAttributes, storeAttributes, productFields }
 */
export function autoMigrate(fieldMappings, sources = {}) {
  const nodes = [];
  const edges = [];
  const enabled = (fieldMappings || []).filter((fm) => fm.isEnabled !== false);

  const maxChain = enabled.reduce(
    (m, fm) => Math.max(m, activeModifiers(fm).length),
    0,
  );
  const adapterX = (maxChain + 1) * COL_W;

  const fields = [];
  enabled.forEach((fm, i) => {
    const field = {
      fieldKey: fm.externalFieldKey,
      label: fm.externalFieldName || fm.externalFieldKey,
      outputType: fm.externalFieldType || "string",
      isRequired: !!fm.isRequired,
    };

    const mods = activeModifiers(fm);
    // Trivial 1:1 fields (a standard source, no transforms) bind directly on the adapter - no node/wire.
    const binding = mods.length === 0 ? directBinding(fm) : null;
    if (binding) {
      field.source = binding;
      fields.push(field);
      return;
    }

    const y = i * ROW_H;
    const source = makeSourceNode(fm, { x: 0, y }, sources);
    nodes.push(source);

    let prevId = source.id;
    mods.forEach((mod, k) => {
      const node = {
        id: genId("mod"),
        type: "modifier",
        position: { x: COL_W * (k + 1), y },
        data: { modifierType: mod.type, params: mod.params || {} },
      };
      nodes.push(node);
      edges.push(makeEdge(prevId, node.id));
      prevId = node.id;
    });

    fields.push(field);
    edges.push(makeEdge(prevId, ADAPTER_NODE_ID, `f:${fm.externalFieldKey}`));
  });

  // Single "adapter fields" sink: one input pin per marketplace field.
  nodes.push({
    id: ADAPTER_NODE_ID,
    type: "adapterFields",
    position: { x: adapterX, y: 0 },
    deletable: false,
    data: { fields },
  });

  return { nodes, edges };
}

function activeModifiers(fm) {
  return (fm.parsedModifiers || []).filter(
    (m) => m && m.enabled !== false && m.type,
  );
}

/** Direct adapter-field source binding for a simple 1:1 field mapping (or null if not bindable). */
function directBinding(fm) {
  const kind = normalizeSourceType(fm.sourceType);
  if (kind === "CategoryAttribute" && fm.sourceAttributeId != null)
    return { type: "attribute", attributeId: fm.sourceAttributeId };
  if (kind === "StoreAttribute" && fm.sourceAttributeId != null)
    return { type: "storeAttribute", attributeId: fm.sourceAttributeId };
  if (kind === "ProductField" && fm.sourceField)
    return { type: "productField", field: fm.sourceField };
  if (kind === "GroupName") return { type: "groupName" };
  return null;
}

function makeSourceNode(fm, position, sources) {
  const kind = normalizeSourceType(fm.sourceType);
  const valueType = fm.externalFieldType || "any";

  switch (kind) {
    case "ProductField":
      return node("productField", position, {
        field: fm.sourceField,
        label: productFieldLabel(fm.sourceField, sources) || fm.sourceField,
        valueType: productFieldType(fm.sourceField, sources) || valueType,
      });
    case "CategoryAttribute":
      return node("attribute", position, {
        attributeId: fm.sourceAttributeId,
        label: attributeLabel(fm.sourceAttributeId, sources.categoryAttributes),
        valueType,
      });
    case "StoreAttribute":
      return node("storeAttribute", position, {
        attributeId: fm.sourceAttributeId,
        label: attributeLabel(fm.sourceAttributeId, sources.storeAttributes),
        valueType,
      });
    case "GroupName":
      return node("groupName", position, {
        label: t("marketplace.graph.groupNameLabel"),
        valueType,
      });
    case "Static":
    default:
      return node("static", position, {
        staticValue: fm.staticValue,
        label: fm.staticValue || t("marketplace.graph.staticValueLabel"),
        valueType,
      });
  }
}

function node(type, position, data) {
  return {
    id: genId(type === "attribute" ? "src" : type),
    type,
    position,
    data,
  };
}

function makeEdge(source, target, targetHandle = "in") {
  return {
    id: `${source}->${target}${targetHandle !== "in" ? ":" + targetHandle : ""}`,
    source,
    target,
    sourceHandle: "out",
    targetHandle,
  };
}

function attributeLabel(id, list) {
  const found = (list || []).find((a) => a.id === id);
  return found ? found.name : t("marketplace.graph.attributeFallback", { id: id ?? "?" });
}

export function mergeProductFieldSources(sources) {
  if (!sources) return [];
  return [
    ...(sources.productFields || []),
    ...(sources.stockFields || []),
    ...(sources.priceFields || []),
  ];
}

function productFieldLabel(key, sources) {
  const found = mergeProductFieldSources(sources).find((f) => f.key === key);
  return found ? found.displayName : key;
}

function productFieldType(key, sources) {
  const found = mergeProductFieldSources(sources).find((f) => f.key === key);
  if (found?.type) return found.type;
  if (key === "images") return "array"; // Images = Array<Text>
  return null;
}

// ---- Palette node factories ---------------------------------------------

export function createSourceNode(kind, position, payload = {}) {
  switch (kind) {
    case "attribute":
    case "storeAttribute":
      return node(kind, position, {
        attributeId: payload.id,
        label: payload.name,
        valueType: payload.valueType || "any",
      });
    case "productField":
      return node("productField", position, {
        field: payload.key,
        label: payload.displayName || payload.key,
        valueType:
          payload.type || (payload.key === "images" ? "array" : "string"),
      });
    case "groupName":
      return node("groupName", position, {
        label: t("marketplace.graph.groupNameLabel"),
        valueType: "string",
      });
    case "storeFileUrl":
      return node("storeFileUrl", position, {
        storeFileId: payload.id ?? null,
        label: payload.fileName || t("marketplace.graph.storeFileLabel"),
        valueType: "string",
      });
    case "storeFileContent":
      return node("storeFileContent", position, {
        storeFileId: payload.id ?? null,
        label: payload.fileName || t("marketplace.graph.storeFileLabel"),
        valueType: "string",
      });
    case "productFileByName":
      return node("productFileByName", position, {
        fileName: payload.fileName || "",
        label: payload.fileName || t("marketplace.graph.productFileByNameLabel"),
        valueType: "string",
      });
    case "productFileContentByName":
      return node("productFileContentByName", position, {
        fileName: payload.fileName || "",
        label: payload.fileName || t("marketplace.graph.productFileByNameLabel"),
        valueType: "string",
      });
    case "productFilesByExtension":
      return node("productFilesByExtension", position, {
        fileExtension: payload.fileExtension || "",
        label: payload.fileExtension
          ? `${t("marketplace.graph.productFilesByExtensionPrefix")}${payload.fileExtension}`
          : t("marketplace.graph.productFilesByExtensionLabel"),
        valueType: "array",
      });
    case "static":
    default:
      // Also backs the typed literal nodes (Number / Text / Boolean / List) via valueType.
      // When payload.isAdapterValue is true, creates a "standard value" node with
      // options populated from the adapter field's parsedOptions. Stored as "static" for
      // backend compatibility - backend evaluates staticValue as a plain constant.
      return node("static", position, {
        staticValue: payload.staticValue ?? "",
        label: payload.label || "",
        valueType: payload.valueType || "string",
        ...(payload.isAdapterValue
          ? { isAdapterValue: true, options: payload.options || [] }
          : {}),
      });
  }
}

export function createModifierNode(modifierType, position) {
  const defaults = {};
  (MODIFIER_CATALOG[modifierType]?.params || []).forEach((p) => {
    defaults[p.key] = p.default;
  });
  const data = { modifierType, params: defaults };
  if (modifierType === "llmGenerate") data.fieldPins = [];
  return {
    id: genId("mod"),
    type: "modifier",
    position,
    data,
  };
}

/** Extra input pins on llmGenerate nodes (wired via handle "field:<id>"). */
export function fieldPinsFor(data) {
  if (data?.modifierType !== "llmGenerate") return [];
  return Array.isArray(data.fieldPins) ? data.fieldPins : [];
}

/** Fill missing modifier params from catalog defaults (e.g. systemPrompt on older graphs). */
export function ensureModifierParamDefaults(nodes) {
  if (!nodes?.length) return nodes;
  for (const n of nodes) {
    if (n.type !== "modifier" || !n.data?.modifierType) continue;
    const catalog = MODIFIER_CATALOG[n.data.modifierType];
    if (!catalog?.params?.length) continue;
    const params = { ...(n.data.params || {}) };
    let changed = false;
    for (const p of catalog.params) {
      if (p.default == null) continue;
      const cur = params[p.key];
      if (cur == null || cur === "") {
        params[p.key] = p.default;
        changed = true;
      }
    }
    if (changed) n.data = { ...n.data, params };
  }
  return nodes;
}

export function createOpNode(type, position) {
  const spec = OP_SPECS[type];
  const data = {};
  (spec?.params || []).forEach((p) => {
    data[p.key] = p.default;
  });
  if (spec?.inputs === "multi") data.inputCount = 2;
  return { id: genId(type), type, position, data };
}

/** A transparent pass-through "reroute" node (inserted by double-clicking a wire to bend its path). */
export function createRerouteNode(position) {
  return { id: genId("rr"), type: "reroute", position, data: {} };
}

// ---- Adapter field reconciliation ----------------------------------------

/**
 * Ensure the single "adapterFields" sink exposes a pin for every current marketplace field.
 * The platform's field sync (CategoryImportService) keeps the FieldMapping rows complete, but a saved
 * graph holds a snapshot - so fields added to the category later are missing. Append (never prune, so a
 * wired field is never lost) the missing ones, matching autoMigrate's field shape. Returns `nodes`.
 */
export function syncAdapterFields(nodes, fieldMappings) {
  const adapter = (nodes || []).find((n) => n.type === "adapterFields");
  if (!adapter) return nodes;
  const fields = adapter.data?.fields || [];
  const have = new Set(fields.map((f) => f.fieldKey));
  for (const fm of fieldMappings || []) {
    const key = fm.externalFieldKey;
    if (!key || have.has(key)) continue;
    fields.push({
      fieldKey: key,
      label: fm.externalFieldName || key,
      outputType: fm.externalFieldType || "string",
      isRequired: !!fm.isRequired,
    });
    have.add(key);
  }
  adapter.data = { ...(adapter.data || {}), fields };
  return nodes;
}

/**
 * Ensure the price sink exists and card sink no longer carries price-API fields.
 * Migrates wired fields + edges from adapterFields when opening an older graph.
 */
export function ensurePriceNode(nodes, edges, priceFieldMappings) {
  const list = [...(nodes || [])];
  const edgeList = [...(edges || [])];
  const priceKeys = new Set([
    ...MIGRATABLE_PRICE_KEYS,
    ...(priceFieldMappings || []).map((fm) => fm.externalFieldKey).filter(Boolean),
  ]);

  let priceNode = list.find((n) => n.type === "adapterPriceFields");
  const adapter = list.find((n) => n.type === "adapterFields");

  if (!priceNode) {
    const adapterPos = adapter?.position;
    priceNode = {
      id: ADAPTER_PRICE_NODE_ID,
      type: "adapterPriceFields",
      deletable: false,
      position: adapterPos
        ? { x: adapterPos.x, y: adapterPos.y + 280 }
        : { x: 0, y: 280 },
      data: { fields: [] },
    };
    list.push(priceNode);
  } else if (!priceNode.position) {
    const adapterPos = adapter?.position;
    priceNode.position = adapterPos
      ? { x: adapterPos.x, y: adapterPos.y + 280 }
      : { x: 0, y: 280 };
  }

  if (adapter?.data?.fields?.length) {
    const move = [];
    const keep = [];
    for (const f of adapter.data.fields) {
      if (priceKeys.has(f.fieldKey)) move.push(f);
      else keep.push(f);
    }
    if (move.length) {
      adapter.data = { ...(adapter.data || {}), fields: keep };
      const have = new Set((priceNode.data?.fields || []).map((f) => f.fieldKey));
      const merged = [...(priceNode.data?.fields || [])];
      for (const f of move) {
        if (!have.has(f.fieldKey)) {
          merged.push(f);
          have.add(f.fieldKey);
        }
      }
      priceNode.data = { ...(priceNode.data || {}), fields: merged };
      for (const e of edgeList) {
        if (
          e.target === ADAPTER_NODE_ID &&
          String(e.targetHandle || "").startsWith("f:")
        ) {
          const key = String(e.targetHandle).slice(2);
          if (priceKeys.has(key)) e.target = ADAPTER_PRICE_NODE_ID;
        }
      }
    }
  }

  syncAdapterPriceFields(list, priceFieldMappings);
  return { nodes: list, edges: edgeList };
}

export function syncAdapterPriceFields(nodes, priceFieldMappings) {
  const priceNode = (nodes || []).find((n) => n.type === "adapterPriceFields");
  if (!priceNode) return nodes;
  const fields = priceNode.data?.fields || [];
  const have = new Set(fields.map((f) => f.fieldKey));
  for (const fm of priceFieldMappings || []) {
    const key = fm.externalFieldKey;
    if (!key || have.has(key)) continue;
    fields.push({
      fieldKey: key,
      label: fm.externalFieldName || key,
      outputType: fm.externalFieldType || "string",
      isRequired: !!fm.isRequired,
    });
    have.add(key);
  }
  priceNode.data = { ...(priceNode.data || {}), fields };
  return nodes;
}

// ---- Display helpers -----------------------------------------------------

export function modifierNodeLabel(data) {
  return getModifierLabel({ type: data.modifierType, params: data.params });
}

export function opSummary(type, data) {
  if (type === "formatString") return data.template || "";
  if (type === "join" || type === "splitToArray")
    return data.delimiter ? `«${data.delimiter}»` : "";
  if (type === "arrayIndex") return `[${data.index ?? 0}]`;
  return "";
}

// ---- Validation ----------------------------------------------------------

/**
 * Returns an array of { level: "error"|"warning", message, nodeId? }.
 */
export function validateGraph(nodes, edges) {
  const issues = [];
  const incoming = new Map();
  nodes.forEach((n) => incoming.set(n.id, []));
  edges.forEach((e) => {
    if (incoming.has(e.target)) incoming.get(e.target).push(e);
  });

  nodes.forEach((n) => {
    const ins = incoming.get(n.id) || [];

    // Required adapter/price fields with no wire feeding their pin.
    if (n.type === "adapterFields" || n.type === "adapterPriceFields") {
      const connected = new Set(
        ins.map((e) => (e.targetHandle || "").replace(/^f:/, "")),
      );
      (n.data?.fields || []).forEach((f) => {
        if (!f.isRequired) return;
        if (f.omit) {
          issues.push({
            level: "warning",
            nodeId: n.id,
            message: t("marketplace.graph.validation.requiredFieldOmitted", {
              label: f.label || f.fieldKey,
            }),
          });
          return;
        }
        if (f.source || f.aliasOf || connected.has(f.fieldKey)) return;
        const key =
          n.type === "adapterPriceFields"
            ? "marketplace.graph.validation.fieldNotConnectedPrice"
            : "marketplace.graph.validation.fieldNotConnectedAdapter";
        issues.push({
          level: "warning",
          nodeId: n.id,
          message: t(key, { label: f.label || f.fieldKey }),
        });
      });
    }

    if (
      (n.type === "modifier" || OP_NODE_TYPES.includes(n.type)) &&
      ins.length === 0
    ) {
      issues.push({
        level: "warning",
        nodeId: n.id,
        message: t("marketplace.graph.validation.nodeWithoutInput"),
      });
    }

    if (n.type === "modifier" && !MODIFIER_CATALOG[n.data?.modifierType]) {
      issues.push({
        level: "error",
        nodeId: n.id,
        message: t("marketplace.graph.validation.unknownModifier", { type: n.data?.modifierType }),
      });
    }
  });

  if (hasCycle(nodes, edges)) {
    issues.push({
      level: "error",
      message: t("marketplace.graph.validation.graphHasCycle"),
    });
  }

  return issues;
}

function hasCycle(nodes, edges) {
  const adj = new Map(nodes.map((n) => [n.id, []]));
  edges.forEach((e) => {
    if (adj.has(e.source)) adj.get(e.source).push(e.target);
  });
  const state = new Map(); // 0=unvisited,1=in-stack,2=done
  let cycle = false;

  const visit = (id) => {
    if (state.get(id) === 1) {
      cycle = true;
      return;
    }
    if (state.get(id) === 2) return;
    state.set(id, 1);
    (adj.get(id) || []).forEach(visit);
    state.set(id, 2);
  };

  nodes.forEach((n) => {
    if (!state.get(n.id)) visit(n.id);
  });
  return cycle;
}

/**
 * Whether the graph fans in anywhere (a node with >1 incoming edge or a combine node
 * with multiple inputs). Fan-in fields are export-only (no reversible import).
 */
export function hasFanIn(nodes, edges) {
  const counts = new Map();
  edges.forEach((e) => counts.set(e.target, (counts.get(e.target) || 0) + 1));
  return [...counts.values()].some((c) => c > 1);
}

// ---- Output type + connect helpers --------------------------------------

/** Output pin type for any node (for drag-to-empty menu filtering). */
export function nodeOutputType(node, nodes, edges) {
  if (!node) return "any";
  switch (node.type) {
    case "attribute":
    case "storeAttribute":
    case "productField":
    case "static":
    case "groupName":
    case "storeFileUrl":
    case "storeFileContent":
    case "productFileByName":
    case "productFileContentByName":
    case "productFilesByExtension":
      return node.data?.valueType || "any";
    case "modifier": {
      const inputType =
        nodes && edges ? upstreamOutputType(node.id, nodes, edges) : "any";
      return node.data?.modifierType === "forceType"
        ? node.data?.params?.type || inputType || "any"
        : modifierOutputType(node.data?.modifierType, inputType);
    }
    case "adapterFields":
    case "adapterPriceFields":
      return "any";
    default:
      return OP_SPECS[node.type]?.output || "any";
  }
}

/** The id of a node's first input handle (where a dragged wire should attach). */
export function firstInputHandle(node) {
  const spec = node && OP_SPECS[node.type];
  const multi =
    spec &&
    (spec.inputs === "multi" ||
      (typeof spec.inputs === "number" && spec.inputs >= 2));
  return multi ? "in-0" : "in";
}

/**
 * Operand params of a modifier that can be driven by an optional override pin (handle `p:<key>`).
 * Scalar operands only (number/text) - config params (select/checkbox) and complex editors
 * (valueMap/optionMap/template/tagInput) keep their in-block control.
 */
export function overridableParams(modifierType) {
  return (MODIFIER_CATALOG[modifierType]?.params || []).filter(
    (p) => p.type === "number" || p.type === "text",
  );
}

/**
 * The type a node's INPUT handle expects (for the drag-from-input → producer menu).
 * Sources have no input ⇒ null. Lists collapse to a representative type; null means "any".
 */
export function nodeInputType(node, handleId) {
  if (!node) return null;
  switch (node.type) {
    case "modifier": {
      // A param-override pin (`p:<key>`) expects that param's type, not the value type.
      if (String(handleId || "").startsWith("p:")) {
        const k = String(handleId).slice(2);
        const p = overridableParams(node.data?.modifierType).find(
          (x) => x.key === k,
        );
        return p ? (p.type === "number" ? "number" : "string") : null;
      }
      if (String(handleId || "").startsWith("field:")) return "string";
      const list = MODIFIER_CATALOG[node.data?.modifierType]?.applicableTypes;
      return list && list.length ? list[0] : null;
    }
    case "adapterFields":
    case "adapterPriceFields": {
      const key = String(handleId || "").startsWith("f:")
        ? String(handleId).slice(2)
        : null;
      const field = (node.data?.fields || []).find((f) => f.fieldKey === key);
      return field?.outputType || null;
    }
    default: {
      const accepts = OP_SPECS[node.type]?.accepts;
      return accepts && accepts.length ? accepts[0] : null;
    }
  }
}

/** Output type a context-menu descriptor would produce once instantiated. */
export function descriptorOutputType(desc) {
  if (!desc) return "any";
  if (desc.group === "source") {
    switch (desc.kind) {
      case "attribute":
      case "storeAttribute":
        return desc.payload?.valueType || "any";
      case "productField":
        return (
          desc.payload?.type ||
          (desc.payload?.key === "images" ? "array" : "string")
        );
      case "storeFileUrl":
      case "storeFileContent":
      case "productFileByName":
      case "productFileContentByName":
        return "string";
      case "productFilesByExtension":
        return "array";
      case "static":
        return desc.payload?.valueType || "string"; // typed literal nodes
      default:
        return "string"; // groupName
    }
  }
  if (desc.group === "modifier")
    return modifierOutputType(
      desc.modifierType,
      desc.payload?.inputType || "any",
    );
  if (desc.group === "op") return OP_SPECS[desc.opType]?.output || "any";
  return "any";
}

const TYPE_SYNONYMS = {
  string: "string",
  text: "string",
  select: "string",
  number: "number",
  integer: "number",
  array: "array",
  multiselect: "array",
  boolean: "boolean",
};
/** Lenient pin-type compatibility (equal, either "any", or same synonym group). */
export function typeCompatible(a, b) {
  if (!a || !b || a === "any" || b === "any") return true;
  return (TYPE_SYNONYMS[a] || a) === (TYPE_SYNONYMS[b] || b);
}

/** Whether a modifier type round-trips on import (false ⇒ shows the ⚠ on the block). */
export function modifierIsReversible(type) {
  return MODIFIER_CATALOG[type]?.reversible !== false;
}

// ---- Reversibility analysis + import projection -------------------------

/**
 * For each adapter field, trace its lane and decide whether it can be IMPORTED back
 * (reverse), and project a FieldMapping (source + modifiersJson) so the existing import path works.
 *
 * Returns { analysis: { fieldKey: { importable, reversible } }, projections: [FieldProjection] }.
 * - importable: single-input chain, source = category/store attribute, only modifier nodes (no op/fan-in).
 * - reversible: importable AND every modifier is reversible (else import writes the raw value).
 * Non-importable lanes project to a Static source so the import path skips them.
 */
export function analyzeFields(nodes, edges) {
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const incoming = new Map();
  nodes.forEach((n) => incoming.set(n.id, []));
  (edges || []).forEach((e) => {
    if (incoming.has(e.target)) incoming.get(e.target).push(e);
  });

  const analysisBySink = {};
  const projections = [];
  const sinks = nodes.filter(
    (n) => n.type === "adapterFields" || n.type === "adapterPriceFields",
  );
  if (!sinks.length) return { analysis: {}, analysisBySink, projections };

  const exportOnly = (key) => ({
    externalFieldKey: key,
    sourceType: "Static",
    sourceAttributeId: null,
    modifiersJson: null,
  });

  const putEntry = (sinkId, key, entry) => {
    if (!analysisBySink[sinkId]) analysisBySink[sinkId] = {};
    analysisBySink[sinkId][key] = entry;
  };

  for (const adapter of sinks) {
  for (const f of adapter.data?.fields || []) {
    const key = f.fieldKey;

    // Explicitly omitted from export/import — no projection, no pin analysis.
    if (f.omit) {
      putEntry(adapter.id, key, {
        importable: false,
        reversible: false,
        connected: false,
        omitted: true,
      });
      continue;
    }

    // Merged duplicate of another adapter field — value copied at eval time.
    if (f.aliasOf) {
      putEntry(adapter.id, key, {
        importable: false,
        reversible: false,
        connected: true,
        aliasOf: f.aliasOf,
      });
      continue;
    }

    // A field bound directly to a standard source (dropdown in the adapter) has no pin/edge.
    if (f.source?.type) {
      const s = f.source;
      if (s.type === "attribute" || s.type === "storeAttribute") {
        putEntry(adapter.id, key, {
          importable: true, reversible: true, connected: true,
          _sk: `${s.type}:${s.attributeId}`,
        });
        projections.push({
          externalFieldKey: key,
          sourceType:
            s.type === "attribute" ? "CategoryAttribute" : "StoreAttribute",
          sourceAttributeId: s.attributeId ?? null,
          modifiersJson: null,
        });
      } else {
        const sk = s.type === "productField"
          ? `productField:${s.field}`
          : s.type === "groupName"
          ? "groupName"
          : `${s.type}:${JSON.stringify(s)}`;
        if (s.type === "productField") {
          // productField bindings are bidirectional by default.
          // explicitImport=true lets the fan-out detector pick this one as the "winner",
          // even if other graph paths to the same productField are explicitly export-only.
          putEntry(adapter.id, key, { importable: true, reversible: true, connected: true, _sk: sk, explicitImport: true });
          projections.push({
            externalFieldKey: key,
            sourceType: "ProductField",
            sourceField: s.field,
            modifiersJson: null,
          });
        } else {
          putEntry(adapter.id, key, { importable: false, reversible: false, connected: true, _sk: sk });
          projections.push(exportOnly(key));
        }
      }
      continue;
    }

    const inEdge = (incoming.get(adapter.id) || []).find(
      (e) => e.targetHandle === `f:${key}`,
    );

    let importable = false;
    let reversible = false;
    let projection = exportOnly(key);
    let terminalSk = null;
    let explicitImport = false;

    if (inEdge) {
      const mods = [];
      const seen = new Set();
      let cur = byId.get(inEdge.source);
      let ok = true;
      while (cur && !seen.has(cur.id)) {
        seen.add(cur.id);
        const ins = incoming.get(cur.id) || [];
        if (cur.type === "modifier") {
          if (!MODIFIER_CATALOG[cur.data?.modifierType] || ins.length !== 1) {
            ok = false;
            break;
          }
          mods.unshift({
            type: cur.data.modifierType,
            params: cur.data.params || {},
            enabled: true,
          });
          cur = byId.get(ins[0].source);
        } else if (cur.type === "reroute") {
          // Reroute is a transparent pass-through - step through it without affecting the lane.
          if (ins.length !== 1) {
            ok = false;
            break;
          }
          cur = byId.get(ins[0].source);
        } else if (cur.type === "attribute" || cur.type === "storeAttribute") {
          if (cur.data?.attributeId != null) {
            const forcedExport = cur.data?.importable === false;
            const forcedImport = cur.data?.importable === true;
            const modsReversible =
              ok && mods.every((m) => MODIFIER_CATALOG[m.type]?.reversible);
            importable = !forcedExport && (forcedImport || ok);
            reversible = importable && modsReversible;
            if (importable) {
              projection = {
                externalFieldKey: key,
                sourceType:
                  cur.type === "attribute"
                    ? "CategoryAttribute"
                    : "StoreAttribute",
                sourceAttributeId: cur.data.attributeId,
                modifiersJson: mods.length ? JSON.stringify(mods) : null,
              };
              terminalSk = `${cur.type}:${cur.data.attributeId}`;
              if (forcedImport || modsReversible) explicitImport = true;
            }
          }
          break;
        } else if (SOURCE_NODE_TYPES.includes(cur.type) && cur.type !== "static") {
          // Non-attribute source (productField, groupName, file types): importable for
          // animation purposes when the modifier chain is fully reversible.
          // Backend projection stays exportOnly - no actual write-back for these types.
          // productField / attribute / storeAttribute: importable=false forces export-only;
          // importable=true can win fan-out resolution (attribute lanes also affect import projection).
          const forcedExport = cur.type === "productField" && cur.data?.importable === false;
          const forcedImport = cur.type === "productField" && cur.data?.importable === true;
          const modsReversible = ok && mods.every((m) => MODIFIER_CATALOG[m.type]?.reversible);
          importable = !forcedExport && (forcedImport || modsReversible);
          // Only mark as explicit winner when modifier chain is reversible - a path through an
          // irreversible modifier (e.g. truncate) must never be explicitImport=true, otherwise
          // two paths to the same _sk both become explicit and neither wins fan-out resolution.
          if (importable && modsReversible) explicitImport = true;
          if (importable) {
            terminalSk =
              cur.type === "productField"
                ? `productField:${cur.data?.field ?? cur.id}`
                : `${cur.type}:${cur.id}`;
          }
          break;
        } else {
          // static value or op nodes (fan-in / lossy) ⇒ always export-only
          ok = false;
          break;
        }
      }
      if (!importable) projection = exportOnly(key);
    }

    putEntry(adapter.id, key, { importable, reversible, connected: !!inEdge, _sk: terminalSk, explicitImport });
    projections.push(projection);
  }
  }

  // Detect fan-out: if 2+ fields share the same logical source (via wires or direct bindings),
  // import is ambiguous - downgrade all of them to export-only and warn.
  const skToRefs = new Map();
  for (const [sinkId, fields] of Object.entries(analysisBySink)) {
    for (const [key, v] of Object.entries(fields)) {
      if (v._sk) {
        const arr = skToRefs.get(v._sk) ?? [];
        arr.push({ sinkId, key });
        skToRefs.set(v._sk, arr);
      }
    }
  }
  for (const refs of skToRefs.values()) {
    if (refs.length > 1) {
      // If exactly one path was explicitly marked importable by the user, let it win.
      // When several price-API fields bind to the same product field, prefer canonical "price".
      const explicit = refs.filter(({ sinkId, key }) => analysisBySink[sinkId][key].explicitImport);
      let winner = explicit.length === 1 ? explicit[0] : null;
      if (!winner && explicit.length > 1) {
        winner = explicit.find(({ key }) => key === "price") ?? null;
      }
      for (const { sinkId, key } of refs) {
        if (winner && winner.sinkId === sinkId && winner.key === key) continue;
        const entry = analysisBySink[sinkId][key];
        entry.importable = false;
        entry.reversible = false;
        entry.fanOut = true;
        const pidx = projections.findIndex((p) => p.externalFieldKey === key);
        if (pidx !== -1) projections[pidx] = exportOnly(key);
      }
    }
  }
  for (const fields of Object.values(analysisBySink)) {
    for (const v of Object.values(fields)) delete v._sk;
  }

  const analysis = {};
  for (const fields of Object.values(analysisBySink)) {
    Object.assign(analysis, fields);
  }

  return { analysis, analysisBySink, projections };
}

/**
 * For each adapter field, a short label of what feeds it - for display inside the adapter block.
 * Walks back through single-input pass-throughs (modifier / single-input op / reroute) to the
 * originating source node's label; on fan-in (>1 input) returns the producing op's display label.
 * Returns { fieldKey: label } (unconnected fields omitted).
 */
export function adapterSourceLabels(nodes, edges) {
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const incoming = new Map();
  nodes.forEach((n) => incoming.set(n.id, []));
  (edges || []).forEach((e) => {
    if (incoming.has(e.target)) incoming.get(e.target).push(e);
  });

  const adapter = nodes.find((n) => n.type === "adapterFields");
  const out = {};
  if (!adapter) return out;
  const isSource = new Set(SOURCE_NODE_TYPES);

  for (const f of adapter.data?.fields || []) {
    const key = f.fieldKey;
    const inEdge = (incoming.get(adapter.id) || []).find(
      (e) => e.targetHandle === `f:${key}`,
    );
    if (!inEdge) continue;

    let cur = byId.get(inEdge.source);
    const seen = new Set();
    let label = null;
    while (cur && !seen.has(cur.id)) {
      seen.add(cur.id);
      if (isSource.has(cur.type)) {
        label = cur.data?.label || cur.type;
        break;
      }
      const ins = incoming.get(cur.id) || [];
      if (ins.length === 1) {
        cur = byId.get(ins[0].source); // transparent pass-through
      } else {
        // fan-in / op - name the producing node
        label =
          (OP_SPECS[cur.type] ? opLabel(cur.type) : null) ||
          (cur.type === "modifier"
            ? MODIFIER_CATALOG[cur.data?.modifierType]?.displayName
            : null) ||
          cur.data?.label ||
          cur.type;
        break;
      }
    }
    if (label) out[key] = label;
  }
  return out;
}
