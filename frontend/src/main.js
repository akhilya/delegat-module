import { createApp } from "vue";
import "./style.css";
import App from "./app.vue";
import { createRouter } from "./router";
import { createStore } from "./store";
import axios from "axios";
import { setupCache } from "axios-cache-interceptor";
import { startLoading, finishLoading } from "./composables/useLoadingBar";
import { toast } from "vue-sonner";

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

// Shared refresh with promise deduplication — prevents race conditions
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

http.interceptors.response.use(undefined, async (error) => {
  const originalRequest = error.config;

  // Auto-refresh on 401
  if (error.response?.status === 401 && !originalRequest._retry) {
    // Auth endpoints return 401 for invalid credentials — don't attempt refresh
    if (/\/auth\/(signIn|signUp|refresh)/.test(originalRequest.url || '')) {
      return Promise.reject(error);
    }

    originalRequest._retry = true;

    try {
      await refreshTokens();
      return http(originalRequest);
    } catch (refreshError) {
      // Retry once after a short delay — another tab may have just rotated the token
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
      const redirect = currentPath.startsWith("/signIn") || currentPath.startsWith("/signUp")
        ? undefined
        : currentPath;
      router.push({ path: "/signIn", query: redirect ? { redirect } : {} });
      return Promise.reject(refreshError);
    }
  }

  if (error.response?.status === 409) {
    toast.error(
      error.response.data?.title || "Достигнут лимит ресурсов",
    );
  }

  if (error.response?.status === 429) {
    toast.warning(
      "Слишком много запросов. Подождите немного и попробуйте снова.",
    );
  }

  return Promise.reject(error);
});

export const router = createRouter();
export const store = createStore();

let isAuthenticated = null;

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
      return true;
    } catch {
      isAuthenticated = null;
      const redirect = to.path.startsWith("/signIn") || to.path.startsWith("/signUp")
        ? undefined
        : to.path;
      return { path: "/signIn", query: redirect ? { redirect } : {} };
    }
  }

  return true;
});

router.afterEach(() => finishLoading());
router.onError(() => finishLoading());

export function resetAuthCache() {
  isAuthenticated = null;
}

const app = createApp(App);

app.use(router);
app.use(store);

router.isReady().then(() => app.mount("#app"));
