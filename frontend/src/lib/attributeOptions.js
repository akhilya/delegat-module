/**
 * Shared parsing and label resolution for select/multiselect attribute options.
 */

function normalizeOptionEntry(option) {
  if (option == null || typeof option !== "object") {
    const text = String(option);
    return { value: text, label: text };
  }

  const value = option.value ?? option.Value ?? option.label ?? option.Label;
  const label = option.label ?? option.Label ?? option.value ?? option.Value;

  return {
    value: String(value ?? ""),
    label: String(label ?? value ?? ""),
  };
}

/** Parse attribute options JSON (or comma-separated fallback) into { value, label }[]. */
export function parseAttributeOptions(optionsJson) {
  if (!optionsJson) return [];

  try {
    const parsed = JSON.parse(optionsJson);
    if (Array.isArray(parsed)) {
      return parsed.map(normalizeOptionEntry);
    }
  } catch {
    // Not JSON — fall through to comma-separated parsing.
  }

  return String(optionsJson)
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean)
    .map((label) => ({ value: label, label }));
}

/** Resolve a stored option value to its display label. */
export function resolveAttributeOptionLabel(optionsJson, storedValue) {
  if (storedValue === null || storedValue === undefined || storedValue === "") {
    return "";
  }

  const normalized = String(storedValue);
  const match = parseAttributeOptions(optionsJson).find(
    (option) =>
      String(option.value) === normalized || option.label === storedValue,
  );

  return match?.label ?? storedValue;
}

/** Map stored values to labels for multiselect display. */
export function resolveAttributeOptionLabels(optionsJson, storedValues) {
  return storedValues.map((value) =>
    resolveAttributeOptionLabel(optionsJson, value),
  );
}

/** Map display labels back to stored values for multiselect persistence. */
export function labelsToStoredValues(optionsJson, labels) {
  const options = parseAttributeOptions(optionsJson);
  return labels.map((label) => {
    const option = options.find((item) => item.label === label);
    return option ? option.value : label;
  });
}
