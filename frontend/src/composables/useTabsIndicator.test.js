import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  indicatorEnterBox,
  peekSidebarNavActiveTop,
  rememberSidebarNavActiveTop,
  resetSidebarNavActiveTop,
  tabIndicatorBox,
  tabIndicatorStyle,
} from "./useTabsIndicator.js";

describe("tabIndicatorBox", () => {
  it("returns null without a list or an active trigger", () => {
    assert.equal(tabIndicatorBox(null, { offsetWidth: 40, offsetHeight: 28 }), null);
    assert.equal(tabIndicatorBox({}, null), null);
  });

  it("reads offset box from the active trigger", () => {
    assert.deepEqual(
      tabIndicatorBox(
        {},
        { offsetLeft: 12, offsetTop: 3, offsetWidth: 64, offsetHeight: 28 },
      ),
      { x: 12, y: 3, width: 64, height: 28 },
    );
  });
});

describe("tabIndicatorStyle", () => {
  it("hides when there is no box", () => {
    assert.deepEqual(tabIndicatorStyle(null), { opacity: "0" });
  });

  it("translates with product motion tokens after the first paint", () => {
    const style = tabIndicatorStyle(
      { x: 12, y: 3, width: 64, height: 28 },
      { motion: true },
    );
    assert.equal(style.opacity, "1");
    assert.equal(style.width, "64px");
    assert.equal(style.height, "28px");
    assert.equal(style.transform, "translate3d(12px, 3px, 0)");
    assert.match(style.transition, /--duration-ui/);
    assert.match(style.transition, /--ease-out-quart/);
    assert.match(style.transition, /opacity/);
    assert.doesNotMatch(style.transition, /bounce|elastic/);
  });

  it("skips transition on the first paint", () => {
    const style = tabIndicatorStyle(
      { x: 0, y: 0, width: 40, height: 24 },
      { motion: false },
    );
    assert.equal(style.transition, "none");
  });

  it("keeps transform when hidden so the pill does not retreat to the top", () => {
    const style = tabIndicatorStyle(
      { x: 0, y: 40, width: 100, height: 28 },
      { motion: true, hidden: true },
    );
    assert.equal(style.opacity, "0");
    assert.equal(style.width, "100px");
    assert.equal(style.height, "28px");
    assert.equal(style.transform, "translate3d(0px, 40px, 0)");
  });

  it("prefers viewport boxes when getBoundingClientRect is present", () => {
    const list = {
      scrollLeft: 0,
      scrollTop: 4,
      clientLeft: 1,
      clientTop: 1,
      getBoundingClientRect() {
        return { left: 10, top: 20, width: 200, height: 80 };
      },
    };
    const active = {
      getBoundingClientRect() {
        return { left: 18, top: 36, width: 160, height: 32 };
      },
    };
    assert.deepEqual(tabIndicatorBox(list, active), {
      x: 7,
      y: 19,
      width: 160,
      height: 32,
    });
  });
});

describe("indicatorEnterBox", () => {
  const first = {
    offsetLeft: 0,
    offsetTop: 0,
    offsetWidth: 100,
    offsetHeight: 28,
  };
  const last = {
    offsetLeft: 0,
    offsetTop: 80,
    offsetWidth: 100,
    offsetHeight: 28,
  };
  const target = { x: 0, y: 40, width: 100, height: 28 };

  it("starts on the last row when arriving from below", () => {
    assert.deepEqual(
      indicatorEnterBox({}, target, { fromBottom: true, items: [first, last] }),
      { x: 0, y: 80, width: 100, height: 28 },
    );
  });

  it("starts on the first row when arriving from above", () => {
    assert.deepEqual(
      indicatorEnterBox({}, target, { fromBottom: false, items: [first, last] }),
      { x: 0, y: 0, width: 100, height: 28 },
    );
  });
});

describe("sidebar nav active top", () => {
  it("remembers and forgets the last active row", () => {
    resetSidebarNavActiveTop();
    rememberSidebarNavActiveTop(120);
    assert.equal(peekSidebarNavActiveTop(), 120);
    resetSidebarNavActiveTop();
    assert.equal(peekSidebarNavActiveTop(), null);
  });
});

describe("tabIndicatorBox", () => {
  it("returns null without a list or an active trigger", () => {
    assert.equal(tabIndicatorBox(null, { offsetWidth: 40, offsetHeight: 28 }), null);
    assert.equal(tabIndicatorBox({}, null), null);
  });

  it("reads offset box from the active trigger", () => {
    assert.deepEqual(
      tabIndicatorBox(
        {},
        { offsetLeft: 12, offsetTop: 3, offsetWidth: 64, offsetHeight: 28 },
      ),
      { x: 12, y: 3, width: 64, height: 28 },
    );
  });
});

describe("tabIndicatorStyle", () => {
  it("hides when there is no box", () => {
    assert.deepEqual(tabIndicatorStyle(null), { opacity: "0" });
  });

  it("translates with product motion tokens after the first paint", () => {
    const style = tabIndicatorStyle(
      { x: 12, y: 3, width: 64, height: 28 },
      { motion: true },
    );
    assert.equal(style.opacity, "1");
    assert.equal(style.width, "64px");
    assert.equal(style.height, "28px");
    assert.equal(style.transform, "translate3d(12px, 3px, 0)");
    assert.match(style.transition, /--duration-ui/);
    assert.match(style.transition, /--ease-out-quart/);
    assert.doesNotMatch(style.transition, /bounce|elastic/);
  });

  it("skips transition on the first paint", () => {
    const style = tabIndicatorStyle(
      { x: 0, y: 0, width: 40, height: 24 },
      { motion: false },
    );
    assert.equal(style.transition, "none");
  });

  it("prefers viewport boxes when getBoundingClientRect is present", () => {
    const list = {
      scrollLeft: 0,
      scrollTop: 4,
      clientLeft: 1,
      clientTop: 1,
      getBoundingClientRect() {
        return { left: 10, top: 20, width: 200, height: 80 };
      },
    };
    const active = {
      getBoundingClientRect() {
        return { left: 18, top: 36, width: 160, height: 32 };
      },
    };
    assert.deepEqual(tabIndicatorBox(list, active), {
      x: 7,
      y: 19,
      width: 160,
      height: 32,
    });
  });
});
