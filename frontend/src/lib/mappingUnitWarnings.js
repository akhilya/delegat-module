import {
  getAttributeUnit,
  getUnitConversion,
  normalizeUnitCode,
} from "./attributeUnits.js";

// Selection ops preserve the quantity (and its unit) when all inputs share one.
const UNIT_PRESERVING_OPS = new Set(["min", "max"]);

const OP_INPUT_HANDLE = /^(in|in-\d+)$/;

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
 * Unit of the value arriving from `nodeId`. `opaque` is true when the unit
 * cannot be inferred at all (arithmetic changed the magnitude, a literal or
 * type-changing op produced the value), so no warning is meaningful.
 * `state` is a per-walk {memo, visiting} pair: memo keeps shared upstream
 * branches resolvable, visiting only guards real cycles.
 */
function resolveUnitInfo(nodeId, nodesById, incomingByTarget, sources, state) {
  if (!nodeId) return { unit: null, opaque: false };
  if (state.memo.has(nodeId)) return state.memo.get(nodeId);
  if (state.visiting.has(nodeId)) return { unit: null, opaque: false };
  const node = nodesById.get(nodeId);
  if (!node) return { unit: null, opaque: false };
  state.visiting.add(nodeId);
  const info = nodeUnitInfo(node, nodesById, incomingByTarget, sources, state);
  state.visiting.delete(nodeId);
  state.memo.set(nodeId, info);
  return info;
}

function nodeUnitInfo(node, nodesById, incomingByTarget, sources, state) {
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
    const incoming = (incomingByTarget.get(node.id) || []).filter(
      (edge) => !edge.targetHandle || edge.targetHandle === "in",
    );
    if (incoming.length !== 1) return { unit: null, opaque: false };
    return resolveUnitInfo(
      incoming[0].source,
      nodesById,
      incomingByTarget,
      sources,
      state,
    );
  }
  // Selection ops propagate the unit only when every connected input agrees.
  if (UNIT_PRESERVING_OPS.has(node.type)) {
    const inputs = (incomingByTarget.get(node.id) || []).filter((edge) =>
      OP_INPUT_HANDLE.test(edge.targetHandle || ""),
    );
    if (!inputs.length) return { unit: null, opaque: true };
    let unit = null;
    for (const edge of inputs) {
      const info = resolveUnitInfo(
        edge.source,
        nodesById,
        incomingByTarget,
        sources,
        state,
      );
      if (info.opaque || info.unit == null) return { unit: null, opaque: true };
      const normalized = normalizeUnitCode(info.unit);
      if (unit && normalized && unit.toLowerCase() !== normalized.toLowerCase())
        return { unit: null, opaque: true };
      unit = unit ?? normalized ?? info.unit;
    }
    return { unit, opaque: false };
  }
  // Literals, product fields, files, type-changing ops and other terminals
  // cannot carry a unit: opaque rather than a bogus "not specified" warning.
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
    // Selection ops keep the unit when every input resolves to the same one.
    if (UNIT_PRESERVING_OPS.has(node?.type)) {
      const inputs = (incoming.get(id) || []).filter((e) =>
        OP_INPUT_HANDLE.test(e.targetHandle || ""),
      );
      if (!inputs.length) return null;
      let unit = null;
      for (const edge of inputs) {
        const resolved = normalizeUnitCode(
          sourceUnit(edge.source, new Set(seen)),
        );
        if (!resolved) return null;
        if (unit && unit.toLowerCase() !== resolved.toLowerCase()) return null;
        unit = resolved;
      }
      return unit;
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
) {
  return resolveUnitInfo(nodeId, nodesById, incomingByTarget, sources, {
    memo: new Map(),
    visiting: new Set(),
  }).unit;
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
  const sharedState = { memo: new Map(), visiting: new Set() };
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
          sharedState,
        );
        if (info.opaque) continue;
        const warning = unitMismatch(info.unit, targetUnit);
        if (warning) edgeWarnings.set(edge.id, { ...warning, edgeId: edge.id });
      }
    }
  }
  return { edgeWarnings, directWarnings };
}
