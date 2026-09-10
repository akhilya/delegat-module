/**
 * Mapping-graph auto-layout.
 *
 * Dagre (LR) places sources and modifiers. Card-field and price-field sinks
 * are then stacked in one right-hand column (adapter on top, prices below)
 * so a 420px-wide adapter card cannot sit on the price card. A last AABB
 * pass pushes leftover collisions apart.
 */

import dagre from "@dagrejs/dagre";

const SINK_GAP = 48;
const LANE_GAP = 24;
const RANK_SEP = 88;
const NODE_SEP = 40;
const SINK_LEAD = 72;

const SINK_TYPES = new Set(["adapterFields", "adapterPriceFields"]);

const ADAPTER_HEADER = 30;
const ADAPTER_ROW = 24;
const ADAPTER_WIDTH = 360;

const CARD_HEADER = 26;
const CARD_PIN_ROW = 18;
const CARD_WIDTH = 180;

const MODIFIERS_WITH_PINS = new Set([
  "multiply",
  "divide",
  "add",
  "subtract",
  "round",
  "clamp",
  "append",
  "prepend",
  "replace",
  "regexReplace",
  "padStart",
  "padEnd",
]);

const MULTI_INPUT_OPS = new Set([
  "formatString",
  "join",
  "makeArray",
  "arrayAppend",
  "arrayPrepend",
]);

/** Visual height estimate — must be >= rendered card height to avoid overlap. */
export function estimateNodeHeight(node) {
  if (!node) return 56;

  if (node.type === "adapterFields" || node.type === "adapterPriceFields") {
    const n = node.data?.fields?.length || 1;
    const extra = node.type === "adapterFields" ? 1 : 0;
    return ADAPTER_HEADER + (n + extra) * ADAPTER_ROW + 6;
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

export function estimateNodeWidth(node) {
  if (!node) return CARD_WIDTH;
  if (node.type === "adapterFields" || node.type === "adapterPriceFields") {
    return ADAPTER_WIDTH;
  }
  if (node.type === "reroute") return 16;
  if (node.data?.collapsed) return 160;
  return CARD_WIDTH;
}

function readMeasured(measured, id) {
  const v = measured?.get?.(id);
  if (v == null) return {};
  if (typeof v === "number") return { height: v };
  return {
    width: typeof v.width === "number" ? v.width : undefined,
    height: typeof v.height === "number" ? v.height : undefined,
  };
}

function sizeOf(node, measured) {
  const m = readMeasured(measured, node.id);
  return {
    width: m.width > 0 ? m.width : estimateNodeWidth(node),
    height: m.height > 0 ? m.height : estimateNodeHeight(node),
  };
}

export function boxesOverlap(a, b, gap = 0) {
  return !(
    a.x + a.w + gap <= b.x ||
    b.x + b.w + gap <= a.x ||
    a.y + a.h + gap <= b.y ||
    b.y + b.h + gap <= a.y
  );
}

export function sinksOverlap(nodes, gap = 8) {
  const adapter = (nodes || []).find((n) => n.type === "adapterFields");
  const price = (nodes || []).find((n) => n.type === "adapterPriceFields");
  if (!adapter?.position || !price?.position) return false;
  return boxesOverlap(
    {
      x: adapter.position.x,
      y: adapter.position.y,
      w: estimateNodeWidth(adapter),
      h: estimateNodeHeight(adapter),
    },
    {
      x: price.position.x,
      y: price.position.y,
      w: estimateNodeWidth(price),
      h: estimateNodeHeight(price),
    },
    gap,
  );
}

function restackSinks(nodes, pos) {
  const adapter = nodes.find((n) => n.type === "adapterFields");
  const price = nodes.find((n) => n.type === "adapterPriceFields");
  if (!adapter && !price) return;

  let maxRight = 0;
  for (const n of nodes) {
    if (SINK_TYPES.has(n.type)) continue;
    const p = pos.get(n.id);
    if (p) maxRight = Math.max(maxRight, p.x + p.w);
  }

  const x = maxRight + (maxRight > 0 ? SINK_LEAD : 0);

  if (adapter) {
    const p = pos.get(adapter.id);
    if (p) {
      p.x = x;
      p.y = 0;
    }
  }
  if (price) {
    const a = adapter ? pos.get(adapter.id) : null;
    const p = pos.get(price.id);
    if (p) {
      p.x = x;
      p.y = a ? a.y + a.h + SINK_GAP : 0;
    }
  }
}

function resolveOverlaps(nodes, pos, { fixedIds, gap = LANE_GAP }) {
  const movable = nodes.filter((n) => !fixedIds.has(n.id));
  for (let iter = 0; iter < 48; iter++) {
    let moved = false;
    for (const n of movable) {
      const a = pos.get(n.id);
      for (const o of nodes) {
        if (o.id === n.id) continue;
        const b = pos.get(o.id);
        if (!a || !b || !boxesOverlap(a, b, gap)) continue;
        const down = b.y + b.h + gap - a.y;
        const right = b.x + b.w + gap - a.x;
        if (down > 0 && (right <= 0 || down <= right)) a.y += down;
        else if (right > 0) a.x += right;
        moved = true;
      }
    }
    if (!moved) break;
  }
}

function runDagre(nodes, edges, measured) {
  const g = new dagre.graphlib.Graph({ directed: true });
  g.setGraph({
    rankdir: "LR",
    nodesep: NODE_SEP,
    ranksep: RANK_SEP,
    edgesep: 16,
    marginx: 16,
    marginy: 16,
    acyclicer: "greedy",
    ranker: "network-simplex",
  });
  g.setDefaultEdgeLabel(() => ({}));

  for (const n of nodes) {
    const { width, height } = sizeOf(n, measured);
    g.setNode(String(n.id), { width, height });
  }
  for (const e of edges || []) {
    const s = String(e.source);
    const t = String(e.target);
    if (g.hasNode(s) && g.hasNode(t) && s !== t) g.setEdge(s, t);
  }

  dagre.layout(g);

  const pos = new Map();
  for (const n of nodes) {
    const { width, height } = sizeOf(n, measured);
    const dn = g.node(String(n.id));
    const cx = Number.isFinite(dn?.x) ? dn.x : width / 2;
    const cy = Number.isFinite(dn?.y) ? dn.y : height / 2;
    pos.set(n.id, {
      x: cx - width / 2,
      y: cy - height / 2,
      w: width,
      h: height,
    });
  }
  return pos;
}

export function layoutGraph(nodes, edges, measured) {
  if (!nodes || nodes.length === 0) return nodes || [];

  let pos;
  try {
    pos = runDagre(nodes, edges, measured);
  } catch {
    pos = new Map();
    for (const n of nodes) {
      const { width, height } = sizeOf(n, measured);
      pos.set(n.id, { x: 0, y: 0, w: width, h: height });
    }
  }

  restackSinks(nodes, pos);
  const fixedIds = new Set(
    nodes.filter((n) => SINK_TYPES.has(n.type)).map((n) => n.id),
  );
  resolveOverlaps(nodes, pos, { fixedIds });

  return nodes.map((n) => {
    const p = pos.get(n.id);
    return {
      ...n,
      position: p ? { x: Math.round(p.x), y: Math.round(p.y) } : n.position,
    };
  });
}
