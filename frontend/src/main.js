import { createApp } from "vue";
import "./style.css";
import App from "./app.vue";
import {
  createRouter,
  routeAllowsAccess,
  routeAccessFallback,
  PAGE_PRODUCTS,
  PAGE_ADMIN_AI,
  PAGE_ADMIN_API,
  PAGE_ADMIN_ERROR_LOGS,
  PAGE_ADMIN_PLUGINS,
  PAGE_ADAPTER_LOGS,
  PAGE_UNAVAILABLE,
} from "./router";
import { createStore } from "./store";
import axios from "axios";
import { setupCache } from "axios-cache-interceptor";
import { startLoading, finishLoading } from "./composables/useLoadingBar";
import {
  configureApiAvailability,
  checkHealth,
  isApiUnavailableError,
  markOffline,
  getAvailabilityStatus,
} from "./composables/useApiAvailability";
import { toast } from "vue-sonner";
import i18n, { bootstrapLocale, getLocale, t } from "./i18n";
import { resolveApiErrorMessage } from "./store/sonnerHelper";
import {
  clearMeCache,
  getCachedMe,
  primeMeCache,
} from "./lib/meCache";
import {
  canEnterSetupWizard,
  resolveSetupNavigation,
  PAGE_SETUP,
} from "./lib/setupGate";
import { fetchSetupState } from "./lib/setupStoreCount";
import { loadBranding } from "./brand";
import "./theme";
import { resolveApiBaseUrl } from "./utils/apiBaseUrl";

export { getCachedMe, primeMeCache } from "./lib/meCache";
export { clearSetupStoreCountCache } from "./lib/setupStoreCount";

const apiBaseUrl = resolveApiBaseUrl(import.meta.env.VITE_API_SERVER);

export const http = setupCache(
  axios.create({
    baseURL: apiBaseUrl,
    adapter: axios.defaults.adapter,
    withCredentials: true,
    timeout: 30000,
    headers: {
      common: {
        "Cache-Control": "no-cache",
        "X-CSRF": "1",
      },
    },
  }),
);

http.interceptors.request.use((config) => {
  config.headers = config.headers || {};
  config.headers["X-UI-Locale"] = getLocale();
  return config;
});

// Shared refresh with promise deduplication - prevents race conditions
// when both the router guard and interceptor trigger refresh simultaneously.
let refreshPromise = null;

function refreshTokens() {
  if (refreshPromise) return refreshPromise;

  refreshPromise = axios
    .post(`${apiBaseUrl}/auth/refresh`, null, {
      withCredentials: true,
      headers: { "X-CSRF": "1" },
    })
    .finally(() => {
      refreshPromise = null;
    });

  return refreshPromise;
}

let isAuthenticated = null;
/** @type {Promise<object|null>|null} */
let mePromise = null;

function extractMePayload(data) {
  return data?.value ?? data ?? null;
}

function isAuthMeRequest(url = "") {
  return /\/auth\/me(?:\?|$)/.test(url);
}

async function loadMe() {
  const cached = getCachedMe();
  if (cached) return cached;
  if (mePromise) return mePromise;

  mePromise = http
    .get("/auth/me", { cache: false })
    .then((response) => {
      const me = extractMePayload(response.data);
      primeMeCache(me);
      return me;
    })
    .catch(() => {
      clearMeCache();
      return null;
    })
    .finally(() => {
      mePromise = null;
    });

  return mePromise;
}

/** Shared /auth/me loader with in-flight dedupe (router guard + shell). */
export { loadMe };

