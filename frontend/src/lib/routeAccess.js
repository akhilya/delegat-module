import { hasAnyPermission, hasPermission, PERMS } from "@/lib/permissions";

const PAGE_SETTINGS = "/settings";
const PAGE_SETTINGS_USER = "/settings/user";
const PAGE_ADMIN_STORES = "/settings/admin/stores";
const PAGE_ADMIN_USERS = "/settings/admin/users";
const PAGE_ADMIN_API = "/settings/admin/api";
const PAGE_ADMIN_PLUGINS = "/settings/admin/plugins";
const PAGE_ADMIN_ERROR_LOGS = "/settings/admin/error-logs";
const PAGE_ADMIN_AI = "/settings/admin/ai";
const PAGE_ADAPTER_LOGS = "/settings/adapter-logs";
const PAGE_PRODUCT_CREATION = "/products/create";
const PAGE_CATEGORIES = "/categories";
const PAGE_BARCODES = "/barcodes";
const PAGE_STORE_ATTRIBUTES = "/store-attributes";
const PAGE_TOOLS = "/tools";
const PAGE_MARKETPLACE = "/adapter";
const PAGE_PRODUCTS = "/products";

/**
 * Route path → required permission(s) (any-of).
 * Missing entry = authenticated members only (no extra gate).
 */
const ROUTE_PERMISSIONS = {
  [PAGE_SETTINGS]: [PERMS.SettingsPlatform],
  [PAGE_ADMIN_USERS]: [PERMS.UsersManage],
  [PAGE_ADMIN_API]: [PERMS.ApiAccess],
  [PAGE_ADMIN_PLUGINS]: [PERMS.PluginsManage],
  [PAGE_ADMIN_ERROR_LOGS]: [PERMS.SettingsPlatform],
  [PAGE_ADMIN_AI]: [PERMS.SettingsPlatform],
  [PAGE_ADAPTER_LOGS]: [PERMS.SettingsPlatform],
  [PAGE_ADMIN_STORES]: [PERMS.StoresCreate, PERMS.StoreSettingsManage, PERMS.StoreMembersManage],
  [PAGE_PRODUCT_CREATION]: [PERMS.CatalogManage],
  [PAGE_CATEGORIES]: [PERMS.CatalogManage],
  [PAGE_BARCODES]: [PERMS.CatalogManage],
  [PAGE_STORE_ATTRIBUTES]: [PERMS.CatalogManage],
  [PAGE_TOOLS]: [PERMS.OperationsManage, PERMS.CatalogManage],
  [PAGE_MARKETPLACE]: [PERMS.MarketplaceManage],
};

/**
 * @param {string} path
 * @returns {string}
 */
export function normalizeRoutePath(path) {
  if (!path) return "/";
  const bare = path.split("?")[0].split("#")[0];
  if (bare.length > 1 && bare.endsWith("/")) return bare.slice(0, -1);
  return bare || "/";
}

/**
 * @param {string} path
 * @returns {string[]|null}
 */
export function getRequiredPermissionsForPath(path) {
  const p = normalizeRoutePath(path);

  if (ROUTE_PERMISSIONS[p]) return ROUTE_PERMISSIONS[p];

  if (p.startsWith(`${PAGE_ADMIN_STORES}/`)) {
    return [PERMS.StoresCreate, PERMS.StoreSettingsManage, PERMS.StoreMembersManage];
  }
  if (p.startsWith("/categories/")) return [PERMS.CatalogManage];
  if (p.startsWith("/product-groups")) return [PERMS.CatalogManage];
  if (p.startsWith("/products/") && (p.endsWith("/edit") || p.includes("/create"))) {
    return [PERMS.CatalogManage];
  }
  if (p.startsWith("/adapter/") && p !== PAGE_MARKETPLACE) {
    return [PERMS.MarketplaceManage];
  }

  return null;
}

/**
 * @param {object|null} me
 * @param {string} path
 */
export function canAccessPath(me, path) {
  const p = normalizeRoutePath(path);
  if (p === PAGE_ADMIN_AI && me?.aiEnabled !== true) return false;
  if (p === PAGE_ADMIN_ERROR_LOGS && me?.logErrorsEnabled !== true) return false;
  if (p === PAGE_ADAPTER_LOGS && me?.logAdapterHttpEnabled !== true) return false;
  if (p === PAGE_ADMIN_API && me?.publicApiEnabled === false) return false;

  const required = getRequiredPermissionsForPath(path);
  if (!required || required.length === 0) return true;
  return hasAnyPermission(me, required);
}

/**
 * @param {object|null} me
 */
export function fallbackPathForUser(me) {
  if (hasPermission(me, PERMS.SettingsPlatform)) return PAGE_SETTINGS;
  if (hasAnyPermission(me, [PERMS.StoresCreate, PERMS.StoreSettingsManage])) return PAGE_ADMIN_STORES;
  if (hasPermission(me, PERMS.CatalogManage) || hasPermission(me, PERMS.ReferenceRead)) return PAGE_PRODUCTS;
  return PAGE_SETTINGS_USER;
}
