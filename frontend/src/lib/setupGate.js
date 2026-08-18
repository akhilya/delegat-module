import { hasAnyPermission, PERMS } from "./permissions.js";

export const PAGE_SETUP = "/setup";

/**
 * Users who can create the first store should enter the first-run wizard
 * when the instance has zero stores, or the first store is still onboarding.
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

function storeItems(payload) {
  if (payload == null) return null;
  const data = payload?.value ?? payload;
  if (Array.isArray(data?.items)) return data.items;
  if (Array.isArray(data)) return data;
  return null;
}

function isOnboardingComplete(store) {
  const value = store?.onboardingCompletedAt ?? store?.OnboardingCompletedAt;
  return value != null && value !== "";
}

/**
 * @param {unknown} payload
 * @returns {boolean|null}
 */
export function parseOnboardingIncomplete(payload) {
  const items = storeItems(payload);
  if (!items) return null;
  return items.some((store) => !isOnboardingComplete(store));
}

/**
 * @param {unknown} payload
 * @returns {{ id: number, name: string } | null}
 */
export function parseIncompleteStore(payload) {
  const items = storeItems(payload);
  if (!items) return null;
  const store = items.find((item) => !isOnboardingComplete(item));
  if (!store) return null;
  const id = store.id ?? store.Id;
  if (id == null) return null;
  return { id, name: store.name ?? store.Name ?? "" };
}

/**
 * Pure navigation decision for first-run setup.
 * @param {{ path: string, me: object|null, storeCount: number|null, onboardingIncomplete?: boolean|null }} args
 * @returns {{ redirect: string } | null}
 */
export function resolveSetupNavigation({
  path,
  me,
  storeCount,
  onboardingIncomplete = false,
}) {
  const onSetup = path === PAGE_SETUP || path.startsWith(`${PAGE_SETUP}/`);
  const canSetup = canEnterSetupWizard(me);
  const incomplete = onboardingIncomplete === true;

  if (onSetup) {
    if (!canSetup) return { redirect: "/products" };
    if (storeCount != null && storeCount > 0 && !incomplete) {
      return { redirect: "/products" };
    }
    return null;
  }

  if (!canSetup) return null;
  if (storeCount === 0) return { redirect: PAGE_SETUP };
  if (incomplete) return { redirect: PAGE_SETUP };
  return null;
}
