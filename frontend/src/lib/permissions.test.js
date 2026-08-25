import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  PERMS,
  canBrowse,
  hasPermission,
  isCabinetViewOnly,
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
  it("treats workspace.view as read access except plugins", () => {
    const me = { permissions: VIEW_ONLY };
    assert.equal(canBrowse(me, PERMS.UsersManage), true);
    assert.equal(canBrowse(me, PERMS.MarketplaceManage), true);
    assert.equal(canBrowse(me, PERMS.OperationsManage), true);
    assert.equal(canBrowse(me, PERMS.PluginsManage), false);
    assert.equal(hasPermission(me, PERMS.AiUse), false);
    assert.equal(canBrowse(me, PERMS.AiUse), false);
  });

  it("still honors an exact grant", () => {
    const me = { permissions: [PERMS.PluginsManage] };
    assert.equal(canBrowse(me, PERMS.PluginsManage), true);
  });
});
