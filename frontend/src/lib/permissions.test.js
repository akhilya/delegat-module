import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  PERMS,
  canBrowse,
  hasPermission,
  isCabinetViewOnly,
  ASSIGNABLE_PERMISSIONS,
  mergeAssignableCatalog,
} from "./permissions.js";

const VIEW_ONLY = [PERMS.ReferenceRead, PERMS.WorkspaceView];

describe("isCabinetViewOnly", () => {
  it("is true for workspace.view plus optional reference.read", () => {
    assert.equal(isCabinetViewOnly({ permissions: VIEW_ONLY }), true);
    assert.equal(isCabinetViewOnly({ permissions: [PERMS.WorkspaceView] }), true);
  });

  it("is false when any write claim is present", () => {
    assert.equal(isCabinetViewOnly({ permissions: [PERMS.ReferenceRead] }), false);
    assert.equal(
      isCabinetViewOnly({
        permissions: [...VIEW_ONLY, PERMS.CatalogManage],
      }),
      false,
    );
  });
});

describe("canBrowse", () => {
  it("treats workspace.view as read access except plugins and the database dump", () => {
    const me = { permissions: VIEW_ONLY };
    assert.equal(PERMS.OrdersManage, "orders.manage");
    assert.equal(canBrowse(me, PERMS.UsersManage), true);
    assert.equal(canBrowse(me, PERMS.MarketplaceManage), true);
    assert.equal(canBrowse(me, PERMS.OrdersManage), true);
    assert.equal(canBrowse(me, PERMS.OperationsManage), true);
    assert.equal(canBrowse(me, PERMS.SettingsPlatform), true);
    assert.equal(canBrowse(me, PERMS.PluginsManage), false);
    assert.equal(canBrowse(me, PERMS.SettingsDump), false);
    assert.equal(hasPermission(me, PERMS.AiUse), false);
    assert.equal(canBrowse(me, PERMS.AiUse), false);
  });

  it("still honors an exact grant", () => {
    const me = { permissions: [PERMS.PluginsManage] };
    assert.equal(canBrowse(me, PERMS.PluginsManage), true);
    assert.equal(
      canBrowse({ permissions: [PERMS.SettingsDump] }, PERMS.SettingsDump),
      true,
    );
  });
});

describe("assignable permission catalog", () => {
  it("always lists orders.manage and never admin-only claims", () => {
    assert.equal(ASSIGNABLE_PERMISSIONS.includes(PERMS.OrdersManage), true);
    assert.equal(ASSIGNABLE_PERMISSIONS.includes(PERMS.MarketplaceManage), true);
    assert.equal(ASSIGNABLE_PERMISSIONS.includes(PERMS.RolesManage), false);
    assert.equal(ASSIGNABLE_PERMISSIONS.includes(PERMS.SettingsDump), false);
  });

  it("keeps SPA grants when the API catalog is stale", () => {
    const merged = mergeAssignableCatalog([
      PERMS.CatalogManage,
      PERMS.MarketplaceManage,
    ]);
    assert.equal(merged.includes(PERMS.OrdersManage), true);
    assert.equal(merged.includes(PERMS.RolesManage), false);
  });
});
