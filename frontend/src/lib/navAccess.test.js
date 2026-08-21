import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { PERMS } from "./permissions.js";
import { canAccessPath } from "./routeAccess.js";
import { canShowNavLink, uniqueByNavTarget } from "./navAccess.js";
import { visibleSettingsNavIds } from "./settingsNavCatalog.js";

const OWNER = [
  PERMS.UsersManage,
  PERMS.PluginsManage,
  PERMS.SettingsPlatform,
  PERMS.StoresCreate,
  PERMS.ReferenceRead,
  PERMS.ApiAccess,
  PERMS.StoreMembersManage,
  PERMS.StoreSettingsManage,
  PERMS.CatalogManage,
  PERMS.MarketplaceManage,
  PERMS.FilesManage,
  PERMS.OperationsManage,
  PERMS.AiUse,
];

const STORE_MANAGER = [
  PERMS.StoreMembersManage,
  PERMS.StoreSettingsManage,
  PERMS.CatalogManage,
  PERMS.MarketplaceManage,
  PERMS.FilesManage,
  PERMS.OperationsManage,
  PERMS.AiUse,
  PERMS.ReferenceRead,
];

const OPERATOR = [
  PERMS.CatalogManage,
  PERMS.MarketplaceManage,
  PERMS.FilesManage,
  PERMS.OperationsManage,
  PERMS.AiUse,
  PERMS.ReferenceRead,
];

const VIEWER = [PERMS.ReferenceRead];

function me(permissions, extra = {}) {
  return { permissions, ...extra };
}

describe("visibleSettingsNavIds", () => {
  it("shows User only for a viewer", () => {
    assert.deepEqual(visibleSettingsNavIds(me(VIEWER)), ["user"]);
  });

  it("shows User only for an operator (no store or platform settings)", () => {
    assert.deepEqual(visibleSettingsNavIds(me(OPERATOR)), ["user"]);
  });

  it("shows Stores once for a store manager", () => {
    assert.deepEqual(visibleSettingsNavIds(me(STORE_MANAGER)), [
      "user",
      "stores",
    ]);
  });

  it("shows Stores once when only store.settings.manage is granted", () => {
    assert.deepEqual(visibleSettingsNavIds(me([PERMS.StoreSettingsManage])), [
      "user",
      "stores",
    ]);
  });

  it("shows Stores once when only store.members.manage is granted", () => {
    assert.deepEqual(visibleSettingsNavIds(me([PERMS.StoreMembersManage])), [
      "user",
      "stores",
    ]);
  });

  it("does not show Stores for catalog.manage alone", () => {
    assert.deepEqual(visibleSettingsNavIds(me([PERMS.CatalogManage])), [
      "user",
    ]);
  });

  it("shows platform settings for an owner when feature flags are on", () => {
    assert.deepEqual(
      visibleSettingsNavIds(
        me(OWNER, {
          aiEnabled: true,
          pluginsEnabled: true,
          logErrorsEnabled: true,
          logAdapterHttpEnabled: true,
          publicApiEnabled: true,
        }),
      ),
      [
        "user",
        "overview",
        "stores",
        "users",
        "api",
        "plugins",
        "logs",
        "adapter-logs",
        "ai",
      ],
    );
  });

  it("hides flagged settings when the instance has them off", () => {
    assert.deepEqual(
      visibleSettingsNavIds(
        me(OWNER, {
          aiEnabled: false,
          pluginsEnabled: false,
          logErrorsEnabled: false,
          logAdapterHttpEnabled: false,
          publicApiEnabled: false,
        }),
      ),
      ["user", "overview", "stores", "users"],
    );
  });
});

/** Destinations the sidebar can offer (settings + catalog + store surfaces). */
const SIDEBAR_PATHS = [
  "/products",
  "/products?trash=1",
  "/products/create",
  "/product-groups",
  "/categories",
  "/barcodes",
  "/notes",
  "/storage",
  "/adapter",
  "/tools",
  "/store-attributes",
  "/settings/user",
  "/settings",
  "/settings/admin/stores",
  "/settings/admin/users",
  "/settings/admin/api",
  "/settings/admin/plugins",
  "/settings/admin/error-logs",
  "/settings/adapter-logs",
  "/settings/admin/ai",
];

function visibleSidebarPaths(permissions, extra = {}) {
  return SIDEBAR_PATHS.filter((path) =>
    canShowNavLink(me(permissions, extra), path),
  );
}

