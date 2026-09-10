/** Public product docs (Control Plane SPA under /docs). */

const ADAPTER_CREDENTIALS_DOC_KEYS = new Set([
  "ozon",
  "wildberries",
  "yandex",
  "opencart",
  "woocommerce",
  "medusa",
]);

const DEFAULT_DOCS_PUBLIC_URL = "https://module.delegat.pro/docs";

/**
 * Resolve docs base URL (may include `/docs` path). Must be the **public
 * Control Plane docs origin** (e.g. https://module.delegat.pro/docs), not
 * this instance SPA.
 * 1. Runtime `window.__DELEGAT_DOCS_PUBLIC_URL__` (Docker DOCS_PUBLIC_URL / Vite middleware from repo `.env.dev`)
 * 2. Build-time `VITE_DOCS_PUBLIC_URL`
 * 3. https://module.delegat.pro/docs
 */
function normalizeDocsBase(url) {
  const trimmed = String(url || "")
    .trim()
    .replace(/\/$/, "");
  if (!trimmed) return "";
  // Legacy VitePress host (baked into older images) → CP SPA path.
  if (/^https?:\/\/docs\.module\.delegat\.pro$/i.test(trimmed)) {
    return DEFAULT_DOCS_PUBLIC_URL;
  }
  return trimmed;
}

function viteEnv() {
  return typeof import.meta !== "undefined" && import.meta.env ? import.meta.env : {};
}

function docsBaseUrl() {
  const runtime =
    typeof window !== "undefined"
      ? String(window.__DELEGAT_DOCS_PUBLIC_URL__ || "").trim()
      : "";
  const env = viteEnv();
  const fromVite = String(env.VITE_DOCS_PUBLIC_URL || "").trim();
  return (
    normalizeDocsBase(runtime) ||
    normalizeDocsBase(fromVite) ||
    DEFAULT_DOCS_PUBLIC_URL
  );
}

/** Origin of the public site that hosts `/docs` and `/docs-media`. */
export function getDocsSiteOrigin() {
  try {
    return new URL(docsBaseUrl()).origin;
  } catch {
    return "https://module.delegat.pro";
  }
}

function isEnglishLocale(locale) {
  return locale === "en" || String(locale || "").startsWith("en");
}

/** Locale home: `/` or `/en/` under the docs base. */
export function getDocsPublicUrl(locale = "ru") {
  const base = docsBaseUrl();
  if (isEnglishLocale(locale)) return `${base}/en/`;
  return `${base}/`;
}

/**
 * Absolute docs page URL for a path like `adapters/ozon` or `/adapters/ozon`.
 * English locales get `/en/...`.
 */
export function getDocsPageUrl(path, locale = "ru") {
  const base = docsBaseUrl();
  const normalized = String(path || "")
    .replace(/^\/+/, "")
    .replace(/\/+$/, "");
  if (!normalized) return getDocsPublicUrl(locale);
  if (isEnglishLocale(locale)) return `${base}/en/${normalized}`;
  return `${base}/${normalized}`;
}

/** Credentials article slug for a known adapter key, or null if unsupported. */
export function getAdapterCredentialsDocsSlug(adapterKey) {
  const key = String(adapterKey || "").trim().toLowerCase();
  if (!ADAPTER_CREDENTIALS_DOC_KEYS.has(key)) return null;
  return `adapters/${key}`;
}

/** Credentials how-to for a known adapter key, or null if unsupported. */
export function getAdapterCredentialsDocsUrl(adapterKey, locale = "ru") {
  const slug = getAdapterCredentialsDocsSlug(adapterKey);
  if (!slug) return null;
  return getDocsPageUrl(slug, locale);
}
