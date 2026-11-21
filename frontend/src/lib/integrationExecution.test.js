import assert from "node:assert/strict";
import { describe, it } from "node:test";
import en from "../locales/en.json" with { type: "json" };
import ru from "../locales/ru.json" with { type: "json" };
import {
  INTEGRATION_ISSUE_CODES,
  INTEGRATION_OPERATION_TYPE,
  buildIntegrationRequestHeaders,
  canStopIntegrationOperation,
  isIntegrationAcceptedResponse,
  normalizeIntegrationState,
  resolveIntegrationIssueKey,
} from "./integrationExecution.js";

describe("integrationExecution (P7)", () => {
  it("recognizes 202 durable integration responses and ignores plain 200 DTOs", () => {
    assert.equal(
      isIntegrationAcceptedResponse(202, {
        operationId: "op-123",
        commandId: "cmd-123",
        integrationState: "pending",
        statusUrl: "/api/operations/op-123/status",
      }),
      true,
    );
    assert.equal(isIntegrationAcceptedResponse(200, { success: true }), false);
    assert.equal(isIntegrationAcceptedResponse(202, { operationId: "" }), false);
  });

  it("normalizes pending/partial/unknown states without falsely marking unknown as applied", () => {
    assert.equal(normalizeIntegrationState("pending"), "pending");
    assert.equal(normalizeIntegrationState("PARTIAL"), "partial");
    assert.equal(normalizeIntegrationState("unknown"), "unknown");
    assert.equal(normalizeIntegrationState("unrecognized_state"), "unknown");
  });

  it("allows user Stop on active durable integration operations but not request-scoped sync", () => {
    assert.equal(canStopIntegrationOperation(INTEGRATION_OPERATION_TYPE, "running"), true);
    assert.equal(canStopIntegrationOperation(INTEGRATION_OPERATION_TYPE, "paused"), true);
    assert.equal(canStopIntegrationOperation(INTEGRATION_OPERATION_TYPE, "completed"), false);
    assert.equal(canStopIntegrationOperation("single-export", "running"), false);
  });

  it("preserves Idempotency-Key and Prefer: respond-async headers", () => {
    assert.deepEqual(
      buildIntegrationRequestHeaders({
        idempotencyKey: " req-42 ",
        preferAsync: true,
      }),
      {
        "Idempotency-Key": "req-42",
        Prefer: "respond-async",
      },
    );
    assert.deepEqual(buildIntegrationRequestHeaders({}), {});
  });

  it("provides EN and RU localized messages for every V1 integration issue code", () => {
    for (const code of INTEGRATION_ISSUE_CODES) {
      assert.equal(resolveIntegrationIssueKey(code), `errors.${code}`);
      assert.equal(typeof en.errors?.[code], "string", `missing EN translation for errors.${code}`);
      assert.ok(en.errors[code].trim().length > 0, `empty EN translation for errors.${code}`);
      assert.equal(typeof ru.errors?.[code], "string", `missing RU translation for errors.${code}`);
      assert.ok(ru.errors[code].trim().length > 0, `empty RU translation for errors.${code}`);
    }
    assert.equal(resolveIntegrationIssueKey("something_else"), "errors.default");
  });

  it("polls 202 shipment creation until integration.result.shipmentId is available and prevents duplicate creates on retry", async () => {
    const {
      createOrResumeShipmentCreation,
      normalizeIntegrationStatusPath,
    } = await import("./integrationExecution.js");

    assert.equal(
      normalizeIntegrationStatusPath("/api/operations/op-9/status", "op-9"),
      "/operations/op-9/status",
    );

    let postCalls = 0;
    let pollCalls = 0;
    const seenHeaders = [];

    // First attempt: POST returns 202, first poll is still running -> returns pending without discarding command identity.
    const first = await createOrResumeShipmentCreation({
      createIdempotencyKey: () => "idem-ship-1",
      maxPolls: 1,
      pollIntervalMs: 0,
      postShipment: async ({ headers }) => {
        postCalls++;
        seenHeaders.push(headers);
        return {
          status: 202,
          body: {
            operationId: "op-ship-1",
            commandId: "cmd-ship-1",
            integrationState: "pending",
            statusUrl: "/api/operations/op-ship-1/status",
          },
        };
      },
      getStatus: async (path) => {
        pollCalls++;
        assert.equal(path, "/operations/op-ship-1/status");
        return {
          status: "running",
          integration: { state: "executing", result: null },
        };
      },
    });

    assert.equal(first.status, "pending");
    assert.equal(postCalls, 1);
    assert.equal(pollCalls, 1);
    assert.equal(seenHeaders[0]["Idempotency-Key"], "idem-ship-1");
    assert.equal(first.pending.operationId, "op-ship-1");

    // Second attempt (user retries Pack): resumes polling existingPending without issuing a second POST!
    const resumed = await createOrResumeShipmentCreation({
      existingPending: first.pending,
      maxPolls: 2,
      pollIntervalMs: 0,
      postShipment: async () => {
        postCalls++;
        throw new Error("must not issue duplicate POST /shipments while existing creation is pending");
      },
      getStatus: async () => {
        pollCalls++;
        if (pollCalls === 2) {
          return { status: "running", integration: { state: "executing", result: null } };
        }
        return {
          status: "completed",
          integration: {
            state: "completed",
            result: { shipmentId: "WB-GI-4242" },
          },
        };
      },
    });

    assert.equal(resumed.status, "completed");
    assert.equal(resumed.shipmentId, "WB-GI-4242");
    assert.equal(resumed.pending, null);
    assert.equal(postCalls, 1);
    assert.equal(pollCalls, 3);
  });

  it("retains pending identity on unknown outcome and clears it on terminal pre-effect failure", async () => {
    const { createOrResumeShipmentCreation } = await import("./integrationExecution.js");

    const unknownRes = await createOrResumeShipmentCreation({
      existingPending: {
        operationId: "op-unk",
        statusPath: "/operations/op-unk/status",
        idempotencyKey: "idem-unk",
      },
      maxPolls: 1,
      pollIntervalMs: 0,
      postShipment: async () => {
        throw new Error("should not post");
      },
      getStatus: async () => ({
        status: "failed",
        summary: "Outcome unknown",
        integration: { state: "unknown", summary: "Outcome unknown", result: null },
      }),
    });
    assert.equal(unknownRes.status, "unknown");
    assert.equal(unknownRes.pending?.operationId, "op-unk");

    const failedRes = await createOrResumeShipmentCreation({
      existingPending: {
        operationId: "op-fail",
        statusPath: "/operations/op-fail/status",
        idempotencyKey: "idem-fail",
      },
      maxPolls: 1,
      pollIntervalMs: 0,
      postShipment: async () => {
        throw new Error("should not post");
      },
      getStatus: async () => ({
        status: "failed",
        summary: "Rejected before effect",
        integration: { state: "failed", summary: "Rejected before effect", result: null },
      }),
    });
    assert.equal(failedRes.status, "failed");
    assert.equal(failedRes.pending, null);
  });

  it("reuses the exact same Idempotency-Key and shipmentName when retrying after a network error during POST", async () => {
    const { createOrResumeShipmentCreation } = await import("./integrationExecution.js");

    let savedPending = null;
    const attempts = [];
    let counter = 0;

    await assert.rejects(
      () =>
        createOrResumeShipmentCreation({
          existingPending: savedPending,
          onPendingUpdate: (p) => {
            savedPending = p;
          },
          createIdempotencyKey: () => `key-${++counter}`,
          createShipmentName: () => `Supply ${counter}`,
          postShipment: async ({ shipmentName, headers }) => {
            attempts.push({ shipmentName, key: headers["Idempotency-Key"] });
            throw new Error("Network Error");
          },
          getStatus: async () => {
            throw new Error("should not poll");
          },
        }),
      /Network Error/,
    );

    assert.equal(savedPending?.idempotencyKey, "key-1");
    assert.equal(savedPending?.shipmentName, "Supply 1");

    const retryRes = await createOrResumeShipmentCreation({
      existingPending: savedPending,
      onPendingUpdate: (p) => {
        savedPending = p;
      },
      createIdempotencyKey: () => `key-${++counter}`,
      createShipmentName: () => `Supply ${counter}`,
      postShipment: async ({ shipmentName, headers }) => {
        attempts.push({ shipmentName, key: headers["Idempotency-Key"] });
        return {
          status: 200,
          body: { success: true, shipment: { externalId: "WB-GI-100" } },
        };
      },
      getStatus: async () => {
        throw new Error("should not poll");
      },
    });

    assert.equal(retryRes.status, "completed");
    assert.equal(retryRes.shipmentId, "WB-GI-100");
    assert.equal(savedPending, null);
    assert.deepEqual(attempts, [
      { shipmentName: "Supply 1", key: "key-1" },
      { shipmentName: "Supply 1", key: "key-1" },
    ]);
  });

  it("packs the captured original order A even if the user opens order B while shipment creation polls", async () => {
    const { executeOrderPackFlow } = await import("./integrationExecution.js");

    let activeOrderId = "ORDER-A";
    let releaseSecondPoll;
    const secondPollGate = new Promise((resolve) => {
      releaseSecondPoll = resolve;
    });
    let polls = 0;
    const packedCalls = [];

    const flowPromise = executeOrderPackFlow({
      targetConnectionId: 10,
      targetOrderId: activeOrderId,
      supplyChoice: "new",
      boxChoice: "new",
      maxPolls: 3,
      pollIntervalMs: 0,
      postShipment: async (connId) => {
        assert.equal(connId, 10);
        return {
          status: 202,
          body: {
            operationId: "op-order-a",
            commandId: "cmd-order-a",
            integrationState: "pending",
            statusUrl: "/api/operations/op-order-a/status",
          },
        };
      },
      getStatus: async () => {
        polls++;
        if (polls === 1) {
          return { status: "running", integration: { state: "executing", result: null } };
        }
        await secondPollGate;
        return {
          status: "completed",
          integration: { state: "completed", result: { shipmentId: "WB-GI-A1" } },
        };
      },
      postOrderStatus: async ({ connectionId, orderId, action, extra }) => {
        packedCalls.push({ connectionId, orderId, action, extra });
        return { status: 202, body: { operationId: "op-pack-a", integrationState: "pending" } };
      },
    });

    // User switches the detail sheet to ORDER-B before the poll resolves.
    activeOrderId = "ORDER-B";
    releaseSecondPoll();

    const result = await flowPromise;
    assert.equal(result.status, "packed");
    assert.equal(result.targetOrderId, "ORDER-A");
    assert.equal(activeOrderId, "ORDER-B");
    assert.deepEqual(packedCalls, [
      {
        connectionId: 10,
        orderId: "ORDER-A",
        action: "pack",
        extra: { supplyId: "WB-GI-A1", createBox: "true" },
      },
    ]);
  });
});
