import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  DEFAULT_ORDER_BUCKET,
  ORDER_ACTION,
  ORDER_BUCKETS,
  ORDER_STATUS,
  canCancelOrder,
  decodeOrderLabelBytes,
  formatOrderMoney,
  isOrderBucket,
  mergeOrderLists,
  orderBucketPath,
  orderDisplayId,
  orderExternalId,
  orderLabelContentType,
  orderLabelFileName,
  ORDER_ITEM_PREVIEW_MAX,
  orderHiddenLineCount,
  orderItemImageUrl,
  orderItemImageUrls,
  orderItemSku,
  orderItemsNeedCollapse,
  orderItemsSummary,
  orderLineCount,
  orderPieceCount,
  orderPrimaryItemName,
  orderPrimaryItemQuantity,
  orderSecondaryItemNames,
  orderVisibleItems,
  orderStatusClass,
  orderStatusLabelKey,
  primaryActionsForStatus,
  statusesForBucket,
} from "./ordersUi.js";

describe("order buckets", () => {
  it("covers every normalized status except unknown", () => {
    const covered = new Set(ORDER_BUCKETS.flatMap((b) => b.statuses));
    for (const status of Object.values(ORDER_STATUS)) {
      if (status === ORDER_STATUS.Unknown) continue;
      assert.equal(covered.has(status), true, status);
    }
    assert.equal(isOrderBucket(DEFAULT_ORDER_BUCKET), true);
    assert.equal(isOrderBucket("nope"), false);
    assert.equal(orderBucketPath("pack"), "/orders/pack");
    assert.equal(orderBucketPath("nope"), "/orders/new");
    assert.deepEqual(statusesForBucket("new"), ["new", "unpaid"]);
    assert.equal(ORDER_BUCKETS[0].descriptionKey, "pages.ordersPage.mailboxNewHint");
    assert.equal(ORDER_BUCKETS[1].emptyDescriptionKey, "pages.ordersPage.emptyPackDescription");
  });
});

describe("mergeOrderLists", () => {
  it("dedupes by externalId and sorts newest first", () => {
    const merged = mergeOrderLists([
      [
        { externalId: "a", createdAt: "2026-01-01T00:00:00Z" },
        { externalId: "b", createdAt: "2026-03-01T00:00:00Z" },
      ],
      [
        {
          externalId: "a",
          createdAt: "2026-02-01T00:00:00Z",
          displayId: "dup",
        },
        { externalId: "c", createdAt: "2026-04-01T00:00:00Z" },
      ],
    ]);
    assert.deepEqual(
      merged.map((row) => orderExternalId(row)),
      ["c", "b", "a"],
    );
    assert.equal(orderDisplayId(merged[2]), "a");
  });
});

describe("orderItemsSummary", () => {
  it("joins names and quantities", () => {
    assert.equal(
      orderItemsSummary({
        items: [
          { name: "Tee", quantity: 2 },
          { Name: "Hat", Quantity: 1 },
        ],
      }),
      "Tee × 2, Hat",
    );
    assert.equal(orderItemsSummary({ items: [] }), "");
    assert.equal(
      orderItemImageUrl({ imageUrl: " https://cdn/a.webp " }),
      "https://cdn/a.webp",
    );
    assert.deepEqual(
      orderItemImageUrls({
        items: [
          { imageUrl: "https://cdn/a.webp" },
          { ImageUrl: "https://cdn/b.webp" },
          { imageUrl: "https://cdn/a.webp" },
        ],
      }),
      ["https://cdn/a.webp", "https://cdn/b.webp"],
    );
    assert.equal(
      orderItemsSummary(
        { items: [{ sku: "SKU-1", quantity: 3 }] },
        (qty) => `× ${qty}`,
      ),
      "SKU-1 × 3",
    );
  });
});

