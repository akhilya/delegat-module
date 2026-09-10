import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { formatAdapterError } from "./adapterErrorText.js";

describe("formatAdapterError", () => {
  it("names the missing Wildberries Content scope instead of dumping JSON", () => {
    const raw =
      'WB API error: Unauthorized: {"title":"unauthorized","detail":"token scope content is required","origin":"content","status":401}';
    const text = formatAdapterError(raw);
    assert.match(text, /Content \(product cards\)/);
    assert.doesNotMatch(text, /\{/);
  });

  it("explains an Ozon 403 without the payload", () => {
    const raw =
      'Ozon API error: Forbidden: {"code":7,"message":"User doesn\'t have permission to the method","details":[]}';
    const text = formatAdapterError(raw);
    assert.match(text, /permission/i);
    assert.doesNotMatch(text, /"code"/);
  });

  it("passes through a clean sentence", () => {
    assert.equal(
      formatAdapterError("sku (vendorCode) is required"),
      "sku (vendorCode) is required",
    );
  });
});
