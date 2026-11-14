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
      (definitionFor(sources, source.type, source.attributeId) || source)
        .unitCode ?? null
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
        (definitionFor(sources, node.type, node.data?.attributeId) || node.data)
          ?.unitCode ?? null,
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
    const incoming = (incomingByTarget.get(nodeId) || []).filter(
      (edge) => !edge.targetHandle || edge.targetHandle === "in",
    );
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

/** Resolve automatic conversions from current definitions and wiring, never cached params. */
export function resolveConversionUnits(nodes = [], edges = [], sources = {}) {
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const incoming = new Map();
  const outgoing = new Map();
  for (const edge of edges) {
    incoming.set(edge.target, [...(incoming.get(edge.target) || []), edge]);
    outgoing.set(edge.source, [...(outgoing.get(edge.source) || []), edge]);
  }
  const resolved = new Map();
  const visiting = new Set();
  function targetUnit(id, seen = new Set()) {
    if (seen.has(id)) return null;
    seen.add(id);
    const units = (outgoing.get(id) || []).map((edge) => {
      const target = byId.get(edge.target);
      if (edge.targetHandle?.startsWith("f:")) {
        const field = target?.data?.fields?.find(
          (f) => `f:${f.fieldKey}` === edge.targetHandle,
        );
        return normalizeUnitCode(field?.unitCode ?? field?.unit);
      }
      if (edge.targetHandle && edge.targetHandle !== "in") return null;
      if (
        target?.type === "reroute" ||
        (target?.type === "modifier" &&
          target.data?.modifierType !== "unitConversion" &&
          !UNIT_CHANGING_MODIFIERS.has(target.data?.modifierType))
      )
        return targetUnit(edge.target, new Set(seen));
      return null;
    });
    return units.length && units.every((unit) => unit && unit === units[0])
      ? units[0]
      : null;
  }
  function sourceUnit(id, seen = new Set()) {
    if (seen.has(id)) return null;
    seen.add(id);
    const node = byId.get(id);
    if (
      node?.type === "modifier" &&
      node.data?.modifierType === "unitConversion"
    )
      return resolve(node)?.toUnit ?? null;
    if (node?.type === "attribute" || node?.type === "storeAttribute")
      return normalizeUnitCode(
        unitForSource({ ...node.data, type: node.type }, sources),
      );
    if (
      node?.type === "reroute" ||
      (node?.type === "modifier" &&
        !UNIT_CHANGING_MODIFIERS.has(node.data?.modifierType))
    ) {
      const inputs = (incoming.get(id) || []).filter(
        (e) => !e.targetHandle || e.targetHandle === "in",
      );
      return inputs.length === 1 ? sourceUnit(inputs[0].source, seen) : null;
    }
    return null;
  }
  function resolve(node) {
    if (resolved.has(node.id)) return resolved.get(node.id);
    if (visiting.has(node.id)) return null;
    visiting.add(node.id);
    const params = node.data?.params || {};
    const inputs = (incoming.get(node.id) || []).filter(
      (e) => !e.targetHandle || e.targetHandle === "in",
    );
    const units =
      params.autoDetect === false
        ? params
        : {
            ...params,
            fromUnit: inputs.length === 1 ? sourceUnit(inputs[0].source) : null,
            toUnit: targetUnit(node.id),
          };
    resolved.set(node.id, units);
    visiting.delete(node.id);
    return units;
  }
  for (const node of nodes)
    if (
      node.type === "modifier" &&
      node.data?.modifierType === "unitConversion"
    )
      resolve(node);
  return resolved;
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
