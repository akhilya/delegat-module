import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { resolvePageBreadcrumbs } from "./pageBreadcrumbs.js";

const t = (key) => key;

function trail(partial) {
  return resolvePageBreadcrumbs({ t, ...partial });
}

describe("resolvePageBreadcrumbs", () => {
  it("uses the products route title on the catalog", () => {
    assert.deepEqual(trail({ name: "/products", path: "/products" }), [
      { label: "routes.products" },
    ]);
  });

  it("nests trash under products", () => {
    assert.deepEqual(
      trail({
        name: "/products",
        path: "/products",
        query: { trash: "1" },
      }),
      [
        { label: "routes.products", to: "/products" },
        { label: "pages.productsPage.breadcrumbTrash" },
      ],
    );
  });

  it("uses the live product title on edit", () => {
    assert.deepEqual(
      trail({
        name: "/products/:id/edit",
        path: "/products/9/edit",
        detail: "SKU-9",
      }),
      [{ label: "routes.products", to: "/products" }, { label: "SKU-9" }],
    );
  });

  it("builds settings from the nav map", () => {
    assert.deepEqual(trail({ path: "/settings" }), [{ label: "nav.overview" }]);
    assert.deepEqual(trail({ path: "/settings/admin/stores" }), [
      { label: "nav.overview", to: "/settings" },
      { label: "nav.stores" },
    ]);
  });

  it("puts the store name on the store detail trail", () => {
    assert.deepEqual(
      trail({
        path: "/settings/admin/stores/3",
        detail: "Main",
      }),
      [
        { label: "nav.overview", to: "/settings" },
        { label: "nav.stores", to: "/settings/admin/stores" },
        { label: "Main" },
      ],
    );
  });

  it("does not repeat connection tabs in the trail", () => {
    assert.deepEqual(
      trail({
        name: "/adapter/:connId",
        path: "/adapter/4",
        query: { tab: "mappings" },
        detail: "Ozon",
      }),
      [{ label: "routes.adapters", to: "/adapter" }, { label: "Ozon" }],
    );
  });

  it("nests category create under the connection", () => {
    assert.deepEqual(
      trail({
        name: "/adapter/:connId/categories/create",
        path: "/adapter/4/categories/create",
        params: { connId: "4" },
        parentLabel: "Medusa",
      }),
      [
        { label: "routes.adapters", to: "/adapter" },
        { label: "Medusa", to: "/adapter/4?tab=mappings" },
        { label: "marketplace.categoryCreatePage.createCategory" },
      ],
    );
  });

  it("nests category import under the connection", () => {
    assert.deepEqual(
      trail({
        name: "/adapter/:connId/categories/import",
        path: "/adapter/4/categories/import",
        params: { connId: "4" },
        parentLabel: "Ozon",
      }),
      [
        { label: "routes.adapters", to: "/adapter" },
        { label: "Ozon", to: "/adapter/4?tab=mappings" },
        { label: "marketplace.categoryImportPage.importCategory" },
      ],
    );
  });

  it("nests field mapping under adapters → connection → mappings", () => {
    assert.deepEqual(
      trail({
        name: "/adapter/:connId/categories/:mappingId",
        path: "/adapter/4/categories/12",
        params: { connId: "4" },
        parentLabel: "Ozon",
        detail: "Phones",
      }),
      [
        { label: "routes.adapters", to: "/adapter" },
        { label: "Ozon", to: "/adapter/4" },
        {
          label: "marketplace.fieldMappingPage.mappings",
          to: "/adapter/4?tab=mappings",
        },
        { label: "Phones" },
      ],
    );
  });

  it("nests tools under the tools hub", () => {
    assert.deepEqual(
      trail({
        name: "/tools/catalog-connect",
        path: "/tools/catalog-connect",
      }),
      [
        { label: "routes.tools", to: "/tools" },
        { label: "routes.catalogConnect" },
      ],
    );
    assert.deepEqual(
      trail({
        name: "/tools/:toolId",
        path: "/tools/export-all-prices",
        detail: "Update all prices",
      }),
      [{ label: "routes.tools", to: "/tools" }, { label: "Update all prices" }],
    );
  });

  it("uses the connection label on order mailboxes", () => {
    assert.deepEqual(
      trail({
        name: "/orders/:connectionId/:bucket",
        path: "/orders/4/new",
        parentLabel: "WB shop",
      }),
      [{ label: "routes.orders", to: "/orders" }, { label: "WB shop" }],
    );
  });

  it("keeps the orders hub as a single crumb (tabs live in the bar)", () => {
    assert.deepEqual(trail({ name: "/orders", path: "/orders" }), [
      { label: "routes.orders" },
    ]);
  });
});
