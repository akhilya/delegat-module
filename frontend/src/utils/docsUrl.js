/** Public product docs (VitePress on Control Plane). Override with VITE_DOCS_PUBLIC_URL. */
export function getDocsPublicUrl(locale = "ru") {
  const base = (import.meta.env.VITE_DOCS_PUBLIC_URL || "https://docs.module.delegat.pro").replace(
    /\/$/,
    "",
  );
  if (locale === "en" || String(locale).startsWith("en")) return `${base}/en/`;
  return `${base}/`;
}
