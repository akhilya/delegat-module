/**
 * Instance invite links: `/signUp?token=<invitation>`.
 * Creating an invite copies this URL; sign-up reads `token` and fills the form.
 */

function firstQueryValue(value) {
  if (value == null) return "";
  if (Array.isArray(value)) return String(value[0] ?? "");
  return String(value);
}

export const SIGN_UP_PATH = "/signUp";

/**
 * @param {string} origin
 * @param {string} token
 * @returns {string}
 */
export function buildInviteSignUpUrl(origin, token) {
  const raw = String(token ?? "").trim();
  if (!raw) return "";
  const url = new URL(SIGN_UP_PATH, origin);
  url.searchParams.set("token", raw);
  return url.toString();
}

/**
 * @param {Record<string, unknown>} [query]
 * @returns {string | null}
 */
export function readInviteTokenQuery(query) {
  if (!query || typeof query !== "object") return null;
  const token = firstQueryValue(
    query.token ?? query.invitationToken,
  ).trim();
  return token || null;
}

/**
 * @param {Record<string, unknown>} [query]
 * @returns {Record<string, unknown>}
 */
export function stripInviteTokenFromQuery(query) {
  const next = {};
  for (const [key, value] of Object.entries(query ?? {})) {
    const lower = key.toLowerCase();
    if (lower === "token" || lower === "invitationtoken") continue;
    next[key] = value;
  }
  return next;
}
