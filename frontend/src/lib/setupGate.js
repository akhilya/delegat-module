import { hasAnyPermission, PERMS } from "./permissions.js";

export const PAGE_SETUP = "/setup";

/**
 * Users who can create the first store should enter the first-run wizard
 * when the instance has zero stores.
 * @param {object|null|undefined} me
 */
export function canEnterSetupWizard(me) {
  if (!me) return false;
  return Boolean(me.isAdmin) || hasAnyPermission(me, [PERMS.StoresCreate]);
}

/**
 * @param {unknown} payload - GET /stores/ response body (or unwrapped value)
 * @returns {number|null}
 */
export function parseStoreCount(payload) {
  if (payload == null) return null;
  const data = payload?.value ?? payload;
  if (typeof data?.total === "number" && Number.isFinite(data.total)) {
    return data.total;
  }
  if (Array.isArray(data?.items)) return data.items.length;
  if (Array.isArray(data)) return data.length;
  return null;
}

/**
 * Pure navigation decision for first-run setup.
 * @param {{ path: string, me: object|null, storeCount: number|null }} args
 * @returns {{ redirect: string } | null}
 */
export function resolveSetupNavigation({ path, me, storeCount }) {
  const onSetup = path === PAGE_SETUP || path.startsWith(`${PAGE_SETUP}/`);
  const canSetup = canEnterSetupWizard(me);

  if (onSetup) {
    if (!canSetup) return { redirect: "/products" };
    if (storeCount != null && storeCount > 0) return { redirect: "/products" };
    return null;
  }

  if (!canSetup) return null;
  if (storeCount === 0) return { redirect: PAGE_SETUP };
  return null;
}
