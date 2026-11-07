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
    async getAdapterHttpLogs(ctx, { params = {}, signal, onSuccess, onError }) {
      try {
        const response = await http.get(
          `/adapter-http-logs${buildQuery(params)}`,
          { cache: false, signal },
        );
        onSuccess?.(response);
      } catch (error) {
        onError?.(error);
      }
    },

    async getAdapterHttpLogDetail(ctx, { id, signal, onSuccess, onError }) {
      try {
        const response = await http.get(`/adapter-http-logs/${id}`, {
          cache: false,
          signal,
        });
        onSuccess?.(response);
      } catch (error) {
        onError?.(error);
      }
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
