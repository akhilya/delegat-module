import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    async getErrorLogs(ctx, { limit = 100, onSuccess, onError }) {
      return await useAxios(
        `/error-logs?limit=${limit}`,
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
  },
  mutations: {},
  state: {},
  getters: {},
};
