import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    // === Adapters (from plugins) ===

    /**
     * Fetches all available marketplace adapters provided by installed plugins.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAvailableAdapters(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/marketplace/adapters`,
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

    // === Plugins ===

    /**
     * Fetches all installed plugins.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getPlugins(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/plugins`,
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
     * Uploads a new plugin package.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {FormData} params.formData - Plugin file as multipart form data.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async uploadPlugin(ctx, { formData, onSuccess, onError }) {
      try {
        const response = await http.post(`/plugins/upload`, formData, {
          headers: {
            'Content-Type': 'multipart/form-data',
          },
        });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Deletes an installed plugin by its key.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string} params.pluginKey - Unique key identifying the plugin.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deletePlugin(ctx, { pluginKey, onSuccess, onError }) {
      try {
        const response = await http.delete(`/plugins/${pluginKey}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Reloads a plugin by its key, refreshing its adapters.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string} params.pluginKey - Unique key identifying the plugin.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async reloadPlugin(ctx, { pluginKey, onSuccess, onError }) {
      return await useAxios(
        `/plugins/${pluginKey}/reload`,
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

    // === Connections CRUD ===

    /**
     * Fetches all marketplace connections.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getConnections(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections`,
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
     * Fetches a single marketplace connection by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID to fetch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getConnection(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}`,
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
     * Creates a new marketplace connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Object} params.data - Connection configuration data.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createConnection(ctx, { data, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections`,
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

    /**
     * Updates an existing marketplace connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID to update.
     * @param {Object} params.data - Partial connection data to patch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateConnection(ctx, { connectionId, data, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}`,
        {
          method: "PATCH",
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

    /**
     * Deletes a marketplace connection by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteConnection(ctx, { connectionId, onSuccess, onError }) {
      try {
        const response = await http.delete(`/marketplace/connections/${connectionId}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Tests a marketplace connection to verify credentials and connectivity.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID to test.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async testConnection(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/test`,
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
     * Fetches the capabilities supported by a marketplace connection's adapter.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getCapabilities(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/capabilities`,
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

    // === Rate Limits & Quotas ===

    /**
     * Fetches API rate limits and quotas for a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getConnectionLimits(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/limits`,
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

    // === External Categories ===

    /**
     * Fetches external categories from the marketplace via a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {string|number} [params.parentId] - Optional parent category ID for hierarchical browsing.
     * @param {string} [params.search] - Optional search query to filter categories.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getExternalCategories(ctx, { connectionId, parentId, search, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (parentId) params.append("parentId", parentId);
      if (search) params.append("search", search);

      return await useAxios(
        `/marketplace/connections/${connectionId}/external-categories?${params.toString()}`,
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
     * Fetches local categories available for mapping with a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getLocalCategories(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/local-categories`,
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

    // === Category Mappings ===

    /**
     * Fetches all category mappings for a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getCategoryMappings(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/mappings`,
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
     * Creates a new category mapping between a local and external category.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Object} params.data - Mapping data (local and external category IDs).
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createCategoryMapping(ctx, { connectionId, data, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/mappings`,
        {
          method: "POST",
          cache: false,
          data,
          timeout: 5 * 60 * 1000,
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
     * Deletes a category mapping by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {string|number} params.mappingId - The mapping ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteCategoryMapping(ctx, { connectionId, mappingId, onSuccess, onError }) {
      try {
        const response = await http.delete(`/marketplace/connections/${connectionId}/mappings/${mappingId}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Imports an external category and its attributes into the local system.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Object} params.data - Import configuration data.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importCategory(ctx, { connectionId, data, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/import-category`,
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

    /**
     * Updates a category mapping (e.g. rename).
     */
    async updateCategoryMapping(ctx, { connectionId, mappingId, data }) {
      const response = await http.patch(
        `/marketplace/connections/${connectionId}/mappings/${mappingId}`,
        data
      );
      return response.data;
    },

    async duplicateCategoryMapping(ctx, { connectionId, mappingId }) {
      const response = await http.post(
        `/marketplace/connections/${connectionId}/mappings/${mappingId}/duplicate`
      );
      return response.data;
    },

    /**
     * Resolves available mappings for a list of products (pre-export check).
     */
    async resolveMappings(ctx, { connectionId, productIds }) {
      const params = productIds.map((id) => `productIds=${id}`).join("&");
      const response = await http.get(
        `/marketplace/connections/${connectionId}/resolve-mappings?${params}`
      );
      return response.data;
    },

    /**
     * Saves mapping selections for products before export.
     */
    async saveMappingSelections(ctx, { connectionId, selections }) {
      const response = await http.post(
        `/marketplace/connections/${connectionId}/save-mapping-selections`,
        { selections }
      );
      return response.data;
    },

    /**
     * Fetches external category fields for preview before import.
     */
    async getExternalCategoryFields(ctx, { connectionId, externalCategoryId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/external-category-fields?externalCategoryId=${encodeURIComponent(externalCategoryId)}`,
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

    // === Field Mappings ===

    /**
     * Fetches external fields available for a category mapping.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getExternalFields(ctx, { mappingId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/external-fields`,
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
     * Fetches all field mappings for a category mapping.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getFieldMappings(ctx, { mappingId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/field-mappings`,
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
     * Saves (creates or replaces) field mappings for a category mapping.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {Array<Object>} params.fieldMappings - Array of field mapping configurations.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async saveFieldMappings(ctx, { mappingId, fieldMappings, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/field-mappings`,
        {
          method: "POST",
          cache: false,
          data: { fieldMappings },
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
     * Toggles the enabled/disabled state of a field mapping.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {string|number} params.fieldMappingId - The field mapping ID to toggle.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async toggleFieldMapping(ctx, { mappingId, fieldMappingId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/field-mappings/${fieldMappingId}/toggle`,
        {
          method: "PATCH",
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
     * Updates a specific field mapping configuration.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {string|number} params.fieldMappingId - The field mapping ID to update.
     * @param {Object} params.data - Partial field mapping data to patch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateFieldMapping(ctx, { mappingId, fieldMappingId, data, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/field-mappings/${fieldMappingId}`,
        {
          method: "PATCH",
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

    /**
     * Fetches available local source fields that can be mapped to external fields.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAvailableSources(ctx, { mappingId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/available-sources`,
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

    // === Field Mapping Preview ===

    /**
     * Previews field mapping modifier results for a specific product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.mappingId - The category mapping ID.
     * @param {number} params.productId - The product ID to preview.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async previewFieldMappings(ctx, { mappingId, productId, fieldMappings, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/mappings/${mappingId}/preview`,
        {
          method: "POST",
          cache: false,
          data: { productId, fieldMappings },
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

    // === Product Links ===

    /**
     * Fetches paginated product links for a connection, optionally filtered by status.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {number} [params.startIndex] - Pagination offset.
     * @param {number} [params.pageSize] - Number of items per page.
     * @param {string} [params.status] - Filter by link status.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getProductLinks(ctx, { connectionId, startIndex, pageSize, status, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (startIndex !== undefined) params.append("startIndex", startIndex);
      if (pageSize !== undefined) params.append("pageSize", pageSize);
      if (status) params.append("status", status);

      return await useAxios(
        `/marketplace/connections/${connectionId}/product-links?${params.toString()}`,
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

    // === Export ===

    /**
     * Exports a single product to a marketplace via a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {string|number} params.productId - The product ID to export.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async exportProduct(ctx, { connectionId, productId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/products/${productId}/export`,
        {
          method: "POST",
          cache: false,
          timeout: 3 * 60 * 1000, // 3 min — backend polls Ozon API for up to 2 min
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
     * Bulk exports multiple products to a marketplace via a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Array<string|number>} params.productIds - IDs of products to export.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async exportProductsBulk(ctx, { connectionId, productIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/products/export-bulk`,
        {
          method: "POST",
          cache: false,
          data: { productIds },
          timeout: 5 * 60 * 1000, // 5 min — each chunk can take a while via Ozon API
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    // === Local Products for Export ===

    async getLocalProductsForExport(ctx, { connectionId, searchQuery, startIndex, pageSize, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (searchQuery) params.append("searchQuery", searchQuery);
      if (startIndex !== undefined) params.append("startIndex", startIndex);
      if (pageSize !== undefined) params.append("pageSize", pageSize);

      return await useAxios(
        `/marketplace/connections/${connectionId}/local-products-for-export?${params.toString()}`,
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

    // === Import ===

    /**
     * Fetches a paginated preview of products available for import from a marketplace.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {number} [params.pageSize] - Number of items per page.
     * @param {string} [params.cursor] - Pagination cursor for the next page.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getImportPreview(ctx, { connectionId, pageSize, cursor, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (pageSize) params.append("pageSize", pageSize);
      if (cursor) params.append("cursor", cursor);

      return await useAxios(
        `/marketplace/connections/${connectionId}/products/import-preview?${params.toString()}`,
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
     * Imports selected products from a marketplace into the local system.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Array<Object>} params.products - Selected products to import.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importSelectedProducts(ctx, { connectionId, products, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/products/import-selected`,
        {
          method: "POST",
          cache: false,
          data: { products },
          timeout: 5 * 60 * 1000,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Imports a single product from a marketplace by its external ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {string} params.externalId - The external marketplace product ID.
     * @param {string|number} [params.targetCategoryId] - Local category to import into.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importProduct(ctx, { connectionId, externalId, targetCategoryId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/products/import/${externalId}`,
        {
          method: "POST",
          cache: false,
          data: { targetCategoryId },
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
     * Imports all products from a marketplace connection in bulk.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {string|number} [params.targetCategoryId] - Local category to import into.
     * @param {number} [params.pageSize] - Batch page size for the import.
     * @param {number} [params.maxProducts] - Maximum number of products to import.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importAllProducts(ctx, { connectionId, targetCategoryId, pageSize, maxProducts, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/products/import-all`,
        {
          method: "POST",
          cache: false,
          data: { targetCategoryId, pageSize, maxProducts },
          timeout: 10 * 60 * 1000,
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
     * Fetches the status of an import batch by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string} params.batchId - The import batch ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getImportBatchStatus(ctx, { batchId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/import-batches/${batchId}/status`,
        {
          method: "GET",
          cache: false,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    // === SKU Update ===

    /**
     * Updates the SKU for a product on a marketplace connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {string|number} params.productId - The product ID.
     * @param {string} params.newSku - The new SKU value.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateSku(ctx, { connectionId, productId, newSku, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/products/${productId}/update-sku`,
        {
          method: "POST",
          cache: false,
          data: { newSku },
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

    // === Price Sync ===

    /**
     * Compares local and marketplace prices for all linked products on a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async comparePrices(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/prices`,
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
     * Imports prices from the marketplace into the local system for specified links.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Array<string|number>} params.linkIds - Product link IDs to import prices for.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importPrices(ctx, { connectionId, linkIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/prices/import`,
        {
          method: "POST",
          cache: false,
          data: { linkIds },
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
     * Exports local prices to the marketplace for specified links.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Array<string|number>} params.linkIds - Product link IDs to export prices for.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async exportPrices(ctx, { connectionId, linkIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/prices/export`,
        {
          method: "POST",
          cache: false,
          data: { linkIds },
          timeout: 5 * 60 * 1000,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Exports local stock quantities to the marketplace for specified links.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Array<string|number>} params.linkIds - Product link IDs to export stocks for.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async exportStocks(ctx, { connectionId, linkIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/connections/${connectionId}/stocks/export`,
        {
          method: "POST",
          cache: false,
          data: { linkIds },
          timeout: 5 * 60 * 1000,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    // === Sync Status ===

    /**
     * Fetches the sync status of products on a marketplace connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {Array<string|number>} [params.productIds] - Optional product IDs to filter by.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getSyncStatus(ctx, { connectionId, productIds, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (productIds && productIds.length > 0) {
        productIds.forEach((id) => params.append("productIds", id));
      }

      return await useAxios(
        `/marketplace/connections/${connectionId}/sync-status?${params.toString()}`,
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

    // === Product Marketplace Links (for product page) ===

    /**
     * Fetches all marketplace links for a specific product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getProductMarketplaceLinks(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/marketplace-links`,
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
     * Fetches marketplace connections available for linking to a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAvailableConnectionsForProduct(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/available-connections`,
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

    // === Product Link Sync (Import/Export for single link) ===

    /**
     * Imports product data from a marketplace using a specific product link.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.linkId - The product link ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importFromLink(ctx, { linkId, excludeFields, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/${linkId}/import`,
        {
          method: "POST",
          data: { excludeFields: excludeFields || null },
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

    async importFromLinkPreview(ctx, { linkId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/${linkId}/import-preview`,
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
     * Exports product data to a marketplace using a specific product link.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.linkId - The product link ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async recreateProductOnMarketplace(ctx, { linkId, newSku, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/${linkId}/recreate`,
        {
          method: "POST",
          cache: false,
          data: { newSku },
          timeout: 5 * 60 * 1000,
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

    async exportFromLink(ctx, { linkId, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/${linkId}/export`,
        {
          method: "POST",
          cache: false,
          timeout: 3 * 60 * 1000, // 3 min — backend polls Ozon API for up to 2 min
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

    // === External Products (all marketplace products with link status) ===

    /**
     * Fetches external products from a marketplace with their local link status.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The connection ID.
     * @param {number} [params.pageSize] - Number of items per page.
     * @param {string} [params.cursor] - Pagination cursor for the next page.
     * @param {string} [params.offerIdSearch] - Search by offer/SKU ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getExternalProducts(ctx, { connectionId, pageSize, cursor, offerIdSearch, onSuccess, onError }) {
      const params = new URLSearchParams();
      if (pageSize) params.append("pageSize", pageSize);
      if (cursor) params.append("cursor", cursor);
      if (offerIdSearch) params.append("offerIdSearch", offerIdSearch);

      return await useAxios(
        `/marketplace/connections/${connectionId}/external-products?${params.toString()}`,
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

    // === Bulk operations for links ===

    /**
     * Bulk imports product data from the marketplace for multiple links.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Array<string|number>} params.linkIds - Product link IDs to import.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importLinksBulk(ctx, { linkIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/import-bulk`,
        {
          method: "POST",
          cache: false,
          data: { linkIds },
          timeout: 5 * 60 * 1000,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async importLinksBulkPreview(ctx, { linkIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/import-bulk-preview`,
        {
          method: "POST",
          cache: false,
          data: { linkIds },
          timeout: 5 * 60 * 1000,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
        },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Bulk exports product data to the marketplace for multiple links.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Array<string|number>} params.linkIds - Product link IDs to export.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async exportLinksBulk(ctx, { linkIds, onSuccess, onError }) {
      return await useAxios(
        `/marketplace/links/export-bulk`,
        {
          method: "POST",
          cache: false,
          data: { linkIds },
          timeout: 5 * 60 * 1000,
        },
        http,
        {
          immediate: true,
          abortPrevious: false,
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
