/** Public product docs (VitePress on Control Plane). */

const ADAPTER_CREDENTIALS_DOC_KEYS = new Set([
  "ozon",
  "wildberries",
  "yandex",
  "opencart",
  "woocommerce",
]);

const DEFAULT_DOCS_PUBLIC_URL = "https://docs.module.delegat.pro";

/**
 * Resolve docs origin:
 * 1. Runtime `window.__DELEGAT_DOCS_PUBLIC_URL__` (Docker DOCS_PUBLIC_URL / Vite middleware from repo `.env.dev`)
 * 2. Build-time `VITE_DOCS_PUBLIC_URL`
 * 3. Dev → http://localhost:5174 · Prod → https://docs.module.delegat.pro
 */
function docsBaseUrl() {
  const runtime =
    typeof window !== "undefined"
      ? String(window.__DELEGAT_DOCS_PUBLIC_URL__ || "").trim()
      : "";
  const fromVite = String(import.meta.env.VITE_DOCS_PUBLIC_URL || "").trim();
  const fallback = import.meta.env.DEV
    ? "http://localhost:5174"
    : DEFAULT_DOCS_PUBLIC_URL;
  return (runtime || fromVite || fallback).replace(/\/$/, "");
}

function isEnglishLocale(locale) {
  return locale === "en" || String(locale || "").startsWith("en");
}

/** Locale home: `/` or `/en/`. */
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

/** Credentials how-to for a known adapter key, or null if unsupported. */
export function getAdapterCredentialsDocsUrl(adapterKey, locale = "ru") {
  const key = String(adapterKey || "").trim().toLowerCase();
  if (!ADAPTER_CREDENTIALS_DOC_KEYS.has(key)) return null;
  return getDocsPageUrl(`adapters/${key}`, locale);
}
