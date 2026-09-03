import { createWebHistory, createRouter as _createRouter } from "vue-router";
import { setPageTitleDetailProvider, syncDocumentTitle } from "@/brand";
import { t } from "@/i18n";
import { canAccessPath, fallbackPathForUser } from "@/lib/routeAccess";
import { PERMS } from "@/lib/permissions";
import { PAGE_SETUP } from "@/lib/setupGate";
import { isOrderBucket, orderBucketById } from "@/lib/ordersUi";

const signUpPage = () => import("./pages/signUpPage.vue");
const signInPage = () => import("./pages/signInPage.vue");
const productsPage = () => import("./pages/productsPage.vue");
const productCreationPage = () => import("./pages/productCreationPage.vue");
const productEditPage = () => import("./pages/productEditPage.vue");
const categoriesPage = () => import("./pages/categoriesPage.vue");
const categoryEditPage = () => import("./pages/categoryEditPage.vue");
const barcodesPage = () => import("./pages/barcodesPage.vue");
const notesPage = () => import("./pages/notesPage.vue");
const productGroupsPage = () => import("./pages/productGroupsPage.vue");
const productGroupEditPage = () => import("./pages/productGroupEditPage.vue");
const storeAttributesPage = () => import("./pages/storeAttributesPage.vue");
const storeAttributeEditPage = () => import("./pages/storeAttributeEditPage.vue");
const storeStoragePage = () => import("./pages/storeStoragePage.vue");
const SettingsShell = () => import("./layouts/SettingsShell.vue");
const SettingsOverviewPage = () => import("./pages/settings/settingsOverviewPage.vue");
const SettingsUserPage = () => import("./pages/settings/settingsUserPage.vue");
const NotFoundPage = () => import("./pages/notFoundPage.vue");
const UnavailablePage = () => import("./pages/unavailablePage.vue");
const ToolsPage = () => import("./pages/toolsPage.vue");
const CatalogConnectPage = () => import("./pages/tools/CatalogConnectPage.vue");
const CatalogScrapePage = () => import("./pages/tools/CatalogScrapePage.vue");
const PublicApiTryPage = () => import("./pages/tools/PublicApiTryPage.vue");
const ToolJobPage = () => import("./pages/tools/ToolJobPage.vue");
const AdminStoresPage = () => import("./pages/admin/storesPage.vue");
const AdminStoreDetailPage = () => import("./pages/admin/storeDetailPage.vue");
const AdminUsersPage = () => import("./pages/admin/usersPage.vue");
const AdminErrorLogsPage = () => import("./pages/admin/errorLogsPage.vue");
const AdminApiPage = () => import("./pages/admin/apiPage.vue");
const MarketplacePage = () => import("./pages/marketplace/marketplacePage.vue");
const MarketplaceConnectionPage = () => import("./pages/marketplace/connectionPage.vue");
const MarketplaceCategoryImportPage = () => import("./pages/marketplace/categoryImportPage.vue");
const MarketplaceFieldMappingPage = () => import("./pages/marketplace/fieldMappingPage.vue");
const AdapterLogsPage = () => import("./pages/marketplace/adapterLogsPage.vue");
const PluginsPage = () => import("./pages/marketplace/pluginsPage.vue");
const AiModelsPage = () => import("./pages/settings/AiModelsPage.vue");
const SetupWizardPage = () => import("./pages/setup/SetupWizardPage.vue");
const OrdersPage = () => import("./pages/orders/OrdersPage.vue");

