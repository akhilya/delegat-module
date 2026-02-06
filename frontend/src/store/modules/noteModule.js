import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    /**
     * Creates a new note for a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to add the note to.
     * @param {Object} params.input - Note data (e.g. text content).
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async postNote(ctx, { productId, input, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/notes`,
        { method: "POST", data: { ...input }, cache: false },
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
     * Updates an existing note for a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID the note belongs to.
     * @param {string|number} params.id - The note ID to update.
     * @param {Object} params.input - Partial note data to patch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async patchNote(ctx, { productId, id, input, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/notes/${id}`,
        { method: "PATCH", data: { ...input }, cache: false },
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
     * Deletes a note from a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID the note belongs to.
     * @param {string|number} params.id - The note ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteNote(ctx, { productId, id, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/notes/${id}`,
        { method: "DELETE", cache: false },
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
