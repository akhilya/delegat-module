import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { applyCompanionExclusive } from "./companionExclusive.js";

function mockStore(open) {
  const state = {
    aiChat: { isOpen: !!open.ai },
    operations: { panelOpen: !!open.operations },
    docs: { panelOpen: !!open.docs },
  };
  return {
    state,
    commit(type, payload) {
      if (type === "aiChat/setOpen") state.aiChat.isOpen = !!payload;
      if (type === "operations/SET_PANEL_OPEN") state.operations.panelOpen = !!payload;
      if (type === "docs/SET_PANEL_OPEN") state.docs.panelOpen = !!payload;
    },
  };
}

describe("companionExclusive", () => {
  it("closes other companions when keeping operations", () => {
    const store = mockStore({ ai: true, operations: true, docs: true });
    applyCompanionExclusive(store, "operations");
    assert.equal(store.state.operations.panelOpen, true);
    assert.equal(store.state.aiChat.isOpen, false);
    assert.equal(store.state.docs.panelOpen, false);
  });
});
