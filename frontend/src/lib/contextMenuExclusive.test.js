import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { readFileSync } from "node:fs";
import {
  closeForeignContextMenus,
  shouldCloseContextMenu,
} from "./contextMenuExclusive.js";

describe("shouldCloseContextMenu", () => {
  it("keeps the menu that already contains the pointer", () => {
    const menu = { contains: (node) => node === "inside" };
    assert.equal(shouldCloseContextMenu(menu, "inside"), false);
  });

  it("closes when the pointer is on another row", () => {
    const menu = { contains: () => false };
    assert.equal(shouldCloseContextMenu(menu, "other-row"), true);
  });
});

describe("closeForeignContextMenus", () => {
  it("dismisses every open menu except the one under the pointer", () => {
    const keep = { id: "keep", contains: (n) => n === "in-keep" };
    const other = { id: "other", contains: () => false };
    const dismissed = [];
    const n = closeForeignContextMenus("in-keep", {
      queryOpen: () => [keep, other],
      dismiss: (el) => dismissed.push(el.id),
    });
    assert.equal(n, 1);
    assert.deepEqual(dismissed, ["other"]);
  });

  it("closes all open menus when the click is outside every panel", () => {
    const a = { id: "a", contains: () => false };
    const b = { id: "b", contains: () => false };
    const dismissed = [];
    const n = closeForeignContextMenus("row-2", {
      queryOpen: () => [a, b],
      dismiss: (el) => dismissed.push(el.id),
    });
    assert.equal(n, 2);
    assert.deepEqual(dismissed, ["a", "b"]);
  });
});

describe("products table context menu", () => {
  it("uses one ContextMenu for the table body, not one per row", () => {
    const src = readFileSync(
      new URL("../components/products/productsTable.vue", import.meta.url),
      "utf8",
    );
    assert.doesNotMatch(src, /<ContextMenu\s+v-for/);
    assert.match(src, /<ContextMenu>/);
    assert.match(src, /@contextmenu="onRowContextMenu\(row\)"/);
  });
});
