import { resolveApiBaseUrl } from "../utils/apiBaseUrl.js";

/** Preset Public API v1 GETs for the Tools → Try Public API page. */
export const PUBLIC_API_TRY_ENDPOINTS = [
  { id: "me", method: "GET", path: "/me" },
  { id: "stores", method: "GET", path: "/stores" },
];

export const PUBLIC_API_TRY_CUSTOM_ID = "custom";

/**
 * Join SPA API base (`/api` or `https://host/api`) with a Public API v1 path (`/me`).
 * @param {string} path
 * @param {string} [apiBase]
 */
export function publicApiV1Url(path, apiBase = "/api") {
  const base = String(apiBase ?? "").replace(/\/+$/, "") || "/api";
  const suffix = String(path ?? "").startsWith("/") ? path : `/${path}`;
  return `${base}/v1${suffix}`;
}

export function publicApiV1UrlFromEnv(path, rawViteApiServer) {
  return publicApiV1Url(path, resolveApiBaseUrl(rawViteApiServer));
}

export function findPublicApiTryEndpoint(id) {
  return (
    PUBLIC_API_TRY_ENDPOINTS.find((e) => e.id === id) ??
    PUBLIC_API_TRY_ENDPOINTS[0]
  );
}

/**
 * Same-origin /api/v1 relative path. Rejects URLs and parent segments.
 * @param {string} raw
 * @returns {string | null}
 */
export function normalizePublicApiTryPath(raw) {
  let value = String(raw ?? "").trim();
  if (!value) return null;
  if (value.includes("://") || value.includes("..") || value.includes("\\")) {
    return null;
  }
  if (value.startsWith("/api/v1")) {
    value = value.slice("/api/v1".length) || "/";
  }
  if (!value.startsWith("/")) value = `/${value}`;
  if (value.includes("://") || value.includes("..")) return null;
  return value;
}

function emptyTryResult(error) {
  return { ok: false, status: 0, error, body: "", durationMs: 0 };
}

/**
 * Call a Public API v1 URL with a partner key (no SPA cookie).
 * @returns {Promise<{ ok: boolean, status: number, error: string | null, body: string, durationMs: number }>}
 */
export async function sendPublicApiTry({
  fetchImpl = globalThis.fetch,
  url,
  apiKey,
  method = "GET",
  now = () =>
    typeof performance !== "undefined" && typeof performance.now === "function"
      ? performance.now()
      : Date.now(),
} = {}) {
  const key = String(apiKey ?? "").trim();
  if (!key) return emptyTryResult("key_required");
  if (typeof fetchImpl !== "function")
    return emptyTryResult("fetch_unavailable");

  const started = now();
  const response = await fetchImpl(url, {
    method: method || "GET",
    headers: {
      Authorization: `Bearer ${key}`,
      Accept: "application/json",
    },
    credentials: "omit",
  });
  const durationMs = Math.max(0, Math.round(now() - started));
  const text = typeof response.text === "function" ? await response.text() : "";
  let body = text;
  if (text) {
    try {
      body = JSON.stringify(JSON.parse(text), null, 2);
    } catch {
      body = text;
    }
  }

  return {
    ok: response.ok === true,
    status: Number(response.status) || 0,
    error: null,
    body,
    durationMs,
  };
}
