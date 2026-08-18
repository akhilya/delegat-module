/**
 * Mapping labels stored when Catalog Connect (or category import) did not
 * receive a marketplace title: "Category 17028973:115944384" or the raw id.
 */

/**
 * @param {unknown} label
 * @param {unknown} externalCategoryId
 */
export function isSyntheticMarketplaceCategoryLabel(label, externalCategoryId) {
  if (label == null) return true;
  const text = String(label).trim();
  if (!text) return true;
  const id =
    externalCategoryId == null ? "" : String(externalCategoryId).trim();
  if (id && text === id) return true;
  if (id && text.toLowerCase() === `category ${id}`.toLowerCase()) return true;
  return false;
}

/**
 * @param {object|null|undefined} mapping
 * @param {string} [fallback]
 */
export function mappingDisplayName(mapping, fallback = "") {
  if (!mapping) return fallback;
  const id = mapping.externalCategoryId;
  const candidates = [
    mapping.name,
    mapping.externalCategoryName,
    mapping.localCategoryName,
  ];
  for (const candidate of candidates) {
    if (
      candidate &&
      !isSyntheticMarketplaceCategoryLabel(candidate, id)
    ) {
      return String(candidate).trim();
    }
  }
  return fallback;
}
