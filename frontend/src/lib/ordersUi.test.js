import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  DEFAULT_ORDER_BUCKET,
  ORDER_ACTION,
  ORDER_BUCKETS,
  ORDER_STATUS,
  canCancelOrder,
  canDownloadBoxLabel,
  canDownloadOrderLabel,
  canDownloadShipmentQr,
  decodeOrderLabelBytes,
  defaultOrderMailbox,
  filterOrdersBySearch,
  formatOrderMoney,
  isOrderBucket,
  mergeOrderLists,
  orderActionNeedsShipmentFields,
  orderBucketPath,
  orderDeliveryIsMarketplaceOwned,
  orderDisplayId,
  orderExternalId,
  orderLabelContentType,
  orderLabelFileName,
  orderMailboxPath,
  orderMailboxesForAdapter,
  ORDER_ITEM_PREVIEW_MAX,
  orderHiddenLineCount,
  orderItemImageUrl,
  orderItemImageUrls,
  orderItemLocalProductId,
  orderItemProductTo,
  orderItemSku,
  orderBulkCapabilities,
  orderStatusUpdatePayload,
  orderItemsNeedCollapse,
  orderItemsSummary,
  orderLineCount,
  orderPieceCount,
  orderBoxId,
  orderSupplyId,
  orderPrimaryItemName,
  orderPrimaryItemQuantity,
  orderSecondaryItemNames,
  orderSearchHitPage,
  orderSearchLookupIds,
  orderVisibleItems,
  orderStatusClass,
  orderStatusLabelKey,
  primaryActionsForStatus,
  mailboxForOrderStatus,
  resolveOrderMailbox,
  resolveOrderSearchNavigation,
  resolveOrdersLocation,
  shouldFetchMailboxAfterRouteSync,
  statusesForBucket,
  assemblyCounts,
  assemblyMailboxesForAdapter,
  assemblyStatusesForAdapter,
  isAssemblyOrderStatus,
  isOrdersOverviewPath,
  mailboxForOverviewTab,
  overviewTabEmptyTitleKey,
  overviewTabForStatus,
  overviewTabPath,
  parseOrdersOverviewTab,
  statusesForOverviewTab,
  orderAssemblyMoreCount,
  orderAssemblyTitle,
  ORDER_ACTION_LABEL,
  marketplaceOrderLabelUrl,
  marketplaceOrderStatusUrl,
  orderJobConfirmMeta,
  runPendingOrderJob,
  saveOrderLabelFile,
} from "./ordersUi.js";

const ordersPageSource = readFileSync(
  new URL("../pages/orders/OrdersPage.vue", import.meta.url),
  "utf8",
);
const routerSource = readFileSync(
  new URL("../router.js", import.meta.url),
  "utf8",
);

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
    assert.equal(statusesForBucket("pack").includes("new"), false);
    assert.equal(statusesForBucket("pack", "ozon").includes("new"), true);
    assert.equal(
      ORDER_BUCKETS.every((box) => !box.descriptionKey),
      true,
    );
    assert.doesNotMatch(ordersPageSource, /mailboxHintKey/);
    assert.equal(
      ORDER_BUCKETS[1].emptyDescriptionKey,
      "pages.ordersPage.emptyPackDescription",
    );
  });
});

