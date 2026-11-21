import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  OPERATION_LOG_LIMIT,
  REQUEST_SCOPED_SYNC_TYPES,
  appendOperationLog,
  canStopOperation,
  classifyStopOperationResponse,
  shouldApplyOperationFinish,
} from "./operationsControl.js";

describe("operationsControl", () => {
  it("hides Stop for one-shot request-scoped sync", () => {
    for (const type of REQUEST_SCOPED_SYNC_TYPES) {
      assert.equal(canStopOperation(type, "running"), false, type);
    }
  });

  it("shows Stop for queued jobs including catalog scrape and match", () => {
    assert.equal(canStopOperation("batch-import", "running"), true);
    assert.equal(canStopOperation("batch-export", "running"), true);
    assert.equal(canStopOperation("catalog-scrape", "running"), true);
    assert.equal(canStopOperation("catalog-match", "running"), true);
    assert.equal(canStopOperation("catalog-connect-import", "running"), true);
    assert.equal(canStopOperation("catalog-connect-match", "running"), true);
    assert.equal(canStopOperation("product-transfer", "running"), true);
    assert.equal(canStopOperation("image-download", "paused"), true);
  });

  it("hides Stop once stopRequested is already true for an active operation", () => {
    assert.equal(canStopOperation("integration", "running", false), true);
    assert.equal(canStopOperation("integration", "running", true), false);
  });

  it("classifies coordinator-owned stop responses as stop_requested so authoritative partial/failed finishes apply", () => {
    const durable = classifyStopOperationResponse({ status: "running", stopRequested: true });
    assert.deepEqual(durable, { mode: "stop_requested", status: "running" });
    assert.equal(shouldApplyOperationFinish(durable.status, "partial"), true);
    assert.equal(shouldApplyOperationFinish(durable.status, "failed"), true);
    assert.equal(shouldApplyOperationFinish(durable.status, "cancelled"), true);

    const legacy = classifyStopOperationResponse({ status: "cancelled" });
    assert.deepEqual(legacy, { mode: "cancelled", status: "cancelled" });
  });

  it("keeps the newest log lines when the window is full", () => {
    const logs = Array.from({ length: OPERATION_LOG_LIMIT }, (_, i) => ({ message: String(i) }));
    const next = appendOperationLog(logs, { message: "latest" });
    assert.equal(next.length, OPERATION_LOG_LIMIT);
    assert.equal(next[0].message, "1");
    assert.equal(next.at(-1).message, "latest");
  });

  it("does not reopen a cancelled operation from a late worker finish", () => {
    assert.equal(shouldApplyOperationFinish("cancelled", "completed"), false);
    assert.equal(shouldApplyOperationFinish("cancelled", "cancelled"), true);
    assert.equal(shouldApplyOperationFinish("running", "cancelled"), true);
  });
});
