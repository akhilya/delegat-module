const PLACEHOLDER_FAVICON_PATHS = [
  "/logo-square-favicon.webp",
  "/logo_sm@0,15x.webp",
];

function faviconPath(url) {
  const trimmed = String(url || "").trim();
  if (!trimmed) return "";
  const withoutQuery = trimmed.split("?")[0];
  try {
    if (/^https?:\/\//i.test(withoutQuery)) {
      return new URL(withoutQuery).pathname;
    }
  } catch {
    return withoutQuery;
  }
  return withoutQuery;
}

/** Control Plane / legacy squares are not a store icon. */
export function isPlaceholderFavicon(url) {
  const path = faviconPath(url);
  if (!path) return true;
  return PLACEHOLDER_FAVICON_PATHS.some(
    (placeholder) => path === placeholder || path.endsWith(placeholder),
  );
}

/** Store tab / switcher image, or empty when none is uploaded. */
export function storeIconUrl(url) {
  if (isPlaceholderFavicon(url)) return "";
  return String(url).trim();
}

export function storeIconInitial(name) {
  const text = String(name || "").trim();
  if (!text) return "";
  return [...text][0].toLocaleUpperCase();
}
