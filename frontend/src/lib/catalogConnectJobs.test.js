import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  isCatalogConnectJobActive,
  shouldRestorePreview,
} from "./catalogConnectJobs.js";

describe("catalogConnectJobs", () => {
  it("treats pending and running jobs as active", () => {
    assert.equal(isCatalogConnectJobActive({ status: "pending" }), true);
    assert.equal(isCatalogConnectJobActive({ status: "running" }), true);
    assert.equal(isCatalogConnectJobActive({ status: "completed" }), false);
  });

  it("restores a completed match preview until the user dismisses it", () => {
    const job = {
      kind: "match",
      status: "completed",
      operationId: "op-1",
      preview: { items: [] },
    };
    assert.equal(shouldRestorePreview(job, ""), true);
    assert.equal(shouldRestorePreview(job, "op-1"), false);
    assert.equal(shouldRestorePreview({ ...job, kind: "import" }, ""), false);
  });
});
