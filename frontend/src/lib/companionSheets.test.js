import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  COMPANION_FAB_CLOSED_RIGHT,
  COMPANION_FAB_GAP,
  COMPANION_GAP,
  COMPANION_INSET,
  COMPANION_STACK_MIN_WIDTH,
  companionCanStack,
  companionEdgeOffset,
  companionFabRight,
  companionKeepWhenCollapsing,
  companionStackExtent,
} from "./companionSheets.js";

describe("companionSheets", () => {
  it("parks a lone assistant on the right edge", () => {
    const snap = {
      operations: { open: false, width: 672 },
      docs: { open: false, width: 880 },
      ai: { open: true, width: 400 },
    };
    assert.equal(companionEdgeOffset("ai", snap), 0);
    assert.equal(companionStackExtent(snap), COMPANION_INSET + 400);
    assert.equal(companionFabRight(snap), COMPANION_INSET + 400 + COMPANION_FAB_GAP);
  });

  it("opens operations to the right of the assistant", () => {
    const snap = {
      operations: { open: true, width: 672 },
      docs: { open: false, width: 880 },
      ai: { open: true, width: 400 },
    };
    assert.equal(companionEdgeOffset("operations", snap), 0);
    assert.equal(companionEdgeOffset("ai", snap), 672 + COMPANION_GAP);
    assert.equal(
      companionStackExtent(snap),
      COMPANION_INSET + 672 + COMPANION_GAP + 400,
    );
  });

  it("opens docs to the right of the assistant", () => {
    const snap = {
      operations: { open: false, width: 672 },
      docs: { open: true, width: 880 },
      ai: { open: true, width: 400 },
    };
    assert.equal(companionEdgeOffset("docs", snap), 0);
    assert.equal(companionEdgeOffset("ai", snap), 880 + COMPANION_GAP);
  });

  it("does not stack companions on a narrow viewport", () => {
    const snap = {
      operations: { open: true, width: 672 },
      docs: { open: true, width: 880 },
      ai: { open: true, width: 400 },
    };
    assert.equal(companionCanStack(390), false);
    assert.equal(companionCanStack(COMPANION_STACK_MIN_WIDTH), true);
    assert.equal(companionEdgeOffset("ai", snap, { stack: false }), 0);
    assert.equal(companionFabRight(snap, { stack: false }), COMPANION_FAB_CLOSED_RIGHT);
    assert.equal(companionKeepWhenCollapsing(snap), "operations");
  });

  it("keeps closed FABs at the corner", () => {
    const snap = {
      operations: { open: false, width: 672 },
      docs: { open: false, width: 880 },
      ai: { open: false, width: 400 },
    };
    assert.equal(companionFabRight(snap), COMPANION_FAB_CLOSED_RIGHT);
  });
});
