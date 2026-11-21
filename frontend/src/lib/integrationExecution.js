import { canStopOperation } from "./operationsControl.js";

export const INTEGRATION_OPERATION_TYPE = "integration";

export const INTEGRATION_STATES = new Set([
  "pending",
  "partial",
  "unknown",
  "applied",
  "rejected",
  "cancelled",
]);

export const INTEGRATION_ISSUE_CODES = [
  "integration_request_conflict",
  "invalid_connection_binding",
  "promotion_update_empty",
  "promotion_conflicting_product",
  "adapter_execution_unsupported",
  "integration_empty_plan",
  "unverified_provider_route",
  "integration_execution_disabled",
];

/**
 * Returns true when an API response is a V1 durable integration 202 acknowledgement.
 */
export function isIntegrationAcceptedResponse(status, body) {
  return (
    status === 202 &&
    Boolean(body && typeof body === "object") &&
    typeof body.operationId === "string" &&
    body.operationId.trim().length > 0 &&
    typeof body.integrationState === "string" &&
    body.integrationState.trim().length > 0
  );
}

/**
 * Normalizes integration state strings for UI rendering without conflating
 * unknown/partial effects with confirmed completion.
 */
export function normalizeIntegrationState(state) {
  const normalized = String(state || "").trim().toLowerCase();
  if (INTEGRATION_STATES.has(normalized)) return normalized;
  return "unknown";
}

/**
 * Durable V1 integration operations can always be stopped while active;
 * caller disconnect does not cancel them, explicit user Stop does.
 */
export function canStopIntegrationOperation(type, status) {
  return canStopOperation(type, status);
}

/**
 * Builds request headers that preserve Idempotency-Key across retries and
 * opt into asynchronous 202 admission when requested.
 */
export function buildIntegrationRequestHeaders({
  idempotencyKey = "",
  preferAsync = false,
} = {}) {
  /** @type {Record<string, string>} */
  const headers = {};
  const key = String(idempotencyKey || "").trim();
  if (key) headers["Idempotency-Key"] = key;
  if (preferAsync) headers["Prefer"] = "respond-async";
  return headers;
}

/**
 * Maps a backend integration issue code to its i18n key under errors.*.
 */
export function resolveIntegrationIssueKey(code) {
  const normalized = String(code || "").trim();
  if (INTEGRATION_ISSUE_CODES.includes(normalized)) {
    return `errors.${normalized}`;
  }
  return "errors.default";
}

/**
 * Converts a backend statusUrl (e.g. "/api/operations/op-1/status") into a path
 * relative to the frontend axios `http` client (whose baseURL already ends with "/api").
 */
export function normalizeIntegrationStatusPath(statusUrl, operationId = "") {
  const raw = String(statusUrl || "").trim();
  if (raw) {
    if (raw.startsWith("/api/")) return raw.slice(4);
    if (raw.startsWith("/")) return raw;
  }
  const op = String(operationId || "").trim();
  return op ? `/operations/${encodeURIComponent(op)}/status` : "";
}

/**
 * Extracts a created shipment external ID from either a synchronous ShipmentMutationResult
 * payload or an operation status response's `integration.result`.
 */
export function extractShipmentIdFromPayload(payload) {
  if (!payload || typeof payload !== "object") return "";
  const direct =
    payload.shipmentId ??
    payload.ShipmentId ??
    payload.shipment?.externalId ??
    payload.shipment?.ExternalId ??
    payload.Shipment?.externalId ??
    payload.Shipment?.ExternalId;
  if (direct != null && String(direct).trim()) return String(direct).trim();

  const integrationResult = payload.integration?.result;
  if (integrationResult && typeof integrationResult === "object") {
    const nested =
      integrationResult.shipmentId ??
      integrationResult.ShipmentId ??
      integrationResult.shipment?.externalId ??
      integrationResult.shipment?.ExternalId;
    if (nested != null && String(nested).trim()) return String(nested).trim();
  }
  return "";
}

