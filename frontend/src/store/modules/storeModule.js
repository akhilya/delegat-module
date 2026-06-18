import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    /**
     * Fetches all stores.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAllStores(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/stores/`,
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
     * Fetches detailed information for a specific store.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The store ID to fetch details for.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getStoreDetail(ctx, { id, onSuccess, onError }) {
      return await useAxios(
        `/stores/${id}/detail`,
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
     * Creates a new store.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string} params.name - Name for the new store.
     * @param {boolean} params.addAllUsers - Whether to add every active user to the new store.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createStore(ctx, { name, addAllUsers, onSuccess, onError }) {
      return await useAxios(
        `/stores/`,
        {
          method: "POST",
          cache: false,
          data: { name, addAllUsers }
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
     * Adds a user as a member of a store.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.storeId - The store ID to add the member to.
     * @param {string|number} params.userId - The user ID to add as a member.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async addMember(ctx, { storeId, userId, onSuccess, onError }) {
      try {
        const response = await http.post(`/stores/${storeId}/members/`, { userId }, { cache: false });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Removes a user from a store's membership.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.storeId - The store ID to remove the member from.
     * @param {string|number} params.userId - The user ID to remove.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async removeMember(ctx, { storeId, userId, onSuccess, onError }) {
      try {
        const response = await http.delete(`/stores/${storeId}/members/${userId}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Fetches all users in the system (admin endpoint).
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAllUsers(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/users/all`,
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
  },
  mutations: {},
  state: {},
  getters: {},
};