export const PAGE_SIGN_UP = "/signUp";
export const PAGE_SIGN_IN = "/signIn";
export const PAGE_UNAVAILABLE = "/unavailable";
export const PAGE_PRODUCTS = "/products";
export { PAGE_SETUP };
export const PAGE_PRODUCT_CREATION = "/products/create";
export const PAGE_PRODUCT_EDIT = "/products/:id/edit";
export const PAGE_CATEGORIES = "/categories";
export const PAGE_CATEGORY_EDIT = "/categories/:id/edit";
export const PAGE_BARCODES = "/barcodes";
export const PAGE_NOTES = "/notes";
export const PAGE_ORDERS = "/orders";
export const PAGE_ORDERS_BUCKET = "/orders/:bucket";
export const PAGE_PRODUCT_GROUPS = "/product-groups";
export const PAGE_PRODUCT_GROUP_CREATE = "/product-groups/create";
export const PAGE_PRODUCT_GROUP_EDIT = "/product-groups/:id/edit";
export const PAGE_STORE_ATTRIBUTES = "/store-attributes";
export const PAGE_STORE_ATTRIBUTE_EDIT = "/store-attributes/:id/edit";
export const PAGE_STORE_STORAGE = "/storage";
export const PAGE_SETTINGS = "/settings";
export const PAGE_SETTINGS_OPERATIONS = "/settings/operations";
export const PAGE_SETTINGS_USER = "/settings/user";
export const PAGE_NOT_FOUND = "/:pathMatch(.*)*";
export const PAGE_TOOLS = "/tools";
export const PAGE_CATALOG_CONNECT = "/tools/catalog-connect";
export const PAGE_CATALOG_SCRAPE = "/tools/catalog-scrape";
export const PAGE_PUBLIC_API_TRY = "/tools/public-api-try";
export const PAGE_TOOL_JOB = "/tools/:toolId";
export const PAGE_ADMIN_STORES = "/settings/admin/stores";
export const PAGE_ADMIN_STORE_DETAIL = "/settings/admin/stores/:id";
export const PAGE_ADMIN_USERS = "/settings/admin/users";
export const PAGE_ADMIN_ERROR_LOGS = "/settings/admin/error-logs";
export const PAGE_ADMIN_API = "/settings/admin/api";
export const PAGE_ADMIN_PLUGINS = "/settings/admin/plugins";
export const PAGE_ADMIN_AI = "/settings/admin/ai";
export const PAGE_ADMIN_USAGE = "/settings/admin/usage";
export const PAGE_MARKETPLACE = "/adapter";
export const PAGE_MARKETPLACE_PLUGINS = "/adapter/plugins";
export const PAGE_ADAPTER_LOGS = "/settings/adapter-logs";
export const PAGE_MARKETPLACE_CONNECTION = "/adapter/:connId";
export const PAGE_MARKETPLACE_CATEGORY_IMPORT = "/adapter/:connId/categories/import";
export const PAGE_MARKETPLACE_FIELD_MAPPING = "/adapter/:connId/categories/:mappingId";

// Track whether a page component set a custom title (prevents afterEach from overwriting it on tab/query changes)
let _customTitle = null;

