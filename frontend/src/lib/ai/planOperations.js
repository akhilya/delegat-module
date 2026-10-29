export const OPERATION_TERMINAL_STATUSES = new Set([
  "completed",
  "failed",
  "partial",
  "cancelled",
]);

function parseResult(value) {
  if (typeof value !== "string") return value;
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}

export function operationIdsFromToolCalls(toolCalls) {
  const ids = new Set();
  const visit = (value, depth = 0) => {
    if (!value || typeof value !== "object" || depth > 12) return;
    if (!Array.isArray(value)) {
      const id = value.operationId;
      if (typeof id === "string" && id.trim()) ids.add(id.trim());
    }
    for (const child of Array.isArray(value) ? value : Object.values(value)) {
      visit(child, depth + 1);
    }
  };

  for (const tool of toolCalls || []) visit(parseResult(tool?.result));
  return [...ids];
}

export function summarizePlanOperations(operationIds, statuses) {
  const ids = [...new Set(operationIds || [])];
  if (!ids.length) {
    return {
      tracking: false,
      active: false,
      complete: false,
      failed: false,
      progress: null,
    };
  }

  const rows = ids.map((id) => statuses?.[id]).filter(Boolean);
  const active = rows.some((row) =>
    ["pending", "running", "paused", "queued"].includes(
      String(row.status || "").toLowerCase(),
    ),
  );
  const allKnown = rows.length === ids.length;
  const complete =
    allKnown &&
    rows.every(
      (row) =>
        OPERATION_TERMINAL_STATUSES.has(
          String(row.status || "").toLowerCase(),
        ) || row.status === "missing",
    );
  const failed =
    complete &&
    rows.some((row) =>
      ["failed", "partial", "cancelled"].includes(
        String(row.status || "").toLowerCase(),
      ),
    );
  const progressRows = rows.filter(
    (row) => Number.isFinite(row.totalItems) && row.totalItems > 0,
  );
  const progress = progressRows.length
    ? {
        total: progressRows.reduce((sum, row) => sum + row.totalItems, 0),
        completed: progressRows.reduce(
          (sum, row) => sum + (row.completedItems || 0),
          0,
        ),
        failed: progressRows.reduce(
          (sum, row) => sum + (row.failedItems || 0),
          0,
        ),
      }
    : null;

  return {
    tracking: !complete,
    active: active || !allKnown,
    complete,
    failed,
    progress,
  };
}
