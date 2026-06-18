import { useAxios } from "@vueuse/integrations/useAxios";
import { http, router } from "@/main";

export default {
  actions: {
    /**
     * Fetches all store memberships for the currently authenticated user.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getMyMemberships(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/users/me/memberships`,
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
     * Selects a store as the active workspace for the current user.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.storeId - The store ID to select.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async postSelectStore(ctx, { onSuccess, storeId, onError }) {
      return await useAxios(
        `/users/me/memberships/${storeId}/select`,
        { method: "POST", cache: false },
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
     * Fetches all users for administrator management.
     */
    async getAdminUsers(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/users/`,
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
     * Updates an existing user from the administrator users page.
     */
    async updateAdminUser(ctx, { userId, data, onSuccess, onError }) {
      try {
        const response = await http.patch(`/users/${userId}`, data, { cache: false });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
