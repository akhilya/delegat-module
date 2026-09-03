import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const columns = readFileSync(
  new URL("../components/products/columns.ts", import.meta.url),
  "utf8",
);
const identity = readFileSync(
  new URL("../components/products/ProductIdentityCell.vue", import.meta.url),
  "utf8",
);
const table = readFileSync(
  new URL("../components/products/productsTable.vue", import.meta.url),
  "utf8",
);
const adapters = readFileSync(
  new URL("../components/products/ProductAdaptersCell.vue", import.meta.url),
  "utf8",
);
const orderItems = readFileSync(
  new URL("../components/orders/OrderItemsCell.vue", import.meta.url),
  "utf8",
);

describe("product identity cell", () => {
  it("keeps a fixed 44px thumb and two truncated lines", () => {
    assert.match(identity, /data-slot="product-identity-cell"/);
    assert.match(identity, /flex h-11 min-w-0 items-center/);
    assert.match(identity, /image-class="size-11/);
    assert.match(
      identity,
      /truncate text-sm leading-none font-medium tracking-tight/,
    );
    assert.match(
      identity,
      /truncate text-xs leading-none text-muted-foreground/,
    );
  });

  it("is used by combined catalog presets instead of wrapping size-14 thumbs", () => {
    assert.match(columns, /import ProductIdentityCell/);
    assert.match(columns, /renderIdentityCell\(row\.original/);
    assert.equal([...columns.matchAll(/renderIdentityCell\(/g)].length, 5);
    assert.doesNotMatch(columns, /size-14/);
    assert.doesNotMatch(columns, /imageClass: "size-32"/);
  });

  it("locks catalog rows to one height so adapter chips cannot wrap them", () => {
    assert.match(table, /h-\[4\.25rem\] cursor-pointer/);
    assert.match(table, /h-\[4\.25rem\] overflow-hidden/);
    assert.match(adapters, /flex-nowrap overflow-hidden/);
    assert.doesNotMatch(adapters, /flex-wrap/);
  });
});

describe("order mailbox items cell", () => {
  it("lists identity rows and collapses long orders behind a disclosure", () => {
    assert.match(orderItems, /ProductIdentityCell/);
    assert.match(orderItems, /orderVisibleItems/);
    assert.match(orderItems, /itemsShowMore/);
    assert.match(orderItems, /itemsShowLess/);
    assert.match(orderItems, /aria-expanded/);
    assert.doesNotMatch(orderItems, /ORDER_ITEM_STACK/);
  });
});
