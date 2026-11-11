import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { PERMS } from "./permissions.js";
import { canAccessPath, fallbackPathForUser } from "./routeAccess.js";
import { canShowNavLink, uniqueByNavTarget } from "./navAccess.js";
import { visibleSettingsNavIds } from "./settingsNavCatalog.js";
import { getAccessibleAdminSections } from "./adminSections.js";

const sidebarNav = readFileSync(
  new URL("../config/sidebarNav.js", import.meta.url),
  "utf8",
);

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
  PERMS.OrdersManage,
  PERMS.FilesManage,
  PERMS.OperationsManage,
  PERMS.AiUse,
];

const STORE_MANAGER = [
  PERMS.StoreMembersManage,
  PERMS.StoreSettingsManage,
  PERMS.CatalogManage,
  PERMS.MarketplaceManage,
  PERMS.OrdersManage,
  PERMS.FilesManage,
  PERMS.OperationsManage,
  PERMS.AiUse,
  PERMS.ReferenceRead,
];

const OPERATOR = [
  PERMS.CatalogManage,
  PERMS.MarketplaceManage,
  PERMS.OrdersManage,
  PERMS.FilesManage,
  PERMS.OperationsManage,
  PERMS.AiUse,
  PERMS.ReferenceRead,
];

const VIEWER = [PERMS.ReferenceRead];
const VIEW_ONLY = [PERMS.ReferenceRead, PERMS.WorkspaceView];

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

  it("shows Administration for a store manager", () => {
    assert.deepEqual(visibleSettingsNavIds(me(STORE_MANAGER)), [
      "user",
      "admin",
    ]);
  });

  it("shows Administration when only store.settings.manage is granted", () => {
    assert.deepEqual(visibleSettingsNavIds(me([PERMS.StoreSettingsManage])), [
      "user",
      "admin",
    ]);
  });

  it("shows Administration when only store.members.manage is granted", () => {
    assert.deepEqual(visibleSettingsNavIds(me([PERMS.StoreMembersManage])), [
      "user",
      "admin",
    ]);
  });

  it("does not show Stores for catalog.manage alone", () => {
    assert.deepEqual(visibleSettingsNavIds(me([PERMS.CatalogManage])), [
      "user",
    ]);
  });

  it("shows Administration for an owner when feature flags are on", () => {
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
      ["user", "admin", "api", "plugins", "logs", "adapter-logs"],
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
      ["user", "admin"],
    );
  });

  it("shows settings except plugins for workspace.view", () => {
    assert.deepEqual(
      visibleSettingsNavIds(
        me(VIEW_ONLY, {
          aiEnabled: true,
          pluginsEnabled: true,
          logErrorsEnabled: true,
          logAdapterHttpEnabled: true,
          publicApiEnabled: true,
        }),
      ),
      ["user", "admin", "api", "logs", "adapter-logs"],
    );
  });
});

describe("admin section access", () => {
  it("keeps Stores, Users, and AI sections gated by their existing permissions", () => {
    assert.deepEqual(
      getAccessibleAdminSections(me([PERMS.StoreSettingsManage])).map(
        (section) => section.id,
      ),
      ["stores"],
    );
    assert.deepEqual(
      getAccessibleAdminSections(me([PERMS.UsersManage])).map(
        (section) => section.id,
      ),
      ["users"],
    );
    assert.deepEqual(
      getAccessibleAdminSections(
        me([PERMS.SettingsPlatform], { aiEnabled: true }),
      ).map((section) => section.id),
      ["ai"],
    );
    assert.deepEqual(
      getAccessibleAdminSections(
        me([PERMS.SettingsPlatform], { aiEnabled: false }),
      ),
      [],
    );
    assert.deepEqual(
      getAccessibleAdminSections(
        me(
          [
            PERMS.StoreSettingsManage,
            PERMS.UsersManage,
            PERMS.SettingsPlatform,
          ],
          { aiEnabled: true },
        ),
      ).map((section) => section.id),
      ["stores", "users", "ai"],
    );
  });

  it("allows the unified route through independent sections or platform access", () => {
    assert.equal(
      canAccessPath(me([PERMS.StoreMembersManage]), "/settings/admin"),
      true,
    );
    assert.equal(
      canAccessPath(me([PERMS.StoreMembersManage]), "/settings"),
      true,
    );
    assert.equal(
      canAccessPath(me([PERMS.UsersManage]), "/settings/admin"),
      true,
    );
    assert.equal(
      canAccessPath(
        me([PERMS.SettingsPlatform], { aiEnabled: false }),
        "/settings/admin",
      ),
      true,
    );
  });
});

/** Destinations the sidebar can offer (settings + catalog + store surfaces). */
const SIDEBAR_PATHS = [
  "/orders/1",
  "/products",
  "/products?trash=1",
  "/products/create",
  "/product-groups",
  "/categories",
  "/storage",
  "/adapter",
  "/tools",
  "/store-attributes",
  "/settings/user",
  "/settings",
  "/settings/admin",
  "/settings/admin/api",
  "/settings/admin/plugins",
  "/settings/admin/error-logs",
  "/settings/adapter-logs",
];

