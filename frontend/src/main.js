import { createApp } from "vue";
import "./style.css";
import App from "./app.vue";
import {
  createRouter,
  routeAllowsAccess,
  routeAccessFallback,
  PAGE_PRODUCTS,
} from "./router";
import { createStore } from "./store";
import axios from "axios";
import { setupCache } from "axios-cache-interceptor";
import { startLoading, finishLoading } from "./composables/useLoadingBar";
import { toast } from "vue-sonner";
import i18n, { getLocale, t } from "./i18n";

export const http = setupCache(
  axios.create({
    baseURL: import.meta.env.VITE_API_SERVER,
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
    .post(`${import.meta.env.VITE_API_SERVER}/auth/refresh`, null, {
      withCredentials: true,
      headers: { "X-CSRF": "1" },
    })
    .finally(() => {
      refreshPromise = null;
    });

  return refreshPromise;
}

let isAuthenticated = null;
/** @type {object|null} */
let cachedMe = null;
/** @type {Promise<object|null>|null} */
let mePromise = null;

function extractMePayload(data) {
  return data?.value ?? data ?? null;
}

function isAuthMeRequest(url = "") {
  return /\/auth\/me(?:\?|$)/.test(url);
}

/**
 * Keep router permission checks sync after any /auth/me (sidebar, settings, app shell).
 * @param {object|null|undefined} me
 */
export function primeMeCache(me) {
  if (me && typeof me === "object") cachedMe = me;
}

async function loadMe() {
  if (cachedMe) return cachedMe;
  if (mePromise) return mePromise;

  mePromise = http
    .get("/auth/me", { cache: false })
    .then((response) => {
      cachedMe = extractMePayload(response.data);
      return cachedMe;
    })
    .catch(() => {
      cachedMe = null;
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
      const code = error.response.data?.code;
      toast.error(
        (code && t(`errors.${code}`)) ||
          error.response.data?.title ||
          t("errors.resourceLimit"),
      );
    }

    if (error.response?.status === 429) {
      toast.warning(t("errors.rateLimited"));
    }

    return Promise.reject(error);
  },
);

export const router = createRouter();
export const store = createStore();

router.beforeEach(async (to) => {
  startLoading();
  if (to.meta.public) return true;

  if (isAuthenticated === null) {
    try {
      const response = await http.get("/auth/isAuthenticated", {
        cache: false,
      });
      isAuthenticated = response.data?.isAuthenticated === true;
    } catch {
      isAuthenticated = false;
    }
  }

  if (!isAuthenticated) {
    // Try refresh before redirecting to signIn
    try {
      await refreshTokens();
      isAuthenticated = true;
    } catch {
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
  if (needsPermissionCheck && cachedMe && !routeAllowsAccess(cachedMe, to)) {
    const fallback = routeAccessFallback(cachedMe);
    if (fallback !== to.path) return { path: fallback };
    return { path: PAGE_PRODUCTS };
  }

  return true;
});

router.afterEach(() => finishLoading());
router.onError(() => finishLoading());

export function resetAuthCache() {
  isAuthenticated = null;
  cachedMe = null;
  mePromise = null;
}

export function invalidateMeCache() {
  cachedMe = null;
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

router.isReady().then(() => {
  app.mount("#app");
  // Warm /auth/me after mount so the first permissioned nav is sync (skip public auth pages).
  if (!router.currentRoute.value.meta?.public) {
    loadMe();
  }
});
