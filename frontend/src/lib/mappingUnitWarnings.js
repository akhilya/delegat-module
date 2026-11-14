import {
  getAttributeUnit,
  getUnitConversion,
  normalizeUnitCode,
} from "./attributeUnits.js";

// Arithmetic changes the magnitude, so the source unit no longer describes the value.
const UNIT_CHANGING_MODIFIERS = new Set([
  "multiply",
  "divide",
  "add",
  "subtract",
]);

function definitionFor(sources, type, id) {
  const list =
    type === "attribute"
      ? sources?.categoryAttributes
      : sources?.storeAttributes;
  return (list || []).find((item) => String(item.id) === String(id));
}

function unitForSource(source, sources) {
  if (!source) return null;
  if (source.type === "attribute" || source.type === "storeAttribute") {
    // The attribute definition is the source of truth; a cached unit may be stale.
    return (
      definitionFor(sources, source.type, source.attributeId)?.unitCode ??
      source.unitCode ??
      null
    );
  }
  return null;
}

/**
 * Unit of the value arriving from `nodeId`. `opaque` is true when arithmetic in the chain
 * changed the magnitude, so the unit cannot be inferred (and no warning is meaningful).
 */
function resolveUnitInfo(nodeId, nodesById, incomingByTarget, sources, seen) {
  if (!nodeId || seen.has(nodeId)) return { unit: null, opaque: false };
  seen.add(nodeId);
  const node = nodesById.get(nodeId);
  if (!node) return { unit: null, opaque: false };
  if (node.type === "attribute" || node.type === "storeAttribute") {
    return {
      unit:
        definitionFor(sources, node.type, node.data?.attributeId)?.unitCode ??
        node.data?.unitCode ??
        null,
      opaque: false,
    };
  }
  if (node.type === "modifier" && node.data?.modifierType === "unitConversion")
    return { unit: node.data?.params?.toUnit ?? null, opaque: false };
  if (
    node.type === "modifier" &&
    UNIT_CHANGING_MODIFIERS.has(node.data?.modifierType)
  )
    return { unit: null, opaque: true };
  if (node.type === "reroute" || node.type === "modifier") {
    const incoming = incomingByTarget.get(nodeId) || [];
    if (incoming.length !== 1) return { unit: null, opaque: false };
    return resolveUnitInfo(
      incoming[0].source,
      nodesById,
      incomingByTarget,
      sources,
      seen,
    );
  }
  return { unit: null, opaque: false };
}

export function resolveTerminalUnit(
  nodeId,
  nodesById,
  incomingByTarget,
  sources,
  seen = new Set(),
) {
  return resolveUnitInfo(nodeId, nodesById, incomingByTarget, sources, seen)
    .unit;
}

export function unitMismatch(sourceUnit, targetUnit) {
  const source = normalizeUnitCode(sourceUnit);
  const target = normalizeUnitCode(targetUnit);
  if (!source && !target) return null;
  if (source && target && source.toLowerCase() === target.toLowerCase())
    return null;
  const from = getAttributeUnit(source);
  const to = getAttributeUnit(target);
  if (from && to && from.code === to.code) return null;
  const kind =
    from && to
      ? from.dimension === to.dimension
        ? "different"
        : "incompatible"
      : "unspecified";
  return {
    kind,
    sourceUnit: source,
    targetUnit: target,
    sourceLabel: from?.symbol || source || null,
    targetLabel: to?.symbol || target || null,
    convertible: !!getUnitConversion(source, target),
  };
}

/** Add transient warnings to graph wires and directly-bound adapter fields. */
export function analyzeUnitWarnings(nodes = [], edges = [], sources = {}) {
  const nodesById = new Map((nodes || []).map((node) => [node.id, node]));
  const incomingByTarget = new Map();
  for (const edge of edges || []) {
    const list = incomingByTarget.get(edge.target) || [];
    list.push(edge);
    incomingByTarget.set(edge.target, list);
  }

  const edgeWarnings = new Map();
  const directWarnings = {};
  for (const sink of nodes || []) {
    if (sink.type !== "adapterFields" && sink.type !== "adapterPriceFields")
      continue;
    for (const field of sink.data?.fields || []) {
      if (!field?.fieldKey) continue;
      const targetUnit = field.unitCode ?? field.unit ?? null;
      if (field.source) {
        const warning = unitMismatch(
          unitForSource(field.source, sources),
          targetUnit,
        );
        if (warning) {
          directWarnings[sink.id] ??= {};
          directWarnings[sink.id][field.fieldKey] = {
            ...warning,
            sinkId: sink.id,
            fieldKey: field.fieldKey,
          };
        }
      }
      for (const edge of incomingByTarget.get(sink.id) || []) {
        if (edge.targetHandle !== `f:${field.fieldKey}`) continue;
        const info = resolveUnitInfo(
          edge.source,
          nodesById,
          incomingByTarget,
          sources,
          new Set(),
        );
        if (info.opaque) continue;
        const warning = unitMismatch(info.unit, targetUnit);
        if (warning) edgeWarnings.set(edge.id, { ...warning, edgeId: edge.id });
      }
    }
  }
  return { edgeWarnings, directWarnings };
}
