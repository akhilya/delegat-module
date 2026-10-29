import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  operationIdsFromToolCalls,
  summarizePlanOperations,
} from "./planOperations.js";

describe("plan background operations", () => {
  it("extracts unique operation ids from structured or serialized results", () => {
    assert.deepEqual(
      operationIdsFromToolCalls([
        { result: { operationId: "op-1", status: "pending" } },
        { result: JSON.stringify({ data: { operationId: "op-2" } }) },
        { result: { operationId: "op-1" } },
        { result: "not-json" },
      ]),
      ["op-1", "op-2"],
    );
  });

  it("keeps a queued operation active until a terminal status is observed", () => {
    const ids = ["a", "b"];
    assert.equal(
      summarizePlanOperations(ids, {
        a: { status: "completed" },
        b: { status: "running" },
      }).active,
      true,
    );
    assert.equal(
      summarizePlanOperations(ids, {
        a: { status: "completed" },
        b: { status: "completed" },
      }).complete,
      true,
    );
  });

  it("surfaces terminal background failures", () => {
    const result = summarizePlanOperations(["a"], { a: { status: "failed" } });
    assert.equal(result.complete, true);
    assert.equal(result.failed, true);
  });
});
