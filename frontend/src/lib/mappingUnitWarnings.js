import {
  getAttributeUnit,
  getUnitConversion,
  normalizeUnitCode,
} from "./attributeUnits.js";
import { OP_NODE_TYPES } from "../components/marketplace/graph/graphTypes.js";

const OP_TYPES = new Set(OP_NODE_TYPES);

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
  // Ops propagate a unit only when every connected input agrees on it.
  if (OP_TYPES.has(node.type)) {
    const inputs = (incomingByTarget.get(nodeId) || []).filter(
      (edge) =>
        edge.targetHandle === "in" || edge.targetHandle?.startsWith("in-"),
    );
    if (!inputs.length) return { unit: null, opaque: true };
    let unit = null;
    for (const edge of inputs) {
      const info = resolveUnitInfo(
        edge.source,
        nodesById,
        incomingByTarget,
        sources,
        seen,
      );
      if (info.opaque || info.unit == null) return { unit: null, opaque: true };
      const normalized = normalizeUnitCode(info.unit);
      if (unit && normalized && unit.toLowerCase() !== normalized.toLowerCase())
        return { unit: null, opaque: true };
      unit = unit ?? normalized ?? info.unit;
    }
    return { unit, opaque: false };
  }
  // Literals, product fields, files and other terminals cannot carry a unit:
  // opaque (no "not specified" warning) rather than a bogus missing unit.
  return { unit: null, opaque: true };
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
  function targetUnits(id, seen = new Set()) {
    const units = new Set();
    if (seen.has(id)) return units;
    seen.add(id);
    for (const edge of outgoing.get(id) || []) {
      const target = byId.get(edge.target);
      if (edge.targetHandle?.startsWith("f:")) {
        const field = target?.data?.fields?.find(
          (f) => `f:${f.fieldKey}` === edge.targetHandle,
        );
        const unit = normalizeUnitCode(field?.unitCode ?? field?.unit);
        if (unit) units.add(unit);
        continue;
      }
      // Only value inputs constrain the flowing unit; operand/auxiliary pins do not.
      if (edge.targetHandle && edge.targetHandle !== "in") continue;
      if (
        target?.type === "modifier" &&
        target.data?.modifierType === "unitConversion"
      ) {
        // An automatic conversion gets its input unit from upstream, whereas a manual
        // conversion provides an independent constraint on the intermediate unit.
        if (target.data.params?.autoDetect === false) {
          const unit = normalizeUnitCode(target.data.params.fromUnit);
          if (unit) units.add(unit);
        }
      } else if (
        target?.type === "reroute" ||
        (target?.type === "modifier" &&
          !UNIT_CHANGING_MODIFIERS.has(target.data?.modifierType))
      ) {
        for (const unit of targetUnits(edge.target, new Set(seen)))
          units.add(unit);
      }
    }
    return units;
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
    const destinations = targetUnits(node.id);
    const units =
      params.autoDetect === false
        ? params
        : {
            ...params,
            fromUnit: inputs.length === 1 ? sourceUnit(inputs[0].source) : null,
            toUnit: destinations.size === 1 ? [...destinations][0] : null,
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
        // Only attributes can carry a unit; other mapping kinds cannot miss one.
        const capable =
          field.source.type === "attribute" ||
          field.source.type === "storeAttribute";
        const warning = capable
          ? unitMismatch(unitForSource(field.source, sources), targetUnit)
          : null;
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
