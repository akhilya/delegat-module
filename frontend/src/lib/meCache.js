/** In-memory /auth/me snapshot shared by router + feature gates (sync reads). */

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
