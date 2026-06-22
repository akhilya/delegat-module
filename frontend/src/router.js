import { createWebHistory, createRouter as _createRouter } from "vue-router";
import { BRAND_NAME } from "@/brand";

const signUpPage = () => import("./pages/signUpPage.vue");
const signInPage = () => import("./pages/signInPage.vue");
const productsPage = () => import("./pages/productsPage.vue");
const productCreationPage = () => import("./pages/productCreationPage.vue");
const productEditPage = () => import("./pages/productEditPage.vue");
const categoriesPage = () => import("./pages/categoriesPage.vue");
const categoryEditPage = () => import("./pages/categoryEditPage.vue");
const productGroupsPage = () => import("./pages/productGroupsPage.vue");
const productGroupEditPage = () => import("./pages/productGroupEditPage.vue");
const storeAttributesPage = () => import("./pages/storeAttributesPage.vue");
const storeStoragePage = () => import("./pages/storeStoragePage.vue");
const SettingsOverviewPage = () => import("./pages/settings/settingsOverviewPage.vue");
const SettingsUserPage = () => import("./pages/settings/settingsUserPage.vue");
const SettingsStorePage = () => import("./pages/settings/settingsStorePage.vue");
const NotFoundPage = () => import("./pages/notFoundPage.vue");
const ToolsPage = () => import("./pages/toolsPage.vue");
const AdminStoresPage = () => import("./pages/admin/storesPage.vue");
const AdminStoreDetailPage = () => import("./pages/admin/storeDetailPage.vue");
const AdminUsersPage = () => import("./pages/admin/usersPage.vue");
const AdminTokensPage = () => import("./pages/admin/tokensPage.vue");
const AdminErrorLogsPage = () => import("./pages/admin/errorLogsPage.vue");
const MarketplacePage = () => import("./pages/marketplace/marketplacePage.vue");
const MarketplaceConnectionPage = () => import("./pages/marketplace/connectionPage.vue");
const MarketplaceCategoryImportPage = () => import("./pages/marketplace/categoryImportPage.vue");
const MarketplaceFieldMappingPage = () => import("./pages/marketplace/fieldMappingPage.vue");
const PluginsPage = () => import("./pages/marketplace/pluginsPage.vue");

export const PAGE_SIGN_UP = "/signUp";
export const PAGE_SIGN_IN = "/signIn";
export const PAGE_PRODUCTS = "/products";
export const PAGE_PRODUCT_CREATION = "/products/create";
export const PAGE_PRODUCT_EDIT = "/products/:id/edit";
export const PAGE_CATEGORIES = "/categories";
export const PAGE_CATEGORY_EDIT = "/categories/:id/edit";
export const PAGE_PRODUCT_GROUPS = "/product-groups";
export const PAGE_PRODUCT_GROUP_CREATE = "/product-groups/create";
export const PAGE_PRODUCT_GROUP_EDIT = "/product-groups/:id/edit";
export const PAGE_STORE_ATTRIBUTES = "/store-attributes";
export const PAGE_STORE_STORAGE = "/storage";
export const PAGE_SETTINGS = "/settings";
export const PAGE_SETTINGS_OPERATIONS = "/settings/operations";
export const PAGE_SETTINGS_USER = "/settings/user";
export const PAGE_SETTINGS_STORE = "/settings/store";
export const PAGE_NOT_FOUND = "/:pathMatch(.*)*";
export const PAGE_TOOLS = "/tools";
export const PAGE_ADMIN_STORES = "/settings/admin/stores";
export const PAGE_ADMIN_STORE_DETAIL = "/settings/admin/stores/:id";
export const PAGE_ADMIN_USERS = "/settings/admin/users";
export const PAGE_ADMIN_TOKENS = "/settings/admin/tokens";
export const PAGE_ADMIN_ERROR_LOGS = "/settings/admin/error-logs";
export const PAGE_ADMIN_PLUGINS = "/settings/admin/plugins";
export const PAGE_MARKETPLACE = "/adapter";
export const PAGE_MARKETPLACE_PLUGINS = "/adapter/plugins";
export const PAGE_MARKETPLACE_CONNECTION = "/adapter/:connId";
export const PAGE_MARKETPLACE_CATEGORY_IMPORT = "/adapter/:connId/categories/import";
export const PAGE_MARKETPLACE_FIELD_MAPPING = "/adapter/:connId/categories/:mappingId";

// Track whether a page component set a custom title (prevents afterEach from overwriting it on tab/query changes)
let _customTitle = null;