describe("canShowNavLink", () => {
  it("allows product list for any member", () => {
    assert.equal(canShowNavLink(me(VIEWER), "/products"), true);
    assert.equal(canShowNavLink(me(VIEWER), "/products?trash=1"), true);
  });

  it("hides catalog write pages from viewers", () => {
    assert.equal(canShowNavLink(me(VIEWER), "/products/create"), false);
    assert.equal(canShowNavLink(me(VIEWER), "/product-groups"), false);
    assert.equal(canShowNavLink(me(VIEWER), "/categories"), false);
    assert.equal(canShowNavLink(me(VIEWER), "/barcodes"), false);
    assert.equal(canShowNavLink(me(VIEWER), "/notes"), false);
    assert.equal(canShowNavLink(me(VIEWER), "/store-attributes"), false);
    assert.equal(canAccessPath(me(VIEWER), "/store-attributes/1/edit"), false);
    assert.equal(canAccessPath(me(OPERATOR), "/store-attributes/1/edit"), true);
  });

  it("shows catalog write pages to operators", () => {
    assert.equal(canShowNavLink(me(OPERATOR), "/products/create"), true);
    assert.equal(canShowNavLink(me(OPERATOR), "/adapter"), true);
    assert.equal(canShowNavLink(me(OPERATOR), "/storage"), true);
  });

  it("hides storage without files.manage", () => {
    assert.equal(canShowNavLink(me(VIEWER), "/storage"), false);
    assert.equal(canShowNavLink(me([PERMS.CatalogManage]), "/storage"), false);
  });

  it("hides adapters without marketplace.manage", () => {
    assert.equal(canShowNavLink(me([PERMS.CatalogManage]), "/adapter"), false);
    assert.equal(
      canShowNavLink(me([PERMS.CatalogManage]), "/adapter/3"),
      false,
    );
  });

  it("matches route gates for setup", () => {
    assert.equal(canAccessPath(me(STORE_MANAGER), "/setup"), false);
    assert.equal(canAccessPath(me([PERMS.StoresCreate]), "/setup"), true);
  });

  it("gates Try Public API like API keys", () => {
    assert.equal(
      canAccessPath(
        me(OWNER, { publicApiEnabled: true }),
        "/tools/public-api-try",
      ),
      true,
    );
    assert.equal(
      canAccessPath(
        me(OWNER, { publicApiEnabled: false }),
        "/tools/public-api-try",
      ),
      false,
    );
    assert.equal(canAccessPath(me(OPERATOR), "/tools/public-api-try"), false);
    assert.equal(
      canAccessPath(
        me([PERMS.ApiAccess], { publicApiEnabled: true }),
        "/tools",
      ),
      true,
    );
  });
});

describe("sidebar path matrix by role", () => {
  it("viewer: products, trash, and user settings only", () => {
    assert.deepEqual(visibleSidebarPaths(VIEWER), [
      "/products",
      "/products?trash=1",
      "/settings/user",
    ]);
  });

  it("operator: catalog + storage + adapters + tools, no Stores", () => {
    assert.deepEqual(visibleSidebarPaths(OPERATOR), [
      "/products",
      "/products?trash=1",
      "/products/create",
      "/product-groups",
      "/categories",
      "/barcodes",
      "/notes",
      "/storage",
      "/adapter",
      "/tools",
      "/store-attributes",
      "/settings/user",
    ]);
  });

  it("store manager: operator surfaces plus Stores once", () => {
    const paths = visibleSidebarPaths(STORE_MANAGER);
    assert.deepEqual(
      paths.filter((path) => path === "/settings/admin/stores"),
      ["/settings/admin/stores"],
    );
    assert.deepEqual(paths, [
      "/products",
      "/products?trash=1",
      "/products/create",
      "/product-groups",
      "/categories",
      "/barcodes",
      "/notes",
      "/storage",
      "/adapter",
      "/tools",
      "/store-attributes",
      "/settings/user",
      "/settings/admin/stores",
    ]);
  });

  it("catalog.manage alone: write catalog + tools, no Stores/storage/adapters", () => {
    assert.deepEqual(visibleSidebarPaths([PERMS.CatalogManage]), [
      "/products",
      "/products?trash=1",
      "/products/create",
      "/product-groups",
      "/categories",
      "/barcodes",
      "/notes",
      "/tools",
      "/store-attributes",
      "/settings/user",
    ]);
  });

  it("owner with flags: platform settings without duplicating Stores", () => {
    const paths = visibleSidebarPaths(OWNER, {
      aiEnabled: true,
      pluginsEnabled: true,
      logErrorsEnabled: true,
      logAdapterHttpEnabled: true,
      publicApiEnabled: true,
    });
    assert.equal(
      paths.filter((path) => path === "/settings/admin/stores").length,
      1,
    );
    assert.deepEqual(
      paths.filter((path) => path.startsWith("/settings")),
      [
        "/settings/user",
        "/settings",
        "/settings/admin/stores",
        "/settings/admin/users",
        "/settings/admin/api",
        "/settings/admin/plugins",
        "/settings/admin/error-logs",
        "/settings/adapter-logs",
        "/settings/admin/ai",
      ],
    );
  });
});

describe("uniqueByNavTarget", () => {
  it("drops a second Stores link to the same path", () => {
    const items = uniqueByNavTarget([
      { id: "user", to: "/settings/user" },
      { id: "stores-personal", to: "/settings/admin/stores" },
      { id: "stores-admin", to: "/settings/admin/stores" },
    ]);
    assert.deepEqual(
      items.map((item) => item.id),
      ["user", "stores-personal"],
    );
  });

  it("keeps Products and Trash (same path, different query)", () => {
    const items = uniqueByNavTarget([
      { id: "products", to: "/products" },
      { id: "new-product", to: "/products/create" },
      { id: "trash", to: "/products?trash=1" },
    ]);
    assert.deepEqual(
      items.map((item) => item.id),
      ["products", "new-product", "trash"],
    );
  });
});
