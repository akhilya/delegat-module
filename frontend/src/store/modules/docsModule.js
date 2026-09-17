import { fetchWithAuth } from "@/utils/fetchWithAuth";
import { getLocale } from "@/i18n";
import { docsSlugForPath } from "@/lib/docsHelp";
import {
  SHEET_WIDTH_DEFAULTS,
  SHEET_WIDTH_STORAGE,
  readStoredSheetWidth,
} from "@/composables/useSheetResize";

const state = {
  panelOpen: false,
  panelWidth: readStoredSheetWidth(
    SHEET_WIDTH_STORAGE.docs,
    SHEET_WIDTH_DEFAULTS.docs,
  ),
  slug: "",
  catalog: null,
  catalogFailed: false,
  page: null,
  pageLoading: false,
  pageError: false,
};

const mutations = {
  SET_PANEL_OPEN(state, value) {
    state.panelOpen = !!value;
  },
  SET_PANEL_WIDTH(state, width) {
    const n = Number(width);
    if (Number.isFinite(n)) state.panelWidth = n;
  },
  SET_SLUG(state, slug) {
    state.slug = slug == null ? "" : String(slug);
  },
  SET_CATALOG(state, catalog) {
    state.catalog = catalog;
    state.catalogFailed = false;
  },
  SET_CATALOG_FAILED(state, failed) {
    state.catalogFailed = !!failed;
    if (failed) state.catalog = null;
  },
  SET_PAGE(state, page) {
    state.page = page;
  },
  SET_PAGE_LOADING(state, value) {
    state.pageLoading = !!value;
  },
  SET_PAGE_ERROR(state, value) {
    state.pageError = !!value;
  },
};

async function fetchCatalog(locale) {
  const res = await fetchWithAuth(
    `/api/docs/catalog?locale=${encodeURIComponent(locale)}`,
  );
  if (!res.ok) throw new Error(`catalog ${res.status}`);
  return res.json();
}

async function fetchPage(slug, locale) {
  const params = new URLSearchParams({ locale });
  if (slug) params.set("slug", slug);
  const res = await fetchWithAuth(`/api/docs/page?${params}`);
  if (res.status === 404) {
    const err = new Error("docs_not_found");
    err.code = "docs_not_found";
    throw err;
  }
  if (!res.ok) throw new Error(`page ${res.status}`);
  return res.json();
}

const actions = {
  async ensureCatalog({ state, commit }, { force = false } = {}) {
    if (state.catalog && !force) return;
    try {
      const catalog = await fetchCatalog(getLocale());
      commit("SET_CATALOG", catalog);
    } catch {
      commit("SET_CATALOG_FAILED", true);
    }
  },

  closePanel({ commit }) {
    commit("SET_PANEL_OPEN", false);
  },

  togglePanel({ state, dispatch }) {
    if (state.panelOpen) return dispatch("closePanel");
    return dispatch("openHub");
  },
  setPanelWidth({ commit }, width) {
    commit("SET_PANEL_WIDTH", width);
  },

  openHub({ dispatch }) {
    return dispatch("openSlug", "");
  },

  async openSlug({ commit, dispatch, state }, slug) {
    commit("SET_SLUG", slug ?? "");
    commit("SET_PANEL_OPEN", true);
    await dispatch("ensureCatalog", { force: !state.catalog });
    commit("SET_PAGE_LOADING", true);
    commit("SET_PAGE_ERROR", false);
    try {
      const page = await fetchPage(state.slug, getLocale());
      commit("SET_PAGE", page);
    } catch {
      commit("SET_PAGE", null);
      commit("SET_PAGE_ERROR", true);
    } finally {
      commit("SET_PAGE_LOADING", false);
    }
  },

  async openForRoute({ dispatch }, { path, connId } = {}) {
    let adapterKey = null;
    if (connId) {
      try {
        const res = await fetchWithAuth(`/api/marketplace/connections/${connId}`);
        if (res.ok) {
          const data = await res.json();
          adapterKey = data?.adapterKey ?? null;
        }
      } catch {
        adapterKey = null;
      }
    }
    const slug = docsSlugForPath(path, { adapterKey });
    await dispatch("openSlug", slug ?? "");
  },
};

export default {
  namespaced: true,
  state,
  mutations,
  actions,
};
