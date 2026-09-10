import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const adapters = readFileSync(
  new URL(
    "../components/products/productMarketplaceIntegrations.vue",
    import.meta.url,
  ),
  "utf8",
);
const sizes = readFileSync(
  new URL("../components/products/ProductVariantsEditor.vue", import.meta.url),
  "utf8",
);

describe("product adapters tab", () => {
  it("hides export without marketplace.manage", () => {
    assert.match(adapters, /canManageAdapters/);
    assert.match(adapters, /PERMS\.MarketplaceManage/);
  });

  it("uses one compact row per connection, not a fact sheet", () => {
    assert.match(adapters, /rowCaption\(/);
    assert.doesNotMatch(adapters, /rowOnMarketplace/);
    assert.doesNotMatch(adapters, /rowExternalPrice/);
    assert.doesNotMatch(adapters, /rowExternalStock/);
    assert.doesNotMatch(adapters, /rowExportMode/);
    assert.doesNotMatch(adapters, /space-y-5/);
  });
});

describe("product sizes tab", () => {
  it("keeps SKU, price, and stock as labeled fields with input chrome", () => {
    assert.match(sizes, /productVariantsEditor\.skuHeader/);
    assert.match(sizes, /productVariantsEditor\.priceHeader/);
    assert.match(sizes, /productVariantsEditor\.quantityHeader/);
    assert.doesNotMatch(sizes, /TRAILING_INPUT/);
    assert.doesNotMatch(
      sizes,
      /border-0 bg-transparent p-0 text-right shadow-none/,
    );
    assert.match(sizes, /sm:grid-cols-3/);
  });
});
