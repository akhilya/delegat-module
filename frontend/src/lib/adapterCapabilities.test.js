import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import {
  adapterCanGetOrderLabels,
  adapterCanGetFulfillmentLocations,
  adapterCanGetOrders,
  adapterCanImportAll,
  adapterCanImportCatalog,
  adapterCanLink,
  adapterCanManageShipments,
  adapterCanSearchPublicCatalog,
  adapterCanUpdateOrderStatus,
  adapterHasCapability,
  parseCapabilityFlags,
} from "./adapterCapabilities.js";

describe("parseCapabilityFlags", () => {
  it("splits enum ToString flags without treating ImportProductBatch as ImportProduct", () => {
    const set = parseCapabilityFlags(
      "GetCategories, ImportProduct, ImportProductBatch, ExportProduct",
    );
    assert.equal(set.has("ImportProduct"), true);
    assert.equal(set.has("ImportProductBatch"), true);
    assert.equal(set.has("ImportAllProducts"), false);
  });

  it("reads boolean capability objects", () => {
    const set = parseCapabilityFlags({
      importProduct: true,
      importAllProducts: false,
      exportProduct: true,
    });
    assert.equal(set.has("ImportProduct"), true);
    assert.equal(set.has("ImportAllProducts"), false);
    assert.equal(set.has("ExportProduct"), true);
  });

  it("expands FullProducts from enum ToString composites", () => {
    const set = parseCapabilityFlags(
      "FullProducts, FieldBasedGrouping, ImportProductBatch, ExportProductBulk, UpdateSku, ArchiveProduct",
    );
    assert.equal(set.has("FullProducts"), true);
    assert.equal(set.has("ImportProduct"), true);
    assert.equal(set.has("ImportAllProducts"), true);
    assert.equal(set.has("ExportProduct"), true);
    assert.equal(set.has("FieldBasedGrouping"), true);
  });

  it("expands Basic to ImportProduct and ExportProduct", () => {
    const set = parseCapabilityFlags("Basic, CreateCategory");
    assert.equal(set.has("ImportProduct"), true);
    assert.equal(set.has("ExportProduct"), true);
    assert.equal(set.has("ImportAllProducts"), false);
    assert.equal(set.has("CreateCategory"), true);
  });
});

describe("adapterCanImportCatalog", () => {
  it("is true when ImportAllProducts is present", () => {
    assert.equal(
      adapterCanImportCatalog({ capabilities: "ImportAllProducts, ExportProduct" }),
      true,
    );
  });

  it("is true for ImportProduct without ImportAllProducts", () => {
    assert.equal(
      adapterCanImportCatalog({ capabilities: "ImportProduct, ExportProduct" }),
      true,
    );
  });

  it("is true when FullProducts hides atomic import flags", () => {
    assert.equal(
      adapterCanImportCatalog({
        capabilities: "FullProducts, FieldBasedGrouping",
      }),
      true,
    );
  });

  it("is false for export-only adapters", () => {
    assert.equal(
      adapterCanImportCatalog({ capabilities: "ExportProduct, ExportPrices" }),
      false,
    );
  });
});

describe("adapterCanImportAll", () => {
  it("requires ImportAllProducts", () => {
    assert.equal(
      adapterCanImportAll({ capabilities: "ImportProduct, ExportProduct" }),
      false,
    );
    assert.equal(
      adapterCanImportAll({ capabilities: "ImportAllProducts" }),
      true,
    );
    assert.equal(
      adapterCanImportAll({
        capabilities: "FullProducts, FieldBasedGrouping",
      }),
      true,
    );
  });
});

describe("adapterCanLink", () => {
  it("is true for FullProducts composites and export-only adapters", () => {
    assert.equal(
      adapterCanLink({ capabilities: "FullProducts, FieldBasedGrouping" }),
      true,
    );
    assert.equal(
      adapterCanLink({ capabilities: "ExportProduct, ExportPrices" }),
      true,
    );
    assert.equal(adapterCanLink({ capabilities: "GetCategories" }), false);
  });
});

describe("adapterCanSearchPublicCatalog", () => {
  it("requires SearchPublicCatalog and is not implied by FullProducts", () => {
    assert.equal(
      adapterCanSearchPublicCatalog({ capabilities: "FullProducts" }),
      false,
    );
    assert.equal(
      adapterCanSearchPublicCatalog({
        capabilities: "FullProducts, SearchPublicCatalog",
      }),
      true,
    );
    assert.equal(
      adapterCanSearchPublicCatalog({ capabilities: { searchPublicCatalog: true } }),
      true,
    );
  });
});

describe("adapterHasCapability", () => {
  it("reads ExportPrices from FullProducts and boolean objects", () => {
    assert.equal(
      adapterHasCapability({ capabilities: "FullProducts" }, "ExportPrices"),
      true,
    );
    assert.equal(
      adapterHasCapability({ capabilities: { exportPrices: true } }, "ExportPrices"),
      true,
    );
    assert.equal(
      adapterHasCapability({ capabilities: "GetOrders" }, "UpdatePromotions"),
      false,
    );
  });
});

describe("adapterCanGetOrders", () => {
  it("reads GetOrders from enum strings and boolean objects", () => {
    assert.equal(adapterCanGetOrders({ capabilities: "GetOrders" }), true);
    assert.equal(
      adapterCanGetOrders({ capabilities: { getOrders: true } }),
      true,
    );
    assert.equal(adapterCanGetOrders({ capabilities: "FullProducts" }), false);
    assert.equal(
      adapterCanUpdateOrderStatus({ capabilities: { updateOrderStatus: true } }),
      true,
    );
    assert.equal(
      adapterCanUpdateOrderStatus({ capabilities: "GetOrders" }),
      false,
    );
    assert.equal(
      adapterCanGetOrderLabels({ capabilities: { getOrderLabels: true } }),
      true,
    );
    assert.equal(
      adapterCanGetOrderLabels({ capabilities: "GetOrders" }),
      false,
    );
    assert.equal(
      adapterCanManageShipments({ capabilities: { manageShipments: true } }),
      true,
    );
    assert.equal(
      adapterCanManageShipments({ capabilities: "GetOrderLabels" }),
      false,
    );
    assert.equal(
      adapterCanGetFulfillmentLocations({
        capabilities: { getFulfillmentLocations: true },
      }),
      true,
    );
  });
});

describe("adapter capability dialog labels", () => {
  it("names packing labels, fulfillment, shipments, and public catalog search", () => {
    const connectionPage = readFileSync(
      new URL("../pages/marketplace/connectionPage.vue", import.meta.url),
      "utf8",
    );
    const en = JSON.parse(
      readFileSync(new URL("../locales/en.json", import.meta.url), "utf8"),
    );
    const ru = JSON.parse(
      readFileSync(new URL("../locales/ru.json", import.meta.url), "utf8"),
    );
    const labels = en.marketplace.connectionPage.capabilities;
    const ruLabels = ru.marketplace.connectionPage.capabilities;
    for (const key of [
      "getOrderLabels",
      "getFulfillmentLocations",
      "manageShipments",
      "searchPublicCatalog",
    ]) {
      assert.match(connectionPage, new RegExp(`capabilities\\.${key}`));
      assert.equal(typeof labels[key], "string");
      assert.ok(labels[key].length > 0);
      assert.equal(typeof ruLabels[key], "string");
      assert.ok(ruLabels[key].length > 0);
    }
    assert.equal(typeof en.pages.usersPage.permissionLabels.orders.manage, "string");
    assert.equal(typeof ru.pages.usersPage.permissionLabels.orders.manage, "string");
  });
});
