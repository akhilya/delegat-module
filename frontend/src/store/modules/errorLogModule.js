import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    async getErrorLogs(ctx, { limit = 100, sortBy = "createdAt", sortDir = "desc", onSuccess, onError }) {
      const q = new URLSearchParams();
      q.set("limit", String(limit));
      q.set("sortBy", sortBy);
      q.set("sortDir", sortDir);
      return await useAxios(
        `/error-logs?${q}`,
        { method: "GET", cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async deleteErrorLog(ctx, { id, onSuccess, onError }) {
      try {
        const response = await http.delete(`/error-logs/${id}`, { cache: false });
        onSuccess(response);
      } catch (error) {
        onError(error);
      }
    },

    async deleteErrorLogs(ctx, { ids, onSuccess, onError }) {
      try {
        const response = await http.post(
          "/error-logs/delete",
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
