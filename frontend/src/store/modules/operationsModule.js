import { HubConnectionBuilder, LogLevel, HttpTransportType } from "@microsoft/signalr";
import { http } from "@/main";

let connection = null;

// Cancel callbacks for client-side operations (e.g. file uploads).
// Keyed by operation ID. Not in Vuex state — functions aren't serializable.
const cancelCallbacks = new Map();

const state = {
  operations: [],
  panelOpen: false,
  historyLoaded: false,
  historyPage: 0,
  historyHasMore: true,
};

const mutations = {
  SET_PANEL_OPEN(state, value) {
    state.panelOpen = value;
  },
  ADD_OPERATION(state, operation) {
    const idx = state.operations.findIndex((o) => o.id === operation.id);
    if (idx >= 0) {
      state.operations[idx] = {
        ...state.operations[idx],
        authorUserId: state.operations[idx].authorUserId ?? operation.authorUserId ?? null,
        authorUserName: state.operations[idx].authorUserName ?? operation.authorUserName ?? null,
        isLocalClientOperation:
          state.operations[idx].isLocalClientOperation ||
          operation.isLocalClientOperation ||
          false,
      };
      return;
    }
    state.operations.unshift(operation);
  },
  MERGE_HISTORY(state, items) {
    for (const item of items) {
      const idx = state.operations.findIndex((o) => o.id === item.id);
      if (idx >= 0) continue;
      state.operations.push(item);
    }
  },
  SET_HISTORY_LOADED(state, value) {
    state.historyLoaded = value;
  },
  SET_HISTORY_PAGE(state, value) {
    state.historyPage = value;
  },
  SET_HISTORY_HAS_MORE(state, value) {
    state.historyHasMore = value;
  },
  ADD_LOG(state, { id, log }) {
    const op = state.operations.find((o) => o.id === id);
    if (!op) return;
    if (op.logs.length >= 500) return;
    op.logs.push(log);
  },
  SET_OPERATION_LOGS(state, { id, logs }) {
    const op = state.operations.find((o) => o.id === id);
    if (!op) return;
    op.logs = logs;
    op._logsLoaded = true;
  },
  UPDATE_PROGRESS(state, { id, progress }) {
    const op = state.operations.find((o) => o.id === id);
    if (!op) return;
    op.progress = progress;
  },
  FINISH_OPERATION(state, { id, status, summary }) {
    const op = state.operations.find((o) => o.id === id);
    if (!op) return;
    op.status = status;
    op.finishedAt = op.finishedAt || new Date().toISOString();
    if (summary) {
      const level = status === "failed" ? "error" : "success";
      const alreadyLogged = op.logs.some(
        (log) => log.message === summary && log.level === level,
      );
      if (!alreadyLogged) {
        op.logs.push({
          timestamp: new Date().toISOString(),
          level,
          message: summary,
        });
      }
    }
  },
  UPDATE_OPERATION_STATUS(state, { id, status }) {
    const op = state.operations.find((o) => o.id === id);
    if (op) op.status = status;
  },
  SET_OPERATION_PRODUCTS(state, { id, products }) {
    const op = state.operations.find((o) => o.id === id);
    if (op) op.products = products;
  },
  REMOVE_OPERATION(state, id) {
    state.operations = state.operations.filter((o) => o.id !== id);
  },
  CLEAR_COMPLETED(state) {
    state.operations = state.operations.filter(
      (o) => o.status === "running",
    );
  },
  RESET_STATE(state) {
    state.operations = [];
    state.historyLoaded = false;
    state.historyPage = 0;
    state.historyHasMore = true;
  },
};

