import { FIELD_LIMITS } from "./fieldLimits.js";

/** Normalize plugin/host credential schema (PascalCase or camelCase) for connection forms. */
export function normalizeCredentialFields(schema) {
  const raw = schema?.fields || schema?.Fields || [];
  return raw
    .map((field) => {
      const type = field.type ?? field.Type ?? "text";
      const optionsRaw = field.options || field.Options || [];
      return {
        key: field.key || field.Key,
        displayName: field.displayName || field.DisplayName || field.key || field.Key,
        description: field.description || field.Description || "",
        isRequired: field.isRequired ?? field.IsRequired ?? true,
        isSecret:
          (field.isSecret ?? field.IsSecret ?? false) || type === "password",
        placeholder: field.placeholder || field.Placeholder || "",
        type,
        defaultValue: field.defaultValue ?? field.DefaultValue ?? "",
        options: optionsRaw
          .map((option) => ({
            value: option.value ?? option.Value,
            label: option.label ?? option.Label,
          }))
          .filter((option) => option.value != null && String(option.value) !== ""),
        maxLength:
          Number(field.maxLength || field.MaxLength) ||
          FIELD_LIMITS.credentialValue,
      };
    })
    .filter((field) => field.key);
}

export function defaultCredentialValues(fields) {
  const values = {};
  for (const field of fields || []) {
    const value = field.defaultValue;
    if (value != null && String(value) !== "") {
      values[field.key] = String(value);
    }
  }
  return values;
}
