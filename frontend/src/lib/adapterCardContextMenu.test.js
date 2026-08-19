import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { shouldOpenAdapterCardContextMenu } from "./adapterCardContextMenu.js";

function click(closestMatch, button = 0) {
  return {
    button,
    target: {
      closest(selector) {
        return closestMatch && selector.includes(closestMatch) ? {} : null;
      },
    },
  };
}

describe("shouldOpenAdapterCardContextMenu", () => {
  it("opens on a primary click on the card body", () => {
    assert.equal(shouldOpenAdapterCardContextMenu(click(null)), true);
  });

  it("ignores right-click (native/context trigger handles that)", () => {
    assert.equal(shouldOpenAdapterCardContextMenu(click(null, 2)), false);
  });

  it("keeps mapping, sizes, marketplace, and ⋮ on their own controls", () => {
    assert.equal(shouldOpenAdapterCardContextMenu(click("button")), false);
    assert.equal(shouldOpenAdapterCardContextMenu(click("a")), false);
  });
});
