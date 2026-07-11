/**
 * Lane auto-layout for the mapping graph.
 *
 * Columns = distance to sink; rows align with adapter field indices.
 * Collision resolution runs per column (global stack), not per row bucket.
 */

const COL_W = 320;
/** Ideal vertical spacing between field-row anchors */
const ROW_PITCH = 48;
const SINK_HEADER = 30;
/** Min gap between node bounding boxes in the same column */
const LANE_GAP = 16;

const SINK_TYPES = new Set(["adapterFields", "adapterPriceFields"]);

/** Match AdapterFieldsNode.vue */
const ADAPTER_HEADER = 30;
const ADAPTER_ROW = 24;

/** Match ModifierNode.vue / SourceNode.vue */
const CARD_HEADER = 26;
const CARD_PIN_ROW = 18;

function buildFieldIndex(node) {
  const map = new Map();
  if (node) {
    (node.data?.fields || []).forEach((f, i) => map.set(f.fieldKey, i));
  }
  return map;
}

function yForRow(row) {
  return SINK_HEADER + row * ROW_PITCH;
}

const MODIFIERS_WITH_PINS = new Set([
  "multiply", "divide", "add", "subtract", "round", "clamp",
  "append", "prepend", "replace", "regexReplace", "padStart", "padEnd",
]);

const MULTI_INPUT_OPS = new Set([
  "formatString", "join", "makeArray", "arrayAppend", "arrayPrepend",
]);

/** Visual height estimate — must be >= rendered card height to avoid overlap. */
export function estimateNodeHeight(node) {
  if (!node) return 56;

  if (node.type === "adapterFields" || node.type === "adapterPriceFields") {
    const n = node.data?.fields?.length || 1;
    return ADAPTER_HEADER + n * ADAPTER_ROW + 6;
  }

  if (node.data?.collapsed) return 44;

  if (node.type === "modifier") {
    const modType = node.data?.modifierType;
    let pinRows = MODIFIERS_WITH_PINS.has(modType) ? 2 : 1;
    if (modType === "llmGenerate") {
      pinRows = 1 + (node.data?.fieldPins?.length || 0);
      if (node.data?.params?.seed != null) pinRows += 1;
    }
    let h = CARD_HEADER + pinRows * CARD_PIN_ROW + 14;
    if (node.data?.params && Object.keys(node.data.params).length > 0) h += 18;
    if (node.data?.previewValue != null) h += 36;
    return h;
  }

  if (MULTI_INPUT_OPS.has(node.type)) {
    const count = Math.max(2, node.data?.inputCount || 2);
    return 54 + count * 10 + (node.data?.previewValue != null ? 32 : 0);
  }

  if (node.type === "reroute") return 18;

  let h = 54;
  if (node.data?.previewValue != null) h += 36;
  if (node.data?.label || node.data?.staticValue) h += 6;
  return h;
}

