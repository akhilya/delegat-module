import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  canEnterSetupWizard,
  parseStoreCount,
  parseOnboardingIncomplete,
  parseIncompleteStore,
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
      resolveSetupNavigation({
        path: PAGE_SETUP,
        me: admin,
        storeCount: 1,
        onboardingIncomplete: false,
      }),
      { redirect: "/products" },
    );
  });

  it("stays on setup when the first store has not finished onboarding", () => {
    assert.equal(
      resolveSetupNavigation({
        path: PAGE_SETUP,
        me: admin,
        storeCount: 1,
        onboardingIncomplete: true,
      }),
      null,
    );
  });

  it("forces setup when onboarding is incomplete", () => {
    assert.deepEqual(
      resolveSetupNavigation({
        path: "/products",
        me: admin,
        storeCount: 1,
        onboardingIncomplete: true,
      }),
      { redirect: PAGE_SETUP },
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

  it("does not force incomplete onboarding for non-creators", () => {
    assert.equal(
      resolveSetupNavigation({
        path: "/products",
        me: member,
        storeCount: 1,
        onboardingIncomplete: true,
      }),
      null,
    );
  });
});

describe("parseOnboardingIncomplete", () => {
  it("is true when a store has null completedAt", () => {
    assert.equal(
      parseOnboardingIncomplete({
        items: [{ id: 1, onboardingCompletedAt: null }],
        total: 1,
      }),
      true,
    );
  });

  it("is false when every store has completedAt", () => {
    assert.equal(
      parseOnboardingIncomplete({
        items: [{ id: 1, onboardingCompletedAt: "2026-08-16T00:00:00Z" }],
        total: 1,
      }),
      false,
    );
  });

  it("unwraps value payloads", () => {
    assert.equal(
      parseOnboardingIncomplete({
        value: { items: [{ id: 2, onboardingCompletedAt: null }], total: 1 },
      }),
      true,
    );
  });

  it("treats omitted completedAt as incomplete", () => {
    assert.equal(
      parseOnboardingIncomplete({
        items: [{ id: 1, name: "WIP" }],
        total: 1,
      }),
      true,
    );
  });
});

describe("parseIncompleteStore", () => {
  it("returns the first store with null completedAt", () => {
    assert.deepEqual(
      parseIncompleteStore({
        items: [
          { id: 1, name: "Done", onboardingCompletedAt: "2026-08-16T00:00:00Z" },
          { id: 2, name: "WIP", onboardingCompletedAt: null },
        ],
      }),
      { id: 2, name: "WIP" },
    );
  });

  it("returns null when every store is complete", () => {
    assert.equal(
      parseIncompleteStore({
        items: [{ id: 1, name: "Done", onboardingCompletedAt: "2026-08-16T00:00:00Z" }],
      }),
      null,
    );
  });
});
