import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  DEFAULT_PRODUCT_IMAGE_SIZE,
  PRODUCT_IMAGE_SIZE_IDS,
  PRODUCT_IMAGE_SIZE_STORAGE_KEY,
  PRODUCT_IMAGE_SIZES,
  parseProductImageSize,
  productImageSizeSpec,
  readStoredProductImageSize,
  writeStoredProductImageSize,
} from "./productImageSize.js";

const identity = readFileSync(
  new URL("../components/products/ProductIdentityCell.vue", import.meta.url),
  "utf8",
);
const table = readFileSync(
  new URL("../components/products/productsTable.vue", import.meta.url),
  "utf8",
);
const columns = readFileSync(
  new URL("../components/products/columns.ts", import.meta.url),
  "utf8",
);
const imageCell = readFileSync(
  new URL("../components/products/ProductImageCell.vue", import.meta.url),
  "utf8",
);
const selector = readFileSync(
  new URL("../components/products/columnSelector.vue", import.meta.url),
  "utf8",
);
const sizeMenu = readFileSync(
  new URL("../components/products/ProductImageSizeMenu.vue", import.meta.url),
  "utf8",
);
const sizeContextSub = readFileSync(
  new URL("../components/products/ProductImageSizeContextSub.vue", import.meta.url),
  "utf8",
);
const productRowMenu = readFileSync(
  new URL("../components/products/ProductRowActionMenuItems.vue", import.meta.url),
  "utf8",
);
const trashRowMenu = readFileSync(
  new URL("../components/products/TrashRowActionMenuItems.vue", import.meta.url),
  "utf8",
);
const ordersPage = readFileSync(
  new URL("../pages/orders/OrdersPage.vue", import.meta.url),
  "utf8",
);
const scrapePage = readFileSync(
  new URL("../pages/tools/CatalogScrapePage.vue", import.meta.url),
  "utf8",
);
const orderItems = readFileSync(
  new URL("../components/orders/OrderItemsCell.vue", import.meta.url),
  "utf8",
);
const assemblyRow = readFileSync(
  new URL("../components/orders/OrderAssemblyRow.vue", import.meta.url),
  "utf8",
);

describe("product image size tokens", () => {
  it("defaults to the current small 44px catalog thumb", () => {
    assert.equal(DEFAULT_PRODUCT_IMAGE_SIZE, "sm");
    assert.deepEqual(PRODUCT_IMAGE_SIZE_IDS, ["sm", "md", "lg"]);
    assert.equal(parseProductImageSize(null), "sm");
    assert.equal(parseProductImageSize("nope"), "sm");
    assert.equal(parseProductImageSize("LG"), "sm");
    assert.equal(parseProductImageSize("md"), "md");
    assert.equal(PRODUCT_IMAGE_SIZES.sm.boxClass, "size-11");
    assert.equal(PRODUCT_IMAGE_SIZES.sm.px, 44);
    assert.equal(PRODUCT_IMAGE_SIZES.sm.stackPeek, 10);
    assert.equal(PRODUCT_IMAGE_SIZES.sm.rowClass, "h-[4.25rem]");
    assert.equal(PRODUCT_IMAGE_SIZES.md.boxClass, "size-16");
    assert.equal(PRODUCT_IMAGE_SIZES.md.px, 64);
    assert.equal(PRODUCT_IMAGE_SIZES.md.rowClass, "h-[5.5rem]");
    assert.equal(PRODUCT_IMAGE_SIZES.lg.boxClass, "size-24");
    assert.equal(PRODUCT_IMAGE_SIZES.lg.px, 96);
    assert.equal(PRODUCT_IMAGE_SIZES.lg.rowClass, "h-[7.5rem]");
    assert.equal(productImageSizeSpec("lg").id, "lg");
  });

  it("reads and writes one cabinet preference", () => {
    const store = new Map();
    const previous = globalThis.localStorage;
    globalThis.localStorage = {
      getItem(key) {
        return store.has(key) ? store.get(key) : null;
      },
      setItem(key, value) {
        store.set(key, String(value));
      },
      removeItem(key) {
        store.delete(key);
      },
    };
    try {
      assert.equal(readStoredProductImageSize(), "sm");
      assert.equal(writeStoredProductImageSize("lg"), "lg");
      assert.equal(store.get(PRODUCT_IMAGE_SIZE_STORAGE_KEY), "lg");
      assert.equal(readStoredProductImageSize(), "lg");
      assert.equal(writeStoredProductImageSize("nope"), "sm");
    } finally {
      if (previous === undefined) delete globalThis.localStorage;
      else globalThis.localStorage = previous;
    }
  });
});

describe("product image size menus", () => {
  it("keeps small / medium / large on catalog scrape, not in the products column menu", () => {
    assert.doesNotMatch(selector, /ProductImageSizeMenu/);
    assert.match(sizeMenu, /columnSelector.photoSize/);
    assert.match(sizeMenu, /photoSizeSm/);
    assert.match(sizeMenu, /photoSizeMd/);
    assert.match(sizeMenu, /photoSizeLg/);
    assert.match(sizeMenu, /DropdownMenuRadioItem/);
    assert.match(sizeMenu, /setSize/);
    assert.match(scrapePage, /ProductImageSizeMenu/);
  });

  it("puts photo size on product, trash, and order row context menus", () => {
    const orderMenu = readFileSync(
      new URL("../components/orders/OrderRowContextMenu.vue", import.meta.url),
      "utf8",
    );
    assert.doesNotMatch(ordersPage, /ProductImageSizeMenu/);
    assert.match(sizeContextSub, /columnSelector.photoSize/);
    assert.match(sizeContextSub, /ContextMenuSub/);
    assert.match(sizeContextSub, /ContextMenuRadioItem/);
    assert.match(orderMenu, /ProductImageSizeContextSub/);
    assert.match(productRowMenu, /ProductImageSizeContextSub/);
    assert.match(productRowMenu, /surface === ['"]context['"]/);
    assert.match(trashRowMenu, /ProductImageSizeContextSub/);
  });
});

describe("product image size wiring", () => {
  it("lets identity cells and catalog rows follow the chosen size", () => {
    assert.match(identity, /useProductImageSize/);
    assert.match(identity, /spec\.identityClass/);
    assert.match(identity, /spec\.thumbClass/);
    assert.match(table, /useProductImageSize/);
    assert.match(table, /productImageRowClass/);
    assert.match(columns, /followTableSize:\s*true/);
    assert.match(imageCell, /followTableSize/);
    assert.match(orderItems, /spec\.identityClass/);
    assert.match(scrapePage, /productImageSpec/);
    assert.match(assemblyRow, /useProductImageSize/);
    assert.match(assemblyRow, /spec\.identityClass/);
    assert.match(assemblyRow, /spec\.imageOnlyClass/);
    assert.match(assemblyRow, /spec\.stackPeek/);
  });
});
