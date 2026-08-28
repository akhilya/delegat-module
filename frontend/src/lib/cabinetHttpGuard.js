import { isCabinetViewOnly } from "./permissions.js";

const SAFE_METHODS = new Set(["get", "head", "options"]);

/**
 * Mutations a cabinet-view-only account may still send.
 * @param {string} [url]
 * @param {string} [method]
 */
export function isAllowedCabinetViewOnlyRequest(url = "", method = "get") {
  const verb = String(method || "get").toLowerCase();
  const path = String(url).split("?")[0];
  if (/\/settings\/database\/dump\/?$/.test(path)) return false;
  if (SAFE_METHODS.has(verb)) return true;
  if (verb !== "post") return false;
  return (
    /\/auth\/refresh\/?$/.test(path) ||
    /\/auth\/logOut\/?$/.test(path) ||
    /\/users\/me\/memberships\/\d+\/select\/?$/.test(path)
  );
}

/**
 * @param {object|null|undefined} me
 * @param {{ url?: string, method?: string }} [config]
 */
export function shouldBlockCabinetViewOnlyRequest(me, config = {}) {
  if (!isCabinetViewOnly(me)) return false;
  return !isAllowedCabinetViewOnlyRequest(config.url, config.method);
}
