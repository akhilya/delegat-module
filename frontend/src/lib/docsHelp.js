/**
 * Cabinet route → public product-docs slug. Existence still comes from the
 * fetched catalog; this table only maps SPA paths (which live in this repo).
 */

const ADAPTER_DOC_KEYS = new Set([
  "ozon",
  "wildberries",
  "yandex",
  "opencart",
  "woocommerce",
  "avito",
]);

function normalizePath(pathname) {
  const path = String(pathname || "")
    .split("?")[0]
    .split("#")[0];
  const trimmed = path.replace(/\/+$/, "");
  return trimmed || "/";
}

/**
 * @param {string} pathname
 * @param {{ adapterKey?: string | null }} [opts]
 * @returns {string | null} docs slug, or null when this screen has no article
 */
export function docsSlugForPath(pathname, opts = {}) {
  const path = normalizePath(pathname);

  if (path === "/" || path === "/products" || path.startsWith("/products/")) {
    return "workspace/products";
  }
  if (path === "/categories" || path.startsWith("/categories/")) {
    return "workspace/categories";
  }
  if (path === "/product-groups" || path.startsWith("/product-groups/")) {
    return "workspace/groups";
  }
  if (path === "/barcodes" || path.startsWith("/barcodes/")) {
    return "workspace/barcodes";
  }
  if (path === "/storage" || path.startsWith("/storage/")) {
    return "workspace/files";
  }
  if (path === "/store-attributes" || path.startsWith("/store-attributes/")) {
    return "workspace/attributes";
  }

  if (path === "/orders" || path.startsWith("/orders/")) {
    return "workspace/orders";
  }
  if (path === "/notes" || path.startsWith("/notes/")) {
    return "workspace/notes";
  }
  if (path === "/setup" || path.startsWith("/setup/")) {
    return "workspace/store";
  }

  if (path === "/adapter/plugins" || path.startsWith("/adapter/plugins/")) {
    return "workspace/adapters";
  }
  const adapterMatch = path.match(/^\/adapter(?:\/(\d+))?(?:\/.*)?$/);
  if (adapterMatch) {
    if (adapterMatch[1] && /\/categories\//.test(path)) {
      return "workspace/mapping";
    }
    if (adapterMatch[1]) {
      const key = String(opts.adapterKey || "")
        .trim()
        .toLowerCase();
      if (ADAPTER_DOC_KEYS.has(key)) return `adapters/${key}`;
    }
    return "workspace/adapters";
  }

  if (path === "/settings/admin/ai" || path.startsWith("/settings/admin/ai/")) {
    return "workspace/ai";
  }
  if (
    path === "/settings/admin/api" ||
    path.startsWith("/settings/admin/api/")
  ) {
    return "workspace/public-api";
  }
  if (path === "/settings" || path.startsWith("/settings/")) {
    return "workspace/settings";
  }

  if (path === "/tools/catalog-connect") {
    return "workspace/catalog-connect";
  }
  if (path === "/tools/catalog-scrape") {
    return "workspace/catalog-scrape";
  }
  if (path === "/tools/public-api-try") {
    return "workspace/public-api";
  }
  if (path === "/tools") {
    return "workspace/tools";
  }

  return null;
}

/**
 * @param {{ groups?: Array<{ items?: Array<{ slug?: string }> }> } | null | undefined} catalog
 * @param {string | null | undefined} slug
 */
export function catalogHasSlug(catalog, slug) {
  if (slug == null) return false;
  const want = String(slug);
  for (const group of catalog?.groups || []) {
    for (const item of group.items || []) {
      if (String(item.slug || "") === want) return true;
    }
  }
  return false;
}

/** Page Help is shown only when the route maps and the catalog lists that slug. */
export function pageHelpSlug(pathname, catalog, opts = {}) {
  const slug = docsSlugForPath(pathname, opts);
  if (slug == null) return null;
  if (!catalogHasSlug(catalog, slug)) return null;
  return slug;
}
