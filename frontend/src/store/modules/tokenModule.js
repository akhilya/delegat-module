import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    /**
     * Fetches all invitation tokens.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
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

    /**
     * Creates a new invitation token.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
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

    /**
     * Revokes (deletes) an unused invitation token.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {number} params.tokenId - Token id to revoke.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async revokeToken(ctx, { tokenId, onSuccess, onError }) {
      return await useAxios(
        `/tokens/${tokenId}`,
        {
          method: "DELETE",
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
