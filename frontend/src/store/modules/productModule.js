import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    /**
     * Fetches a paginated list of products with optional search filtering.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Query parameters.
     * @param {string} params.searchQuery - Text to search products by.
     * @param {number} params.startIndex - Pagination offset.
     * @param {number} params.pageSize - Number of products per page.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAllProducts(
      ctx,
      { searchQuery, startIndex, pageSize, categoryId, sortBy, sortDesc, onSuccess, onError },
    ) {
      const params = new URLSearchParams();
      if (searchQuery) params.set("searchQuery", searchQuery);
      params.set("startIndex", startIndex);
      params.set("pageSize", pageSize);
      if (categoryId != null) params.set("categoryId", categoryId);
      if (sortBy) params.set("sortBy", sortBy);
      if (sortDesc != null) params.set("sortDesc", sortDesc);

      return await useAxios(
        `/products?${params.toString()}`,
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
     * Fetches a single product by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to fetch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getProduct(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}`,
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
     * Creates a new product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Object} params.input - Product data to create.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createProduct(ctx, { input, onSuccess, onError }) {
      return await useAxios(
        `/products`,
        { method: "POST", data: input },
        http,
        {
          immediate: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Updates an existing product by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to update.
     * @param {Object} params.input - Partial product data to patch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateProduct(ctx, { productId, input, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}`,
        { method: "PATCH", data: input },
        http,
        {
          immediate: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Deletes a product by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteProduct(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}`,
        { method: "DELETE" },
        http,
        {
          immediate: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Bulk updates multiple products at once.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Array<string|number>} params.productIds - IDs of products to update.
     * @param {Object} params.updates - Fields to update on all specified products.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async bulkUpdateProducts(ctx, { productIds, updates, onSuccess, onError }) {
      return await useAxios(
        `/products/bulk`,
        { method: "PATCH", data: { productIds, updates } },
        http,
        {
          immediate: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Bulk deletes multiple products at once.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Array<string|number>} params.productIds - IDs of products to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async bulkDeleteProducts(ctx, { productIds, onSuccess, onError }) {
      return await useAxios(
        `/products/bulk-delete`,
        { method: "POST", data: { productIds } },
        http,
        {
          immediate: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Uploads files and attaches them to a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {FileList|Array<File>} params.files - Files to upload.
     * @param {string|number} params.productId - The product ID to attach files to.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    /**
     * Adds image references to a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to attach images to.
     * @param {Array<Object>} params.files - Image file references to associate.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    /**
     * Fetches the product group that a product belongs to.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to look up group for.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateProductSku(ctx, { productId, newSku, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/update-sku`,
        { method: "POST", data: { newSku } },
        http,
        {
          immediate: true,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async getProductGroup(ctx, { productId, onSuccess, onError }) {
      try {
        const response = await http.get(`/products/${productId}/group`);
        onSuccess({ data: { value: response.data } });
      } catch (error) {
        onError(error);
      }
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
