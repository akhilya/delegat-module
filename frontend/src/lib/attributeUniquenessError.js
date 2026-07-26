import { resolveApiErrorMessage } from "@/store/sonnerHelper";

/**
 * Maps `attribute_value_not_unique` API errors onto vee-validate attribute fields.
 * @returns {boolean} true when the error was applied to a field
 */
export function applyAttributeUniquenessError(form, error) {
  const payload = error?.response?.data;
  if (!payload || payload.code !== "attribute_value_not_unique") return false;

  const data = payload.data || {};
  const message = resolveApiErrorMessage(payload);

  if (data.attributeId != null) {
    form.setFieldError(`categoryAttributes.${data.attributeId}`, message);
    return true;
  }
  if (data.storeAttributeId != null) {
    form.setFieldError(`storeAttributes.${data.storeAttributeId}`, message);
    return true;
  }
  return false;
}
