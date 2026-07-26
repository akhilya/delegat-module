import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

/** Deduplicate concurrent picker fetches (e.g. many OperationItem mounts). */
let pickerInFlight = null;
let pickerFetchedAt = 0;
const PICKER_TTL_MS = 60_000;

export default {
  namespaced: true,

  state: () => ({
    models: [],
    pickerModels: [],
  }),

  mutations: {
    setModels(state, models) {
      state.models = models;
    },
    setPickerModels(state, models) {
      state.pickerModels = models;
    },
  },

  actions: {
    async getAiModels(ctx, { onSuccess, onError }) {
      // cache: false - the axios instance caches GETs, which made the list stale after create/edit
      return await useAxios("/ai/models", { method: "GET", cache: false }, http, { immediate: true })
        .then((r) => { ctx.commit("setModels", r.data.value ?? []); onSuccess(r); })
        .catch((e) => onError(e));
    },

    async getPickerModels(ctx, { onSuccess, onError, force } = {}) {
      const fresh =
        !force &&
        ctx.state.pickerModels.length > 0 &&
        Date.now() - pickerFetchedAt < PICKER_TTL_MS;

      if (fresh) {
        onSuccess?.({ data: { value: ctx.state.pickerModels } });
        return ctx.state.pickerModels;
      }

      if (pickerInFlight) {
        try {
          const models = await pickerInFlight;
          onSuccess?.({ data: { value: models } });
          return models;
        } catch (e) {
          onError?.(e);
          throw e;
        }
      }

      pickerInFlight = useAxios("/ai/models/picker", { method: "GET", cache: false }, http, {
        immediate: true,
      })
        .then((r) => {
          const models = r.data.value ?? [];
          ctx.commit("setPickerModels", models);
          pickerFetchedAt = Date.now();
          onSuccess?.(r);
          return models;
        })
        .catch((e) => {
          onError?.(e);
          throw e;
        })
        .finally(() => {
          pickerInFlight = null;
        });

      return await pickerInFlight;
    },

    async discoverModels(ctx, { data, onSuccess, onError }) {
      return await useAxios("/ai/models/discover", { method: "POST", data }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async createAiModel(ctx, { data, onSuccess, onError }) {
      return await useAxios("/ai/models", { method: "POST", data }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async updateAiModel(ctx, { id, data, onSuccess, onError }) {
      return await useAxios(`/ai/models/${id}`, { method: "PUT", data }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async deleteAiModel(ctx, { id, onSuccess, onError }) {
      return await useAxios(`/ai/models/${id}`, { method: "DELETE" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async setDefaultAiModel(ctx, { id, onSuccess, onError }) {
      return await useAxios(`/ai/models/${id}/set-default`, { method: "POST" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async toggleAiModelEnabled(ctx, { id }) {
      return await useAxios(`/ai/models/${id}/toggle-enabled`, { method: "POST" }, http, { immediate: true });
    },

    async testAiModel(ctx, { id, onSuccess, onError }) {
      return await useAxios(`/ai/models/${id}/test`, { method: "POST" }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async getStoreSettings(ctx, { onSuccess, onError }) {
      return await useAxios("/ai/store-settings", { method: "GET", cache: false }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },

    async saveStoreSettings(ctx, { data, onSuccess, onError }) {
      return await useAxios("/ai/store-settings", { method: "PUT", data }, http, { immediate: true })
        .then((r) => onSuccess(r))
        .catch((e) => onError(e));
    },
  },
};
