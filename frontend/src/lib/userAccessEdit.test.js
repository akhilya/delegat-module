import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  canMutateUserAccess,
  isOwnerRoleLocked,
  isUserAccessRowLocked,
} from "./userAccessEdit.js";

describe("canMutateUserAccess", () => {
  it("allows an owner editing someone else", () => {
    assert.equal(
      canMutateUserAccess({ canManageRoles: true, editingSelf: false }),
      true,
    );
  });

  it("blocks editing your own roles", () => {
    assert.equal(
      canMutateUserAccess({ canManageRoles: true, editingSelf: true }),
      false,
    );
  });

  it("blocks when roles.manage is missing", () => {
    assert.equal(
      canMutateUserAccess({ canManageRoles: false, editingSelf: false }),
      false,
    );
  });
});

describe("isOwnerRoleLocked", () => {
  it("locks Owner on yourself", () => {
    assert.equal(
      isOwnerRoleLocked({
        editingSelf: true,
        canToggleOwner: true,
        targetIsAdmin: true,
      }),
      true,
    );
  });

  it("locks Owner while the instance-owner flag is on", () => {
    assert.equal(
      isOwnerRoleLocked({
        editingSelf: false,
        canToggleOwner: true,
        targetIsAdmin: true,
      }),
      true,
    );
  });

  it("locks Owner when the actor cannot assign it", () => {
    assert.equal(
      isOwnerRoleLocked({
        editingSelf: false,
        canToggleOwner: false,
        targetIsAdmin: false,
      }),
      true,
    );
  });

  it("allows Owner on another non-owner", () => {
    assert.equal(
      isOwnerRoleLocked({
        editingSelf: false,
        canToggleOwner: true,
        targetIsAdmin: false,
      }),
      false,
    );
  });
});

describe("isUserAccessRowLocked", () => {
  it("locks rows while saving or editing self", () => {
    assert.equal(
      isUserAccessRowLocked({
        canManageRoles: true,
        editingSelf: true,
        saving: false,
      }),
      true,
    );
    assert.equal(
      isUserAccessRowLocked({
        canManageRoles: true,
        editingSelf: false,
        saving: true,
      }),
      true,
    );
  });
});