export function layoutGraph(nodes, edges, measuredHeights) {
  if (!nodes || nodes.length === 0) return nodes || [];

  const byId = new Map(nodes.map((n) => [n.id, n]));
  const outgoing = new Map();
  nodes.forEach((n) => outgoing.set(n.id, []));
  for (const e of edges || []) {
    if (byId.has(e.source) && byId.has(e.target)) {
      outgoing.get(e.source).push(e);
    }
  }

  const cardSink = nodes.find((n) => n.type === "adapterFields");
  const priceSink = nodes.find((n) => n.type === "adapterPriceFields");
  const cardFieldIndex = buildFieldIndex(cardSink);
  const priceFieldIndex = buildFieldIndex(priceSink);
  const cardFieldCount = cardFieldIndex.size || 1;
  const priceFieldCount = priceFieldIndex.size || 1;
  const maxFieldRows = Math.max(cardFieldCount, priceFieldCount);

  const distMemo = new Map();
  function distToSink(id, seen = new Set()) {
    if (distMemo.has(id)) return distMemo.get(id);
    const node = byId.get(id);
    if (node && SINK_TYPES.has(node.type)) {
      distMemo.set(id, 0);
      return 0;
    }
    if (seen.has(id)) return 0;
    seen.add(id);
    let best = -Infinity;
    for (const e of outgoing.get(id) || []) {
      const d = distToSink(e.target, seen);
      if (d >= 0) best = Math.max(best, d + 1);
    }
    seen.delete(id);
    const result = best === -Infinity ? 1 : best;
    distMemo.set(id, result);
    return result;
  }
  nodes.forEach((n) => distToSink(n.id));

  let maxDist = 0;
  nodes.forEach((n) => {
    if (!SINK_TYPES.has(n.type))
      maxDist = Math.max(maxDist, distMemo.get(n.id) || 0);
  });

  const colOf = (n) => {
    if (n.type === "adapterFields") return maxDist;
    if (n.type === "adapterPriceFields") return maxDist + 1;
    return maxDist - (distMemo.get(n.id) || 0);
  };

  const rowMemo = new Map();
  function reachedFields(id, sinkType, indexMap, seen = new Set()) {
    const memoKey = `${id}:${sinkType}`;
    if (rowMemo.has(memoKey)) return rowMemo.get(memoKey);
    if (seen.has(id)) return [];
    seen.add(id);
    let acc = [];
    for (const e of outgoing.get(id) || []) {
      const t = byId.get(e.target);
      if (
        t?.type === sinkType &&
        String(e.targetHandle).startsWith("f:")
      ) {
        const idx = indexMap.get(String(e.targetHandle).slice(2));
        if (idx != null) acc.push(idx);
      } else {
        acc = acc.concat(reachedFields(e.target, sinkType, indexMap, seen));
      }
    }
    seen.delete(id);
    rowMemo.set(memoKey, acc);
    return acc;
  }

  function rowOf(n) {
    if (n.type === "adapterFields") return (cardFieldCount - 1) / 2;
    if (n.type === "adapterPriceFields") return (priceFieldCount - 1) / 2;

    const priceReach = reachedFields(n.id, "adapterPriceFields", priceFieldIndex);
    if (priceReach.length) {
      return priceReach.reduce((a, b) => a + b, 0) / priceReach.length;
    }
    const cardReach = reachedFields(n.id, "adapterFields", cardFieldIndex);
    if (cardReach.length) {
      return cardReach.reduce((a, b) => a + b, 0) / cardReach.length;
    }
    return maxFieldRows + 0.5;
  }

  const idealY = new Map();
  for (const n of nodes) {
    idealY.set(n.id, yForRow(rowOf(n)));
  }

  // Group by column and stack top-to-bottom, pushing down only when boxes overlap.
  const byColumn = new Map();
  for (const n of nodes) {
    const c = colOf(n);
    if (!byColumn.has(c)) byColumn.set(c, []);
    byColumn.get(c).push(n);
  }

  const pos = new Map();
  for (const [c, colNodes] of byColumn) {
    const sorted = [...colNodes].sort((a, b) => {
      const dy = (idealY.get(a.id) ?? 0) - (idealY.get(b.id) ?? 0);
      if (dy !== 0) return dy;
      return a.id.localeCompare(b.id);
    });

    let stackBottom = -Infinity;
    for (const n of sorted) {
      const measured = measuredHeights?.get?.(n.id);
      const h =
        typeof measured === "number" && measured > 0
          ? measured
          : estimateNodeHeight(n);
      let y = idealY.get(n.id) ?? SINK_HEADER;
      if (stackBottom !== -Infinity && y < stackBottom + LANE_GAP) {
        y = stackBottom + LANE_GAP;
      }
      pos.set(n.id, { x: c * COL_W, y });
      stackBottom = y + h;
    }
  }

  return nodes.map((n) => ({ ...n, position: pos.get(n.id) || n.position }));
}