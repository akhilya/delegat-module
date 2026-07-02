/**
 * Lane auto-layout for the mapping graph.
 *
 * The graph has a special shape: many independent lanes (source → modifiers → field) all flow into
 * the single "Поля адаптера" sink. A generic layout (dagre) re-orders the lanes and tangles the
 * wires. Instead we order lanes by the **adapter field index** they feed: rows become monotonic in
 * field order, so the wires fan into the sink without crossing.
 *
 *  - column  = distance to the adapter (right-aligned: a node sits just left of its successor)
 *  - row     = average index of the adapter field(s) it ultimately feeds — used only to ORDER
 *              each column (preserves field-index order ⇒ wires fan in without crossing)
 *  - within a column, nodes are packed at a fixed pitch (no scattering) and the column is
 *    centered vertically, so the graph stays compact regardless of how many fields there are
 */

const COL_W = 210;
const PITCH = 74; // vertical spacing between stacked nodes (>= node height incl. preview chip)

export function layoutGraph(nodes, edges) {
  if (!nodes || nodes.length === 0) return nodes || [];

  const byId = new Map(nodes.map((n) => [n.id, n]));
  const outgoing = new Map();
  const incoming = new Map();
  nodes.forEach((n) => {
    outgoing.set(n.id, []);
    incoming.set(n.id, []);
  });
  for (const e of edges || []) {
    if (byId.has(e.source) && byId.has(e.target)) {
      outgoing.get(e.source).push(e);
      incoming.get(e.target).push(e);
    }
  }

  const adapter = nodes.find((n) => n.type === "adapterFields");
  const fieldIndex = new Map();
  if (adapter) {
    (adapter.data?.fields || []).forEach((f, i) =>
      fieldIndex.set(f.fieldKey, i),
    );
  }
  const fieldCount = fieldIndex.size || 1;

  // ---- column: longest forward distance to the adapter (memoized) ----
  const distMemo = new Map();
  function distToAdapter(id, seen = new Set()) {
    if (distMemo.has(id)) return distMemo.get(id);
    const node = byId.get(id);
    if (node?.type === "adapterFields") {
      distMemo.set(id, 0);
      return 0;
    }
    if (seen.has(id)) return 0;
    seen.add(id);
    let best = -Infinity;
    for (const e of outgoing.get(id) || []) {
      const d = distToAdapter(e.target, seen);
      if (d >= 0) best = Math.max(best, d + 1);
    }
    seen.delete(id);
    const result = best === -Infinity ? 1 : best; // dangling ⇒ treat as one step away
    distMemo.set(id, result);
    return result;
  }
  nodes.forEach((n) => distToAdapter(n.id));
  let maxDist = 0;
  nodes.forEach((n) => (maxDist = Math.max(maxDist, distMemo.get(n.id) || 0)));
  const colOf = (n) =>
    n.type === "adapterFields" ? maxDist : maxDist - (distMemo.get(n.id) || 0);

  // ---- row: average adapter-field index reached downstream ----
  const rowMemo = new Map();
  function reachedFields(id, seen = new Set()) {
    if (rowMemo.has(id)) return rowMemo.get(id);
    if (seen.has(id)) return [];
    seen.add(id);
    let acc = [];
    for (const e of outgoing.get(id) || []) {
      const t = byId.get(e.target);
      if (
        t?.type === "adapterFields" &&
        String(e.targetHandle).startsWith("f:")
      ) {
        const idx = fieldIndex.get(String(e.targetHandle).slice(2));
        if (idx != null) acc.push(idx);
      } else {
        acc = acc.concat(reachedFields(e.target, seen));
      }
    }
    seen.delete(id);
    rowMemo.set(id, acc);
    return acc;
  }
  function rowOf(n) {
    if (n.type === "adapterFields") return (fieldCount - 1) / 2;
    const r = reachedFields(n.id);
    if (!r.length) return fieldCount; // unconnected ⇒ bottom
    return r.reduce((a, b) => a + b, 0) / r.length;
  }

  // ---- assign positions, resolving overlaps per column ----
  const columns = new Map();
  for (const n of nodes) {
    const c = colOf(n);
    if (!columns.has(c)) columns.set(c, []);
    columns.get(c).push(n);
  }

  const pos = new Map();
  for (const [c, list] of columns) {
    const ordered = list
      .map((n) => ({ n, row: rowOf(n) }))
      .sort((a, b) => a.row - b.row);
    // Pack at a fixed pitch and center the column around y=0 (keeps the graph compact & aligned).
    const offset = ((ordered.length - 1) * PITCH) / 2;
    ordered.forEach(({ n }, k) => {
      pos.set(n.id, { x: c * COL_W, y: k * PITCH - offset });
    });
  }

  return nodes.map((n) => ({ ...n, position: pos.get(n.id) || n.position }));
}
