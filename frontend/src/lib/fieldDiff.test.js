import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { displayFieldValue } from "./fieldDiff.js";

describe("displayFieldValue", () => {
  it("pretty-prints a JSON array of strings", () => {
    assert.equal(
      displayFieldValue('["другу","любимому","школа"]', "—"),
      "другу, любимому, школа",
    );
  });

  it("unwraps a nested JSON-array string and unicode escapes", () => {
    const inner = JSON.stringify(["другу", "любимому", "школа"]);
    const nested = JSON.stringify([inner]);
    assert.equal(displayFieldValue(nested, "—"), "другу, любимому, школа");
  });

  it("joins real arrays", () => {
    assert.equal(displayFieldValue(["a", "b"], "—"), "a, b");
  });
});
