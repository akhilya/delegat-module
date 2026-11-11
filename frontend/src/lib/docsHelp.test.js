import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { catalogHasSlug, docsSlugForPath, pageHelpSlug } from "./docsHelp.js";

const catalog = {
  locale: "ru",
  groups: [
    {
      text: "Кабинет",
      items: [
        { text: "Товары", slug: "workspace/products" },
        { text: "Площадки", slug: "workspace/adapters" },
        { text: "Настройки", slug: "workspace/settings" },
        { text: "ИИ-помощник", slug: "workspace/ai" },
        { text: "Инструменты", slug: "workspace/tools" },
        { text: "Связать каталоги", slug: "workspace/catalog-connect" },
        { text: "Ozon", slug: "adapters/ozon" },
        { text: "Avito", slug: "adapters/avito" },
      ],
    },
  ],
};

describe("docsSlugForPath", () => {
  it("maps catalog screens to workspace articles", () => {
    assert.equal(docsSlugForPath("/"), "workspace/products");
    assert.equal(docsSlugForPath("/products"), "workspace/products");
    assert.equal(docsSlugForPath("/products/create"), "workspace/products");
    assert.equal(docsSlugForPath("/products/12/edit"), "workspace/products");
    assert.equal(docsSlugForPath("/categories"), "workspace/categories");
    assert.equal(docsSlugForPath("/categories/3/edit"), "workspace/categories");
    assert.equal(docsSlugForPath("/product-groups"), "workspace/groups");
    assert.equal(docsSlugForPath("/product-groups/create"), "workspace/groups");
    assert.equal(docsSlugForPath("/product-groups/4/edit"), "workspace/groups");
    assert.equal(docsSlugForPath("/barcodes"), "workspace/barcodes");
    assert.equal(docsSlugForPath("/storage"), "workspace/files");
    assert.equal(docsSlugForPath("/store-attributes"), "workspace/attributes");
    assert.equal(
      docsSlugForPath("/store-attributes/2/edit"),
      "workspace/attributes",
    );
  });

  it("maps adapters and a known connection key", () => {
    assert.equal(docsSlugForPath("/adapter"), "workspace/adapters");
    assert.equal(docsSlugForPath("/adapter/plugins"), "workspace/adapters");
    assert.equal(docsSlugForPath("/adapter/9"), "workspace/adapters");
    assert.equal(
      docsSlugForPath("/adapter/9", { adapterKey: "ozon" }),
      "adapters/ozon",
    );
    assert.equal(
      docsSlugForPath("/adapter/9", { adapterKey: "avito" }),
      "adapters/avito",
    );
    assert.equal(
      docsSlugForPath("/adapter/9/categories/import", {
        adapterKey: "wildberries",
      }),
      "workspace/mapping",
    );
    assert.equal(
      docsSlugForPath("/adapter/9/categories/4", { adapterKey: "ozon" }),
      "workspace/mapping",
    );
    assert.equal(
      docsSlugForPath("/adapter/9", { adapterKey: "unknown" }),
      "workspace/adapters",
    );
  });

  it("maps settings, AI, and tools", () => {
    assert.equal(docsSlugForPath("/settings"), "workspace/settings");
    assert.equal(docsSlugForPath("/settings/user"), "workspace/settings");
    assert.equal(docsSlugForPath("/settings/admin"), "workspace/settings");
    assert.equal(docsSlugForPath("/settings/operations"), "workspace/settings");
    assert.equal(docsSlugForPath("/settings/admin/ai"), "workspace/ai");
    assert.equal(docsSlugForPath("/tools"), "workspace/tools");
    assert.equal(
      docsSlugForPath("/tools/catalog-connect"),
      "workspace/catalog-connect",
    );
    assert.equal(
      docsSlugForPath("/tools/catalog-scrape"),
      "workspace/catalog-scrape",
    );
    assert.equal(
      docsSlugForPath("/tools/public-api-try"),
      "workspace/public-api",
    );
    assert.equal(docsSlugForPath("/tools/export-all-prices"), null);
    assert.equal(
      docsSlugForPath("/settings/admin/api"),
      "workspace/public-api",
    );
    assert.equal(
      docsSlugForPath("/settings/admin/stores"),
      "workspace/settings",
    );
    assert.equal(
      docsSlugForPath("/settings/admin/users"),
      "workspace/settings",
    );
    assert.equal(
      docsSlugForPath("/settings/adapter-logs"),
      "workspace/settings",
    );
    assert.equal(docsSlugForPath("/setup"), "workspace/store");
  });

  it("maps orders and notes", () => {
    assert.equal(docsSlugForPath("/orders"), "workspace/orders");
    assert.equal(docsSlugForPath("/orders/3/pack"), "workspace/orders");
    assert.equal(docsSlugForPath("/notes"), "workspace/notes");
  });

  it("returns null when there is no product article", () => {
    assert.equal(docsSlugForPath("/signIn"), null);
    assert.equal(docsSlugForPath("/tools/export-all-prices"), null);
  });
});

describe("pageHelpSlug", () => {
  it("hides Help when the catalog is down or the slug is missing", () => {
    assert.equal(pageHelpSlug("/products", null), null);
    assert.equal(pageHelpSlug("/products", { groups: [] }), null);
    assert.equal(pageHelpSlug("/orders", catalog), null);
    assert.equal(
      pageHelpSlug("/orders", {
        groups: [{ items: [{ slug: "workspace/orders" }] }],
      }),
      "workspace/orders",
    );
    assert.equal(pageHelpSlug("/products", catalog), "workspace/products");
    assert.equal(
      pageHelpSlug("/adapter/2", catalog, { adapterKey: "ozon" }),
      "adapters/ozon",
    );
  });

  it("catalogHasSlug treats hub empty slug as present when listed", () => {
    const withHub = {
      groups: [{ items: [{ slug: "" }, { slug: "workspace/products" }] }],
    };
    assert.equal(catalogHasSlug(withHub, ""), true);
    assert.equal(catalogHasSlug(withHub, "workspace/missing"), false);
  });
});
