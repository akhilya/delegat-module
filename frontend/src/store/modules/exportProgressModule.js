import { http } from "@/main";

/** @type {ReturnType<typeof setInterval>|null} Interval handle for polling. */
let pollingInterval = null;

const state = {
  /** @type {string|null} The current export batch ID. */
  batchId: null,
  /** @type {Object|null} The current batch progress status object. */
  batchProgress: null,
  /** @type {string|number|null} Connection ID used to refresh the page after export completes. */
  connectionId: null,
};

const mutations = {
  SET_BATCH_ID(state, batchId) {
    state.batchId = batchId;
  },

  SET_BATCH_PROGRESS(state, progress) {
    state.batchProgress = progress;
  },

  SET_CONNECTION_ID(state, connectionId) {
    state.connectionId = connectionId;
  },

  CLEAR_EXPORT_PROGRESS(state) {
    state.batchId = null;
    state.batchProgress = null;
    state.connectionId = null;
  },
};

const actions = {
  /**
   * Starts tracking export progress for a batch and begins polling.
   */
  startExportProgress({ commit, dispatch }, { batchId, connectionId, initialProgress }) {
    commit("SET_BATCH_ID", batchId);
    commit("SET_CONNECTION_ID", connectionId);
    commit("SET_BATCH_PROGRESS", initialProgress);
    dispatch("startPolling");
  },

  startPolling({ state, dispatch }) {
    if (pollingInterval) {
      clearInterval(pollingInterval);
    }
    pollingInterval = setInterval(() => {
      dispatch("pollBatchStatus");
    }, 2000);
  },

  stopPolling() {
    if (pollingInterval) {
      clearInterval(pollingInterval);
      pollingInterval = null;
    }
  },

  async pollBatchStatus({ state, commit, dispatch }) {
    if (!state.batchId) {
      dispatch("stopPolling");
      return;
    }

    try {
      const response = await http.get(`/marketplace/export-batches/${state.batchId}/status?_t=${Date.now()}`);
      const status = response.data;

      commit("SET_BATCH_PROGRESS", status);

      if (status.isFinished) {
        dispatch("stopPolling");
      }
    } catch (error) {
      console.error("Export progress polling error:", error);
    }
  },

  closeProgress({ commit, dispatch }) {
    dispatch("stopPolling");
    commit("CLEAR_EXPORT_PROGRESS");
  },

  async retryFailedExports({ state, dispatch }) {
    if (!state.batchId) return;

    try {
      const response = await http.post(`/marketplace/export-batches/${state.batchId}/retry-failed`);
      dispatch("startPolling");
      return response.data;
    } catch (error) {
      throw error;
    }
  },
};

const getters = {
  hasActiveExport: (state) => state.batchProgress !== null,

  isExportFinished: (state) => state.batchProgress?.isFinished ?? false,

  progressPercent: (state) => {
    if (!state.batchProgress || state.batchProgress.total === 0) return 0;
    return ((state.batchProgress.completed + state.batchProgress.failed) / state.batchProgress.total) * 100;
  },
};

export default {
  namespaced: true,
  state,
  mutations,
  actions,
  getters,
};
