import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
    async putProductFiles(ctx, { productId, files, folderId, onSuccess, onError, onProgress }) {
      let formData = new FormData();

      for (let i = 0; i < files.length; i++) {
        formData.append("files", files[i]);
      }

      const url = folderId != null
        ? `/products/${productId}/files?folderId=${folderId}`
        : `/products/${productId}/files`;

      const config = {
        method: "PUT",
        data: formData,
        headers: {
          "Content-Type": "multipart/form-data",
        },
        timeout: 60 * 60 * 1000,
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

      return await useAxios(url, config, http, {
        immediate: true,
        abortPrevious: true,
      })
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

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

    async moveProductFile(ctx, { productId, fileId, folderId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/files/${fileId}/move`,
        { method: "PATCH", data: { folderId }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async renameProductFile(ctx, { productId, fileId, name, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/files/${fileId}/rename`,
        { method: "PATCH", data: { name }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async getProductFolders(ctx, { productId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/folders`,
        { method: "GET", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async createProductFolder(ctx, { productId, name, parentFolderId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/folders`,
        { method: "POST", data: { name, parentFolderId }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async renameProductFolder(ctx, { productId, folderId, name, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/folders/${folderId}`,
        { method: "PUT", data: { name }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async deleteProductFolder(ctx, { productId, folderId, recursive, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/folders/${folderId}?recursive=${!!recursive}`,
        { method: "DELETE", cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async shareFile(ctx, { fileId, ttlHours, onSuccess, onError }) {
      return await useAxios(
        `/files/${fileId}/share`,
        { method: "POST", data: { ttlHours }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async shareFolder(ctx, { productId, folderId, ttlHours, onSuccess, onError }) {
      const id = folderId ?? 'root';
      return await useAxios(
        `/products/${productId}/folders/${id}/share`,
        { method: "POST", data: { ttlHours }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },

    async moveProductFolder(ctx, { productId, folderId, newParentFolderId, onSuccess, onError }) {
      return await useAxios(
        `/products/${productId}/folders/${folderId}/move`,
        { method: "PATCH", data: { newParentFolderId }, cache: false },
        http,
        { immediate: true, abortPrevious: true },
      )
        .then((response) => onSuccess(response))
        .catch((error) => onError(error));
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
