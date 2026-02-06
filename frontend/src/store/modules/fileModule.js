import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    /**
     * Uploads files and attaches them to a product, with upload progress tracking.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID to attach files to.
     * @param {FileList|Array<File>} params.files - Files to upload.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @param {Function} [params.onProgress] - Callback for upload progress updates.
     * @returns {Promise<void>}
     */
    async putProductFiles(ctx, { productId, files, onSuccess, onError, onProgress }) {
      let formData = new FormData();

      for (let i = 0; i < files.length; i++) {
        formData.append("files", files[i]);
      }

      const config = {
        method: "PUT",
        data: formData,
        headers: {
          "Content-Type": "multipart/form-data",
        },
        timeout: 60 * 60 * 1000, // 1 hour for large files
        cache: false,
        onUploadProgress: (progressEvent) => {
          if (onProgress && progressEvent.total) {
            const percentCompleted = Math.round((progressEvent.loaded * 100) / progressEvent.total);
            onProgress({
              loaded: progressEvent.loaded,
              total: progressEvent.total,
              percent: percentCompleted,
            });
          }
        },
      };

      return await useAxios(`/products/${productId}/files`, config, http, {
        immediate: true,
        abortPrevious: true,
      })
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    /**
     * Deletes a file attached to a product.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {string|number} params.productId - The product ID the file belongs to.
     * @param {string|number} params.id - The file ID to delete.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async deleteProductFile(ctx, { productId, id, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/files/${id}`,
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
