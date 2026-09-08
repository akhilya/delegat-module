import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";

const table = readFileSync(
  new URL("../components/ui/table/Table.vue", import.meta.url),
  "utf8",
);
const cell = readFileSync(
  new URL("../components/ui/table/TableCell.vue", import.meta.url),
  "utf8",
);
const frame = readFileSync(
  new URL("../components/logs/LogTableFrame.vue", import.meta.url),
  "utf8",
);
const chrome = readFileSync(
  new URL("../components/AppPageChrome.vue", import.meta.url),
  "utf8",
);
const productsPage = readFileSync(
  new URL("../pages/productsPage.vue", import.meta.url),
  "utf8",
);
const productsTable = readFileSync(
  new URL("../components/products/productsTable.vue", import.meta.url),
  "utf8",
);
const ordersPage = readFileSync(
  new URL("../pages/orders/OrdersPage.vue", import.meta.url),
  "utf8",
);
const assemblyPage = readFileSync(
  new URL("../pages/orders/OrdersAssemblyPage.vue", import.meta.url),
  "utf8",
);
const appShell = readFileSync(
  new URL("../app.vue", import.meta.url),
  "utf8",
);
const dockFooter = readFileSync(
  new URL("../components/PageDockFooter.vue", import.meta.url),
  "utf8",
);
const navigationBar = readFileSync(
  new URL("../components/navigationBar.vue", import.meta.url),
  "utf8",
);

describe("flush table primitive", () => {
  it("keeps contain=false so sticky headers and row-height refs still bind to <table>", () => {
    assert.match(table, /contain:\s*\{\s*type:\s*Boolean,\s*default:\s*true/);
    assert.match(table, /defineExpose\(\{\s*\$el:\s*rootEl\s*\}\)/);
    assert.match(table, /:is="contain \? 'div' : 'table'"/);
    assert.doesNotMatch(table, /"table-fixed"/);
    assert.doesNotMatch(table, /rounded-/);
  });

  it("forwards cell attrs (colspan, style) onto <td>", () => {
    assert.match(cell, /inheritAttrs:\s*false/);
    assert.match(cell, /v-bind="\$attrs"/);
  });

  it("keeps catalog tables flush with a pinned footer when the page fills the inset", () => {
    assert.doesNotMatch(frame, /rounded/);
    assert.doesNotMatch(frame, /ring-/);
    assert.match(frame, /fill:\s*\{\s*type:\s*Boolean/);
    assert.match(frame, /dockFooter:\s*\{\s*type:\s*Boolean/);
    assert.match(frame, /\$slots\.footer/);
    assert.match(frame, /<slot name="footer"/);
    assert.match(frame, /overflow-x-auto/);
    assert.match(chrome, /fillViewport/);
    assert.match(chrome, /h-full max-h-full/);
    assert.match(chrome, /page-scroll-end/);
    assert.match(appShell, /SidebarInset class="[^"]*flex-col/);
    assert.match(appShell, /data-slot="page-scroll"/);
    assert.match(appShell, /id="page-footer-dock"/);
    assert.doesNotMatch(productsPage, /fill-viewport/);
    assert.match(productsPage, /PageDockFooter/);
    assert.doesNotMatch(productsTable, /overflow-auto/);
    assert.doesNotMatch(productsTable, /overflow-x-auto/);
    assert.doesNotMatch(productsTable, /overflow-y-clip/);
    assert.doesNotMatch(ordersPage, /fill-viewport/);
    assert.match(ordersPage, /<LogTableFrame[^>]*dock-footer/);
    assert.match(ordersPage, /:contain="false"/);
    assert.doesNotMatch(assemblyPage, /fill-viewport/);
    assert.doesNotMatch(assemblyPage, /overflow-y-auto/);
    assert.match(dockFooter, /adaptive-padding/);
    assert.match(dockFooter, /border-t/);
    assert.match(dockFooter, /:disabled="!pinned"/);
    assert.match(dockFooter, /shouldPinPageFooter/);
    assert.doesNotMatch(dockFooter, /ring-1/);
    assert.doesNotMatch(dockFooter, /rounded-xl/);
    assert.doesNotMatch(productsTable, /class="flex w-full flex-1 flex-col"/);
    assert.doesNotMatch(productsPage, /class="flex w-full flex-1 flex-col"/);
  });

  it("sticks catalog and mailbox headers to the page scroller under the navbar", () => {
    const pageStickyTop = /top-\[var\(--app-navbar-height\)\]/;
    assert.match(productsTable, pageStickyTop);
    assert.match(productsTable, /\[data-slot="page-scroll"\]/);
    assert.match(ordersPage, pageStickyTop);
    assert.match(ordersPage, /:contain="false"/);
    assert.match(ordersPage, /\[&_td\]:border-b/);
    assert.match(
      frame,
      /fill \? 'min-h-0 flex-1 overflow-auto' : dockFooter \? 'min-w-0' : 'overflow-x-auto'/,
    );
    assert.match(navigationBar, /sticky top-0 left-0 z-30/);
  });
});
