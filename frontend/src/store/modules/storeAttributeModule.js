import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    // Store Attributes CRUD

    /**
     * Fetches all store-level attributes.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAllStoreAttributes(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/store-attributes`,
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
     * Fetches a single store attribute by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The store attribute ID to fetch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getStoreAttribute(ctx, { id, onSuccess, onError }) {
      return await useAxios(
        `/store-attributes/${id}`,
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
     * Creates a new store-level attribute.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Object} params.attribute - Attribute data to create.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createStoreAttribute(ctx, { attribute, onSuccess, onError }) {
      return await useAxios(
        `/store-attributes`,
        {
          method: "POST",
          cache: false,
          data: attribute,
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
     * Updates an existing store-level attribute by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The store attribute ID to update.
     * @param {Object} params.attribute - Partial attribute data to patch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateStoreAttribute(ctx, { id, attribute, onSuccess, onError }) {
      return await useAxios(
        `/store-attributes/${id}`,
        {
          method: "PATCH",
          cache: false,
          data: attribute,
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
     * Deletes a store-level attribute by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The store attribute ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteStoreAttribute(ctx, { id, onSuccess, onError }) {
      try {
        const response = await http.delete(`/store-attributes/${id}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Reorders store-level attributes by submitting the new order of IDs.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Array<string|number>} params.attributeIds - Ordered list of attribute IDs.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async reorderStoreAttributes(ctx, { attributeIds, onSuccess, onError }) {
      return await useAxios(
        `/store-attributes/reorder`,
        {
          method: "POST",
          cache: false,
          data: attributeIds,
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
