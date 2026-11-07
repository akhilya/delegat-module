import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    // Categories CRUD

    /**
     * Fetches all categories.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAllCategories(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/categories`,
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
     * Fetches a single category by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The category ID to fetch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getCategory(ctx, { id, summary = false, onSuccess, onError }) {
      return await useAxios(
        `/categories/${id}${summary ? "/summary" : ""}`,
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
     * Creates a new category.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string} params.name - Name for the new category.
     * @param {string} params.description - Description for the new category.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createCategory(ctx, { name, description, onSuccess, onError }) {
      return await useAxios(
        `/categories`,
        {
          method: "POST",
          cache: false,
          data: { name, description },
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
     * Updates an existing category by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The category ID to update.
     * @param {string} params.name - Updated category name.
     * @param {string} params.description - Updated category description.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateCategory(
      ctx,
      { id, name, description, hasSizes, onSuccess, onError },
    ) {
      return await useAxios(
        `/categories/${id}`,
        {
          method: "PATCH",
          cache: false,
          data: { name, description, hasSizes },
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
     * Deletes a category by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The category ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteCategory(ctx, { id, onSuccess, onError }) {
      try {
        const response = await http.delete(`/categories/${id}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    // Category Attributes CRUD

    /**
     * Fetches all attributes for a given category.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.categoryId - The category ID to fetch attributes for.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getCategoryAttributes(
      ctx,
      { categoryId, summary = false, onSuccess, onError },
    ) {
      return await useAxios(
        `/categories/${categoryId}/attributes${summary ? "/summary" : ""}`,
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

    /** Loads one definition, including its dictionary, for the attribute editor. */
    async getCategoryAttribute(
      ctx,
      { categoryId, attributeId, onSuccess, onError },
    ) {
      return await useAxios(
        `/categories/${categoryId}/attributes/${attributeId}`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Creates a new attribute within a category.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.categoryId - The category ID to add the attribute to.
     * @param {Object} params.attribute - Attribute data to create.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createAttribute(ctx, { categoryId, attribute, onSuccess, onError }) {
      return await useAxios(
        `/categories/${categoryId}/attributes`,
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
     * Updates an existing attribute within a category.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.categoryId - The category ID the attribute belongs to.
     * @param {string|number} params.attributeId - The attribute ID to update.
     * @param {Object} params.attribute - Partial attribute data to patch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateAttribute(
      ctx,
      { categoryId, attributeId, attribute, onSuccess, onError },
    ) {
      return await useAxios(
        `/categories/${categoryId}/attributes/${attributeId}`,
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
     * Deletes an attribute from a category.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.categoryId - The category ID the attribute belongs to.
     * @param {string|number} params.attributeId - The attribute ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteAttribute(
      ctx,
      { categoryId, attributeId, onSuccess, onError },
    ) {
      try {
        const response = await http.delete(
          `/categories/${categoryId}/attributes/${attributeId}`,
        );
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Reorders attributes within a category by submitting the new order of IDs.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.categoryId - The category ID to reorder attributes for.
     * @param {Array<string|number>} params.attributeIds - Ordered list of attribute IDs.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async reorderAttributes(
      ctx,
      { categoryId, attributeIds, onSuccess, onError },
    ) {
      return await useAxios(
        `/categories/${categoryId}/attributes/reorder`,
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
