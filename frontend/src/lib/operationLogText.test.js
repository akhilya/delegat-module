import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { splitOperationLog } from "./operationLogText.js";

describe("operation log text", () => {
  it("pretty-prints a JSON object after a label", () => {
    const parts = splitOperationLog('Payload to send:\n{"sku":"A","qty":1}');
    assert.equal(parts[0].type, "text");
    assert.match(parts[0].text, /Payload to send/);
    assert.equal(parts[1].type, "json");
    assert.match(parts[1].text, /"sku": "A"/);
    assert.match(parts[1].text, /\n {2}"qty": 1/);
  });

  it("turns a product mention into a link and keeps the surrounding text", () => {
    const parts = splitOperationLog("Exported: product #12 «Фигурка» - ok");
    assert.deepEqual(
      parts.map((part) => part.type),
      ["text", "product", "text"],
    );
    assert.equal(parts[1].productId, 12);
    assert.equal(parts[1].text, "Фигурка");
    assert.equal(parts[2].text, " - ok");
  });

  it("links a bare product id", () => {
    const parts = splitOperationLog("Loading product #7");
    assert.equal(parts.at(-1).type, "product");
    assert.equal(parts.at(-1).productId, 7);
    assert.equal(parts.at(-1).text, "#7");
  });
});
