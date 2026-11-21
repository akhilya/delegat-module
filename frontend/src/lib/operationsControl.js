/** Request-scoped jobs that cannot abort the in-flight marketplace HTTP call. */
export const REQUEST_SCOPED_SYNC_TYPES = new Set([
  "single-export",
  "single-import",
  "price-export",
  "price-import",
  "stock-export",
  "stock-import",
  "excel-import",
  "tool",
]);

export const OPERATION_TERMINAL_STATUSES = new Set([
  "completed",
  "failed",
  "partial",
  "cancelled",
]);

/** Live and loaded operation logs keep the newest lines. */
export const OPERATION_LOG_LIMIT = 500;

export function appendOperationLog(logs, log, limit = OPERATION_LOG_LIMIT) {
  const next = [...(logs || []), log];
  if (next.length <= limit) return next;
  return next.slice(next.length - limit);
}

export function isOperationActive(status) {
  return status === "running" || status === "paused";
}

export function canStopOperation(type, status, stopRequested = false) {
  return isOperationActive(status) && !stopRequested && !REQUEST_SCOPED_SYNC_TYPES.has(type);
}

/**
 * Classifies `POST /operations/{id}/stop` responses. Coordinator-owned durable
 * integration operations return `{ stopRequested: true, status }` and stay active
 * until the worker finishes reconciling in-flight stages and broadcasts the
 * authoritative terminal state (`cancelled`, `partial`, or `failed`).
 */
export function classifyStopOperationResponse(payload) {
  const body = payload?.value ?? payload;
  if (body && body.stopRequested === true) {
    return {
      mode: "stop_requested",
      status: typeof body.status === "string" && body.status ? body.status : "running",
    };
  }
  return {
    mode: "cancelled",
    status: typeof body?.status === "string" && body.status ? body.status : "cancelled",
  };
}

/** Cancelled is sticky so a late worker finish cannot reopen the row. */
export function shouldApplyOperationFinish(currentStatus, incomingStatus) {
  if (currentStatus === "cancelled" && incomingStatus !== "cancelled") return false;
  return true;
}
