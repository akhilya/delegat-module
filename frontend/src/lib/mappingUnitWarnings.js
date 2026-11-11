import { getAttributeUnit, normalizeUnitCode } from "./attributeUnits.js";

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
    return (
      source.unitCode ??
      definitionFor(sources, source.type, source.attributeId)?.unitCode ??
      null
    );
  }
  return null;
}

export function resolveTerminalUnit(
  nodeId,
  nodesById,
  incomingByTarget,
  sources,
  seen = new Set(),
) {
  if (!nodeId || seen.has(nodeId)) return null;
  seen.add(nodeId);
  const node = nodesById.get(nodeId);
  if (!node) return null;
  if (node.type === "attribute" || node.type === "storeAttribute") {
    return (
      node.data?.unitCode ??
      definitionFor(sources, node.type, node.data?.attributeId)?.unitCode ??
      null
    );
  }
  if (node.type === "modifier" && node.data?.modifierType === "unitConversion")
    return node.data?.params?.toUnit ?? null;
  if (node.type === "reroute" || node.type === "modifier") {
    const incoming = incomingByTarget.get(nodeId) || [];
    if (incoming.length !== 1) return null;
    return resolveTerminalUnit(
      incoming[0].source,
      nodesById,
      incomingByTarget,
      sources,
      seen,
    );
  }
  return null;
}

export function unitMismatch(sourceUnit, targetUnit) {
  const source = normalizeUnitCode(sourceUnit);
  const target = normalizeUnitCode(targetUnit);
  if (!source && !target) return null;
  if (source && target && source.toLowerCase() === target.toLowerCase())
    return null;
  const from = getAttributeUnit(source);
  const to = getAttributeUnit(target);
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
          directWarnings[sink.id][field.fieldKey] = warning;
        }
      }
      for (const edge of incomingByTarget.get(sink.id) || []) {
        if (edge.targetHandle !== `f:${field.fieldKey}`) continue;
        const sourceUnit = resolveTerminalUnit(
          edge.source,
          nodesById,
          incomingByTarget,
          sources,
        );
        const warning = unitMismatch(sourceUnit, targetUnit);
        if (warning) edgeWarnings.set(edge.id, warning);
      }
    }
  }
  return { edgeWarnings, directWarnings };
}
