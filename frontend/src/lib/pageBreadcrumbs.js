import { parseOrdersPath } from "./ordersUi.js";

const PAGE_PRODUCTS = "/products";
const PAGE_PRODUCT_CREATION = "/products/create";
const PAGE_PRODUCT_EDIT = "/products/:id/edit";
const PAGE_CATEGORIES = "/categories";
const PAGE_CATEGORY_EDIT = "/categories/:id/edit";
const PAGE_PRODUCT_GROUPS = "/product-groups";
const PAGE_PRODUCT_GROUP_CREATE = "/product-groups/create";
const PAGE_PRODUCT_GROUP_EDIT = "/product-groups/:id/edit";
const PAGE_STORE_ATTRIBUTES = "/store-attributes";
const PAGE_STORE_ATTRIBUTE_EDIT = "/store-attributes/:id/edit";
const PAGE_STORE_STORAGE = "/storage";
const PAGE_BARCODES = "/barcodes";
const PAGE_NOTES = "/notes";
const PAGE_ORDERS = "/orders";
const PAGE_ORDERS_CONNECTION = "/orders/:connectionId";
const PAGE_ORDERS_MAILBOX = "/orders/:connectionId/:bucket";
const PAGE_MARKETPLACE = "/adapter";
const PAGE_MARKETPLACE_CONNECTION = "/adapter/:connId";
const PAGE_MARKETPLACE_CATEGORY_IMPORT = "/adapter/:connId/categories/import";
const PAGE_MARKETPLACE_CATEGORY_CREATE = "/adapter/:connId/categories/create";
const PAGE_MARKETPLACE_FIELD_MAPPING = "/adapter/:connId/categories/:mappingId";
const PAGE_TOOLS = "/tools";
const PAGE_CATALOG_CONNECT = "/tools/catalog-connect";
const PAGE_CATALOG_SCRAPE = "/tools/catalog-scrape";
const PAGE_PUBLIC_API_TRY = "/tools/public-api-try";
const PAGE_TOOL_JOB = "/tools/:toolId";
const PAGE_SCHEDULED_TASKS = "/tools/scheduled-tasks";
const PAGE_SETTINGS = "/settings";
const PAGE_SETTINGS_USER = "/settings/user";
const PAGE_ADMIN_STORES = "/settings/admin/stores";

const SETTINGS_CRUMB_KEYS = {
  [PAGE_SETTINGS]: "nav.overview",
  [PAGE_SETTINGS_USER]: "nav.user",
  [PAGE_ADMIN_STORES]: "nav.stores",
  "/settings/admin/users": "nav.users",
  "/settings/admin/api": "nav.api",
  "/settings/admin/plugins": "nav.plugins",
  "/settings/admin/error-logs": "nav.errorLogs",
  "/settings/admin/ai": "nav.ai",
  "/settings/adapter-logs": "nav.adapterLogs",
};

function crumb(label, to) {
  return to ? { label, to } : { label };
}

function queryFlag(query, key) {
  const value = query?.[key];
  return value === "1" || value === 1 || value === true;
}

function settingsRootPath(canPlatformSettings) {
  return canPlatformSettings ? PAGE_SETTINGS : PAGE_SETTINGS_USER;
}

function settingsCrumbs(path, { t, detail, canPlatformSettings }) {
  const settingsRoot = settingsRootPath(canPlatformSettings);
  if (path === PAGE_SETTINGS) {
    return [crumb(t("nav.overview"))];
  }

  const storeDetailPrefix = `${PAGE_ADMIN_STORES}/`;
  if (path.startsWith(storeDetailPrefix)) {
    return [
      crumb(t("nav.overview"), settingsRoot),
      crumb(t("nav.stores"), PAGE_ADMIN_STORES),
      crumb(detail || t("routes.store")),
    ];
  }

  const key = SETTINGS_CRUMB_KEYS[path];
  if (!key) return [crumb(t("nav.overview"))];
  return [crumb(t("nav.overview"), settingsRoot), crumb(detail || t(key))];
}

function adaptersCrumb(t) {
  return crumb(t("routes.adapters"), PAGE_MARKETPLACE);
}

function connectionCrumb(t, connId, parentLabel) {
  const label =
    parentLabel ||
    t("marketplace.connectionPage.breadcrumbs.connectionFallback");
  return crumb(label, `${PAGE_MARKETPLACE}/${connId}`);
}

/**
 * Trail for the page chrome (Home is added by the Breadcrumbs control).
 * Route `name` is the Vue Router path pattern (`/products/:id/edit`).
 *
 * @param {object} opts
 * @param {string} [opts.name]
 * @param {string} opts.path
 * @param {Record<string, unknown>} [opts.query]
 * @param {Record<string, unknown>} [opts.params]
 * @param {{ titleKey?: string }} [opts.meta]
 * @param {string|null} [opts.detail]
 * @param {string|null} [opts.parentLabel]
 * @param {(key: string, values?: Record<string, unknown>) => string} opts.t
 * @param {boolean} [opts.canPlatformSettings]
 */
