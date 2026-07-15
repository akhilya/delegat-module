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
      { searchQuery, startIndex, pageSize, categoryId, sort, sortBy, sortDesc, onSuccess, onError },
    ) {
      const params = new URLSearchParams();
      if (searchQuery) params.set("searchQuery", searchQuery);
      params.set("startIndex", startIndex);
      params.set("pageSize", pageSize);
      if (categoryId != null) params.set("categoryId", categoryId);
      if (sort) {
        const parts = Array.isArray(sort)
          ? sort
          : String(sort).split(/[,;|]/);
        for (const part of parts) {
          const trimmed = part?.trim();
          if (trimmed) params.append("sort", trimmed);
        }
      } else {
        if (sortBy) params.set("sortBy", sortBy);
        if (sortDesc != null) params.set("sortDesc", sortDesc);
      }

      const requestUrl = `/products?${params.toString()}`;
      if (import.meta.env.DEV) {
        console.debug("[productSort] API request", {
          url: requestUrl,
          sortParams: [...params.getAll("sort")],
        });
      }

      return await useAxios(
        requestUrl,
        { method: "GET", cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          if (import.meta.env.DEV && response.data?.value?._sortDebug) {
            console.debug("[productSort] API _sortDebug", response.data.value._sortDebug);
          }
          onSuccess(response);
        })
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
     * Read-only preview of a clone/move operation: SKU collisions, category match status,
     * marketplace links that will be broken, target-store group list, etc.
     * @param {Object} ctx
     * @param {Object} params
     * @param {string|number} params.productId
     * @param {string|number} params.targetStoreId
     * @param {Function} params.onSuccess
     * @param {Function} params.onError
     */
    async getTransferPreview(ctx, { productId, targetStoreId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/transfer-preview?targetStoreId=${targetStoreId}`,
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
     * Execute clone or move of a product into another store.
     * @param {Object} ctx
     * @param {Object} params
     * @param {string|number} params.productId
     * @param {Object} params.input - TransferRequest body (mode, targetStoreId, sku/category/group settings, confirmBreakLinks)
     * @param {Function} params.onSuccess
     * @param {Function} params.onError
     */
    async transferProduct(ctx, { productId, input, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/transfer`,
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
        const response = await http.get(`/products/${productId}/group`, { cache: false });
        onSuccess({ data: { value: response.data } });
      } catch (error) {
        onError(error);
      }
    },

    async exportProductsTemplate(ctx, { productIds, onSuccess, onError }) {
      try {
        const response = await http.post(
          `/products/export-template`,
          { productIds },
          { responseType: "blob" },
        );
        const url = window.URL.createObjectURL(new Blob([response.data]));
        const link = document.createElement("a");
        link.href = url;
        link.setAttribute("download", "products-export.xlsx");
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.URL.revokeObjectURL(url);
        if (onSuccess) onSuccess();
      } catch (error) {
        if (onError) onError(error);
      }
    },

    async importProductsPreview(ctx, { file, onSuccess, onError }) {
      try {
        const formData = new FormData();
        formData.append("file", file);
        const response = await http.post(`/products/import-preview`, formData, {
          headers: { "Content-Type": "multipart/form-data" },
        });
        if (onSuccess) onSuccess(response.data);
      } catch (error) {
        if (onError) onError(error);
      }
    },

    async importProductsApply(ctx, { file, onSuccess, onError }) {
      try {
        const formData = new FormData();
        formData.append("file", file);
        const response = await http.post(`/products/import-apply`, formData, {
          headers: { "Content-Type": "multipart/form-data" },
        });
        if (onSuccess) onSuccess(response.data);
      } catch (error) {
        if (onError) onError(error);
      }
    },

    async getTrashProducts(ctx, { searchQuery, startIndex, pageSize, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (searchQuery) params.set("searchQuery", searchQuery);
      params.set("startIndex", startIndex);
      params.set("pageSize", pageSize);

      return await useAxios(
        `/products/trash?${params.toString()}`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async getTrashCount(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/products/trash/count`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async restoreProduct(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/restore`,
        { method: "POST" },
        http,
        { immediate: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async bulkRestoreProducts(ctx, { productIds, onSuccess, onError }) {
      return await useAxios(
        `/products/bulk-restore`,
        { method: "POST", data: { productIds } },
        http,
        { immediate: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async permanentDeleteProduct(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/permanent`,
        { method: "DELETE" },
        http,
        { immediate: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async bulkPermanentDeleteProducts(ctx, { productIds, onSuccess, onError }) {
      return await useAxios(
        `/products/bulk-permanent-delete`,
        { method: "POST", data: { productIds } },
        http,
        { immediate: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
