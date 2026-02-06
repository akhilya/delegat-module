import { createApp } from "vue";
import "./style.css";
import App from "./app.vue";
import { createRouter } from "./router";
import { createStore } from "./store";
import axios from "axios";
import { setupCache } from "axios-cache-interceptor";
import { startLoading, finishLoading } from "./composables/useLoadingBar";

export const http = setupCache(
  axios.create({
    baseURL: import.meta.env.VITE_API_SERVER,
    adapter: axios.defaults.adapter,
    withCredentials: true,
    timeout: 5000,
    headers: {
      common: {
        "Cache-Control": "no-cache",
      },
    },
  }),
);

export const router = createRouter();
export const store = createStore();

let isAuthenticated = null;

router.beforeEach(async (to) => {
  startLoading();
  if (to.meta.public) return true;

  if (isAuthenticated === null) {
    try {
      const response = await http.get("/auth/isAuthenticated", { cache: false });
      isAuthenticated = response.data?.isAuthenticated === true;
    } catch {
      isAuthenticated = false;
    }
  }

  if (!isAuthenticated) {
    isAuthenticated = null;
    return { path: "/signIn" };
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
