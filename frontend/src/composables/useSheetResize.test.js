import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  SHEET_RESIZE_MIN,
  SHEET_WIDTH_DEFAULTS,
  SIDEBAR_RESIZE_MAX,
  SIDEBAR_RESIZE_MIN,
  clampSheetWidth,
  readStoredSheetWidth,
} from "./useSheetResize.js";

describe("clampSheetWidth", () => {
  it("clamps to min and max", () => {
    assert.equal(clampSheetWidth(10, { min: 300, max: 1000 }), 300);
    assert.equal(clampSheetWidth(5000, { min: 300, max: 1000 }), 1000);
    assert.equal(clampSheetWidth(480, { min: 300, max: 1000 }), 480);
  });

  it("treats non-numeric as min", () => {
    assert.equal(
      clampSheetWidth("nope", { min: SHEET_RESIZE_MIN, max: 1000 }),
      SHEET_RESIZE_MIN,
    );
  });

  it("keeps the sidebar narrower than a sheet", () => {
    assert.equal(SHEET_WIDTH_DEFAULTS.sidebar, 256);
    assert.ok(SIDEBAR_RESIZE_MIN < SHEET_WIDTH_DEFAULTS.sidebar);
    assert.ok(SIDEBAR_RESIZE_MAX > SHEET_WIDTH_DEFAULTS.sidebar);
    assert.ok(SIDEBAR_RESIZE_MAX < 1000);
    assert.equal(
      clampSheetWidth(180, {
        min: SIDEBAR_RESIZE_MIN,
        max: SIDEBAR_RESIZE_MAX,
      }),
      SIDEBAR_RESIZE_MIN,
    );
  });
});

describe("readStoredSheetWidth", () => {
  it("returns fallback without a key", () => {
    assert.equal(readStoredSheetWidth("", 512), 512);
    assert.equal(readStoredSheetWidth(null, 400), 400);
  });
});
