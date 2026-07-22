/** In-memory /auth/me snapshot shared by router + feature gates (sync reads). */

/** Fired on `window` after me cache is refreshed (sidebar / layout should re-read). */
export const ME_UPDATED_EVENT = "delegat:me-updated";

/** @type {object|null|undefined} */
let cachedMe = null;

/** @param {object|null|undefined} me */
export function primeMeCache(me) {
  if (me && typeof me === "object") cachedMe = me;
}

export function getCachedMe() {
  return cachedMe;
}

export function clearMeCache() {
  cachedMe = null;
}

/** Notify UI that /auth/me snapshot changed. */
export function notifyMeUpdated(me = cachedMe) {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new CustomEvent(ME_UPDATED_EVENT, { detail: me ?? null }));
}
