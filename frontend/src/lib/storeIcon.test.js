import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  isPlaceholderFavicon,
  storeIconInitial,
  storeIconUrl,
} from "./storeIcon.js";

describe("storeIconUrl", () => {
  it("treats the Control Plane square as no icon", () => {
    assert.equal(storeIconUrl("/logo-square-favicon.webp"), "");
    assert.equal(storeIconUrl("/logo_sm@0,15x.webp"), "");
    assert.equal(
      storeIconUrl("https://app.example/logo-square-favicon.webp"),
      "",
    );
    assert.equal(isPlaceholderFavicon(""), true);
  });

  it("keeps an uploaded store icon", () => {
    assert.equal(
      storeIconUrl("/api/settings/branding/assets/favicon?storeId=1&v=9"),
      "/api/settings/branding/assets/favicon?storeId=1&v=9",
    );
  });

  it("takes the first letter of the store name", () => {
    assert.equal(storeIconInitial("my store"), "M");
    assert.equal(storeIconInitial("  магазин"), "М");
    assert.equal(storeIconInitial(""), "");
  });
});
