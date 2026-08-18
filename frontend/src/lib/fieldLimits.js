/**
 * User-facing field caps. Keep in sync with backend `[StringLength]` /
 * `*_MAX_LENGTH` constants (Store, Product, Category, …).
 */
export const FIELD_LIMITS = {
  storeName: 40,
  timezone: 64,
  currencyCode: 3,
  connectionName: 100,
  adapterKey: 50,
  credentialValue: 512,
  productName: 100,
  productSku: 20,
  productBarcode: 13,
  productDescription: 5000,
  categoryName: 100,
  categoryDescription: 500,
  groupName: 200,
  groupDescription: 1000,
  attributeName: 100,
  attributeKey: 50,
  attributeDescription: 2000,
  attributeDefault: 1000,
  attributeGroup: 100,
  attributeGroupIcon: 80,
  attributeOptions: 5000,
  mappingName: 200,
  mappingFieldKey: 200,
  mappingFieldName: 500,
  folderName: 255,
  tagName: 50,
  noteText: 10000,
  apiKeyName: 100,
  /** Sign-up / sign-in policy (`authValidation`), stricter than DB. */
  userName: 45,
  /** `User.USER_NAME_MAX_LENGTH` — admin / profile edits. */
  userNameStored: 100,
  password: 128,
  aiModelName: 100,
  aiModelUrl: 500,
  aiModelId: 200,
  variantOption: 50,
  externalId: 100,
  externalSku: 100,
};

/**
 * @param {unknown} value
 * @param {number} max
 */
export function trimmedLength(value) {
  return String(value ?? "").trim().length;
}

/**
 * @param {unknown} value
 * @param {number} max
 */
export function isWithinLimit(value, max) {
  return trimmedLength(value) <= max;
}

/**
 * @param {unknown} value
 * @param {number} max
 */
export function isNonEmptyWithinLimit(value, max) {
  const n = trimmedLength(value);
  return n > 0 && n <= max;
}

/**
 * @param {unknown} value
 * @param {number} max
 * @param {{ required?: boolean }} [options]
 * @returns {null | "empty" | "tooLong"}
 */
export function fieldLimitIssue(value, max, { required = true } = {}) {
  const n = trimmedLength(value);
  if (required && n === 0) return "empty";
  if (n > max) return "tooLong";
  return null;
}

/**
 * Trim and clip to `max` characters (marketplace import / silent fit).
 * @param {unknown} value
 * @param {number} max
 */
export function clipToLimit(value, max) {
  const s = String(value ?? "").trim();
  return s.length <= max ? s : s.slice(0, max);
}
