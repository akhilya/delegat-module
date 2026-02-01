import { useAxios } from "@vueuse/integrations/useAxios";
import { http } from "@/main";

export default {
  actions: {
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
    async createStore(ctx, { name, onSuccess, onError }) {
      return await useAxios(
        `/stores/`,
        {
          method: "POST",
          cache: false,
          data: { name }
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
    async addMember(ctx, { storeId, userId, onSuccess, onError }) {
      try {
        const response = await http.post(`/stores/${storeId}/members/`, { userId }, { cache: false });
        onSuccess({ data: response.data });
      } catch (error) {
        onError(error);
      }
    },
    async removeMember(ctx, { storeId, userId, onSuccess, onError }) {
      try {
        const response = await http.delete(`/stores/${storeId}/members/${userId}`);
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
