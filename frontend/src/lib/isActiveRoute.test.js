import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  getNavRouteMatchScore,
  resolveActiveNavItemId,
} from "./isActiveRoute.js";

const ORDER_NAV = [
  { id: "orders-assembly", type: "link", to: "/orders" },
  { id: "orders-conn-2", type: "link", to: "/orders/2" },
  { id: "orders-conn-9", type: "link", to: "/orders/9" },
];

describe("order overview nav", () => {
  it("highlights To pack only on /orders, not on a mailbox", () => {
    assert.equal(
      getNavRouteMatchScore({ path: "/orders/2/pack", query: {} }, "/orders"),
      -1,
    );
    assert.ok(
      getNavRouteMatchScore({ path: "/orders", query: {} }, "/orders") > 0,
    );
    assert.equal(
      resolveActiveNavItemId(
        { path: "/orders", query: { tab: "ship" } },
        ORDER_NAV,
      ),
      "orders-assembly",
    );
    assert.equal(
      resolveActiveNavItemId(
        { path: "/orders/2/pack", query: {} },
        ORDER_NAV,
      ),
      "orders-conn-2",
    );
  });
});