/**
 * Creates a marketplace shipment or resumes polling an already-admitted 202 shipment
 * creation command without issuing duplicate POST /shipments calls.
 *
 * Returns:
 * - `{ status: "completed", shipmentId, pending: null }` when confirmed
 * - `{ status: "pending", pending }` when still executing after poll budget expires
 * - `{ status: "unknown", pending, message }` when outcome is unknown (retains pending identity to prevent duplicate creates)
 * - `{ status: "failed", pending: null, message }` when terminal pre-effect failure/cancellation occurs
 */
export async function createOrResumeShipmentCreation({
  postShipment,
  getStatus,
  existingPending = null,
  onPendingUpdate = () => {},
  createIdempotencyKey = () => `ship-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
  createShipmentName = () => `Delegat ${new Date().toISOString().slice(0, 16).replace("T", " ")}`,
  maxPolls = 20,
  pollIntervalMs = 500,
  sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
}) {
  let pending = existingPending && existingPending.operationId
    ? { ...existingPending }
    : null;

  if (!pending) {
    const idempotencyKey =
      existingPending?.idempotencyKey || createIdempotencyKey();
    const shipmentName =
      existingPending?.shipmentName || createShipmentName();
    // Retain idempotencyKey and shipmentName before sending so a network timeout
    // reuses the exact same key and payload on retry (avoiding duplicate creates or 409 conflicts).
    onPendingUpdate({
      operationId: null,
      commandId: null,
      statusPath: "",
      idempotencyKey,
      shipmentName,
    });
    let response;
    try {
      response = await postShipment({
        shipmentName,
        headers: buildIntegrationRequestHeaders({ idempotencyKey }),
      });
    } catch (error) {
      const httpStatus = error?.response?.status;
      if (httpStatus && httpStatus >= 400 && httpStatus < 500 && httpStatus !== 408 && httpStatus !== 409 && httpStatus !== 429) {
        onPendingUpdate(null);
      }
      throw error;
    }
    const status = response?.status ?? 200;
    const body = response?.body ?? response?.data ?? null;

    if (!isIntegrationAcceptedResponse(status, body)) {
      const shipmentId = extractShipmentIdFromPayload(body);
      if (shipmentId) {
        onPendingUpdate(null);
        return { status: "completed", shipmentId, pending: null };
      }
      onPendingUpdate(null);
      return {
        status: "failed",
        pending: null,
        message: body?.errorMessage || body?.ErrorMessage || body?.message || "",
      };
    }

    pending = {
      operationId: body.operationId,
      commandId: body.commandId || null,
      statusPath: normalizeIntegrationStatusPath(body.statusUrl, body.operationId),
      idempotencyKey,
      shipmentName,
    };
    onPendingUpdate(pending);
  }

  const polls = Math.max(1, maxPolls);
  for (let i = 0; i < polls; i++) {
    if (i > 0 && pollIntervalMs > 0) {
      await sleep(pollIntervalMs);
    }

    const statusPayload = await getStatus(pending.statusPath);
    const integration = statusPayload?.integration || null;
    const integrationState = String(integration?.state || "").trim().toLowerCase();
    const opStatus = String(statusPayload?.status || "").trim().toLowerCase();
    const shipmentId = extractShipmentIdFromPayload(statusPayload);

    if (
      (integrationState === "completed" || integrationState === "partial" || opStatus === "completed") &&
      shipmentId
    ) {
      onPendingUpdate(null);
      return { status: "completed", shipmentId, pending: null };
    }

    if (integrationState === "unknown") {
      onPendingUpdate(pending);
      return {
        status: "unknown",
        pending,
        message: integration?.summary || statusPayload?.summary || "",
      };
    }

    if (
      integrationState === "failed" ||
      integrationState === "cancelled" ||
      opStatus === "failed" ||
      opStatus === "cancelled" ||
      (integrationState === "completed" && !shipmentId)
    ) {
      onPendingUpdate(null);
      return {
        status: "failed",
        pending: null,
        message: integration?.summary || statusPayload?.summary || "",
      };
    }
  }

  onPendingUpdate(pending);
  return { status: "pending", pending };
}
