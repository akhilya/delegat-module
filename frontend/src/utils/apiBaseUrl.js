/**
 * Normalize VITE_API_SERVER so SPA calls hit the /api route prefix.
 *
 * Controllers are under /api (ApiRoutePrefixConvention). Common misconfig:
 * root `.env` sets VITE_API_SERVER=http://localhost:5000 (bare origin) which
 * the API process inherits into Vite — then /auth/signIn 404s (needs /api/auth/signIn).
 *
 * @param {string | undefined | null} raw
 * @returns {string}
 */
export function resolveApiBaseUrl(raw) {
  const value = String(raw ?? "").trim().replace(/\/+$/, "");
  if (!value) return "/api";

  if (value.startsWith("/")) {
    if (value === "/api" || value.endsWith("/api")) return value;
    return `${value}/api`.replace(/\/{2,}/g, "/");
  }

  if (/^https?:\/\//i.test(value)) {
    try {
      const url = new URL(value);
      const path = (url.pathname || "/").replace(/\/+$/, "") || "/";
      if (path === "/" || path === "") {
        return `${url.origin}/api`;
      }
      if (path === "/api" || path.endsWith("/api")) {
        return `${url.origin}${path}`;
      }
      return `${url.origin}${path}`;
    } catch {
      return value.endsWith("/api") ? value : `${value}/api`;
    }
  }

  return value;
}
