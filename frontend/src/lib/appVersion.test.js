import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { normalizeAppVersion, resolveDisplayVersion } from "./appVersion.js";

describe("normalizeAppVersion", () => {
  it("strips leading v and rejects dev placeholders", () => {
    assert.equal(normalizeAppVersion("v1.5.0"), "1.5.0");
    assert.equal(normalizeAppVersion("1.5.0"), "1.5.0");
    assert.equal(normalizeAppVersion("dev"), null);
    assert.equal(normalizeAppVersion("vdev"), null);
    assert.equal(normalizeAppVersion(""), null);
    assert.equal(normalizeAppVersion(null), null);
  });
});

describe("resolveDisplayVersion", () => {
  it("prefers health over Vite and ignores Vite dev", () => {
    assert.equal(resolveDisplayVersion("1.5.0", "dev"), "1.5.0");
    assert.equal(resolveDisplayVersion(null, "1.4.0"), "1.4.0");
    assert.equal(resolveDisplayVersion(null, "dev"), "");
    assert.equal(resolveDisplayVersion("v1.5.0", "v1.0.0"), "1.5.0");
  });
});