describe("adapter order mailboxes", () => {
  it("hides unused mailboxes and folds Ozon approve into pack", () => {
    assert.deepEqual(
      orderMailboxesForAdapter("ozon").map((box) => box.id),
      ["pack", "ship", "delivery", "done", "closed"],
    );
    assert.deepEqual(
      orderMailboxesForAdapter("wildberries").map((box) => box.id),
      ["pack", "ship", "delivery", "done", "closed"],
    );
    assert.deepEqual(
      orderMailboxesForAdapter("yandex").map((box) => box.id),
      ["new", "pack", "ship", "delivery", "done", "closed"],
    );
    assert.deepEqual(
      orderMailboxesForAdapter("woocommerce").map((box) => box.id),
      ["new", "pack", "done", "closed"],
    );
    assert.deepEqual(
      orderMailboxesForAdapter("opencart").map((box) => box.id),
      ["new", "pack", "done", "closed"],
    );
    assert.equal(defaultOrderMailbox("ozon"), "pack");
    assert.equal(defaultOrderMailbox("yandex"), "new");
    assert.equal(resolveOrderMailbox("ozon", "new"), "pack");
    assert.equal(resolveOrderMailbox("ozon", "ship"), "ship");
    assert.equal(resolveOrderMailbox("woocommerce", "delivery"), "new");
    assert.equal(orderMailboxPath(5, "ship", "ozon"), "/orders/5/ship");
    assert.equal(orderMailboxPath(5, "new", "ozon"), "/orders/5/pack");
    assert.equal(orderMailboxPath(0, "pack", "ozon"), "/orders");
  });

  it("canonicalizes legacy bucket URLs onto a connection", () => {
    const connections = [
      { id: 5, adapterKey: "ozon" },
      { id: 9, adapterKey: "yandex" },
    ];
    assert.deepEqual(
      resolveOrdersLocation({
        connections,
        storedConnectionId: 5,
        routePath: "/orders/new",
        query: { connectionId: "9", order: "A-1" },
      }),
      {
        path: "/orders/9/new",
        query: { order: "A-1" },
      },
    );
    assert.deepEqual(
      resolveOrdersLocation({
        connections,
        storedConnectionId: 5,
        routePath: "/orders",
        query: { order: "A-1", connectionId: "9" },
      }),
      { path: "/orders", query: { order: "A-1", connectionId: "9" } },
    );
    assert.deepEqual(
      resolveOrdersLocation({
        connections,
        storedConnectionId: 5,
        routePath: "/orders/12",
        query: {},
      }),
      { path: "/orders/5/pack", query: {} },
    );
    assert.deepEqual(
      resolveOrdersLocation({
        connections,
        storedConnectionId: 5,
        preferredMailbox: "ship",
        routePath: "/orders/9",
        query: {},
      }),
      { path: "/orders/9/ship", query: {} },
    );
    assert.deepEqual(
      resolveOrdersLocation({
        connections,
        storedConnectionId: 5,
        routePath: "/orders/5/new",
        query: { connectionId: "5" },
      }),
      { path: "/orders/5/pack", query: {} },
    );
    assert.equal(
      resolveOrdersLocation({
        connections: [],
        routePath: "/orders",
        query: {},
      }).path,
      "/orders",
    );
  });
});