export function resolvePageBreadcrumbs({
  name,
  path,
  query,
  params,
  meta,
  detail,
  parentLabel,
  t,
  canPlatformSettings = true,
}) {
  const pathname =
    String(path || "")
      .split("?")[0]
      .replace(/\/+$/, "") || "/";
  let routeName = name || pathname;
  if (/\/adapter\/[^/]+\/categories\/create$/.test(pathname)) {
    routeName = PAGE_MARKETPLACE_CATEGORY_CREATE;
  }
  const connId = params?.connId ?? parseOrdersPath(pathname).connectionId;

  if (pathname === PAGE_SETTINGS || pathname.startsWith(`${PAGE_SETTINGS}/`)) {
    return settingsCrumbs(pathname, { t, detail, canPlatformSettings });
  }

  switch (routeName) {
    case "/":
    case PAGE_PRODUCTS: {
      if (queryFlag(query, "trash")) {
        return [
          crumb(t("routes.products"), PAGE_PRODUCTS),
          crumb(detail || t("pages.productsPage.breadcrumbTrash")),
        ];
      }
      return [crumb(t("routes.products"))];
    }
    case PAGE_PRODUCT_CREATION:
      return [
        crumb(t("routes.products"), PAGE_PRODUCTS),
        crumb(t("routes.productCreation")),
      ];
    case PAGE_PRODUCT_EDIT:
      return [
        crumb(t("routes.products"), PAGE_PRODUCTS),
        crumb(detail || t("pages.productEditPage.breadcrumbEditing")),
      ];
    case PAGE_CATEGORIES:
      return [crumb(t("routes.categories"))];
    case PAGE_CATEGORY_EDIT:
      return [
        crumb(t("routes.categories"), PAGE_CATEGORIES),
        crumb(detail || t("pages.categoryEditPage.breadcrumbEditing")),
      ];
    case PAGE_PRODUCT_GROUPS:
      return [crumb(t("routes.groups"))];
    case PAGE_PRODUCT_GROUP_CREATE:
      return [
        crumb(t("routes.groups"), PAGE_PRODUCT_GROUPS),
        crumb(t("routes.groupCreate")),
      ];
    case PAGE_PRODUCT_GROUP_EDIT:
      return [
        crumb(t("routes.groups"), PAGE_PRODUCT_GROUPS),
        crumb(detail || t("pages.productGroupEditPage.breadcrumbEditing")),
      ];
    case PAGE_STORE_ATTRIBUTES:
      return [crumb(t("routes.attributes"))];
    case PAGE_STORE_ATTRIBUTE_EDIT:
      return [
        crumb(t("routes.attributes"), PAGE_STORE_ATTRIBUTES),
        crumb(detail || t("pages.storeAttributesPage.breadcrumbEditing")),
      ];
    case PAGE_STORE_STORAGE:
      return [crumb(t("routes.storage"))];
    case PAGE_BARCODES:
      return [crumb(t("routes.barcodes"))];
    case PAGE_NOTES:
      return [crumb(t("routes.notes"))];
    case PAGE_ORDERS:
      return [crumb(t("routes.orders"))];
    case PAGE_ORDERS_CONNECTION:
    case PAGE_ORDERS_MAILBOX:
      return [
        crumb(t("routes.orders"), PAGE_ORDERS),
        crumb(
          parentLabel ||
            t("marketplace.connectionPage.breadcrumbs.connectionFallback"),
        ),
      ];
    case PAGE_MARKETPLACE:
      return [crumb(t("routes.adapters"))];
    case PAGE_MARKETPLACE_CONNECTION:
      return [
        adaptersCrumb(t),
        crumb(
          detail ||
            parentLabel ||
            t("marketplace.connectionPage.breadcrumbs.connectionFallback"),
        ),
      ];
    case PAGE_MARKETPLACE_CATEGORY_IMPORT:
      return [
        adaptersCrumb(t),
        crumb(
          parentLabel || t("marketplace.categoryImportPage.connection"),
          `${PAGE_MARKETPLACE}/${connId}?tab=mappings`,
        ),
        crumb(t("marketplace.categoryImportPage.importCategory")),
      ];
    case PAGE_MARKETPLACE_CATEGORY_CREATE:
      return [
        adaptersCrumb(t),
        crumb(
          parentLabel || t("marketplace.categoryCreatePage.connection"),
          `${PAGE_MARKETPLACE}/${connId}?tab=mappings`,
        ),
        crumb(t("marketplace.categoryCreatePage.createCategory")),
      ];
    case PAGE_MARKETPLACE_FIELD_MAPPING:
      return [
        adaptersCrumb(t),
        connectionCrumb(t, connId, parentLabel),
        crumb(
          t("marketplace.fieldMappingPage.mappings"),
          `${PAGE_MARKETPLACE}/${connId}?tab=mappings`,
        ),
        crumb(detail || t("marketplace.fieldMappingPage.category")),
      ];
    case PAGE_TOOLS:
      return [crumb(t("routes.tools"))];
    case PAGE_SCHEDULED_TASKS:
      return [
        crumb(t("routes.tools"), PAGE_TOOLS),
        crumb(t("routes.scheduledTasks")),
      ];
    case PAGE_CATALOG_CONNECT:
      return [
        crumb(t("routes.tools"), PAGE_TOOLS),
        crumb(t("routes.catalogConnect")),
      ];
    case PAGE_CATALOG_SCRAPE:
      return [
        crumb(t("routes.tools"), PAGE_TOOLS),
        crumb(t("routes.catalogScrape")),
      ];
    case PAGE_PUBLIC_API_TRY:
      return [
        crumb(t("routes.tools"), PAGE_TOOLS),
        crumb(t("routes.publicApiTry")),
      ];
    case PAGE_TOOL_JOB:
      return [
        crumb(t("routes.tools"), PAGE_TOOLS),
        crumb(detail || t("routes.toolJob")),
      ];
    default:
      break;
  }

  if (meta?.titleKey) return [crumb(t(meta.titleKey))];
  return [];
}
