import { useAxios } from "@vueuse/integrations/useAxios";
import { http, router, resetAuthCache, loadMe } from "@/main";
import { PAGE_SIGN_IN } from "@/router";

export default {
  actions: {
    /**
     * Registers a new user account using an invitation token.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Object} params.input - User sign-up data (e.g. name, email, password).
     * @param {string} params.token - Invitation token for registration.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async postSignUpUser(ctx, { input, token, onSuccess, onError }) {
      return await useAxios(
        `/auth/signUp?token=${token}`,
        { method: "POST", data: { ...input }, cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          resetAuthCache();
          onSuccess(response);
        })
        .catch((error) => onError(error));
    },

    /**
     * Signs in a user with credentials.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Object} params.input - Sign-in credentials (e.g. email, password).
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async postSignInUser(ctx, { input, onSuccess, onTwoFactor, onError }) {
      return await useAxios(
        `/auth/signIn`,
        { method: "POST", data: { ...input }, cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          const body = response.data?.value ?? response.data;
          if (body?.status === "twoFactorRequired") {
            onTwoFactor?.(body);
            return;
          }
          resetAuthCache();
          onSuccess(response);
        })
        .catch((error) => onError(error));
    },

    async postSignInTotp(ctx, { input, onSuccess, onError }) {
      return await useAxios(
        `/auth/signIn/totp`,
        { method: "POST", data: { ...input }, cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          resetAuthCache();
          onSuccess(response);
        })
        .catch((error) => onError(error));
    },

    async postSignInRecovery(ctx, { input, onSuccess, onError }) {
      return await useAxios(
        `/auth/signIn/recovery`,
        { method: "POST", data: { ...input }, cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          resetAuthCache();
          onSuccess(response);
        })
        .catch((error) => onError(error));
    },

    async postSignInPasskey(ctx, { assertion, rememberMe, onSuccess, onError }) {
      return await useAxios(
        `/auth/signIn/passkey`,
        { method: "POST", data: { ...assertion, rememberMe }, cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          resetAuthCache();
          onSuccess(response);
        })
        .catch((error) => onError(error));
    },

    /**
     * Fetches the currently authenticated user's profile.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getMe(ctx, { onSuccess, onError }) {
      try {
        const me = await loadMe();
        if (!me) {
          onError(new Error("Failed to load current user"));
          return;
        }
        onSuccess({ data: { value: me } });
      } catch (error) {
        onError(error);
      }
    },

    /**
     * Checks whether the current user session is authenticated.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async getIsAuthenticated(ctx, { onSuccess, onError }) {
      return await useAxios(
        `/auth/isAuthenticated`,
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
     * Logs out the current user and redirects to the next page or sign-in.
     * @param {Object} ctx - Vuex action context.
     * @param {Object} params - Action parameters.
     * @param {Function} params.onSuccess - Callback on successful response.
     * @param {Function} params.onError - Callback on error.
     * @returns {Promise<void>}
     */
    async postLogOut(ctx, { onSuccess, onError } = {}) {
      return await useAxios(
        `/auth/logOut`,
        { method: "POST", cache: false },
        http,
        {
          immediate: true,
          abortPrevious: true,
        },
      )
        .then((response) => {
          resetAuthCache();
          router.push(response.data.value.next ?? PAGE_SIGN_IN);
          onSuccess?.(response);
        })
        .catch((error) => {
          resetAuthCache();
          router.push(PAGE_SIGN_IN);
          onError?.(error);
        });
    },
  },
  mutations: {},
  state: {},
  getters: {},
};