http.interceptors.response.use(
  (response) => {
    const url = response.config?.url || "";
    if (isAuthMeRequest(url)) {
      primeMeCache(extractMePayload(response.data));
    }
    return response;
  },
  async (error) => {
    const originalRequest = error.config;

    // Auto-refresh on 401
    if (error.response?.status === 401 && !originalRequest._retry) {
      // Auth endpoints return 401 for invalid credentials - don't attempt refresh
      if (/\/auth\/(signIn|signUp|refresh)/.test(originalRequest.url || "")) {
        return Promise.reject(error);
      }

      originalRequest._retry = true;

      try {
        await refreshTokens();
        return http(originalRequest);
      } catch (refreshError) {
        // Retry once after a short delay - another tab may have just rotated the token
        if (!originalRequest._retryAfterRefresh) {
          originalRequest._retryAfterRefresh = true;
          await new Promise((r) => setTimeout(r, 500));
          try {
            await refreshTokens();
            return http(originalRequest);
          } catch {
            // fall through to redirect
          }
        }
        resetAuthCache();
        const currentPath = router.currentRoute.value.path;
        const redirect =
          currentPath.startsWith("/signIn") || currentPath.startsWith("/signUp")
            ? undefined
            : currentPath;
        router.push({ path: "/signIn", query: redirect ? { redirect } : {} });
        return Promise.reject(refreshError);
      }
    }

    if (error.response?.status === 409) {
      toast.error(resolveApiErrorMessage(error.response.data, "errors.resourceLimit"));
    }

    if (error.response?.status === 429) {
      const code = error.response?.data?.code;
      toast.warning(
        code === "account_locked"
          ? t("errors.account_locked")
          : t("errors.rateLimited"),
      );
    }

    // Backend down / proxy gateway errors (deploy, restart) → dedicated page.
    if (isApiUnavailableError(error)) {
      const url = originalRequest?.url || "";
      if (!/\/health(?:\?|$)/.test(url)) {
        const currentPath = router.currentRoute.value.fullPath;
        markOffline(currentPath);
      }
    }

    return Promise.reject(error);
  },
);

export const router = createRouter();
export const store = createStore();

configureApiAvailability({
  getRouter: () => router,
  onRecovered: () => {
    resetAuthCache();
  },
});

