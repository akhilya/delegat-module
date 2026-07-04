import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  namespaced: true,
  actions: {
    async getAiModels(ctx, { onSuccess, onError }) {
      return await useAxios("/api/ai/models", { method: "GET" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async createAiModel(ctx, { data, onSuccess, onError }) {
      return await useAxios("/api/ai/models", { method: "POST", data }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async updateAiModel(ctx, { id, data, onSuccess, onError }) {
      return await useAxios(`/api/ai/models/${id}`, { method: "PUT", data }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async deleteAiModel(ctx, { id, onSuccess, onError }) {
      return await useAxios(`/api/ai/models/${id}`, { method: "DELETE" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async setDefaultAiModel(ctx, { id, onSuccess, onError }) {
      return await useAxios(`/api/ai/models/${id}/set-default`, { method: "POST" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async testAiModel(ctx, { id, onSuccess, onError }) {
      return await useAxios(`/api/ai/models/${id}/test`, { method: "POST" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },
  },
};
