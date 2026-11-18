const unknown = (unit = "records") => ({
  value: null,
  accuracy: "unknown",
  unit,
});
const unknownCounts = () => ({
  read: unknown(),
  matched: unknown(),
  written: unknown(),
});
const units = new Set(["records", "rows", "results", "products", "files"]);
const sources = new Set([
  "sql_readonly",
  "search_catalog",
  "platform_request",
  "import_generated_file",
]);
const nonNegativeInteger = (value) => Number.isSafeInteger(value) && value >= 0;
const toolName = (value) =>
  typeof value === "string" ? value.replace(/^functions\./i, "") : "";

function record(value) {
  try {
    const parsed = typeof value === "string" ? JSON.parse(value) : value;
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? parsed
      : null;
  } catch {
    return null;
  }
}

// Model-supplied plan counts are never execution evidence, including old plans.
export function planCountEstimate(plan) {
  const value = plan?.affectedCount;
  const accuracy = plan?.affectedCountAccuracy;
  if (
    !nonNegativeInteger(value) ||
    (accuracy !== undefined && accuracy !== "estimated") ||
    (accuracy === undefined && value === 0)
  )
    return { value: null, accuracy: "unknown" };
  return { value, accuracy: "estimated" };
}

function canonical(value) {
  if (Array.isArray(value)) return value.map(canonical);
  if (value && typeof value === "object")
    return Object.fromEntries(
      Object.keys(value)
        .sort()
        .map((key) => [key, canonical(value[key])]),
    );
  return value;
}

function metric(value) {
  if (!record(value) || !units.has(value.unit)) return null;
  if (value.accuracy === "unknown" && value.value === null)
    return unknown(value.unit);
  return value.accuracy === "exact" && nonNegativeInteger(value.value)
    ? { value: value.value, accuracy: "exact", unit: value.unit }
    : null;
}

// Call with only this proposal's execution tools, never proposal-time reads or
// the linked review's tools. Results stay per-step; overlapping IDs are not added.
export function stepCountEvidence(
  plan,
  tools,
  index,
  { confirmed = false } = {},
) {
  const none = unknownCounts();
  if (
    !confirmed ||
    typeof plan?.proposalId !== "string" ||
    !plan.proposalId.trim() ||
    !Number.isInteger(index) ||
    index < 0
  )
    return none;
  const step = plan?.steps?.[index];
  const call = tools?.[index];
  const name = toolName(step?.tool);
  const args = record(step?.args);
  const input = record(call?.input);
  if (
    !sources.has(name) ||
    toolName(call?.name) !== name ||
    call?.status !== "done" ||
    !args ||
    !input ||
    JSON.stringify(canonical(args)) !== JSON.stringify(canonical(input))
  )
    return none;
  // counts is a server event field, not a field parsed from the tool result.
  const counts = record(call.counts);
  if (counts?.version !== 1 || counts.source !== name) return none;
  const read = metric(counts.read);
  const matched = metric(counts.matched);
  const written = metric(counts.written);
  return read && matched && written ? { read, matched, written } : none;
}
