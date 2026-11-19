import { fetchWithAuth } from "@/utils/fetchWithAuth";
import { randomUuid } from "@/utils/uuid";
import { t } from "@/i18n";
import {
  apiHistoryFromBlocks,
  appendAssistantApiBlock,
} from "@/lib/ai/chatHistory.js";
import { appendTextDelta, appendTool } from "@/lib/ai/timeline";
import { getApiErrorCode } from "@/lib/apiErrorText.js";
import {
  emptyAssistantMessage,
  hydrateConversation,
  isAiControlMessage,
  isHiddenUserMessage,
  serializeToolCallsJson,
  toolResultFailed,
  finalizeTurnTools,
} from "@/lib/ai/persist";
import { applyStagedIds, attachmentMetaList } from "@/lib/ai/attachments";
import { readSseEvents } from "@/lib/ai/sse";
import { canConfirmPlan, reconcilePlanMessages } from "@/lib/ai/planState";
import {
  assignSavedMessageIds,
  isCanonicalEvent,
  isDuplicateTurnEvent,
  selectTurnMessage,
} from "@/lib/ai/turnIdentity";
import {
  hasExecutionContext,
  isCurrentFormOwner,
  terminalTurnPatch,
  visibleChatForms,
} from "@/lib/ai/turnLifecycle";

function newSessionId() {
  return randomUuid();
}

async function uploadChatAttachments(sessionId, attached) {
  if (!attached.length) return [];
  const formData = new FormData();
  for (const item of attached) {
    formData.append("files", item.file);
    formData.append("relativePaths", item.relativePath);
  }
  const response = await fetchWithAuth(
    `/api/ai/chat/attachments?sessionId=${encodeURIComponent(sessionId)}`,
    {
      method: "POST",
      headers: { "X-CSRF": "1" },
      body: formData,
    },
  );
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  try {
    const body = await response.json();
    return Array.isArray(body.items) ? body.items : [];
  } catch {
    return [];
  }
}

async function responseError(response) {
  const payload = await response.json().catch(() => null);
  const error = new Error("AI request failed");
  error.code = getApiErrorCode(payload);
  error.status = response.status;
  const details = payload?.data ?? payload?.error?.details;
  error.reason = typeof details?.reason === "string" ? details.reason : "";
  return error;
}

function requestErrorDetails(error) {
  return {
    code: error?.code || "",
    status: Number.isInteger(error?.status) ? error.status : null,
    reason: error?.reason || "",
  };
}

let activeStream = null;
let navigationVersion = 0;

function beginStream(sessionId) {
  activeStream?.controller.abort();
  const stream = {
    sessionId,
    controller: new AbortController(),
    stopped: false,
    started: false,
  };
  activeStream = stream;
  return stream;
}

function ownsStream(state, stream) {
  return activeStream === stream && state.sessionId === stream.sessionId;
}

function isOpenPlan(m) {
  return !!(
    m?.plan &&
    !m.planConfirmed &&
    !m.planCancelled &&
    !m.planSuperseded &&
    !m.planExecuted
  );
}

function planHistoryStatus(m) {
  if (m.turnStopped)
    return "stopped by user — some actions may already have applied. Do not resume or repeat without a new explicit confirmation";
  if (m.planConfirmed) return "confirmed by user";
  if (m.planCancelled) return "cancelled by user";
  if (m.planSuperseded)
    return "outdated — the user sent a new message without confirming or cancelling. Revise the plan to match that message. Do not run these steps.";
  return "pending";
}

function titleSeedFromUserMessage(msg) {
  if (msg?.contextCard?.title) {
    return msg.contextCard.title;
  }
  return msg?.content || "";
}

// Build the conversation history array to send to the backend.
// When a message has apiBlocks (full tool_use + tool_result pairs), we use them directly
// so the model sees properly structured tool call history instead of text fragments.
// Otherwise fall back to text serialization (for messages loaded from DB).
function buildHistory(messages) {
  const result = [];

  const prior = messages
    .filter((m) => m.role === "user" || m.role === "assistant")
    .slice(0, -1);

  for (const m of prior) {
    if (m.role === "user") {
      result.push({ role: "user", content: m.content || "" });
      continue;
    }

    if (m.apiBlocks?.length && !m.turnStopped) {
      result.push(...apiHistoryFromBlocks(m.apiBlocks));

      if (m.plan) {
        const steps = (m.plan.steps || [])
          .map((s) => `- ${s.description || s.tool}`)
          .join("\n");
        const status = planHistoryStatus(m);
        result.push({
          role: "assistant",
          content: `[Plan: "${m.plan.summary || ""}" | Steps:\n${steps}\n| Status: ${status}]`,
        });
      }

      continue;
    }

    // Fallback text serialization (messages loaded from DB or pre-apiBlocks)
    let content = m.content || "";

    if (m.toolCalls?.length) {
      const toolLines = m.toolCalls
        .map((tc) => {
          const input =
            (tc.input || "").length > 200
              ? tc.input.slice(0, 200) + "..."
              : tc.input || "";
          const outcome =
            tc.status === "stopped"
              ? "interrupted; outcome unknown"
              : tc.summary;
          return `${tc.name}(${input})${outcome ? ` -> ${outcome}` : ""}`;
        })
        .join("\n");
      const heading = m.turnStopped
        ? "PREVIOUS TURN TOOL ATTEMPTS — interrupted calls have unknown outcomes; do not repeat automatically"
        : "ALREADY EXECUTED IN PREVIOUS TURN - DO NOT REPEAT OR IMITATE";
      content = `[${heading}:\n${toolLines}]\n${content}`;
    }

    if (m.plan) {
      const steps = (m.plan.steps || [])
        .map((s) => `- ${s.description || s.tool}`)
        .join("\n");
      const status = planHistoryStatus(m);
      content += `\n[Plan: "${m.plan.summary || m.plan.description || ""}" | Steps:\n${steps}\n| Status: ${status}]`;
    }

    if (m.turnStopped)
      content +=
        "\n[Reply stopped by the user. Partial text and completed actions are not a finished answer. Do not continue the interrupted task without a new user instruction.]";
    result.push({ role: "assistant", content });
  }

  return result;
}

