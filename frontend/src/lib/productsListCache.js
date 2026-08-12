/**
 * Short in-memory TTL cache for products / trash list pages.
 * Survives productsPage remount when navigating product ↔ table.
 */

export const PRODUCTS_LIST_CACHE_TTL_MS = 10_000;

export const PRODUCTS_LIST_NS = "list";
export const PRODUCTS_TRASH_NS = "trash";

/** @type {Map<string, { at: number, payload: object }>} */
const entries = new Map();

function entryKey(ns, key) {
  return `${ns}::${key}`;
}

/**
 * @param {object} q
 * @param {string|number|null|undefined} q.storeId
 * @param {number} q.startIndex
 * @param {number} q.pageSize
 * @param {string} [q.searchQuery]
 * @param {number|string|null} [q.categoryId]
 * @param {string|null} [q.sort]
 */
export function buildProductsListCacheKey({
  storeId,
  startIndex,
  pageSize,
  searchQuery = "",
  categoryId = "",
  sort = "",
}) {
  return [
    storeId ?? "",
    startIndex ?? 0,
    pageSize ?? 25,
    searchQuery ?? "",
    categoryId ?? "",
    sort ?? "",
  ].join("|");
}

/**
 * @param {object} q
 * @param {string|number|null|undefined} q.storeId
 * @param {number} q.startIndex
 * @param {number} q.pageSize
 * @param {string} [q.searchQuery]
 */
export function buildProductsTrashCacheKey({
  storeId,
  startIndex,
  pageSize,
  searchQuery = "",
}) {
  return [
    storeId ?? "",
    startIndex ?? 0,
    pageSize ?? 25,
    searchQuery ?? "",
  ].join("|");
}

/**
 * @param {string} ns
 * @param {string} key
 * @returns {{ items: any[], totalCount: number, lastPage: boolean }|null}
 */
export function getProductsListCache(ns, key) {
  const entry = entries.get(entryKey(ns, key));
  if (!entry) return null;
  if (Date.now() - entry.at > PRODUCTS_LIST_CACHE_TTL_MS) {
    entries.delete(entryKey(ns, key));
    return null;
  }
  return entry.payload;
}

/**
 * @param {string} ns
 * @param {string} key
 * @param {{ items: any[], totalCount: number, lastPage: boolean }} payload
 */
export function setProductsListCache(ns, key, payload) {
  entries.set(entryKey(ns, key), {
    at: Date.now(),
    payload: {
      items: payload.items,
      totalCount: payload.totalCount,
      lastPage: payload.lastPage,
    },
  });
}

/** Drop all list/trash pages (call after catalog mutations). */
export function invalidateProductsListCache() {
  entries.clear();
}
