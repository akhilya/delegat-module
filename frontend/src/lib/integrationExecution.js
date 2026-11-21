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