describe("assembly overview", () => {
  it("keeps /orders as the cross-connection packing page", () => {
    assert.equal(isOrdersOverviewPath("/orders"), true);
    assert.equal(isOrdersOverviewPath("/orders/"), true);
    assert.equal(isOrdersOverviewPath("/orders?order=1"), true);
    assert.equal(isOrdersOverviewPath("/orders/5/pack"), false);
    assert.equal(isOrdersOverviewPath("/orders/pack"), false);
    assert.equal(parseOrdersOverviewTab(undefined), "assembly");
    assert.equal(parseOrdersOverviewTab("SHIP"), "ship");
    assert.deepEqual(overviewTabPath("ship"), {
      path: "/orders",
      query: { tab: "ship" },
    });
    assert.deepEqual(overviewTabPath("assembly"), {
      path: "/orders",
      query: {},
    });
    assert.deepEqual(statusesForOverviewTab("assembly"), [
      "new",
      "unpaid",
      "confirmed",
      "awaiting_pack",
    ]);
    assert.equal(mailboxForOverviewTab("ship", "ozon"), "ship");
    assert.equal(mailboxForOverviewTab("ship", "woocommerce"), "new");
    assert.equal(mailboxForOverviewTab("assembly", "ozon"), "pack");
    assert.equal(overviewTabForStatus("awaiting_deliver"), "ship");
    assert.equal(overviewTabForStatus("cancelled"), null);
    assert.equal(
      overviewTabEmptyTitleKey("done"),
      "pages.ordersPage.emptyOverviewDoneTitle",
    );
    assert.deepEqual(
      assemblyMailboxesForAdapter("ozon").map((box) => box.id),
      ["pack"],
    );
    assert.deepEqual(
      assemblyMailboxesForAdapter("wildberries").map((box) => box.id),
      ["pack"],
    );
    assert.deepEqual(
      assemblyMailboxesForAdapter("yandex").map((box) => box.id),
      ["new", "pack"],
    );
    assert.deepEqual(assemblyStatusesForAdapter("ozon"), [
      "confirmed",
      "awaiting_pack",
      "new",
      "unpaid",
    ]);
    assert.deepEqual(assemblyStatusesForAdapter("wildberries"), [
      "confirmed",
      "awaiting_pack",
    ]);
    assert.deepEqual(assemblyStatusesForAdapter("yandex"), [
      "new",
      "unpaid",
      "confirmed",
      "awaiting_pack",
    ]);
    assert.equal(isAssemblyOrderStatus("new"), true);
    assert.equal(isAssemblyOrderStatus("awaiting_pack"), true);
    assert.equal(isAssemblyOrderStatus("awaiting_deliver"), false);
    assert.deepEqual(
      assemblyCounts([
        { status: "new" },
        { status: "unpaid" },
        { status: "awaiting_pack" },
        { status: "confirmed" },
        { status: "delivering" },
      ]),
      { waiting: 2, packing: 2, total: 4 },
    );
  });

  it("mounts the overview on /orders instead of a mailbox table", () => {
    const assemblyPageSource = readFileSync(
      new URL("../pages/orders/OrdersAssemblyPage.vue", import.meta.url),
      "utf8",
    );
    const rowSource = readFileSync(
      new URL("../components/orders/OrderAssemblyRow.vue", import.meta.url),
      "utf8",
    );
    const sidebarSource = readFileSync(
      new URL("../config/sidebarNav.js", import.meta.url),
      "utf8",
    );
    assert.match(routerSource, /OrdersAssemblyPage/);
    assert.match(assemblyPageSource, /Collapsible/);
    assert.match(assemblyPageSource, /InsetGroup/);
    assert.match(assemblyPageSource, /OrderAssemblyRow/);
    assert.doesNotMatch(assemblyPageSource, /LogTableFrame/);
    assert.doesNotMatch(assemblyPageSource, /OrderItemsCell/);
    assert.match(rowSource, /ProductImageCell/);
    assert.match(rowSource, /assemblyRowMore/);
    assert.match(rowSource, /useProductImageSize/);
    assert.match(rowSource, /spec\.identityClass/);
    assert.doesNotMatch(rowSource, /OrderItemsCell/);
    assert.doesNotMatch(rowSource, /orderStatusClass/);
    assert.doesNotMatch(rowSource, /const THUMB_SIZE/);
    assert.match(sidebarSource, /id: "orders-assembly"/);
    assert.match(sidebarSource, /to: PAGE_ORDERS/);
    assert.match(assemblyPageSource, /OrderRowContextMenu/);
    assert.match(assemblyPageSource, /OrderJobConfirmDialog/);
    assert.doesNotMatch(assemblyPageSource, /<ContextMenu\s+v-for/);
    assert.match(assemblyPageSource, /@contextmenu="onRowContextMenu\(/);
  });

  it("summarizes an assembly row as one Mail-style cell", () => {
    const two = {
      displayId: "A-1",
      items: [
        { name: "Tee", quantity: 1, imageUrl: "https://cdn/a.webp" },
        { name: "Hat", quantity: 2, imageUrl: "https://cdn/b.webp" },
      ],
    };
    assert.equal(orderAssemblyTitle(two), "Tee");
    assert.equal(orderAssemblyMoreCount(two), 1);
    assert.equal(
      orderAssemblyTitle({ displayId: "B-2", items: [] }),
      "B-2",
    );
    assert.equal(orderAssemblyMoreCount({ items: [{ name: "Tee" }] }), 0);
  });
});

describe("order number search", () => {
  it("opens the mailbox that actually holds the order", () => {
    assert.equal(
      mailboxForOrderStatus("awaiting_deliver", "wildberries"),
      "ship",
    );
    assert.equal(mailboxForOrderStatus("awaiting_pack", "wildberries"), "pack");
    assert.equal(mailboxForOrderStatus("new", "ozon"), "pack");
    assert.equal(mailboxForOrderStatus("new", "yandex"), "new");
    assert.equal(
      mailboxForOrderStatus("awaiting_deliver", "woocommerce"),
      null,
    );
  });

  it("does not keep a ship order on the pack mailbox after a number search", () => {
    const next = resolveOrderSearchNavigation({
      found: { externalId: "5656512202", status: "awaiting_deliver" },
      connectionId: 1,
      adapterKey: "wildberries",
      currentPath: "/orders/1/pack",
    });
    assert.deepEqual(next, {
      path: "/orders/1/ship",
      query: { order: "5656512202" },
    });
  });

  it("retries the first Ozon posting when the typed id is the order number", () => {
    assert.deepEqual(orderSearchLookupIds("30625619-0031"), [
      "30625619-0031",
      "30625619-0031-1",
    ]);
    assert.deepEqual(orderSearchLookupIds("30625619-0031-1"), [
      "30625619-0031-1",
    ]);
    assert.deepEqual(orderSearchLookupIds("5656512202"), ["5656512202"]);
    assert.deepEqual(orderSearchLookupIds("  "), []);
  });

  it("keeps only the matching row after a number search", () => {
    const found = {
      externalId: "30625619-0031-1",
      displayId: "30625619-0031",
      status: "awaiting_pack",
    };
    const page = orderSearchHitPage(found);
    assert.deepEqual(
      page.items.map((row) => orderExternalId(row)),
      ["30625619-0031-1"],
    );
    assert.equal(page.hasMore, false);
    assert.equal(orderSearchHitPage({ displayId: "no-id" }), null);

    const dumped = [found, { externalId: "111-222-1", displayId: "111-222" }];
    assert.deepEqual(
      filterOrdersBySearch(dumped, "30625619-0031").map((row) =>
        orderExternalId(row),
      ),
      ["30625619-0031-1"],
    );
    assert.equal(filterOrdersBySearch(dumped, "").length, 2);
  });

  it("pins the found order instead of clearing search and reloading the tab", () => {
    const lookup = ordersPageSource.slice(
      ordersPageSource.indexOf("async lookupAndOpenOrder"),
      ordersPageSource.indexOf("onChannelChange"),
    );
    assert.match(lookup, /orderSearchLookupIds/);
    assert.match(lookup, /orderSearchHitPage/);
    assert.match(ordersPageSource, /filterOrdersBySearch/);
    assert.doesNotMatch(lookup, /searchQuery = ""/);
    assert.match(lookup, /syncingRoute = true/);
    assert.equal(
      shouldFetchMailboxAfterRouteSync({
        fetch: false,
        connectionChanged: false,
        mailboxChanged: true,
        searchQuery: "30625619-0031",
      }),
      false,
    );
    assert.equal(
      shouldFetchMailboxAfterRouteSync({
        fetch: true,
        connectionChanged: false,
        mailboxChanged: false,
        searchQuery: "",
      }),
      true,
    );
    assert.match(ordersPageSource, /shouldFetchMailboxAfterRouteSync/);
    assert.match(ordersPageSource, /fetchedMailbox !== parsed\.bucket/);
    assert.match(ordersPageSource, /markFetchedLocation/);
    assert.equal(
      [...routerSource.matchAll(/stableKey:\s*"orders"/g)].length,
      2,
    );
    assert.match(routerSource, /stableKey:\s*"orders-assembly"/);
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

  it("only offers seller actions the marketplace actually has", () => {
    assert.deepEqual(primaryActionsForStatus("awaiting_pack", "ozon", "fbs"), [
      ORDER_ACTION.Pack,
    ]);
    assert.deepEqual(
      primaryActionsForStatus("awaiting_deliver", "ozon", "fbs"),
      [ORDER_ACTION.Ship],
    );
    assert.deepEqual(primaryActionsForStatus("delivering", "ozon", "fbs"), []);
    assert.deepEqual(
      primaryActionsForStatus("awaiting_pack", "ozon", "fbo"),
      [],
    );
    assert.equal(canCancelOrder("awaiting_pack", "ozon", "fbs"), true);
    assert.equal(canCancelOrder("delivering", "ozon", "fbs"), false);
    assert.equal(canCancelOrder("awaiting_pack", "ozon", "fbo"), false);

    assert.deepEqual(primaryActionsForStatus("awaiting_pack", "wildberries"), [
      ORDER_ACTION.Pack,
    ]);
    assert.deepEqual(
      primaryActionsForStatus("awaiting_deliver", "wildberries"),
      [ORDER_ACTION.Ship],
    );
    assert.deepEqual(primaryActionsForStatus("delivering", "wildberries"), []);
    assert.equal(canCancelOrder("awaiting_pack", "wildberries"), true);
    assert.equal(canCancelOrder("delivering", "wildberries"), false);

    assert.deepEqual(
      primaryActionsForStatus("delivering", "yandex", "fbs"),
      [],
    );
    assert.deepEqual(primaryActionsForStatus("delivering", "yandex", "dbs"), [
      ORDER_ACTION.Deliver,
    ]);
    assert.deepEqual(
      primaryActionsForStatus("delivering", "yandex", "fby"),
      [],
    );

    assert.equal(orderActionNeedsShipmentFields("ship", "ozon"), false);
    assert.equal(orderActionNeedsShipmentFields("ship", "wildberries"), false);
    assert.equal(orderActionNeedsShipmentFields("ship", "woocommerce"), true);
    assert.equal(orderDeliveryIsMarketplaceOwned("ozon"), true);
    assert.equal(orderDeliveryIsMarketplaceOwned("woocommerce"), false);

    const sheet = readFileSync(
      new URL("../components/orders/OrderDetailSheet.vue", import.meta.url),
      "utf8",
    );
    assert.match(sheet, /adapterKey/);
    assert.match(sheet, /orderActionNeedsShipmentFields/);
    assert.match(ordersPageSource, /adapter-key/);
  });

  it("only offers the barcode when the marketplace can print it", () => {
    assert.equal(canDownloadOrderLabel("awaiting_deliver", "ozon", "fbs"), true);
    assert.equal(canDownloadOrderLabel("awaiting_pack", "ozon", "fbs"), false);
    assert.equal(canDownloadOrderLabel("delivering", "ozon", "fbs"), false);
    assert.equal(canDownloadOrderLabel("awaiting_deliver", "ozon", "fbo"), false);

    assert.equal(canDownloadOrderLabel("awaiting_pack", "wildberries"), false);
    assert.equal(
      canDownloadOrderLabel("awaiting_deliver", "wildberries"),
      true,
    );
    assert.equal(canDownloadOrderLabel("delivering", "wildberries"), true);
    assert.equal(canDownloadOrderLabel("delivered", "wildberries"), false);

    assert.equal(canDownloadOrderLabel("confirmed", "yandex", "fbs"), true);
    assert.equal(canDownloadOrderLabel("awaiting_pack", "yandex", "dbs"), true);
    assert.equal(canDownloadOrderLabel("unpaid", "yandex", "fbs"), false);
    assert.equal(canDownloadOrderLabel("delivering", "yandex", "fbs"), false);
    assert.equal(canDownloadOrderLabel("confirmed", "yandex", "fby"), false);

    assert.equal(canDownloadOrderLabel("delivering", "woocommerce"), false);
    assert.equal(canDownloadOrderLabel("awaiting_pack", "opencart"), false);

    assert.equal(
      canDownloadBoxLabel("awaiting_deliver", "wildberries", "WB-TRBX-1"),
      true,
    );
    assert.equal(canDownloadBoxLabel("awaiting_deliver", "wildberries", ""), false);
    assert.equal(
      canDownloadBoxLabel("awaiting_deliver", "ozon", "WB-TRBX-1"),
      false,
    );
    assert.equal(
      canDownloadShipmentQr("delivering", "wildberries", "WB-GI-1"),
      true,
    );
    assert.equal(canDownloadShipmentQr("awaiting_deliver", "wildberries", "WB-GI-1"), false);
    assert.equal(orderSupplyId({ extra: { supplyId: "WB-GI-9" } }), "WB-GI-9");
    assert.equal(orderBoxId({ Extra: { boxId: "WB-TRBX-2" } }), "WB-TRBX-2");

    const sheet = readFileSync(
      new URL("../components/orders/OrderDetailSheet.vue", import.meta.url),
      "utf8",
    );
    assert.match(sheet, /canDownloadOrderLabel/);
    assert.match(sheet, /showLabelButton/);
    assert.match(sheet, /canManageShipments/);
    assert.match(sheet, /canDownloadBoxLabel/);
    assert.match(sheet, /packSupplyChoice/);
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

describe("local order item product", () => {
  it("exposes a catalog edit path only when the host linked a product", () => {
    assert.equal(orderItemLocalProductId({ localProductId: 12 }), 12);
    assert.equal(orderItemLocalProductId({ LocalProductId: "8" }), 8);
    assert.equal(orderItemLocalProductId({ localProductId: 0 }), null);
    assert.equal(orderItemLocalProductId({}), null);
    assert.equal(orderItemProductTo({ localProductId: 12 }), "/products/12/edit");
    assert.equal(orderItemProductTo({ sku: "ART" }), null);

    const itemsCell = readFileSync(
      new URL("../components/orders/OrderItemsCell.vue", import.meta.url),
      "utf8",
    );
    const sheet = readFileSync(
      new URL("../components/orders/OrderDetailSheet.vue", import.meta.url),
      "utf8",
    );
    const identity = readFileSync(
      new URL("../components/products/ProductIdentityCell.vue", import.meta.url),
      "utf8",
    );
    assert.match(itemsCell, /orderItemProductTo/);
    assert.match(sheet, /orderItemProductTo/);
    assert.match(identity, /RouterLink/);
    assert.match(identity, /@click\.stop/);
  });
});

describe("order bulk capabilities", () => {
  it("intersects seller actions across the selected rows", () => {
    const pack = { status: "awaiting_pack", channel: "fbs" };
    const ship = {
      status: "awaiting_deliver",
      channel: "fbs",
      extra: { supplyId: "WB-GI-1" },
    };
    const flags = { canMutate: true, canDownloadLabel: true };

    assert.deepEqual(orderBulkCapabilities([pack, pack], "ozon", flags), {
      actions: [ORDER_ACTION.Pack],
      canCancel: true,
      canDownloadLabel: false,
    });
    assert.deepEqual(
      orderBulkCapabilities([pack, ship], "ozon", flags),
      {
        actions: [],
        canCancel: true,
        canDownloadLabel: false,
      },
    );
    assert.deepEqual(orderBulkCapabilities([ship, ship], "ozon", flags), {
      actions: [ORDER_ACTION.Ship],
      canCancel: true,
      canDownloadLabel: true,
    });
    assert.deepEqual(
      orderBulkCapabilities([pack, pack], "wildberries", flags),
      {
        actions: [ORDER_ACTION.Pack],
        canCancel: true,
        canDownloadLabel: false,
      },
    );
    assert.deepEqual(
      orderBulkCapabilities([ship, ship], "wildberries", flags),
      {
        actions: [ORDER_ACTION.Ship],
        canCancel: false,
        canDownloadLabel: true,
      },
    );
    assert.deepEqual(
      orderBulkCapabilities([pack], "ozon", {
        canMutate: false,
        canDownloadLabel: false,
      }),
      { actions: [], canCancel: false, canDownloadLabel: false },
    );
    assert.deepEqual(orderBulkCapabilities([], "ozon", flags), {
      actions: [],
      canCancel: false,
      canDownloadLabel: false,
    });

    assert.deepEqual(orderStatusUpdatePayload("pack", pack, "wildberries"), {
      action: "pack",
    });
    assert.deepEqual(orderStatusUpdatePayload("ship", ship, "wildberries"), {
      action: "ship",
      extra: { supplyId: "WB-GI-1" },
    });
    assert.deepEqual(orderStatusUpdatePayload("ship", ship, "ozon"), {
      action: "ship",
    });
  });

  it("puts bulk actions and a row context menu on the orders table", () => {
    const menu = readFileSync(
      new URL("../components/orders/OrderRowContextMenu.vue", import.meta.url),
      "utf8",
    );
    assert.match(ordersPageSource, /OrderRowContextMenu/);
    assert.match(ordersPageSource, /selectedIds/);
    assert.match(ordersPageSource, /orderBulkCapabilities/);
    const footer = ordersPageSource.slice(
      ordersPageSource.indexOf('<template #footer>'),
      ordersPageSource.indexOf("</template>", ordersPageSource.indexOf('<template #footer>')),
    );
    const bulkActions = footer.slice(
      footer.indexOf("bulkCaps.actions"),
      footer.indexOf("bulkCaps.canCancel"),
    );
    assert.match(bulkActions, /variant="default"/);
    assert.doesNotMatch(bulkActions, /variant="outline"/);
    assert.doesNotMatch(ordersPageSource, /ProductImageSizeMenu/);
    assert.match(menu, /ProductImageSizeContextSub/);
    assert.match(menu, /openOrder/);
    assert.match(menu, /ORDER_ACTION_LABEL/);
    assert.match(ordersPageSource, /OrderJobConfirmDialog/);
    assert.doesNotMatch(ordersPageSource, /const ACTION_CONFIRM/);
    assert.doesNotMatch(ordersPageSource, /const ACTION_LABEL/);
  });
});

describe("order job helpers", () => {
  function t(key, params) {
    return params ? `${key}:${JSON.stringify(params)}` : key;
  }

  it("builds confirm copy for pack, Wildberries ship, label, and bulk", () => {
    assert.deepEqual(
      orderJobConfirmMeta({ kind: "status", action: "pack", ids: ["1"] }, t),
      {
        title: "pages.ordersPage.confirmPackTitle",
        description: "pages.ordersPage.confirmPackDescription",
        label: "pages.ordersPage.actionPack",
        destructive: false,
      },
    );
    assert.equal(
      orderJobConfirmMeta(
        { kind: "status", action: "ship", ids: ["1"] },
        t,
        { adapterKey: "wildberries" },
      ).description,
      "pages.ordersPage.confirmShipSupplyDescription",
    );
    assert.equal(
      orderJobConfirmMeta({ kind: "label", ids: ["1"] }, t).title,
      "pages.ordersPage.actionDownloadLabel",
    );
    assert.match(
      orderJobConfirmMeta(
        { kind: "status", action: "pack", ids: ["1", "2"] },
        t,
      ).title,
      /bulkConfirmTitle/,
    );
    assert.equal(ORDER_ACTION_LABEL.pack, "pages.ordersPage.actionPack");
  });

  it("encodes marketplace order status and label URLs", () => {
    assert.equal(
      marketplaceOrderStatusUrl(3, "a/b"),
      "/marketplace/connections/3/orders/a%2Fb/status",
    );
    assert.equal(
      marketplaceOrderLabelUrl(9, "42"),
      "/marketplace/connections/9/orders/42/label",
    );
  });

  it("throws when the label payload is empty", () => {
    assert.throws(
      () =>
        saveOrderLabelFile(
          { contentBase64: "" },
          "1",
          { t, triggerDownload: () => {} },
        ),
      { message: "pages.ordersPage.labelEmpty" },
    );
  });

  it("runs each order id and reports partial failure", async () => {
    const errors = [];
    const result = await runPendingOrderJob(
      { kind: "status", action: "pack", ids: ["a", "b"] },
      {
        t,
        runOne: async (id) => {
          if (id === "b") throw new Error("nope");
        },
        onItemError: (error) => errors.push(error.message),
        onSuccess: () => {},
        onPartial: (message) => errors.push(message),
      },
    );
    assert.deepEqual(result, { ok: 1, failedIds: ["b"] });
    assert.equal(errors[0], "nope");
    assert.match(errors[1], /bulkPartialFailed/);
  });
});

describe("formatOrderMoney", () => {
  it("formats a numeric total with currency", () => {
    const out = formatOrderMoney(1200, "RUB", "ru-RU");
    assert.match(out, /1[\s\u00a0]?200/);
    assert.equal(formatOrderMoney(null, "RUB"), "");
  });
});
