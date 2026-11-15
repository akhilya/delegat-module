import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  boxesOverlap,
  estimateNodeHeight,
  layoutGraph,
  sinksOverlap,
} from "./graphLayout.js";

function adapterNode(id, fieldCount, type = "adapterFields") {
  return {
    id,
    type,
    data: {
      fields: Array.from({ length: fieldCount }, (_, i) => ({
        fieldKey: `f${i}`,
      })),
    },
  };
}

function boxOf(node, measured) {
  const h =
    typeof measured?.get?.(node.id) === "object"
      ? measured.get(node.id).height
      : estimateNodeHeight(node);
  const w =
    typeof measured?.get?.(node.id) === "object"
      ? measured.get(node.id).width
      : node.type?.startsWith("adapter")
        ? 360
        : 180;
  return {
    x: node.position.x,
    y: node.position.y,
    w,
    h,
  };
}

function assertNoOverlaps(nodes, measured, gap = 8) {
  for (let i = 0; i < nodes.length; i++) {
    for (let j = i + 1; j < nodes.length; j++) {
      const a = boxOf(nodes[i], measured);
      const b = boxOf(nodes[j], measured);
      assert.equal(
        boxesOverlap(a, b, gap),
        false,
        `${nodes[i].id} overlaps ${nodes[j].id}: ${JSON.stringify({ a, b })}`,
      );
    }
  }
}

describe("mapping graph layout", () => {
  it("stacks a tall adapter card above price fields without overlap", () => {
    const nodes = [
      adapterNode("adapter-fields", 80),
      adapterNode("adapter-price-fields", 24, "adapterPriceFields"),
    ];
    const laid = layoutGraph(nodes, []);
    const adapter = laid.find((n) => n.type === "adapterFields");
    const price = laid.find((n) => n.type === "adapterPriceFields");
    assert.equal(adapter.position.x, price.position.x);
    assert.ok(
      price.position.y >= adapter.position.y + estimateNodeHeight(adapter) + 24,
      `price y ${price.position.y} must sit below adapter`,
    );
    assertNoOverlaps(laid);
  });

  it("keeps sources to the left of stacked sinks", () => {
    const nodes = [
      {
        id: "src-name",
        type: "productField",
        data: { label: "Name" },
      },
      adapterNode("adapter-fields", 40),
      adapterNode("adapter-price-fields", 12, "adapterPriceFields"),
    ];
    const edges = [
      {
        source: "src-name",
        target: "adapter-fields",
        targetHandle: "f:f0",
      },
    ];
    const laid = layoutGraph(nodes, edges);
    const src = laid.find((n) => n.id === "src-name");
    const adapter = laid.find((n) => n.type === "adapterFields");
    assert.ok(
      src.position.x + 180 <= adapter.position.x,
      `source x ${src.position.x} should be left of adapter x ${adapter.position.x}`,
    );
    assertNoOverlaps(laid);
  });

  it("uses measured sizes when provided", () => {
    const nodes = [
      adapterNode("adapter-fields", 3),
      adapterNode("adapter-price-fields", 3, "adapterPriceFields"),
    ];
    const measured = new Map([
      ["adapter-fields", { width: 400, height: 900 }],
      ["adapter-price-fields", { width: 400, height: 200 }],
    ]);
    const laid = layoutGraph(nodes, [], measured);
    const adapter = laid.find((n) => n.type === "adapterFields");
    const price = laid.find((n) => n.type === "adapterPriceFields");
    assert.ok(price.position.y >= adapter.position.y + 900 + 24);
    assertNoOverlaps(laid, measured);
  });

  it("includes price-group headings in the sink height estimate", () => {
    const ungrouped = adapterNode("prices-plain", 3, "adapterPriceFields");
    const grouped = {
      ...adapterNode("prices-grouped", 3, "adapterPriceFields"),
      data: {
        fields: [
          { fieldKey: "price", group: "Prices" },
          { fieldKey: "oldPrice", group: "Prices" },
          { fieldKey: "autoActionEnabled", group: "Price strategy" },
        ],
      },
    };
    assert.equal(
      estimateNodeHeight(grouped),
      estimateNodeHeight(ungrouped) + 56,
    );
  });

  it("detects overlapping sinks from the old side-by-side columns", () => {
    const nodes = [
      {
        ...adapterNode("adapter-fields", 80),
        position: { x: 0, y: 0 },
      },
      {
        ...adapterNode("adapter-price-fields", 24, "adapterPriceFields"),
        position: { x: 320, y: 40 },
      },
    ];
    assert.equal(sinksOverlap(nodes), true);
    assert.equal(sinksOverlap(layoutGraph(nodes, [])), false);
  });
});