// Shared SSE consumer for /ai/chat and /ai/execute-plan
async function consumeSse(response, commit, { execution = false } = {}) {
  let completed = false;
  for await (const event of readSseEvents(response)) {
    // A canonical phase stamp is authoritative over the legacy caller option,
    // and it must win before terminal bookkeeping runs: a late subscriber can
    // see a review-phase terminal event as its very first frame.
    const hasCanonicalPhase =
      isCanonicalEvent(event) &&
      ["chat", "execution", "review"].includes(event.phase);
    if (hasCanonicalPhase) execution = event.phase === "execution";
    // Transport lifecycle bookkeeping runs BEFORE state deduplication: a
    // duplicate terminal event still completes the leg, and a segment start
    // resets the in-flight execution flag even when its state projection was
    // already applied.
    switch (event.type) {
      case "assistant_start":
        completed = false;
        if (!hasCanonicalPhase) execution = false;
        break;
      case "message_start":
        completed = false;
        break;
      case "done":
        completed =
          !execution ||
          typeof event.data?.ok === "boolean" ||
          event.data?.cancelled === true ||
          event.data?.interrupted === true;
        break;
      case "stopped":
      case "interrupted":
        completed = true;
        break;
    }
    if (commit("acceptStreamEvent", event) === false) continue;
    switch (event.type) {
      case "message_start":
        // Canonical segment binding only - no visible mutation.
        break;
      case "assistant_start":
        // Canonical selection already created the review row; the legacy
        // mutation only applies to uncanonical (old-server) streams.
        if (!isCanonicalEvent(event)) commit("startPlanReview", event.data);
        break;
      case "text":
        commit("updateLastAssistantMessage", {
          delta: event.data?.delta ?? "",
          textBlock: event.data?.delta ?? "",
        });
        break;
      case "tool":
        if (event.data?.status === "running") {
          commit("updateLastAssistantMessage", {
            toolCall: {
              id: event.data.id,
              name: event.data.name,
              status: "running",
              input: event.data.input || "",
            },
          });
        } else {
          commit("updateToolCall", {
            id: event.data?.id,
            name: event.data?.name,
            status: "done",
            summary: event.data?.summary,
            result: event.data?.result,
            counts: event.data?.counts,
          });
          let parsedResult = event.data?.result;
          if (typeof parsedResult === "string") {
            try {
              parsedResult = JSON.parse(parsedResult);
            } catch {
              parsedResult = null;
            }
          }
          const failed = toolResultFailed(parsedResult, event.data?.summary);
          const effects = event.data?.effects || [];
          if (!failed) {
            if (effects.includes("mapping")) commit("triggerMappingRefresh");
            if (effects.includes("product")) commit("triggerProductRefresh");
          }
        }
        break;
      case "plan":
        commit("updateLastAssistantMessage", { plan: event.data });
        break;
      case "execution_status":
        commit("setExecutionOutcome", event.data);
        break;
      case "done": {
        if (event.data?.cancelled === true) {
          commit("setTurnStopped");
          break;
        }
        commit("finalizeRunningTools");
        commit("finishAssistantTurn", event.data || {});
        break;
      }
      case "stopped":
      case "interrupted":
        commit("setTurnStopped");
        break;
      case "error":
        commit("finalizeRunningTools");
        commit("setLastAssistantRequestError", {
          code: event.data?.code || "",
          status: Number.isInteger(event.data?.status)
            ? event.data.status
            : null,
          reason: "",
        });
        break;
    }
  }
  if (!completed) throw new Error("AI stream ended without a terminal result");
}

/**
 * The assistant message stream mutations write to. Canonical events select their
 * target by stable messageId; legacy/local actions fall back to the last message.
 */
function selectedAssistant(state) {
  if (state.streamMessageId != null) {
    const selected = state.messages.find((m) => m.id === state.streamMessageId);
    if (selected?.role === "assistant") return selected;
  }
  return state.messages.at(-1);
}

