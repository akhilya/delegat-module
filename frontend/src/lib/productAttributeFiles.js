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

export const ATTRIBUTE_FILE_INITIAL_RESULT_LIMIT = 50;
export const ATTRIBUTE_FILE_SEARCH_RESULT_LIMIT = 100;

export function attributeFileName(file) {
  return (
    [file?.fileName, file?.displayName, file?.name].find(
      (name) => typeof name === "string" && name.trim(),
    ) || `File ${file?.id ?? file?.Id ?? ""}`
  );
}

export function filterAttributeFiles(files, query = "") {
  const list = Array.isArray(files) ? files : [];
  const normalizedQuery = String(query).trim().toLowerCase();
  if (!normalizedQuery) return list;

  return list.filter((file) =>
    [attributeFileName(file), file?.contentType]
      .filter(Boolean)
      .some((value) => String(value).toLowerCase().includes(normalizedQuery)),
  );
}

/** Keep the picker DOM bounded while ensuring current selections remain visible. */
export function limitAttributeFileResults(
  files,
  selectedIds = [],
  searching = false,
) {
  const matches = Array.isArray(files) ? files : [];
  if (searching) return matches.slice(0, ATTRIBUTE_FILE_SEARCH_RESULT_LIMIT);

  const selected = new Set((selectedIds || []).map(String));
  const selectedFiles = matches.filter((file) =>
    selected.has(String(file?.id ?? file?.Id ?? "")),
  );
  const unselectedFiles = matches.filter(
    (file) => !selected.has(String(file?.id ?? file?.Id ?? "")),
  );
  const remaining = Math.max(
    0,
    ATTRIBUTE_FILE_INITIAL_RESULT_LIMIT - selectedFiles.length,
  );

  return [...selectedFiles, ...unselectedFiles.slice(0, remaining)];
}
