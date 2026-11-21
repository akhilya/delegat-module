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
});
