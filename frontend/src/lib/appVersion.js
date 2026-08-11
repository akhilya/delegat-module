/**
 * Normalize a product SemVer for the user-menu badge.
 * Strips a leading "v", treats empty / "dev" / "vdev" as unset.
 * @param {unknown} value
 * @returns {string|null}
 */
export function normalizeAppVersion(value) {
  if (value == null) return null;
  let v = String(value).trim();
  if (!v) return null;

  if ((v[0] === "v" || v[0] === "V") && v.length > 1 && /\d/.test(v[1])) {
    v = v.slice(1);
  }

  const lower = v.toLowerCase();
  if (lower === "dev" || lower === "vdev") return null;

  return v;
}

/**
 * Prefer API /health version (runtime), then a real Vite bake-time SemVer.
 * @param {unknown} healthVersion
 * @param {unknown} viteVersion
 * @returns {string}
 */
export function resolveDisplayVersion(healthVersion, viteVersion) {
  return (
    normalizeAppVersion(healthVersion) ||
    normalizeAppVersion(viteVersion) ||
    ""
  );
}
