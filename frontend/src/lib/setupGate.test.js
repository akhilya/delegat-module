import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  canEnterSetupWizard,
  parseStoreCount,
  resolveSetupNavigation,
  PAGE_SETUP,
} from "./setupGate.js";

describe("canEnterSetupWizard", () => {
  it("allows admin", () => {
    assert.equal(canEnterSetupWizard({ isAdmin: true, permissions: [] }), true);
  });

  it("allows stores.create", () => {
    assert.equal(
      canEnterSetupWizard({ isAdmin: false, permissions: ["stores.create"] }),
      true,
    );
  });

  it("rejects members without create", () => {
    assert.equal(
      canEnterSetupWizard({ isAdmin: false, permissions: ["catalog.manage"] }),
      false,
    );
  });
});

describe("parseStoreCount", () => {
  it("reads total", () => {
    assert.equal(parseStoreCount({ total: 0, items: [] }), 0);
    assert.equal(parseStoreCount({ value: { total: 2, items: [{}, {}] } }), 2);
  });

  it("falls back to items length", () => {
    assert.equal(parseStoreCount({ items: [{}] }), 1);
  });
});

describe("resolveSetupNavigation", () => {
  const admin = { isAdmin: true, permissions: ["stores.create"] };
  const member = { isAdmin: false, permissions: ["catalog.manage"] };

  it("forces setup when admin has zero stores", () => {
    assert.deepEqual(
      resolveSetupNavigation({ path: "/products", me: admin, storeCount: 0 }),
      { redirect: PAGE_SETUP },
    );
  });

  it("leaves app alone when stores exist", () => {
    assert.equal(
      resolveSetupNavigation({ path: "/products", me: admin, storeCount: 1 }),
      null,
    );
  });

  it("redirects away from setup when stores already exist", () => {
    assert.deepEqual(
      resolveSetupNavigation({ path: PAGE_SETUP, me: admin, storeCount: 1 }),
      { redirect: "/products" },
    );
  });

  it("does not force setup for non-creators", () => {
    assert.equal(
      resolveSetupNavigation({ path: "/products", me: member, storeCount: 0 }),
      null,
    );
  });

  it("kicks non-creators off /setup", () => {
    assert.deepEqual(
      resolveSetupNavigation({ path: PAGE_SETUP, me: member, storeCount: 0 }),
      { redirect: "/products" },
    );
  });
});
