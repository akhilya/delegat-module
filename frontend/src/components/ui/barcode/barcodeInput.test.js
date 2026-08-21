import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const barcode = readFileSync(new URL("./BarcodeInput.vue", import.meta.url), "utf8");
const productEdit = readFileSync(
  new URL("../../../pages/productEditPage.vue", import.meta.url),
  "utf8",
);

describe("product property field actions", () => {
  it("puts barcode generate under the field as caption text, not a dice icon", () => {
    assert.match(barcode, /barcodeInput\.generateAction/);
    assert.match(barcode, /text-xs transition-colors/);
    assert.doesNotMatch(barcode, /Dices/);
    assert.doesNotMatch(barcode, /size="icon"/);
  });

  it("puts SKU copy on the trailing edge of the caption row", () => {
    assert.match(
      productEdit,
      /justify-between[\s\S]*sku\.changeAction[\s\S]*skuDialog\.copyAction/,
    );
  });
});