function visibleSidebarPaths(permissions, extra = {}) {
  return SIDEBAR_PATHS.filter((path) =>
    canShowNavLink(me(permissions, extra), path),
  );
}

describe("canShowNavLink", () => {
  it("hides the standalone barcode page from navigation while keeping its route", () => {
    assert.doesNotMatch(sidebarNav, /id:\s*"barcodes"/);
    assert.equal(canAccessPath(me([PERMS.CatalogManage]), "/barcodes"), true);
  });

  it("hides notes from navigation while keeping the route", () => {
    assert.doesNotMatch(sidebarNav, /id:\s*"notes"/);
    assert.equal(canAccessPath(me([PERMS.CatalogManage]), "/notes"), true);
  });

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

  it("hides orders without orders.manage", () => {
    assert.equal(canShowNavLink(me([PERMS.CatalogManage]), "/orders/1"), false);
    assert.equal(
      canShowNavLink(me([PERMS.MarketplaceManage]), "/orders/1"),
      false,
    );
    assert.equal(canShowNavLink(me(VIEWER), "/orders/1"), false);
    assert.equal(canShowNavLink(me([PERMS.OrdersManage]), "/orders"), true);
    assert.equal(canShowNavLink(me(OPERATOR), "/orders/1"), true);
    assert.equal(canShowNavLink(me(VIEW_ONLY), "/orders/1/pack"), true);
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

  it("lets workspace.view browse the cabinet except plugins, create, and setup", () => {
    const demo = me(VIEW_ONLY, {
      aiEnabled: true,
      pluginsEnabled: true,
      logErrorsEnabled: true,
      logAdapterHttpEnabled: true,
      publicApiEnabled: true,
    });
    assert.equal(canAccessPath(demo, "/settings"), true);
    assert.equal(canAccessPath(demo, "/settings/admin/users"), true);
    assert.equal(canAccessPath(demo, "/adapter"), true);
    assert.equal(canAccessPath(demo, "/storage"), true);
    assert.equal(canAccessPath(demo, "/products/1/edit"), true);
    assert.equal(canAccessPath(demo, "/products/create"), false);
    assert.equal(canAccessPath(demo, "/setup"), false);
    assert.equal(canAccessPath(demo, "/settings/admin/plugins"), false);
    assert.equal(fallbackPathForUser(demo), "/products");
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

  it("workspace.view: cabinet browse without create or plugins", () => {
    assert.deepEqual(
      visibleSidebarPaths(VIEW_ONLY, {
        aiEnabled: true,
        pluginsEnabled: true,
        logErrorsEnabled: true,
        logAdapterHttpEnabled: true,
        publicApiEnabled: true,
      }),
      [
        "/orders/1",
        "/products",
        "/products?trash=1",
        "/product-groups",
        "/categories",
        "/storage",
        "/adapter",
        "/tools",
        "/store-attributes",
        "/settings/user",
        "/settings",
        "/settings/admin",
        "/settings/admin/api",
        "/settings/admin/error-logs",
        "/settings/adapter-logs",
      ],
    );
  });

  it("operator: catalog + storage + adapters + tools, no Stores", () => {
    assert.deepEqual(visibleSidebarPaths(OPERATOR), [
      "/orders/1",
      "/products",
      "/products?trash=1",
      "/products/create",
      "/product-groups",
      "/categories",
      "/storage",
      "/adapter",
      "/tools",
      "/store-attributes",
      "/settings/user",
    ]);
  });

  it("store manager: operator surfaces plus Administration once", () => {
    const paths = visibleSidebarPaths(STORE_MANAGER);
    assert.deepEqual(
      paths.filter((path) => path === "/settings/admin"),
      ["/settings/admin"],
    );
    assert.deepEqual(paths, [
      "/orders/1",
      "/products",
      "/products?trash=1",
      "/products/create",
      "/product-groups",
      "/categories",
      "/storage",
      "/adapter",
      "/tools",
      "/store-attributes",
      "/settings/user",
      "/settings",
      "/settings/admin",
    ]);
  });

  it("catalog.manage alone: write catalog + tools, no Stores/storage/adapters", () => {
    assert.deepEqual(visibleSidebarPaths([PERMS.CatalogManage]), [
      "/products",
      "/products?trash=1",
      "/products/create",
      "/product-groups",
      "/categories",
      "/tools",
      "/store-attributes",
      "/settings/user",
    ]);
  });

  it("owner with flags: Administration without duplicate sidebar links", () => {
    const paths = visibleSidebarPaths(OWNER, {
      aiEnabled: true,
      pluginsEnabled: true,
      logErrorsEnabled: true,
      logAdapterHttpEnabled: true,
      publicApiEnabled: true,
    });
    assert.equal(paths.filter((path) => path === "/settings/admin").length, 1);
    assert.deepEqual(
      paths.filter((path) => path.startsWith("/settings")),
      [
        "/settings/user",
        "/settings",
        "/settings/admin",
        "/settings/admin/api",
        "/settings/admin/plugins",
        "/settings/admin/error-logs",
        "/settings/adapter-logs",
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
