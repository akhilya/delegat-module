import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    async getReferenceCurrencies(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/reference/currencies`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async getReferenceTimezones(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/reference/timezones`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async getReferencePlugins(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/reference/plugins`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async getReferenceStatus(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/reference/status`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },
  },
};
