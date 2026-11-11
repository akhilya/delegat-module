/** Parses the JSON-array bridge used by File attribute fields. Negative IDs are request-local create keys. */
export function parseAttributeFileIds(raw) {
  if (raw === "" || raw == null) return [];

  let parsed;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return null;
  }

  if (!Array.isArray(parsed)) return null;
  if (!parsed.every((id) => Number.isSafeInteger(id) && id !== 0)) {
    return null;
  }
  if (new Set(parsed).size !== parsed.length) return null;
  return parsed;
}
