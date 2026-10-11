import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  ORDER_LIVE_SYNC_MS,
  shouldPullLiveOrders,
  startOrderLiveSync,
} from "./orderLiveSync.js";

const ordersPage = readFileSync(
  new URL("../pages/orders/OrdersPage.vue", import.meta.url),
  "utf8",
);
const assemblyPage = readFileSync(
  new URL("../pages/orders/OrdersAssemblyPage.vue", import.meta.url),
  "utf8",
);

describe("shouldPullLiveOrders", () => {
  it("pulls while the operator is looking at the current page of orders", () => {
    assert.equal(shouldPullLiveOrders({}), true);
    assert.equal(shouldPullLiveOrders({ search: "   " }), true);
  });

  it("stays quiet during bootstrap, another load, search, paging, or a hidden tab", () => {
    assert.equal(shouldPullLiveOrders({ bootstrapping: true }), false);
    assert.equal(shouldPullLiveOrders({ busy: true }), false);
    assert.equal(shouldPullLiveOrders({ search: "76728543-0063" }), false);
    assert.equal(shouldPullLiveOrders({ pagedFurther: true }), false);
    assert.equal(shouldPullLiveOrders({ hidden: true }), false);
  });
});

describe("startOrderLiveSync", () => {
  it("refreshes often enough that a new order is not a multi-minute wait", () => {
    assert.ok(ORDER_LIVE_SYNC_MS <= 20_000);
    assert.equal(typeof startOrderLiveSync(() => {}), "function");
  });

  it("is wired into every cabinet orders screen", () => {
    for (const source of [ordersPage, assemblyPage]) {
      assert.match(
        source,
        /startOrderLiveSync\(\(\) => this\.pullLiveOrders\(\)\)/,
      );
      assert.match(source, /shouldPullLiveOrders/);
      assert.match(source, /fetchOrders\(\{ silent: true, quiet: true \}\)/);
      assert.match(source, /beforeUnmount/);
    }
  });
});
