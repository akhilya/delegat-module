import { http } from "@/main";

/** @type {ReturnType<typeof setInterval>|null} Interval handle for polling. */
let pollingInterval = null;

const state = {
  /** @type {string|null} The current import batch ID. */
  batchId: null,
  /** @type {Object|null} The current batch progress status object. */
  batchProgress: null,
  /** @type {string|number|null} Connection ID used to refresh the page after import completes. */
  connectionId: null,
};

const mutations = {
  /**
   * Sets the active import batch ID.
   * @param {Object} state - Vuex module state.
   * @param {string} batchId - The batch ID to store.
   */
  SET_BATCH_ID(state, batchId) {
    state.batchId = batchId;
  },

  /**
   * Sets the current batch progress status.
   * @param {Object} state - Vuex module state.
   * @param {Object} progress - The batch progress object from the API.
   */
  SET_BATCH_PROGRESS(state, progress) {
    state.batchProgress = progress;
  },

  /**
   * Sets the connection ID associated with the import.
   * @param {Object} state - Vuex module state.
   * @param {string|number} connectionId - The marketplace connection ID.
   */
  SET_CONNECTION_ID(state, connectionId) {
    state.connectionId = connectionId;
  },

  /**
   * Clears all import progress state, resetting batch, progress, and connection.
   * @param {Object} state - Vuex module state.
   */
  CLEAR_IMPORT_PROGRESS(state) {
    state.batchId = null;
    state.batchProgress = null;
    state.connectionId = null;
  },
};

const actions = {
  /**
   * Starts tracking import progress for a batch and begins polling.
   * @param {Object} context - Vuex action context.
   * @param {Object} params - Action parameters.
   * @param {string} params.batchId - The import batch ID to track.
   * @param {string|number} params.connectionId - The associated connection ID.
   * @param {Object} params.initialProgress - Initial progress snapshot from the API.
   */
  startImportProgress({ commit, dispatch }, { batchId, connectionId, initialProgress }) {
    commit("SET_BATCH_ID", batchId);
    commit("SET_CONNECTION_ID", connectionId);
    commit("SET_BATCH_PROGRESS", initialProgress);
    dispatch("startPolling");
  },

  /**
   * Starts the periodic polling interval to check batch status every 2 seconds.
   * @param {Object} context - Vuex action context.
   */
  startPolling({ state, dispatch }) {
    if (pollingInterval) {
      clearInterval(pollingInterval);
    }
    pollingInterval = setInterval(() => {
      dispatch("pollBatchStatus");
    }, 2000);
  },

  /**
   * Stops the periodic polling interval.
   */
  stopPolling() {
    if (pollingInterval) {
      clearInterval(pollingInterval);
      pollingInterval = null;
    }
  },

  /**
   * Polls the API for the current batch import status and updates state.
   * Stops polling automatically when the batch is finished.
   * @param {Object} context - Vuex action context.
   * @returns {Promise<void>}
   */
  async pollBatchStatus({ state, commit, dispatch }) {
    if (!state.batchId) {
      dispatch("stopPolling");
      return;
    }

    try {
      // Cache busting - append timestamp so the browser does not cache the response
      const response = await http.get(`/marketplace/import-batches/${state.batchId}/status?_t=${Date.now()}`);
      const status = response.data;

      console.log("[ImportProgress] Received status:", JSON.stringify({
        total: status.total,
        pending: status.pending,
        inProgress: status.inProgress,
        completed: status.completed,
        failed: status.failed,
        isFinished: status.isFinished,
        products: status.products?.map(p => ({ id: p.id, status: p.status, name: p.productName }))
      }, null, 2));

      commit("SET_BATCH_PROGRESS", status);

      if (status.isFinished) {
        dispatch("stopPolling");
      }
    } catch (error) {
      // Ignore polling errors
      console.error("Import progress polling error:", error);
    }
  },

  /**
   * Closes the import progress panel and clears all related state.
   * @param {Object} context - Vuex action context.
   */
  closeProgress({ commit, dispatch }) {
    dispatch("stopPolling");
    commit("CLEAR_IMPORT_PROGRESS");
  },

  /**
   * Retries all failed imports in the current batch and resumes polling.
   * @param {Object} context - Vuex action context.
   * @returns {Promise<Object>} The retry response data from the API.
   */
  async retryFailedImports({ state, dispatch }) {
    if (!state.batchId) return;

    try {
      const response = await http.post(`/marketplace/import-batches/${state.batchId}/retry-failed`);
      dispatch("startPolling");
      return response.data;
    } catch (error) {
      throw error;
    }
  },
};

const getters = {
  /**
   * Returns whether there is an active import in progress.
   * @param {Object} state - Vuex module state.
   * @returns {boolean} True if an import batch is being tracked.
   */
  hasActiveImport: (state) => state.batchProgress !== null,

  /**
   * Returns whether the current import batch has finished.
   * @param {Object} state - Vuex module state.
   * @returns {boolean} True if the import is complete.
   */
  isImportFinished: (state) => state.batchProgress?.isFinished ?? false,

  /**
   * Returns the import progress as a percentage (0-100).
   * @param {Object} state - Vuex module state.
   * @returns {number} Percentage of completed and failed items out of total.
   */
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