function turnCommit(state, commit) {
  const sessionId = state.sessionId;
  let messageId = state.messages.at(-1)?.id;
  const stream = activeStream;
  return (type, payload) => {
    if (state.sessionId !== sessionId || activeStream !== stream) return false;
    if (type === "acceptStreamEvent") {
      const event = payload;
      if (!isCanonicalEvent(event)) return true; // additive old-server stream
      const existing = state.messages.find(
        (m) =>
          m.messageId === event.messageId ||
          (!m.messageId &&
            Number.isInteger(event.dbId) &&
            m.dbId === event.dbId),
      );
      if (existing && isDuplicateTurnEvent(existing, event)) return false;
      commit("selectStreamMessage", event);
      if (state.streamMessageId == null) return false;
      messageId = state.streamMessageId;
      // Selection already bound the target; the switch treats canonical
      // message_start/assistant_start as metadata-only.
      return true;
    }
    const canonical = state.streamMessageId != null;
    const targetExists = canonical
      ? state.messages.some((m) => m.id === state.streamMessageId)
      : state.messages.at(-1)?.id === messageId;
    if (targetExists) {
      commit(type, payload);
      if (type === "startPlanReview") messageId = state.messages.at(-1)?.id;
    }
    return undefined;
  };
}

export default {
  namespaced: true,

  state: () => ({
    isOpen: false,
    messages: [],
    sessionId: newSessionId(),
    conversationTitle: null,
    isLoading: false,
    isStopping: false,
    pageContext: null,
    panelWidth: parseInt(localStorage.getItem("ai-panel-width") || "400"),
    mappingRefreshToken: 0,
    productRefreshToken: 0,
    fabsHidden: localStorage.getItem("fabs-hidden") === "1",
    selectedModelId: null,
    attachedFiles: [],
    streamMessageId: null,
  }),

  mutations: {
    setOpen(state, val) {
      state.isOpen = val;
    },
    toggle(state) {
      state.isOpen = !state.isOpen;
    },
    addMessage(state, msg) {
      state.messages.push(msg);
    },
    removeLastUserMessage(state) {
      const last = state.messages.at(-1);
      if (last?.role === "user") state.messages.pop();
    },
    patchLastUserAttachments(state, attachments) {
      for (let i = state.messages.length - 1; i >= 0; i--) {
        if (state.messages[i].role === "user") {
          state.messages[i].attachments = attachments;
          return;
        }
      }
    },
    setMessages(state, messages) {
      state.messages = messages;
    },
    replaceLastAssistant(state, msg) {
      const i = state.messages.length - 1;
      if (i < 0 || state.messages[i].role !== "assistant") return;
      const prev = state.messages[i];
      state.messages.splice(i, 1, {
        ...msg,
        id: prev.id,
        dbId: prev.dbId,
      });
    },
    startPlanReview(state, { executionProposalId } = {}) {
      const last = state.messages.at(-1);
      if (
        !executionProposalId ||
        last?.plan?.proposalId !== executionProposalId
      )
        return;
      state.messages.push({
        ...emptyAssistantMessage(),
        id: randomUuid(),
        executionProposalId,
      });
    },
    preparePlanReplay(state) {
      const last = state.messages.at(-1);
      if (!last?.executionProposalId || last.planConfirmed) return;
      const index = state.messages.findIndex(
        (m) => m.plan?.proposalId === last.executionProposalId,
      );
      if (index < 0) return;
      state.messages.splice(index + 1);
      state.messages[index].planExecuted = false;
    },
    setSessionId(state, id) {
      state.sessionId = id;
    },
    setConversationTitle(state, title) {
      state.conversationTitle = title;
    },
    applyDbIds(state, { saved, submitted }) {
      assignSavedMessageIds(state.messages, saved, submitted);
    },
    selectStreamMessage(state, event) {
      const selected = selectTurnMessage(state.messages, event);
      state.streamMessageId = selected?.id ?? null;
    },
    setExecutionOutcome(state, outcome) {
      const selected = selectedAssistant(state);
      if (selected?.role === "assistant") selected.executionOutcome = outcome;
    },
    updateLastAssistantMessage(
      state,
      {
        delta,
        textBlock,
        toolCall,
        plan,
        done,
        modelName,
        dbId,
        resume,
        planFailed,
        planExecuted,
        requestError,
      },
    ) {
      const last = selectedAssistant(state);
      if (!last || last.role !== "assistant") return;

      if (resume) last.done = false;
      if (delta !== undefined) last.content += delta;
      if (delta) last.timeline = appendTextDelta(last.timeline, delta);
      if (modelName) last.modelName = modelName;
      if (dbId !== undefined) last.dbId = dbId;
      if (planFailed === true) last.planFailed = true;
      if (planFailed === false) last.planFailed = false;
      if (planExecuted === true && last.planConfirmed) last.planExecuted = true;
      if (requestError !== undefined) last.requestError = requestError;

      if (!last.apiBlocks) last.apiBlocks = [];
      if (!last.toolCalls) last.toolCalls = [];
      if (!last.timeline) last.timeline = [];

      if (textBlock && textBlock.length > 0) {
        appendAssistantApiBlock(last.apiBlocks, {
          type: "text",
          text: textBlock,
        });
      }

      if (toolCall) {
        last.toolCalls.push(toolCall);
        last.timeline = appendTool(last.timeline, toolCall);

        let inputObj = {};
        try {
          inputObj = JSON.parse(toolCall.input || "{}");
        } catch {
          inputObj = {};
        }
        appendAssistantApiBlock(last.apiBlocks, {
          type: "tool_use",
          id: toolCall.id || toolCall.name,
          name: toolCall.name,
          input: inputObj,
        });
      }

      if (plan) {
        last.plan = plan;
        // The proposing row owns its plan under canonical identity.
        if (last.messageId) last.planOwnerMessageId = last.messageId;
        if (plan.discardDraftText) {
          last.content = "";
          last.timeline = last.timeline.filter((item) => item.type !== "text");
          last.apiBlocks = [];
        }
      }
      if (done) {
        last.done = true;
        // Canonical lifecycle mirrors the server's folded state; an already
        // terminal server state is never rewritten locally.
        if (last.messageState === "running") last.messageState = "completed";
      }
    },
    finalizeRunningTools(state) {
      const last = selectedAssistant(state);
      if (!last) return;
      finalizeTurnTools(last);
    },
    finishAssistantTurn(state, data) {
      const message = selectedAssistant(state);
      if (!message || message.role !== "assistant") return;
      Object.assign(message, terminalTurnPatch(message, data));
    },
    setStopping(state, value) {
      state.isStopping = value;
    },
    setTurnStopped(state) {
      const last = selectedAssistant(state);
      if (!last || last.role !== "assistant") return;
      last.turnStopped = true;
      last.done = true;
      if (last.messageState === "running") last.messageState = "stopped";
      last.planConfirming = false;
      last.planConfirmationError = null;
      last.requestError = null;
      finalizeTurnTools(last, { stopped: true });
    },
    resetPlanExecution(state) {
      const last = selectedAssistant(state);
      if (!Number.isInteger(last?.planExecStart)) return;
      last.timeline = (last.timeline || []).slice(0, last.planExecStart);
      last.toolCalls = last.timeline
        .filter((item) => item.type === "tool" && item.tool)
        .map((item) => item.tool);
      last.content = last.timeline
        .filter((item) => item.type === "text")
        .map((item) => item.text)
        .join("");
      last.apiBlocks = [];
      last.planConfirmationError = null;
      last.planFailed = false;
      last.done = false;
    },
    updateToolCall(state, { id, name, status, summary, result, counts }) {
      const last = selectedAssistant(state);
      if (!last) return;
      const tc = last.toolCalls?.find(
        (t) =>
          (id && t.id === id) ||
          (!id && t.name === name && t.status === "running"),
      );
      if (tc) {
        tc.status = status;
        if (summary) tc.summary = summary;
        if (counts !== undefined) tc.counts = counts;
        if (result !== undefined && result !== null) {
          try {
            tc.result =
              typeof result === "string" ? JSON.parse(result) : result;
          } catch {
            tc.result = result;
          }
        }
        if (
          hasExecutionContext(last) &&
          toolResultFailed(tc.result, tc.summary || summary)
        ) {
          last.planFailed = true;
        }
      }

      if (!last.apiBlocks) last.apiBlocks = [];
      const toolUseId = id || name;
      last.apiBlocks.push({
        role: "tool_result",
        type: "tool_result",
        tool_use_id: toolUseId,
        content: summary || "",
      });
    },
    setLoading(state, val) {
      state.isLoading = val;
      if (!val) state.streamMessageId = null;
    },
    clearMessages(state) {
      state.messages = [];
      state.sessionId = newSessionId();
      state.conversationTitle = null;
      state.attachedFiles = [];
      state.streamMessageId = null;
    },
    setAttachedFiles(state, files) {
      state.attachedFiles = Array.isArray(files) ? files : [];
    },
    setPageContext(state, ctx) {
      state.pageContext = ctx;
    },
    clearPageContext(state) {
      state.pageContext = null;
    },
    setPanelWidth(state, w) {
      state.panelWidth = w;
    },
    setFabsHidden(state, val) {
      state.fabsHidden = val;
      localStorage.setItem("fabs-hidden", val ? "1" : "0");
    },
    setSelectedModel(state, id) {
      state.selectedModelId = id;
    },
    triggerMappingRefresh(state) {
      state.mappingRefreshToken++;
    },
    triggerProductRefresh(state) {
      state.productRefreshToken++;
    },
    setPlanConfirming(state, { proposalId, confirming }) {
      const planMsg = [...state.messages]
        .reverse()
        .find((m) => m.plan?.proposalId === proposalId);
      if (!planMsg) return;
      planMsg.planConfirming = confirming;
      if (confirming) planMsg.planConfirmationError = null;
    },
    setPlanConfirmationError(state, { proposalId, error }) {
      const planMsg = [...state.messages]
        .reverse()
        .find((m) => m.plan?.proposalId === proposalId);
      if (!planMsg) return;
      planMsg.planConfirming = false;
      planMsg.planConfirmationError = error;
    },
    setPlanConfirmed(state, proposalId) {
      const planMsg = [...state.messages]
        .reverse()
        .find((m) => m.plan?.proposalId === proposalId);
      if (!planMsg) return;
      planMsg.planConfirming = false;
      planMsg.planConfirmed = true;
      planMsg.planFailed = false;
      planMsg.planConfirmationError = null;
      if (!Number.isInteger(planMsg.planExecStart)) {
        planMsg.planExecStart = (planMsg.timeline || []).length;
      }
      planMsg.done = false;
    },
    setLastAssistantRequestError(state, error) {
      let last = selectedAssistant(state);
      if (!last || last.role !== "assistant") {
        last = emptyAssistantMessage();
        state.messages.push(last);
      }
      last.requestError = error;
      last.done = true;
      if (last.messageState === "running") last.messageState = "interrupted";
    },
    setFormReply(state, { messageId, formId, answers }) {
      const id = String(formId ?? "");
      if (!id) return;
      const msg = state.messages.find((m) => m.id === messageId);
      if (!msg || msg.role !== "assistant") return;
      msg.formReplies = {
        ...(msg.formReplies || {}),
        [id]: {
          answers: answers && typeof answers === "object" ? answers : {},
        },
      };
    },
    setPlanCancelled(state, proposalId) {
      const planMsg = [...state.messages]
        .reverse()
        .find((m) => m.plan?.proposalId === proposalId && canConfirmPlan(m));
      if (planMsg) planMsg.planCancelled = true;
    },
    supersedePendingPlans(state) {
      for (const m of state.messages) {
        if (isOpenPlan(m)) m.planSuperseded = true;
      }
    },
  },

  actions: {
    async startNewConversation({ commit, dispatch }) {
      ++navigationVersion;
      await dispatch("detachLiveStream");
      commit("clearMessages");
    },

    detachLiveStream({ commit }) {
      const previous = activeStream;
      activeStream = null;
      previous?.controller.abort();
      commit("setLoading", false);
      commit("setStopping", false);
    },

    async loadLastConversation({ state, commit, dispatch }) {
      if (state.messages.length || state.isLoading) return;
      const version = navigationVersion;
      try {
        const { http } = await import("@/main");
        const res = await http.get("/ai/conversations/latest", {
          cache: false,
        });
        const data = res.data;
        if (
          !data ||
          version !== navigationVersion ||
          state.messages.length ||
          state.isLoading
        )
          return;

        commit("setSessionId", data.sessionId);
        if (data.title) commit("setConversationTitle", data.title);

        const messages = hydrateConversation(data.messages || []);
        commit("setMessages", messages);
        dispatch("maybeAttachLiveStream", data);
      } catch {
        /* silent */
      }
    },

    maybeAttachLiveStream({ dispatch, state }, data) {
      if (!data?.generating) return;
      if (activeStream?.sessionId === state.sessionId) return;
      return dispatch("attachLiveStream");
    },

    async reloadConversationMessages({ state, commit }) {
      if (!state.sessionId) return;
      const sessionId = state.sessionId;
      const streamAtRead = activeStream;
      try {
        const { http } = await import("@/main");
        const res = await http.get(
          `/ai/conversations/${encodeURIComponent(sessionId)}`,
          { cache: false },
        );
        const data = res.data;
        if (
          !data ||
          state.sessionId !== sessionId ||
          activeStream !== streamAtRead
        )
          return;
        if (data.title) commit("setConversationTitle", data.title);
        commit(
          "setMessages",
          reconcilePlanMessages(
            state.messages,
            hydrateConversation(data.messages || []),
          ),
        );
      } catch {
        /* silent */
      }
    },

    async attachLiveStream({ state, commit, dispatch }) {
      if (!state.sessionId) return;
      const sessionId = state.sessionId;
      const last = state.messages.at(-1);
      // An executed uncanonical tail can never gain a review segment; a
      // canonical one may still be the parent of a live review run.
      if (last?.planExecuted && !last?.messageId) return;
      const stream = beginStream(sessionId);
      stream.started = true;
      commit("setLoading", true);
      const { signal } = stream.controller;

      try {
        const response = await fetchWithAuth(
          `/api/ai/chat/stream?sessionId=${encodeURIComponent(sessionId)}`,
          { method: "GET", headers: { "X-CSRF": "1" }, signal },
        );
        if (!ownsStream(state, stream)) return;
        if (response.status === 404) {
          await dispatch("reloadConversationMessages");
          if (
            ownsStream(state, stream) &&
            state.messages.at(-1)?.executionProposalId &&
            !state.messages.at(-1)?.done
          )
            commit("setLastAssistantRequestError", {
              code: "",
              status: null,
              reason: "",
            });
          return;
        }
        if (!response.ok) throw await responseError(response);

        const current = state.messages.at(-1);
        // Canonical runs are replayed and deduplicated by runId/messageId/sequence:
        // no plan-replay splice or snapshot reset - that is the legacy path only.
        if (!state.messages.some((m) => m.messageId)) {
          commit("preparePlanReplay");
          if (current?.role === "assistant") {
            if (
              current.plan &&
              current.planConfirmed &&
              !current.planExecuted
            ) {
              commit("resetPlanExecution");
            } else if (!current.plan) {
              commit("replaceLastAssistant", emptyAssistantMessage());
            }
          } else {
            commit("addMessage", emptyAssistantMessage());
          }
        }
        await consumeSse(response, turnCommit(state, commit), {
          execution: !!current?.planConfirmed && !current?.planExecuted,
        });
        if (ownsStream(state, stream)) {
          if (stream.stopped) commit("setTurnStopped");
          await dispatch("reloadConversationMessages");
        }
      } catch (err) {
        if (!ownsStream(state, stream)) return;
        if (err.name === "AbortError") {
          if (stream.stopped) {
            commit("setTurnStopped");
          }
        } else {
          await dispatch("reloadConversationMessages");
          const planMessage = state.messages.at(-1);
          if (!ownsStream(state, stream)) return;
          if (planMessage?.planConfirmed && !planMessage.planExecuted) {
            commit("setPlanConfirmationError", {
              proposalId: planMessage.plan.proposalId,
              error: { stage: "execution", ...requestErrorDetails(err) },
            });
            commit("updateLastAssistantMessage", { done: true });
          } else if (!planMessage?.planExecuted) {
            commit("setLastAssistantRequestError", requestErrorDetails(err));
          }
        }
      } finally {
        if (ownsStream(state, stream)) {
          activeStream = null;
          commit("setLoading", false);
          commit("setStopping", false);
        }
      }
    },

    async sendMessage({ state, commit, dispatch }, payload) {
      if (state.isLoading) return;
      const activeSessionId = state.sessionId; // capture to detect session switches mid-stream

      const content = typeof payload === "string" ? payload : payload?.content;
      const contextCard =
        typeof payload === "object" && payload?.contextCard
          ? payload.contextCard
          : null;
      const formReply =
        typeof payload === "object" && payload?.formReply
          ? payload.formReply
          : null;
      // Composer attachments are an independent unsent draft: a bound form
      // reply carries only its own answers, and the composer files stay put.
      const attached = formReply ? [] : [...(state.attachedFiles || [])];
      const attachments = attachmentMetaList(attached);
      const text =
        content || (attachments.length ? t("ai.attachedFilesMessage") : "");
      if (!text) return;

      if (!formReply && !isAiControlMessage(text))
        commit("supersedePendingPlans");

      const stream = beginStream(activeSessionId);
      commit("setLoading", true);

      const userMsg = {
        id: Date.now(),
        role: "user",
        content: text,
        attachments,
        ...(contextCard ? { contextCard } : {}),
        ...(formReply ? { formReply } : {}),
      };
      commit("addMessage", userMsg);
      if (!formReply) commit("setAttachedFiles", []);

      if (attached.length) {
        try {
          const staged = await uploadChatAttachments(state.sessionId, attached);
          if (!ownsStream(state, stream)) return;
          commit(
            "patchLastUserAttachments",
            applyStagedIds(attachments, staged),
          );
        } catch (err) {
          if (!ownsStream(state, stream)) return;
          commit("removeLastUserMessage");
          commit("setAttachedFiles", attached);
          commit("setLoading", false);
          activeStream = null;
          commit("addMessage", {
            id: Date.now(),
            role: "assistant",
            content: t("ai.attachFailed", { message: err.message }),
            done: true,
          });
          return;
        }
      }

      commit("addMessage", emptyAssistantMessage());

      // Persist the user turn before the server live-turn can append it. The live
      // endpoint remains the sole writer for the generated assistant turn.
      await dispatch("syncHistory");
      if (!ownsStream(state, stream)) return;

      const history = buildHistory(state.messages);
      const streamCommit = turnCommit(state, commit);

      try {
        stream.started = true;
        const response = await fetchWithAuth("/api/ai/chat", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF": "1" },
          body: JSON.stringify({
            messages: history,
            pageContext: state.pageContext,
            sessionId: activeSessionId,
            modelId: state.selectedModelId
              ? parseInt(state.selectedModelId)
              : null,
          }),
          signal: stream.controller.signal,
        });

        if (!response.ok) throw await responseError(response);
        await consumeSse(response, streamCommit);
        if (ownsStream(state, stream)) {
          if (stream.stopped) commit("setTurnStopped");
          await dispatch("reloadConversationMessages");
        }
      } catch (err) {
        // If the session was switched while streaming, don't corrupt the new conversation's messages
        if (!ownsStream(state, stream)) return;

        if (err.name === "AbortError") {
          if (stream.stopped) {
            commit("setTurnStopped");
          }
        } else {
          await dispatch("reloadConversationMessages");
          if (!ownsStream(state, stream)) return;
          commit("setLastAssistantRequestError", requestErrorDetails(err));
        }
      } finally {
        if (ownsStream(state, stream)) {
          activeStream = null;
          commit("setStopping", false);
          const last = state.messages.at(-1);
          if (!stream.stopped && last?.role === "assistant" && !last.done) {
            await dispatch("attachLiveStream");
          } else {
            commit("setLoading", false);
          }
        }
      }
    },

    submitFormReply({ state, commit, dispatch }, payload) {
      if (state.isLoading || payload?.sessionId !== state.sessionId)
        return false;
      const owner = state.messages.find((m) => m.id === payload.messageId);
      if (!isCurrentFormOwner(state.messages, owner)) return false;
      if (
        payload.assistantMessageId &&
        owner.messageId !== payload.assistantMessageId
      )
        return false;
      const formId = String(payload.formId || "");
      if (
        !formId ||
        !visibleChatForms(owner).some((form) => String(form.id) === formId) ||
        owner.formReplies?.[formId] ||
        typeof payload.prompt !== "string" ||
        !payload.prompt.trim()
      )
        return false;
      const answers =
        payload.answers && typeof payload.answers === "object"
          ? payload.answers
          : {};
      commit("setFormReply", { messageId: owner.id, formId, answers });
      return dispatch("sendMessage", {
        content: payload.prompt,
        formReply: {
          formId,
          answers,
          ...(owner.messageId ? { assistantMessageId: owner.messageId } : {}),
          ...(Number.isInteger(owner.dbId)
            ? { assistantDbId: owner.dbId }
            : {}),
        },
      });
    },

    async stopGeneration({ state, commit, dispatch }) {
      const stream = activeStream;
      if (!stream || !ownsStream(state, stream) || state.isStopping) return;
      commit("setStopping", true);
      try {
        if (!stream.started) {
          stream.stopped = true;
          stream.controller.abort();
          if (state.messages.at(-1)?.role !== "assistant")
            commit("addMessage", emptyAssistantMessage());
          commit("setTurnStopped");
          commit("setLoading", false);
          commit("setStopping", false);
          activeStream = null;
          await dispatch("syncHistory");
          return;
        }
        const response = await fetchWithAuth("/api/ai/stop", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF": "1" },
          body: JSON.stringify({ sessionId: stream.sessionId }),
        });
        if (!response.ok) throw await responseError(response);
        const result = await response.json();
        if (!ownsStream(state, stream)) return;
        if (result.stopped) {
          stream.stopped = true;
          commit("setTurnStopped");
          commit("setLoading", false);
          commit("setStopping", false);
          stream.controller.abort();
        }
      } catch (error) {
        if (ownsStream(state, stream))
          commit("updateLastAssistantMessage", {
            requestError: { stage: "stop", ...requestErrorDetails(error) },
          });
      } finally {
        if (ownsStream(state, stream)) commit("setStopping", false);
      }
    },

    async confirmPlan({ state, commit, dispatch }, selectedProposalId) {
      const planMsg = state.messages.find(
        (m) => m.plan?.proposalId === selectedProposalId,
      );
      const steps = planMsg?.plan?.steps || [];
      const proposalId = planMsg?.plan?.proposalId;
      const executable =
        proposalId && steps.length > 0 && steps.every((s) => s.tool && s.args);

      if (
        !canConfirmPlan(planMsg) ||
        state.messages.at(-1) !== planMsg ||
        state.isLoading
      )
        return;

      if (!executable) {
        commit("updateLastAssistantMessage", {
          delta: `\n\n${t("store.aiChatModule.planUnavailable")}`,
        });
        return;
      }

      commit("setPlanConfirming", { proposalId, confirming: true });
      const activeSessionId = state.sessionId;
      const stream = beginStream(activeSessionId);
      commit("setLoading", true);
      let responseAccepted = false;

      try {
        stream.started = true;
        const response = await fetchWithAuth("/api/ai/execute-plan", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF": "1" },
          body: JSON.stringify({
            proposalId,
            sessionId: activeSessionId,
          }),
          signal: stream.controller.signal,
        });
        if (!response.ok) {
          throw await responseError(response);
        }
        responseAccepted = true;
        if (!ownsStream(state, stream)) return;
        commit("setPlanConfirmed", proposalId);
        await consumeSse(response, turnCommit(state, commit), {
          execution: true,
        });
        if (ownsStream(state, stream)) {
          if (stream.stopped) commit("setTurnStopped");
          await dispatch("reloadConversationMessages");
        }
      } catch (err) {
        if (!ownsStream(state, stream)) return;
        if (err.name === "AbortError") {
          await dispatch("reloadConversationMessages");
          if (!ownsStream(state, stream)) return;
          if (stream.stopped) {
            commit("setTurnStopped");
          }
        } else {
          await dispatch("reloadConversationMessages");
          if (!ownsStream(state, stream)) return;
          if (
            state.messages.find((m) => m.plan?.proposalId === proposalId)
              ?.planExecuted
          )
            return;
          commit("setPlanConfirmationError", {
            proposalId,
            error: {
              stage: responseAccepted ? "execution" : "confirmation",
              ...requestErrorDetails(err),
            },
          });
          commit("updateLastAssistantMessage", { done: true });
        }
      } finally {
        if (ownsStream(state, stream)) {
          activeStream = null;
          commit("setStopping", false);
          commit("setPlanConfirming", { proposalId, confirming: false });
          const last = state.messages.at(-1);
          if (!stream.stopped && last?.role === "assistant" && !last.done) {
            await dispatch("attachLiveStream");
          } else {
            commit("setLoading", false);
          }
        }
      }
    },

    async syncHistory({ state, commit, dispatch }) {
      const sessionId = state.sessionId;
      const streamAtSync = activeStream;
      // Include completed assistant messages and all user messages; skip empty assistant placeholders
      const msgs = state.messages.filter(
        (m) =>
          (m.role === "user" && !isAiControlMessage(m)) ||
          (m.role === "assistant" &&
            (m.done ||
              (m.plan &&
                (m.planConfirmed || m.planCancelled || m.planSuperseded))) &&
            (m.content || m.toolCalls?.length || m.plan || m.turnStopped)),
      );
      if (msgs.length < 1) return;
      // Capture the exact submitted set as immutable identity proof: response
      // correlation must only reach these messages, never later placeholders
      // or appended turns. The request body still serializes the full rows.
      const submitted = msgs.map((m) => ({
        id: m.id,
        dbId: m.dbId,
        role: m.role,
        messageId: m.messageId,
      }));
      const modelName =
        [...msgs].reverse().find((m) => m.modelName)?.modelName ?? null;
      try {
        const { http } = await import("@/main");
        const body = {
          sessionId,
          modelName,
          messages: msgs.map((m) => ({
            role: m.role,
            content: m.content || "",
            toolCallsJson: serializeToolCallsJson(m),
            createdAt: new Date().toISOString(),
            dbId: m.dbId || undefined,
            clientMessageId: String(m.id),
          })),
        };
        const res = await http.post("/ai/conversations/messages", body);
        if (state.sessionId !== sessionId || activeStream !== streamAtSync)
          return;
        const saved = res.data?.saved ?? [];
        if (saved.length > 0) {
          commit("applyDbIds", { saved, submitted });
          if (!state.conversationTitle) {
            const firstUserMsg = state.messages.find(
              (m) => m.role === "user" && !isHiddenUserMessage(m),
            );
            const seed = titleSeedFromUserMessage(firstUserMsg);
            if (seed) {
              dispatch("generateTitle", seed);
            }
          }
        }
      } catch {
        /* silent */
      }
    },

    async loadConversation({ commit, dispatch }, sessionId) {
      // Navigation detaches the subscriber, never the server-owned generation.
      await dispatch("detachLiveStream");
      const version = ++navigationVersion;
      try {
        const { http } = await import("@/main");
        const res = await http.get(
          `/ai/conversations/${encodeURIComponent(sessionId)}`,
          { cache: false },
        );
        const data = res.data;
        if (!data || version !== navigationVersion) return;
        commit("setSessionId", data.sessionId);
        commit("setConversationTitle", data.title ?? null);
        const messages = hydrateConversation(data.messages || []);
        commit("setMessages", messages);
        dispatch("maybeAttachLiveStream", data);
      } catch {
        /* silent */
      }
    },

    async renameConversation({ state, commit }, title) {
      commit("setConversationTitle", title);
      try {
        const { http } = await import("@/main");
        await http.patch(`/ai/conversations/${state.sessionId}`, { title });
      } catch {
        // title stays updated locally even on error
      }
    },

    async deleteConversation({ state, commit }, sessionId) {
      try {
        const { http } = await import("@/main");
        await http.delete(`/ai/conversations/${sessionId}`);
        // If the deleted conversation is the active one, start a new one
        if (state.sessionId === sessionId) {
          commit("clearMessages");
        }
      } catch {
        /* silent */
      }
    },

    async generateTitle({ state, commit }, firstUserMessage) {
      if (state.conversationTitle) return;
      const sessionId = state.sessionId;
      try {
        const { http } = await import("@/main");
        const res = await http.post(
          `/ai/conversations/${sessionId}/generate-title`,
          { firstUserMessage },
        );
        if (
          res.data?.title &&
          state.sessionId === sessionId &&
          !state.conversationTitle
        )
          commit("setConversationTitle", res.data.title);
      } catch {
        /* silent */
      }
    },

    async sendFeedback({ state }, { dbId, rating }) {
      if (!dbId) return;
      const msg = state.messages.find((m) => m.dbId === dbId);
      if (msg) msg.feedbackRating = rating;
      try {
        const { http } = await import("@/main");
        await http.post(`/ai/messages/${dbId}/feedback`, { rating });
      } catch {
        if (msg) msg.feedbackRating = null;
      }
    },

    cancelPlan({ state, commit, dispatch }, proposalId) {
      if (
        state.isLoading ||
        !state.messages.some(
          (m) => m.plan?.proposalId === proposalId && canConfirmPlan(m),
        )
      )
        return;
      commit("setPlanCancelled", proposalId);
      return dispatch("syncHistory");
    },
  },
};
