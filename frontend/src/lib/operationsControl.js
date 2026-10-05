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

export function canStopOperation(type, status) {
  return isOperationActive(status) && !REQUEST_SCOPED_SYNC_TYPES.has(type);
}

/** Cancelled is sticky so a late worker finish cannot reopen the row. */
export function shouldApplyOperationFinish(currentStatus, incomingStatus) {
  if (currentStatus === "cancelled" && incomingStatus !== "cancelled") return false;
  return true;
}
