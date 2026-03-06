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
    timeout: 5000,
    headers: {
      common: {
        "Cache-Control": "no-cache",
        "X-CSRF": "1",
      },
    },
  }),
);

// Token refresh state
let isRefreshing = false;
let failedQueue = [];

function processQueue(error) {
  failedQueue.forEach((p) => {
    if (error) p.reject(error);
    else p.resolve();
  });
  failedQueue = [];
}

http.interceptors.response.use(undefined, async (error) => {
  const originalRequest = error.config;

  // Auto-refresh on 401
  if (error.response?.status === 401 && !originalRequest._retry) {
    if (isRefreshing) {
      return new Promise((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      }).then(() => http(originalRequest));
    }

    originalRequest._retry = true;
    isRefreshing = true;

    try {
      await axios.post(
        `${import.meta.env.VITE_API_SERVER}/auth/refresh`,
        null,
        { withCredentials: true, headers: { "X-CSRF": "1" } },
      );
      processQueue(null);
      return http(originalRequest);
    } catch (refreshError) {
      processQueue(refreshError);
      resetAuthCache();
      router.push("/signIn");
      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
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
      await axios.post(
        `${import.meta.env.VITE_API_SERVER}/auth/refresh`,
        null,
        { withCredentials: true, headers: { "X-CSRF": "1" } },
      );
      isAuthenticated = true;
      return true;
    } catch {
      isAuthenticated = null;
      return { path: "/signIn" };
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