describe("order item list", () => {
  it("counts lines and pieces without a photo stack", () => {
    const one = {
      items: [{ name: "Tee", quantity: 2, imageUrl: "https://cdn/a.webp" }],
    };
    assert.equal(orderLineCount(one), 1);
    assert.equal(orderPieceCount(one), 2);
    assert.equal(orderPrimaryItemName(one), "Tee");
    assert.equal(orderPrimaryItemQuantity(one), 2);
    assert.deepEqual(orderSecondaryItemNames(one), []);
    assert.equal(orderHiddenLineCount(one), 0);
    assert.equal(orderItemsNeedCollapse(one), false);
    assert.equal(orderVisibleItems(one, false).length, 1);
    assert.equal(orderItemSku({ sku: "SKU-1", name: "Tee" }), "SKU-1");
  });

  it("collapses past the preview cap and expands to every line", () => {
    const row = {
      items: [
        { name: "A", quantity: 1, imageUrl: "https://cdn/a.webp" },
        { Name: "B", Quantity: 2 },
        { name: "C", quantity: 1, imageUrl: "https://cdn/c.webp" },
        { name: "D", quantity: 1 },
      ],
    };
    assert.equal(ORDER_ITEM_PREVIEW_MAX, 3);
    assert.equal(orderVisibleItems(row, false).length, 3);
    assert.equal(orderVisibleItems(row, true).length, 4);
    assert.equal(orderLineCount(row), 4);
    assert.equal(orderPieceCount(row), 5);
    assert.deepEqual(orderSecondaryItemNames(row), ["B", "C", "D"]);
    assert.equal(orderHiddenLineCount(row), 1);
    assert.equal(orderItemsNeedCollapse(row), true);
    assert.equal(orderVisibleItems({ items: [] }, false).length, 0);
  });
});

describe("status actions", () => {
  it("maps primary actions and cancel by status", () => {
    assert.deepEqual(primaryActionsForStatus("new"), [ORDER_ACTION.Confirm]);
    assert.deepEqual(primaryActionsForStatus("unpaid"), [ORDER_ACTION.Confirm]);
    assert.deepEqual(primaryActionsForStatus("awaiting_pack"), [
      ORDER_ACTION.Pack,
    ]);
    assert.deepEqual(primaryActionsForStatus("awaiting_deliver"), [
      ORDER_ACTION.Ship,
    ]);
    assert.deepEqual(primaryActionsForStatus("delivering"), [
      ORDER_ACTION.Deliver,
      ORDER_ACTION.Complete,
    ]);
    assert.deepEqual(primaryActionsForStatus("delivered"), []);
    assert.equal(canCancelOrder("new"), true);
    assert.equal(canCancelOrder("delivered"), false);
    assert.equal(canCancelOrder("cancelled"), false);
    assert.equal(orderStatusClass("cancelled"), "text-destructive");
    assert.equal(orderStatusClass("delivered"), "text-success");
    assert.equal(
      orderStatusLabelKey("awaiting_pack"),
      "pages.ordersPage.status.awaiting_pack",
    );
    assert.equal(
      orderStatusLabelKey("nope"),
      "pages.ordersPage.status.unknown",
    );
  });
});

describe("order label payload", () => {
  it("decodes Base64 and sanitizes the file name", () => {
    const bytes = decodeOrderLabelBytes({
      contentBase64: Buffer.from("pdf").toString("base64"),
      fileName: "a/b\\c.pdf",
      contentType: "application/pdf",
    });
    assert.equal(Buffer.from(bytes).toString(), "pdf");
    assert.equal(orderLabelFileName({ FileName: "a/b\\c.pdf" }), "a-b-c.pdf");
    assert.equal(orderLabelContentType({}), "application/octet-stream");
    assert.equal(decodeOrderLabelBytes({ contentBase64: "" }), null);
    assert.equal(decodeOrderLabelBytes({ contentBase64: "%%%" }), null);
  });
});

describe("formatOrderMoney", () => {
  it("formats a numeric total with currency", () => {
    const out = formatOrderMoney(1200, "RUB", "ru-RU");
    assert.match(out, /1[\s\u00a0]?200/);
    assert.equal(formatOrderMoney(null, "RUB"), "");
  });
});
