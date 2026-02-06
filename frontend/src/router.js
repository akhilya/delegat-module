import { createWebHistory, createRouter as _createRouter } from "vue-router";

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
const NotFoundPage = () => import("./pages/notFoundPage.vue");
const ToolsPage = () => import("./pages/toolsPage.vue");
const AdminStoresPage = () => import("./pages/admin/storesPage.vue");
const AdminStoreDetailPage = () => import("./pages/admin/storeDetailPage.vue");
const AdminTokensPage = () => import("./pages/admin/tokensPage.vue");
const MarketplacePage = () => import("./pages/marketplace/marketplacePage.vue");
const MarketplaceConnectionPage = () => import("./pages/marketplace/connectionPage.vue");
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
export const PAGE_NOT_FOUND = "/:pathMatch(.*)*";
export const PAGE_TOOLS = "/tools";
export const PAGE_ADMIN_STORES = "/admin/stores";
export const PAGE_ADMIN_STORE_DETAIL = "/admin/stores/:id";
export const PAGE_ADMIN_TOKENS = "/admin/tokens";
export const PAGE_MARKETPLACE = "/marketplace";
export const PAGE_MARKETPLACE_PLUGINS = "/marketplace/plugins";
export const PAGE_MARKETPLACE_CONNECTION = "/marketplace/:connId";
export const PAGE_MARKETPLACE_FIELD_MAPPING = "/marketplace/:connId/categories/:mappingId";

export const routes = [
  { path: PAGE_SIGN_UP, component: signUpPage },
  { path: PAGE_SIGN_IN, component: signInPage },
  { path: "/", component: productsPage },
  { path: PAGE_PRODUCTS, component: productsPage },
  { path: PAGE_PRODUCT_CREATION, component: productCreationPage },
  { path: PAGE_PRODUCT_EDIT, component: productEditPage },
  { path: PAGE_CATEGORIES, component: categoriesPage },
  { path: PAGE_CATEGORY_EDIT, component: categoryEditPage },
  { path: PAGE_PRODUCT_GROUPS, component: productGroupsPage },
  { path: PAGE_PRODUCT_GROUP_CREATE, component: productGroupEditPage },
  { path: PAGE_PRODUCT_GROUP_EDIT, component: productGroupEditPage },
  { path: PAGE_STORE_ATTRIBUTES, component: storeAttributesPage },
  { path: PAGE_ADMIN_STORES, component: AdminStoresPage },
  { path: PAGE_ADMIN_STORE_DETAIL, component: AdminStoreDetailPage },
  { path: PAGE_ADMIN_TOKENS, component: AdminTokensPage },
  { path: PAGE_NOT_FOUND, component: NotFoundPage },
  { path: PAGE_TOOLS, component: ToolsPage },
  { path: PAGE_MARKETPLACE, component: MarketplacePage },
  { path: PAGE_MARKETPLACE_PLUGINS, component: PluginsPage },
  { path: PAGE_MARKETPLACE_CONNECTION, component: MarketplaceConnectionPage },
  { path: PAGE_MARKETPLACE_FIELD_MAPPING, component: MarketplaceFieldMappingPage },
];
routes.forEach((r) => (r.name = r.path));

export function createRouter() {
  return _createRouter({
    history: createWebHistory(),
    routes,
  });
}
