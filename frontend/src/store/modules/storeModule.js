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
     * @param {string} [params.timezone] - Optional IANA timezone.
     * @param {string} [params.baseCurrency] - Optional ISO 4217 base currency.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createStore(ctx, { name, addAllUsers, timezone, baseCurrency, onSuccess, onError }) {
      const data = { name, addAllUsers };
      if (timezone) data.timezone = timezone;
      if (baseCurrency) data.baseCurrency = baseCurrency;
      return await useAxios(
        `/stores/`,
        {
          method: "POST",
          cache: false,
          data,
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

    async updateStore(ctx, { storeId, data, onSuccess, onError }) {
      try {
        const response = await http.patch(`/stores/${storeId}`, data, { cache: false });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
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
    async addStoreCurrency(ctx, { storeId, data, onSuccess, onError }) {
      try {
        const response = await http.post(`/stores/${storeId}/currencies`, data, { cache: false });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    async patchStoreCurrency(ctx, { storeId, code, data, onSuccess, onError }) {
      try {
        const response = await http.patch(`/stores/${storeId}/currencies/${code}`, data, { cache: false });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    async deleteStoreCurrency(ctx, { storeId, code, onSuccess, onError }) {
      try {
        const response = await http.delete(`/stores/${storeId}/currencies/${code}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Permanently deletes a store (instance owner only).
     * @param {Object} ctx
     * @param {Object} params
     * @param {string|number} params.storeId
     * @param {string} params.confirmationName - Exact store name
     * @param {Function} params.onSuccess
     * @param {Function} params.onError
     */
    async deleteStore(ctx, { storeId, confirmationName, onSuccess, onError }) {
      try {
        const response = await http.delete(`/stores/${storeId}`, {
          data: { confirmationName },
          cache: false,
        });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

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
