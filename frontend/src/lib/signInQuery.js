/**
 * Demo / shared sign-in links: `/signIn?username=user&password=user123`
 * Credentials are read once, then stripped from the URL so they do not stay
 * in history or get sent as Referer.
 */

function firstQueryValue(value) {
  if (value == null) return "";
  if (Array.isArray(value)) return String(value[0] ?? "");
  return String(value);
}

/**
 * @param {Record<string, unknown>} [query]
 * @returns {{ username: string, password: string } | null}
 */
export function readSignInQuery(query) {
  if (!query || typeof query !== "object") return null;
  const username = firstQueryValue(
    query.username ?? query.userName,
  ).trim();
  const password = firstQueryValue(query.password);
  if (!username || !password) return null;
  return { username, password };
}

/**
 * @param {Record<string, unknown>} [query]
 * @returns {Record<string, unknown>}
 */
export function stripSignInSecretsFromQuery(query) {
  const next = {};
  for (const [key, value] of Object.entries(query ?? {})) {
    const lower = key.toLowerCase();
    if (lower === "username" || lower === "password") continue;
    next[key] = value;
  }
  return next;
}
