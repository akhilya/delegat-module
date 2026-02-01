import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    async getAllTokens(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/tokens/`,
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
    async createToken(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/tokens/`,
        {
          method: "POST",
          cache: false,
        },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
