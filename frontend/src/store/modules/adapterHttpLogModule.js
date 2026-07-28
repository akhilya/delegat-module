import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

function buildQuery(params = {}) {
  const q = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v === undefined || v === null || v === "") return;
    q.set(k, String(v));
  });
  const s = q.toString();
  return s ? `?${s}` : "";
}

export default {
  actions: {
    async getAdapterHttpLogs(ctx, { params = {}, onSuccess, onError }) {
      return await useAxios(
        `/adapter-http-logs${buildQuery(params)}`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess?.(response))
        .catch((error) => onError?.(error));
    },

    async getAdapterHttpLogDetail(ctx, { id, onSuccess, onError }) {
      return await useAxios(
        `/adapter-http-logs/${id}`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess?.(response))
        .catch((error) => onError?.(error));
    },

    async deleteAdapterHttpLogs(ctx, { ids, onSuccess, onError }) {
      try {
        const response = await http.post(
          "/adapter-http-logs/delete",
          { ids },
          { cache: false },
        );
        onSuccess?.(response);
      } catch (error) {
        onError?.(error);
      }
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
