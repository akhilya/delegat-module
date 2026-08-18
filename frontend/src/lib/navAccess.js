import { canAccessPath } from "./routeAccess.js";

/**
 * Whether a sidebar link should show for this user.
 * Query strings are ignored; gates match `canAccessPath`.
 * @param {object|null} me
 * @param {string|undefined} to
 */
export function canShowNavLink(me, to) {
  if (!to) return true;
  const path = String(to).split("?")[0];
  return canAccessPath(me, path);
}

/**
 * Drop duplicate destinations (same `to`, including query). Actions keep unique ids.
 * Query-distinct URLs are different places (Products vs Trash).
 * @param {Array<{ id?: string, to?: string, action?: string }>} items
 */
export function uniqueByNavTarget(items) {
  const seen = new Set();
  return items.filter((item) => {
    const key = item.to
      ? `to:${String(item.to)}`
      : `id:${item.action || item.id}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}