function mapServerOperation(item) {
  return {
    id: item.operationId,
    type: item.type,
    title: item.title,
    status: item.status,
    connectionName: item.connectionName || null,
    connectionId: item.connectionId || null,
    authorUserName: item.authorUserName || null,
    authorUserId: item.authorUserId || null,
    startedAt: item.createdAt,
    finishedAt: item.finishedAt || null,
    progress: item.totalItems != null ? {
      total: item.totalItems,
      completed: item.completedItems || 0,
      failed: item.failedItems || 0,
      pending: (item.totalItems || 0) - (item.completedItems || 0) - (item.failedItems || 0),
    } : null,
    logs: [],
    products: [],
    canRetry: item.batchType === "export" || item.batchType === "import",
    batchId: item.batchId || null,
    batchType: item.batchType || null,
    _fromHistory: true,
    isLocalClientOperation: false,
    _logsLoaded: false,
  };
}

const actions = {
  async initSignalR({ commit, dispatch }) {
    if (connection) return;

    const hubUrl = "/hubs/operations";

    connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
        withCredentials: true,
      })
      .withAutomaticReconnect([1000, 3000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    // Refresh JWT before each reconnect so the new cookie is in place for the negotiate request
    connection.onreconnecting(() => {
      fetch("/auth/refresh", { method: "POST", credentials: "include", headers: { "X-CSRF": "1" } }).catch(() => {});
    });

    connection.on("OperationStarted", (data) => {
      const op = {
        id: data.operationId,
        type: data.type || "unknown",
        title: data.title || "Операция",
        status: "running",
        connectionName: data.connectionName || null,
        connectionId: data.connectionId || null,
        authorUserName: data.authorUserName || null,
        authorUserId: data.authorUserId || null,
        startedAt: data.createdAt || new Date().toISOString(),
        finishedAt: null,
        progress: data.totalItems != null ? {
          total: data.totalItems,
          completed: 0,
          failed: 0,
          pending: data.totalItems,
        } : null,
        logs: [],
        products: [],
        canRetry: data.batchType === "export" || data.batchType === "import",
        batchId: data.batchId || null,
        batchType: data.batchType || null,
        isLocalClientOperation: false,
      };
      commit("ADD_OPERATION", op);
      // Don't auto-open panel for client-side operations (file-upload, file-delete) —
      // they register silently via addOperation; the SignalR echo shouldn't override that.
      const silentTypes = ["file-upload", "file-delete"];
      if (!silentTypes.includes(op.type)) {
        commit("SET_PANEL_OPEN", true);
      }
    });

    connection.on("OperationLog", (data) => {
      commit("ADD_LOG", {
        id: data.operationId,
        log: {
          timestamp: data.timestamp,
          level: data.level,
          message: data.message,
        },
      });
    });

    connection.on("OperationProgress", (data) => {
      commit("UPDATE_PROGRESS", {
        id: data.operationId,
        progress: {
          total: data.total,
          completed: data.completed,
          failed: data.failed,
          pending: data.pending,
        },
      });
    });

    connection.on("OperationFinished", (data) => {
      commit("FINISH_OPERATION", {
        id: data.operationId,
        status: data.status,
        summary: data.summary,
      });
    });

    connection.on("OperationStatusChanged", (data) => {
      commit("UPDATE_OPERATION_STATUS", {
        id: data.operationId,
        status: data.status,
      });
    });

    try {
      await connection.start();
      console.log("[Operations] SignalR connected");
    } catch (err) {
      console.error("[Operations] SignalR connection failed:", err);
      connection = null;
    }
  },

  async destroySignalR({ commit }) {
    if (connection) {
      try {
        await connection.stop();
      } catch {
        // ignore
      }
      connection = null;
    }
    commit("RESET_STATE");
  },

  async reconnectSignalR({ dispatch }) {
    // destroySignalR commits RESET_STATE which clears operations + flips
    // historyLoaded back to false. After reconnecting we explicitly reload
    // history so the panel reflects the newly-selected store without a
    // page refresh. Fire-and-forget — UI shows empty state during fetch.
    await dispatch("destroySignalR");
    await dispatch("initSignalR");
    dispatch("loadHistory");
  },

  async startOperation({ commit }, { id, type, title, connectionId, connectionName }) {
    const op = {
      id,
      type,
      title,
      status: "running",
      connectionName: connectionName || null,
      connectionId: connectionId || null,
      authorUserId: null,
      authorUserName: null,
      startedAt: new Date().toISOString(),
      finishedAt: null,
      progress: null,
      logs: [],
      products: [],
      canRetry: false,
      batchId: null,
      batchType: null,
      isLocalClientOperation: false,
    };
    commit("ADD_OPERATION", op);
    commit("SET_PANEL_OPEN", true);
    try {
      await http.post("/operations/start", { operationId: id, type, title, connectionId, connectionName });
    } catch { /* SignalR will handle */ }
  },

  addOperation({ commit }, operation) {
    const op = {
      id: operation.id || crypto.randomUUID().replace(/-/g, ""),
      type: operation.type || "unknown",
      title: operation.title || "Операция",
      status: "running",
      connectionName: operation.connectionName || null,
      connectionId: operation.connectionId || null,
      authorUserId: operation.authorUserId || null,
      authorUserName: operation.authorUserName || null,
      startedAt: new Date().toISOString(),
      finishedAt: null,
      progress: operation.progress || null,
      logs: [],
      products: [],
      canRetry: operation.canRetry ?? false,
      batchId: operation.batchId || null,
      batchType: operation.batchType || null,
      isLocalClientOperation:
        operation.isLocalClientOperation ?? typeof operation.onCancel === "function",
    };
    commit("ADD_OPERATION", op);
    // Register cancel callback for client-side operations
    if (typeof operation.onCancel === "function") {
      cancelCallbacks.set(op.id, operation.onCancel);
    }
    // Don't auto-open panel for silent operations (e.g. file uploads —
    // user sees progress in the trigger button badge, opens panel manually if needed).
    if (!operation.silent) {
      commit("SET_PANEL_OPEN", true);
    }
    return op.id;
  },

  addLog({ commit }, { id, level, message }) {
    commit("ADD_LOG", {
      id,
      log: {
        timestamp: new Date().toISOString(),
        level,
        message,
      },
    });
    // Persist to backend (fire-and-forget) so logs survive page refresh
    http.post(`/operations/${id}/log`, { level, message }, { cache: false }).catch(() => {});
  },

  updateProgress({ commit }, { id, progress }) {
    commit("UPDATE_PROGRESS", { id, progress });
  },

  finishOperation({ commit }, { id, status, summary }) {
    commit("FINISH_OPERATION", { id, status, summary });
  },

  async loadHistory({ commit, state: _state }) {
    if (_state.historyLoaded) return;
    const page = 1;
    try {
      // cache:false is critical — axios-cache-interceptor would otherwise
      // return the previous store's history when the user switches stores.
      const response = await http.get("/operations", {
        params: { page, pageSize: 20 },
        cache: false,
      });
      const data = response.data;
      const items = (data.items || []).map((item) => ({ ...mapServerOperation(item), _canExpand: true }));
      commit("MERGE_HISTORY", items);
      commit("SET_HISTORY_PAGE", page);
      commit("SET_HISTORY_HAS_MORE", data.hasMore ?? false);
      commit("SET_HISTORY_LOADED", true);
    } catch (err) {
      console.error("[Operations] Failed to load history:", err);
    }
  },

  async loadMoreHistory({ commit, state: _state }) {
    if (!_state.historyHasMore) return;
    const page = _state.historyPage + 1;
    try {
      const response = await http.get("/operations", {
        params: { page, pageSize: 20 },
        cache: false,
      });
      const data = response.data;
      const items = (data.items || []).map((item) => ({ ...mapServerOperation(item), _canExpand: false }));
      commit("MERGE_HISTORY", items);
      commit("SET_HISTORY_PAGE", page);
      commit("SET_HISTORY_HAS_MORE", data.hasMore ?? false);
    } catch (err) {
      console.error("[Operations] Failed to load more history:", err);
    }
  },

  async loadOperationLogs({ commit }, operationId) {
    try {
      const response = await http.get(`/operations/${operationId}/logs`, {
        cache: false,
        timeout: 10000, // 10s safety — don't hang the spinner forever
      });
      const logs = (response.data.logs || []).map((l) => ({
        timestamp: l.timestamp,
        level: l.level,
        message: l.message,
      }));
      commit("SET_OPERATION_LOGS", { id: operationId, logs });
    } catch (err) {
      console.error("[Operations] Failed to load logs:", err);
      // Mark as loaded even on failure so we don't retry infinitely on expand/collapse.
      // User will see empty log timeline — better than infinite spinner.
      commit("SET_OPERATION_LOGS", { id: operationId, logs: [] });
    }
  },

  async retryFailed({ commit }, id) {
    const op = state.operations.find((o) => o.id === id);
    if (!op || !op.batchId) return;

    const endpoint = op.batchType === "import"
      ? `/marketplace/import-batches/${op.batchId}/retry-failed`
      : `/marketplace/export-batches/${op.batchId}/retry-failed`;

    const response = await http.post(endpoint);
    commit("UPDATE_OPERATION_STATUS", { id, status: "running" });
    return response.data;
  },

  async pauseOperation({ commit }, id) {
    commit("UPDATE_OPERATION_STATUS", { id, status: "paused" });
    try { await http.post(`/operations/${id}/pause`); } catch { /* ignore */ }
  },

  async resumeOperation({ commit }, id) {
    commit("UPDATE_OPERATION_STATUS", { id, status: "running" });
    try { await http.post(`/operations/${id}/resume`); } catch { /* ignore */ }
  },

  async stopOperation({ commit }, id) {
    // Client-side operations (e.g. file uploads) have a local cancel callback
    // instead of a server-side stop API.
    const localCancel = cancelCallbacks.get(id);
    if (localCancel) {
      localCancel();
      cancelCallbacks.delete(id);
      commit("FINISH_OPERATION", { id, status: "cancelled", summary: "Остановлено" });
      return;
    }
    try {
      await http.post(`/operations/${id}/stop`);
    } catch { /* ignore */ }
  },

  async clearOperation({ commit }, id) {
    cancelCallbacks.delete(id);
    commit("REMOVE_OPERATION", id);
    try { await http.delete(`/operations/${id}`); } catch { /* ignore 404 */ }
  },

  async clearCompleted({ state, commit }) {
    const completed = state.operations.filter((o) => o.status !== "running");
    const ids = completed.map((o) => o.id);
    commit("CLEAR_COMPLETED");
    if (ids.length) {
      await http.delete("/operations/bulk", { data: ids }).catch(() => {});
    }
  },

  togglePanel({ commit, state: _state }) {
    commit("SET_PANEL_OPEN", !_state.panelOpen);
  },

  openPanel({ commit }) {
    commit("SET_PANEL_OPEN", true);
  },

  closePanel({ commit }) {
    commit("SET_PANEL_OPEN", false);
  },
};

const getters = {
  activeOperations: (state) =>
    state.operations.filter((o) => o.status === "running" || o.status === "paused"),

  historyOperations: (state) =>
    state.operations
      .filter((o) => o.status !== "running" && o.status !== "paused")
      .sort((a, b) => {
        const aTime = a.finishedAt || a.startedAt;
        const bTime = b.finishedAt || b.startedAt;
        return new Date(bTime) - new Date(aTime);
      }),

  activeCount: (_state, getters) => getters.activeOperations.length,

  hasLocalActiveUploads: (state) =>
    state.operations.some(
      (o) =>
        o.type === "file-upload" &&
        o.status === "running" &&
        o.isLocalClientOperation,
    ),

  hasOperations: (state) => state.operations.length > 0,
};

export default {
  namespaced: true,
  state,
  mutations,
  actions,
  getters,
};