export const routes = [
  { path: PAGE_SIGN_UP, component: signUpPage, meta: { public: true, title: "Регистрация" } },
  { path: PAGE_SIGN_IN, component: signInPage, meta: { public: true, title: "Вход" } },
  { path: "/", component: productsPage, meta: { title: "Товары" } },
  { path: PAGE_PRODUCTS, component: productsPage, meta: { title: "Товары" } },
  { path: PAGE_PRODUCT_CREATION, component: productCreationPage, meta: { title: "Новый товар" } },
  { path: PAGE_PRODUCT_EDIT, component: productEditPage, meta: { title: "Товар", stableKey: "product-edit" } },
  { path: PAGE_CATEGORIES, component: categoriesPage, meta: { title: "Категории" } },
  { path: PAGE_CATEGORY_EDIT, component: categoryEditPage, meta: { title: "Категория" } },
  { path: PAGE_PRODUCT_GROUPS, component: productGroupsPage, meta: { title: "Группы товаров" } },
  { path: PAGE_PRODUCT_GROUP_CREATE, component: productGroupEditPage, meta: { title: "Новая группа" } },
  { path: PAGE_PRODUCT_GROUP_EDIT, component: productGroupEditPage, meta: { title: "Группа товаров" } },
  { path: PAGE_STORE_ATTRIBUTES, component: storeAttributesPage, meta: { title: "Глобальные атрибуты" } },
  { path: PAGE_STORE_STORAGE, component: storeStoragePage, meta: { title: "Хранилище" } },
  { path: PAGE_SETTINGS, component: SettingsOverviewPage, meta: { title: "Общие настройки" } },
  { path: PAGE_SETTINGS_OPERATIONS, redirect: PAGE_SETTINGS },
  { path: PAGE_SETTINGS_USER, component: SettingsUserPage, meta: { title: "Настройки пользователя" } },
  { path: PAGE_SETTINGS_STORE, component: SettingsStorePage, meta: { title: "Настройки магазина" } },
  { path: PAGE_ADMIN_STORES, component: AdminStoresPage, meta: { title: "Магазины" } },
  { path: PAGE_ADMIN_STORE_DETAIL, component: AdminStoreDetailPage, meta: { title: "Магазин" } },
  { path: PAGE_ADMIN_USERS, component: AdminUsersPage, meta: { title: "Пользователи" } },
  { path: PAGE_ADMIN_TOKENS, component: AdminTokensPage, meta: { title: "Токены" } },
  { path: PAGE_ADMIN_ERROR_LOGS, component: AdminErrorLogsPage, meta: { title: "Логи ошибок" } },
  { path: PAGE_ADMIN_PLUGINS, component: PluginsPage, meta: { title: "Плагины" } },
  { path: PAGE_NOT_FOUND, component: NotFoundPage, meta: { title: "Не найдено" } },
  { path: PAGE_TOOLS, component: ToolsPage, meta: { title: "Инструменты" } },
  { path: PAGE_MARKETPLACE, component: MarketplacePage, meta: { title: "Адаптеры" } },
  { path: PAGE_MARKETPLACE_PLUGINS, redirect: PAGE_ADMIN_PLUGINS },
  { path: PAGE_MARKETPLACE_CONNECTION, component: MarketplaceConnectionPage, meta: { title: "Подключение" } },
  { path: PAGE_MARKETPLACE_CATEGORY_IMPORT, component: MarketplaceCategoryImportPage, meta: { title: "Импорт категории" } },
  { path: PAGE_MARKETPLACE_FIELD_MAPPING, component: MarketplaceFieldMappingPage, meta: { title: "Маппинг полей" } },
  { path: "/marketplace/:connId/categories/import", redirect: to => ({ path: `/adapter/${to.params.connId}/categories/import`, query: to.query }) },
  { path: "/marketplace/:connId/categories/:mappingId", redirect: to => ({ path: `/adapter/${to.params.connId}/categories/${to.params.mappingId}`, query: to.query }) },
  { path: "/marketplace/plugins", redirect: PAGE_ADMIN_PLUGINS },
  { path: "/admin/plugins", redirect: PAGE_ADMIN_PLUGINS },
  { path: "/marketplace/:connId", redirect: to => ({ path: `/adapter/${to.params.connId}`, query: to.query }) },
  { path: "/marketplace", redirect: PAGE_MARKETPLACE },
  { path: "/admin/stores/:id", redirect: to => ({ path: `/settings/admin/stores/${to.params.id}`, query: to.query }) },
  { path: "/admin/stores", redirect: PAGE_ADMIN_STORES },
  { path: "/admin/users", redirect: PAGE_ADMIN_USERS },
  { path: "/admin/tokens", redirect: PAGE_ADMIN_TOKENS },
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
  document.title = detail ? `${detail} — ${BRAND_NAME}` : BRAND_NAME;
}

export function createRouter() {
  const router = _createRouter({
    history: createWebHistory(),
    routes,
  });

  router.afterEach((to, from) => {
    // If navigating within the same route path (e.g. tab/query change),
    // keep the custom title set by the page component
    if (_customTitle && to.path === from.path) return;

    // New route — reset custom title and apply route meta
    _customTitle = null;
    const title = to.meta?.title;
    document.title = title ? `${title} — ${BRAND_NAME}` : BRAND_NAME;
  });

  return router;
}
