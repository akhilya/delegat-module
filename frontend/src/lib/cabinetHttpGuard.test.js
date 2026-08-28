import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { PERMS } from "./permissions.js";
import { shouldBlockCabinetViewOnlyRequest } from "./cabinetHttpGuard.js";

const viewOnly = {
  permissions: [PERMS.ReferenceRead, PERMS.WorkspaceView],
};

describe("shouldBlockCabinetViewOnlyRequest", () => {
  it("allows GET and the session exceptions", () => {
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "get",
        url: "/users",
      }),
      false,
    );
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "post",
        url: "/auth/refresh",
      }),
      false,
    );
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "post",
        url: "/auth/logOut",
      }),
      false,
    );
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "post",
        url: "/users/me/memberships/12/select",
      }),
      false,
    );
  });

  it("blocks other mutations for view-only accounts", () => {
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "patch",
        url: "/users/me",
      }),
      true,
    );
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "post",
        url: "/products",
      }),
      true,
    );
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(viewOnly, {
        method: "get",
        url: "/settings/database/dump",
      }),
      true,
    );
  });

  it("does not block owners who also have workspace.view", () => {
    assert.equal(
      shouldBlockCabinetViewOnlyRequest(
        { permissions: [PERMS.WorkspaceView, PERMS.CatalogManage] },
        { method: "post", url: "/products" },
      ),
      false,
    );
  });
});