export const routes = [
  { path: PAGE_SIGN_UP, component: signUpPage, meta: { public: true, titleKey: "routes.signUp" } },
  { path: PAGE_SIGN_IN, component: signInPage, meta: { public: true, titleKey: "routes.signIn" } },
  {
    path: PAGE_UNAVAILABLE,
    component: UnavailablePage,
    meta: { public: true, titleKey: "routes.unavailable" },
  },
  {
    path: PAGE_SETUP,
    component: SetupWizardPage,
    meta: { bareLayout: true, titleKey: "routes.setup", permissions: [PERMS.StoresCreate] },
  },
  { path: "/", component: productsPage, meta: { titleKey: "routes.products" } },
  { path: PAGE_PRODUCTS, component: productsPage, meta: { titleKey: "routes.products" } },
  { path: PAGE_ORDERS, redirect: "/orders/new" },
  {
    path: PAGE_ORDERS_BUCKET,
    component: OrdersPage,
    meta: { titleKey: "routes.orders", permissions: [PERMS.MarketplaceManage] },
    beforeEnter(to) {
      if (!isOrderBucket(to.params.bucket)) {
        return { path: "/orders/new", query: to.query };
      }
      const bucket = orderBucketById(to.params.bucket);
      if (bucket?.titleKey) to.meta.titleKey = bucket.titleKey;
      return true;
    },
  },
  {
    path: PAGE_PRODUCT_CREATION,
    component: productCreationPage,
    meta: { titleKey: "routes.productCreation", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_PRODUCT_EDIT,
    component: productEditPage,
    meta: { titleKey: "routes.productEdit", stableKey: "product-edit", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_CATEGORIES,
    component: categoriesPage,
    meta: { titleKey: "routes.categories", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_CATEGORY_EDIT,
    component: categoryEditPage,
    meta: { titleKey: "routes.categoryEdit", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_BARCODES,
    component: barcodesPage,
    meta: { titleKey: "routes.barcodes", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_NOTES,
    component: notesPage,
    meta: { titleKey: "routes.notes", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_PRODUCT_GROUPS,
    component: productGroupsPage,
    meta: { titleKey: "routes.groups", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_PRODUCT_GROUP_CREATE,
    component: productGroupEditPage,
    meta: { titleKey: "routes.groupCreate", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_PRODUCT_GROUP_EDIT,
    component: productGroupEditPage,
    meta: { titleKey: "routes.groupEdit", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_STORE_ATTRIBUTE_EDIT,
    component: storeAttributeEditPage,
    meta: { titleKey: "routes.attributeEdit", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_STORE_ATTRIBUTES,
    component: storeAttributesPage,
    meta: { titleKey: "routes.attributes", permissions: [PERMS.CatalogManage] },
  },
  {
    path: PAGE_STORE_STORAGE,
    component: storeStoragePage,
    meta: { titleKey: "routes.storage", permissions: [PERMS.FilesManage] },
  },
  {
    path: PAGE_SETTINGS,
    component: SettingsShell,
    meta: { stableKey: "settings" },
    children: [
      {
        path: "",
        component: SettingsOverviewPage,
        meta: { titleKey: "routes.settingsOverview", permissions: [PERMS.SettingsPlatform] },
      },
      { path: "operations", redirect: "" },
      { path: "user", component: SettingsUserPage, meta: { titleKey: "routes.settingsUser" } },
      { path: "store", redirect: "/settings/admin/stores" },
      {
        path: "admin/stores",
        component: AdminStoresPage,
        meta: {
          titleKey: "routes.stores",
          permissions: [PERMS.StoresCreate, PERMS.StoreSettingsManage, PERMS.StoreMembersManage],
        },
      },
      {
        path: "admin/stores/:id",
        component: AdminStoreDetailPage,
        meta: {
          titleKey: "routes.store",
          permissions: [PERMS.StoresCreate, PERMS.StoreSettingsManage, PERMS.StoreMembersManage],
        },
      },
      {
        path: "admin/users",
        component: AdminUsersPage,
        meta: { titleKey: "routes.users", permissions: [PERMS.UsersManage] },
      },
      {
        path: "admin/tokens",
        redirect: PAGE_ADMIN_USERS,
      },
      {
        path: "admin/api",
        component: AdminApiPage,
        meta: { titleKey: "routes.api", permissions: [PERMS.ApiAccess] },
      },
      {
        path: "admin/error-logs",
        component: AdminErrorLogsPage,
        meta: { titleKey: "routes.errorLogs", permissions: [PERMS.SettingsPlatform] },
      },
      {
        path: "admin/plugins",
        component: PluginsPage,
        meta: { titleKey: "routes.plugins", permissions: [PERMS.PluginsManage] },
      },
      {
        path: "admin/ai",
        component: AiModelsPage,
        meta: { titleKey: "routes.aiModels", permissions: [PERMS.SettingsPlatform] },
      },
      {
        path: "admin/usage",
        redirect: PAGE_SETTINGS,
      },
      {
        path: "adapter-logs",
        component: AdapterLogsPage,
        meta: { titleKey: "routes.adapterLogs", permissions: [PERMS.SettingsPlatform] },
      },
    ],
  },
  { path: PAGE_SETTINGS_OPERATIONS, redirect: PAGE_SETTINGS },
  { path: "/adapter-logs", redirect: PAGE_ADAPTER_LOGS },
  { path: PAGE_NOT_FOUND, component: NotFoundPage, meta: { titleKey: "routes.notFound" } },
  {
    path: PAGE_CATALOG_CONNECT,
    component: CatalogConnectPage,
    meta: {
      titleKey: "routes.catalogConnect",
      permissions: [PERMS.MarketplaceManage],
    },
  },
  {
    path: PAGE_CATALOG_SCRAPE,
    component: CatalogScrapePage,
    meta: {
      titleKey: "routes.catalogScrape",
      permissions: [PERMS.MarketplaceManage],
    },
  },
  {
    path: PAGE_PUBLIC_API_TRY,
    component: PublicApiTryPage,
    meta: {
      titleKey: "routes.publicApiTry",
      permissions: [PERMS.ApiAccess],
    },
  },
  {
    path: PAGE_TOOL_JOB,
    component: ToolJobPage,
    meta: {
      titleKey: "routes.toolJob",
      permissions: [PERMS.MarketplaceManage],
    },
  },
  {
    path: PAGE_TOOLS,
    component: ToolsPage,
    meta: { titleKey: "routes.tools", permissions: [PERMS.OperationsManage, PERMS.CatalogManage, PERMS.MarketplaceManage, PERMS.ApiAccess] },
  },
  {
    path: PAGE_MARKETPLACE,
    component: MarketplacePage,
    meta: { titleKey: "routes.adapters", permissions: [PERMS.MarketplaceManage] },
  },
  { path: PAGE_MARKETPLACE_PLUGINS, redirect: PAGE_ADMIN_PLUGINS },
  {
    path: PAGE_MARKETPLACE_CONNECTION,
    component: MarketplaceConnectionPage,
    meta: { titleKey: "routes.connection", permissions: [PERMS.MarketplaceManage] },
  },
  {
    path: PAGE_MARKETPLACE_CATEGORY_IMPORT,
    component: MarketplaceCategoryImportPage,
    meta: { titleKey: "routes.categoryImport", permissions: [PERMS.MarketplaceManage] },
  },
  {
    path: PAGE_MARKETPLACE_FIELD_MAPPING,
    component: MarketplaceFieldMappingPage,
    meta: { titleKey: "routes.fieldMapping", permissions: [PERMS.MarketplaceManage] },
  },
  { path: "/marketplace/:connId/categories/import", redirect: to => ({ path: `/adapter/${to.params.connId}/categories/import`, query: to.query }) },
  { path: "/marketplace/:connId/categories/:mappingId", redirect: to => ({ path: `/adapter/${to.params.connId}/categories/${to.params.mappingId}`, query: to.query }) },
  { path: "/marketplace/plugins", redirect: PAGE_ADMIN_PLUGINS },
  { path: "/admin/plugins", redirect: PAGE_ADMIN_PLUGINS },
  { path: "/marketplace/:connId", redirect: to => ({ path: `/adapter/${to.params.connId}`, query: to.query }) },
  { path: "/marketplace", redirect: PAGE_MARKETPLACE },
  { path: "/admin/stores/:id", redirect: to => ({ path: `/settings/admin/stores/${to.params.id}`, query: to.query }) },
  { path: "/admin/stores", redirect: PAGE_ADMIN_STORES },
  { path: "/admin/users", redirect: PAGE_ADMIN_USERS },
  { path: "/admin/tokens", redirect: PAGE_ADMIN_USERS },
  { path: "/admin/api", redirect: PAGE_ADMIN_API },
  { path: "/admin/error-logs", redirect: PAGE_ADMIN_ERROR_LOGS },
];
routes.forEach((r) => (r.name = r.path));

/**
 * Set a dynamic page title from a page component (e.g. after loading data).
 * Persists across tab/query changes on the same route.
 * @param {string} detail - e.g. product SKU, category name
 */
export function setPageTitle(detail) {
  _customTitle = detail || null;
  syncDocumentTitle(_customTitle);
}

function resolveRouteTitle(to) {
  const key = to.meta?.titleKey;
  if (key) return t(key);
  return to.meta?.title || null;
}

function currentPageTitleDetail(route) {
  if (_customTitle) return _customTitle;
  if (route) return resolveRouteTitle(route);
  return null;
}

/** Used by main.js permission guard. */
export function routeAllowsAccess(me, to) {
  return canAccessPath(me, to.path);
}

export function routeAccessFallback(me) {
  return fallbackPathForUser(me);
}

export function createRouter() {
  const router = _createRouter({
    history: createWebHistory(),
    routes,
  });

  setPageTitleDetailProvider(() => currentPageTitleDetail(router.currentRoute.value));

  router.afterEach((to, from) => {
    if (_customTitle && to.path === from.path) {
      // Keep custom detail; still refresh brand suffix after store switch.
      syncDocumentTitle(_customTitle);
      return;
    }

    _customTitle = null;
    syncDocumentTitle(resolveRouteTitle(to));
  });

  return router;
}
