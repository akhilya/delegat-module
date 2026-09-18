function pick(raw, camel, pascal, fallback = "") {
  if (!raw || typeof raw !== "object") return fallback;
  const value = raw[camel] ?? raw[pascal];
  return value == null ? fallback : value;
}

export function normalizeOrderActionField(raw) {
  if (!raw || typeof raw !== "object") return null;
  const key = String(pick(raw, "key", "Key", "")).trim();
  if (!key) return null;
  const options = (raw.options ?? raw.Options ?? [])
    .map((option) => ({
      value: String(pick(option, "value", "Value", "")),
      label: String(pick(option, "label", "Label", "")),
    }))
    .filter((option) => option.value);
  return {
    key,
    name: String(pick(raw, "name", "Name", key)),
    type: String(pick(raw, "type", "Type", "text")).toLowerCase(),
    required: Boolean(raw.required ?? raw.Required),
    description: String(pick(raw, "description", "Description", "")),
    placeholder: String(pick(raw, "placeholder", "Placeholder", "")),
    defaultValue: String(pick(raw, "defaultValue", "DefaultValue", "")),
    target: String(pick(raw, "target", "Target", "extra") || "extra"),
    visibleWhenKey: String(pick(raw, "visibleWhenKey", "VisibleWhenKey", "")),
    visibleWhenValue: String(pick(raw, "visibleWhenValue", "VisibleWhenValue", "")),
    requiredWhenKey: String(pick(raw, "requiredWhenKey", "RequiredWhenKey", "")),
    requiredWhenValue: String(pick(raw, "requiredWhenValue", "RequiredWhenValue", "")),
    options,
  };
}

export function normalizeOrderActionFields(fields) {
  if (!Array.isArray(fields)) return [];
  return fields.map(normalizeOrderActionField).filter(Boolean);
}

export function adapterOrderActionFields(adapter, action) {
  const raw = adapter?.orderActionFields ?? adapter?.OrderActionFields ?? {};
  if (!raw || typeof raw !== "object") return [];
  const key = String(action || "");
  const list =
    raw[key] ??
    raw[key.toLowerCase()] ??
    raw[key.charAt(0).toUpperCase() + key.slice(1)] ??
    [];
  return normalizeOrderActionFields(list);
}

function valueOf(values, key) {
  if (!key) return "";
  const value = values?.[key];
  if (value === true) return "true";
  if (value === false) return "false";
  return String(value ?? "").trim();
}

export function orderActionFieldIsVisible(field, values) {
  if (!field?.visibleWhenKey) return true;
  return valueOf(values, field.visibleWhenKey) === String(field.visibleWhenValue ?? "");
}

export function orderActionFieldIsRequired(field, values) {
  if (!orderActionFieldIsVisible(field, values)) return false;
  if (field?.required) return true;
  if (!field?.requiredWhenKey) return false;
  return valueOf(values, field.requiredWhenKey) === String(field.requiredWhenValue ?? "");
}

export function defaultOrderActionFieldValues(fields) {
  const values = {};
  for (const field of normalizeOrderActionFields(fields)) {
    if (field.type === "checkbox") {
      const raw = field.defaultValue.toLowerCase();
      values[field.key] = raw === "true" || raw === "1" || raw === "yes";
      continue;
    }
    values[field.key] = field.defaultValue || field.options[0]?.value || "";
  }
  return values;
}

export function orderActionFieldsValid(fields, values) {
  for (const field of normalizeOrderActionFields(fields)) {
    if (field.type === "checkbox") continue;
    if (!orderActionFieldIsRequired(field, values)) continue;
    if (!valueOf(values, field.key)) return false;
  }
  return true;
}

export function applyOrderActionFieldValues(fields, values) {
  const payload = {};
  const extra = {};
  for (const field of normalizeOrderActionFields(fields)) {
    if (!orderActionFieldIsVisible(field, values)) continue;
    let next = valueOf(values, field.key);
    if (field.type === "checkbox") {
      next = values?.[field.key] === true || next === "true" ? "true" : "false";
    } else if (!next) {
      continue;
    }
    const target = String(field.target || "extra");
    if (target === "reason") payload.reason = next;
    else if (target === "trackingNumber") payload.trackingNumber = next;
    else if (target === "deliveryService") payload.deliveryService = next;
    else extra[field.key] = next;
  }
  if (Object.keys(extra).length) payload.extra = extra;
  return payload;
}

export function mergeOrderStatusPayload(base, update) {
  const extra = {
    ...(base?.extra && typeof base.extra === "object" ? base.extra : {}),
    ...(update?.extra && typeof update.extra === "object" ? update.extra : {}),
  };
  const payload = { ...(base || {}), ...(update || {}) };
  if (Object.keys(extra).length) payload.extra = extra;
  else delete payload.extra;
  return payload;
}
