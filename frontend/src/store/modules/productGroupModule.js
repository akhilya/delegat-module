import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    async mergeProductGroups(
      ctx,
      { targetGroupId, sourceGroupIds, onSuccess, onError },
    ) {
      try {
        const response = await http.post(
          `/productgroups/${targetGroupId}/merge`,
          { sourceGroupIds },
        );
        onSuccess?.(response);
      } catch (error) {
        onError?.(error);
      }
    },
    // Product Groups CRUD

    /**
     * Fetches all product groups.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getAllGroups(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/productgroups`,
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
     * Fetches a single product group by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The product group ID to fetch.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getGroup(ctx, { id, onSuccess, onError }) {
      return await useAxios(
        `/productgroups/${id}`,
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
     * Creates a new product group with optional initial product members.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string} params.name - Name for the new group.
     * @param {string} params.description - Description for the new group.
     * @param {Array<string|number>} [params.productIds] - Optional initial product IDs to include.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async createGroup(
      ctx,
      { name, description, productIds, onSuccess, onError },
    ) {
      return await useAxios(
        `/productgroups`,
        {
          method: "POST",
          cache: false,
          data: { name, description, productIds },
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
     * Updates an existing product group's name and description.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The product group ID to update.
     * @param {string} params.name - Updated group name.
     * @param {string} params.description - Updated group description.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateGroup(ctx, { id, name, description, onSuccess, onError }) {
      return await useAxios(
        `/productgroups/${id}`,
        {
          method: "PATCH",
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
     * Deletes a product group by its ID.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.id - The product group ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteGroup(ctx, { id, onSuccess, onError }) {
      try {
        const response = await http.delete(`/productgroups/${id}`);
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Deletes selected product groups in one all-or-nothing request.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Array<string|number>} params.groupIds - The product group IDs to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteGroups(ctx, { groupIds, onSuccess, onError }) {
      try {
        const response = await http.post(
          "/productgroups/bulk-delete",
          { groupIds },
          { cache: false },
        );
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    // Group Membership

    /**
     * Adds products to an existing product group.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.groupId - The group ID to add products to.
     * @param {Array<string|number>} params.productIds - IDs of products to add.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async addProductsToGroup(ctx, { groupId, productIds, onSuccess, onError }) {
      return await useAxios(
        `/productgroups/${groupId}/products`,
        {
          method: "POST",
          cache: false,
          data: { productIds },
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
     * Removes a product from a product group.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.groupId - The group ID to remove the product from.
     * @param {string|number} params.productId - The product ID to remove.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async removeProductFromGroup(
      ctx,
      { groupId, productId, onSuccess, onError },
    ) {
      try {
        const response = await http.delete(
          `/productgroups/${groupId}/products/${productId}`,
        );
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Updates a group member's sort order within the group.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.groupId - The group ID.
     * @param {string|number} params.productId - The product ID within the group.
     * @param {number} params.sortOrder - New sort order value.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async updateGroupMember(
      ctx,
      { groupId, productId, sortOrder, onSuccess, onError },
    ) {
      return await useAxios(
        `/productgroups/${groupId}/products/${productId}`,
        {
          method: "PATCH",
          cache: false,
          data: { sortOrder },
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

    // Marketplace Integration

    /**
     * Exports a product group to a marketplace via a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.groupId - The group ID to export.
     * @param {string|number} params.connectionId - The marketplace connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async exportGroup(ctx, { groupId, connectionId, onSuccess, onError }) {
      return await useAxios(
        `/productgroups/${groupId}/export/${connectionId}`,
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
     * Imports product groups from a marketplace via a connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.connectionId - The marketplace connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async importGroups(ctx, { connectionId, onSuccess, onError }) {
      return await useAxios(
        `/productgroups/import/${connectionId}`,
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
     * Fetches the marketplace sync status for a product group.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.groupId - The group ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getGroupSyncStatus(ctx, { groupId, onSuccess, onError }) {
      return await useAxios(
        `/productgroups/${groupId}/sync-status`,
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
     * Deletes a product group's marketplace association for a specific connection.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.groupId - The group ID.
     * @param {string|number} params.connectionId - The marketplace connection ID.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteMarketplaceGroup(
      ctx,
      { groupId, connectionId, onSuccess, onError },
    ) {
      try {
        const response = await http.delete(
          `/productgroups/${groupId}/marketplace/${connectionId}`,
        );
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
