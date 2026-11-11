import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { parseAttributeFileIds } from "./productAttributeFiles.js";

describe("product attribute file values", () => {
  it("parses ordered persisted IDs and request-local create keys", () => {
    assert.deepEqual(parseAttributeFileIds("[12,34]"), [12, 34]);
    assert.deepEqual(parseAttributeFileIds("[-1,-2]"), [-1, -2]);
  });

  it("treats empty values as cleared attachments", () => {
    assert.deepEqual(parseAttributeFileIds(""), []);
    assert.deepEqual(parseAttributeFileIds("[]"), []);
    assert.deepEqual(parseAttributeFileIds(null), []);
  });

  it("rejects malformed, duplicate, non-integer, zero and unsafe IDs", () => {
    for (const raw of [
      "not-json",
      "12",
      "[1,1]",
      "[0]",
      "[1.5]",
      '["12"]',
      "[9007199254740992]",
    ]) {
      assert.equal(parseAttributeFileIds(raw), null, raw);
    }
  });
});
