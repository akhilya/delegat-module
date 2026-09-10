/** Ghost target on the adapter sink: dropping a wire here creates a mapping-local field. */
export const ADD_ADAPTER_FIELD_HANDLE = "f:__add__";

/**
 * Allocate a mapping-local adapter field (store-engine sinks are open-ended).
 * Keys stay unique among existing fields and the reserved add-handle.
 */
export function nextCustomAdapterField(fields, opts = {}) {
  const used = new Set(
    (fields || [])
      .map((f) => String(f.fieldKey || "").toLowerCase())
      .filter(Boolean),
  );
  used.add("__add__");
  const requested = String(opts.fieldKey || "").trim();
  let key = requested || "custom";
  if (requested) {
    let n = 2;
    while (used.has(key.toLowerCase())) {
      key = `${requested}_${n}`;
      n += 1;
    }
  } else {
    let n = 1;
    while (used.has(key.toLowerCase())) {
      n += 1;
      key = `custom_${n}`;
    }
  }
  const label = String(opts.label || "").trim() || key;
  return {
    fieldKey: key,
    label,
    outputType: opts.outputType || "string",
    isRequired: false,
    isCustom: true,
  };
}

export function appendCustomAdapterField(fields, opts = {}) {
  const list = [...(fields || [])];
  const created = nextCustomAdapterField(list, opts);
  list.push(created);
  return { fields: list, created };
}