router.beforeEach(async (to) => {
  startLoading();

  if (to.path === PAGE_UNAVAILABLE) return true;

  const availability = getAvailabilityStatus();
  if (availability === "offline") {
    return {
      path: PAGE_UNAVAILABLE,
      query: { from: to.fullPath },
    };
  }

  // First navigation (or after a full reload): probe before auth / shell.
  if (availability === "unknown") {
    const ok = await checkHealth();
    if (!ok) {
      return {
        path: PAGE_UNAVAILABLE,
        query: { from: to.fullPath },
      };
    }
  }

  if (to.meta.public) return true;

  if (isAuthenticated === null) {
    try {
      const response = await http.get("/auth/isAuthenticated", {
        cache: false,
      });
      isAuthenticated = response.data?.isAuthenticated === true;
    } catch (error) {
      if (isApiUnavailableError(error)) {
        markOffline(to.fullPath);
        return {
          path: PAGE_UNAVAILABLE,
          query: { from: to.fullPath },
        };
      }
      isAuthenticated = false;
    }
  }

  if (!isAuthenticated) {
    // Try refresh before redirecting to signIn
    try {
      await refreshTokens();
      isAuthenticated = true;
    } catch (error) {
      if (isApiUnavailableError(error)) {
        markOffline(to.fullPath);
        return {
          path: PAGE_UNAVAILABLE,
          query: { from: to.fullPath },
        };
      }
      isAuthenticated = null;
      const redirect =
        to.path.startsWith("/signIn") || to.path.startsWith("/signUp")
          ? undefined
          : to.path;
      return { path: "/signIn", query: redirect ? { redirect } : {} };
    }
  }

  // Sync-only: never await /auth/me here - that blocked every permissioned route.
  // Sidebar already hides unauthorized links; API still enforces permissions.
  const needsPermissionCheck = to.matched.some(
    (r) => Array.isArray(r.meta?.permissions) && r.meta.permissions.length > 0,
  );
  if (needsPermissionCheck) {
    const me = getCachedMe();
    if (me && !routeAllowsAccess(me, to)) {
      const fallback = routeAccessFallback(me);
      if (fallback !== to.path) return { path: fallback };
      return { path: PAGE_PRODUCTS };
    }
  }

  // AI settings requires instance AI_ENABLED (from /auth/me).
  if (to.path === PAGE_ADMIN_AI || to.path.startsWith(`${PAGE_ADMIN_AI}/`)) {
    const me = getCachedMe() || (await loadMe());
    if (me?.aiEnabled !== true) {
      return { path: routeAccessFallback(me) || PAGE_PRODUCTS };
    }
  }

  // Plugin management requires instance PLUGINS_ENABLED (from /auth/me).
  if (to.path === PAGE_ADMIN_PLUGINS || to.path.startsWith(`${PAGE_ADMIN_PLUGINS}/`)) {
    const me = getCachedMe() || (await loadMe());
    if (me?.pluginsEnabled !== true) {
      return { path: routeAccessFallback(me) || PAGE_PRODUCTS };
    }
  }

  // Log pages require the corresponding admin logging toggle.
  if (to.path === PAGE_ADMIN_ERROR_LOGS || to.path.startsWith(`${PAGE_ADMIN_ERROR_LOGS}/`)) {
    const me = getCachedMe() || (await loadMe());
    if (me?.logErrorsEnabled !== true) {
      return { path: routeAccessFallback(me) || PAGE_PRODUCTS };
    }
  }
  if (to.path === PAGE_ADAPTER_LOGS || to.path.startsWith(`${PAGE_ADAPTER_LOGS}/`)) {
    const me = getCachedMe() || (await loadMe());
    if (me?.logAdapterHttpEnabled !== true) {
      return { path: routeAccessFallback(me) || PAGE_PRODUCTS };
    }
  }

  // API keys / Swagger page requires the Public API instance toggle.
  if (to.path === PAGE_ADMIN_API || to.path.startsWith(`${PAGE_ADMIN_API}/`)) {
    const me = getCachedMe() || (await loadMe());
    if (me?.publicApiEnabled === false) {
      return { path: routeAccessFallback(me) || PAGE_PRODUCTS };
    }
  }

  // First-run wizard: admin (or stores.create) with zero stores → /setup.
  {
    const me = getCachedMe() || (await loadMe());
    const needsSetupCheck =
      to.path === PAGE_SETUP ||
      to.path.startsWith(`${PAGE_SETUP}/`) ||
      canEnterSetupWizard(me);
    if (needsSetupCheck) {
      const setupState = await fetchSetupState({
        force: to.path === PAGE_SETUP,
      });
      const decision = resolveSetupNavigation({
        path: to.path,
        me,
        storeCount: setupState.count,
        onboardingIncomplete: setupState.onboardingIncomplete,
      });
      if (decision?.redirect && decision.redirect !== to.path) {
        return { path: decision.redirect };
      }
    }
  }

  return true;
});

router.afterEach(() => finishLoading());
router.onError(() => finishLoading());

export function resetAuthCache() {
  isAuthenticated = null;
  clearMeCache();
  mePromise = null;
}

export function invalidateMeCache() {
  clearMeCache();
  mePromise = null;
}

// Proactive token refresh every 8 minutes - prevents 401s during active use.
// Fires only when the tab is visible; pauses silently when hidden.
const REFRESH_INTERVAL_MS = 8 * 60 * 1000;
setInterval(() => {
  if (document.visibilityState === "visible") {
    refreshTokens().catch(() => {});
  }
}, REFRESH_INTERVAL_MS);

const app = createApp(App);

app.use(i18n);
app.use(router);
app.use(store);

Promise.all([router.isReady(), bootstrapLocale(), loadBranding(http)]).then(() => {
  app.mount("#app");
  // Warm /auth/me after mount so the first permissioned nav is sync (skip public auth pages).
  if (!router.currentRoute.value.meta?.public) {
    loadMe();
  }
});
