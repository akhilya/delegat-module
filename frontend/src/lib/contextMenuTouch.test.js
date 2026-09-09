import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import {
  dispatchContextMenuAtPointer,
  isTouchLikePointer,
  openContextMenuFromTouchRelease,
  shouldOpenContextMenuOnTouchRelease,
  suppressNextClick,
  touchMovedTooFar,
} from "./contextMenuTouch.js";

function pointer(overrides = {}) {
  return {
    pointerType: "touch",
    button: 0,
    clientX: 40,
    clientY: 80,
    target: {
      closest() {
        return null;
      },
    },
    ...overrides,
  };
}

describe("isTouchLikePointer", () => {
  it("treats touch and pen as coarse, not a mouse", () => {
    assert.equal(isTouchLikePointer({ pointerType: "touch" }), true);
    assert.equal(isTouchLikePointer({ pointerType: "pen" }), true);
    assert.equal(isTouchLikePointer({ pointerType: "mouse" }), false);
    assert.equal(isTouchLikePointer({ pointerType: "" }), false);
  });
});

describe("touchMovedTooFar", () => {
  it("ignores a tap that stayed put and rejects a scroll", () => {
    assert.equal(
      touchMovedTooFar({ x: 10, y: 10 }, { x: 12, y: 11 }),
      false,
    );
    assert.equal(
      touchMovedTooFar({ x: 10, y: 10 }, { x: 10, y: 40 }),
      true,
    );
  });
});

describe("shouldOpenContextMenuOnTouchRelease", () => {
  it("opens on a primary touch tap on the row body", () => {
    assert.equal(
      shouldOpenContextMenuOnTouchRelease(pointer(), { x: 40, y: 80 }),
      true,
    );
  });

  it("does not steal mouse clicks or nested controls", () => {
    assert.equal(
      shouldOpenContextMenuOnTouchRelease(
        pointer({ pointerType: "mouse" }),
        { x: 40, y: 80 },
      ),
      false,
    );
    assert.equal(
      shouldOpenContextMenuOnTouchRelease(
        pointer({
          target: {
            closest(sel) {
              return sel.includes("button") ? {} : null;
            },
          },
        }),
        { x: 40, y: 80 },
      ),
      false,
    );
  });

  it("does not open after a finger drag", () => {
    assert.equal(
      shouldOpenContextMenuOnTouchRelease(pointer(), { x: 40, y: 10 }),
      false,
    );
  });
});

describe("suppressNextClick", () => {
  it("stops the click from the same tap", () => {
    const listeners = [];
    const doc = {
      addEventListener(type, fn, capture) {
        listeners.push({ type, fn, capture });
      },
      removeEventListener(type, fn, capture) {
        const i = listeners.findIndex(
          (l) => l.type === type && l.fn === fn && l.capture === capture,
        );
        if (i >= 0) listeners.splice(i, 1);
      },
    };
    const prev = globalThis.document;
    globalThis.document = doc;
    try {
      suppressNextClick(20);
      assert.equal(listeners.length, 1);
      assert.equal(listeners[0].capture, true);
      const event = {
        prevented: false,
        stopped: false,
        preventDefault() {
          this.prevented = true;
        },
        stopPropagation() {
          this.stopped = true;
        },
        stopImmediatePropagation() {
          this.stopped = true;
        },
      };
      listeners[0].fn(event);
      assert.equal(event.prevented, true);
      assert.equal(event.stopped, true);
      assert.equal(listeners.length, 0);
    } finally {
      globalThis.document = prev;
    }
  });
});

describe("dispatchContextMenuAtPointer", () => {
  it("fires a bubbling contextmenu at the tap", () => {
    const types = [];
    const target = {
      dispatchEvent(ev) {
        types.push(ev.type);
        return true;
      },
    };
    assert.equal(
      dispatchContextMenuAtPointer({ target, clientX: 1, clientY: 2 }),
      true,
    );
    assert.deepEqual(types, ["contextmenu"]);
  });
});

describe("openContextMenuFromTouchRelease", () => {
  it("no-ops for a mouse pointer", () => {
    assert.equal(
      openContextMenuFromTouchRelease(
        pointer({ pointerType: "mouse", target: { dispatchEvent() {} } }),
        { x: 40, y: 80 },
      ),
      false,
    );
  });
});

describe("context menu trigger wiring", () => {
  it("opens from a touch release on ContextMenuTrigger, not only long-press", () => {
    const trigger = readFileSync(
      new URL("../components/ui/context-menu/ContextMenuTrigger.vue", import.meta.url),
      "utf8",
    );
    assert.match(trigger, /openContextMenuFromTouchRelease/);
    assert.match(trigger, /@pointerup/);
  });
});

describe("mobile sidebar dismiss", () => {
  it("closes the sheet when a nav option is chosen", () => {
    const sub = readFileSync(
      new URL("../components/ui/sidebar/SidebarMenuSubButton.vue", import.meta.url),
      "utf8",
    );
    const nav = readFileSync(
      new URL("../components/sidebar/AppSidebarNavCollapsible.vue", import.meta.url),
      "utf8",
    );
    const provider = readFileSync(
      new URL("../components/ui/sidebar/SidebarProvider.vue", import.meta.url),
      "utf8",
    );
    assert.match(sub, /setOpenMobile\(false\)/);
    assert.match(nav, /setOpenMobile\(false\)/);
    assert.match(provider, /route\.fullPath/);
    assert.match(provider, /setOpenMobile\(false\)/);
  });
});
