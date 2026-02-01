import { createWebHistory, createRouter as _createRouter } from "vue-router";

import signUpPage from "./pages/signUpPage.vue";
import signInPage from "./pages/signInPage.vue";
import productsPage from "./pages/productsPage.vue";
import productCreationPage from "./pages/productCreationPage.vue";
import productEditPage from "./pages/productEditPage.vue";
import NotFoundPage from "./pages/notFoundPage.vue";
import ToolsPage from "./pages/toolsPage.vue";
import AdminStoresPage from "./pages/admin/storesPage.vue";
import AdminStoreDetailPage from "./pages/admin/storeDetailPage.vue";
import AdminTokensPage from "./pages/admin/tokensPage.vue";

export const PAGE_SIGN_UP = "/signUp";
export const PAGE_SIGN_IN = "/signIn";
export const PAGE_PRODUCTS = "/products";
export const PAGE_PRODUCT_CREATION = "/products/create";
export const PAGE_PRODUCT_EDIT = "/products/:id/edit";
export const PAGE_NOT_FOUND = "/:pathMatch(.*)*";
export const PAGE_TOOLS = "/tools";
export const PAGE_ADMIN_STORES = "/admin/stores";
export const PAGE_ADMIN_STORE_DETAIL = "/admin/stores/:id";
export const PAGE_ADMIN_TOKENS = "/admin/tokens";

export const routes = [
  { path: PAGE_SIGN_UP, component: signUpPage },
  { path: PAGE_SIGN_IN, component: signInPage },
  { path: "/", component: productsPage },
  { path: PAGE_PRODUCTS, component: productsPage },
  { path: PAGE_PRODUCT_CREATION, component: productCreationPage },
  { path: PAGE_PRODUCT_EDIT, component: productEditPage },
  { path: PAGE_ADMIN_STORES, component: AdminStoresPage },
  { path: PAGE_ADMIN_STORE_DETAIL, component: AdminStoreDetailPage },
  { path: PAGE_ADMIN_TOKENS, component: AdminTokensPage },
  { path: PAGE_NOT_FOUND, component: NotFoundPage },
  { path: PAGE_TOOLS, component: ToolsPage },
];
routes.forEach((r) => (r.name = r.path));

export function createRouter() {
  return _createRouter({
    history: createWebHistory(),
    routes,
  });
}
