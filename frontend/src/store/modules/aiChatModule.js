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

let abortController = null;
let userStopped = false;

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

    if (m.apiBlocks?.length) {
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
          return `${tc.name}(${input})${tc.summary ? ` -> ${tc.summary}` : ""}`;
        })
        .join("\n");
      content = `[ALREADY EXECUTED IN PREVIOUS TURN - DO NOT REPEAT OR IMITATE:\n${toolLines}]\n${content}`;
    }

    if (m.plan) {
      const steps = (m.plan.steps || [])
        .map((s) => `- ${s.description || s.tool}`)
        .join("\n");
      const status = planHistoryStatus(m);
      content += `\n[Plan: "${m.plan.summary || m.plan.description || ""}" | Steps:\n${steps}\n| Status: ${status}]`;
    }

    result.push({ role: "assistant", content });
  }

  return result;
}

// Shared SSE consumer for /ai/chat and /ai/execute-plan
async function consumeSse(response, commit) {
  if (!response.body) throw new Error("No response body");
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    const lines = buffer.split("\n\n");
    buffer = lines.pop() ?? "";

    for (const line of lines) {
      if (!line.startsWith("data: ")) continue;
      try {
        const event = JSON.parse(line.slice(6));
        switch (event.type) {
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
              });
              let parsedResult = event.data?.result;
              if (typeof parsedResult === "string") {
                try {
                  parsedResult = JSON.parse(parsedResult);
                } catch {
                  parsedResult = null;
                }
              }
              const failed = toolResultFailed(
                parsedResult,
                event.data?.summary,
              );
              const effects = event.data?.effects || [];
              if (!failed) {
                if (effects.includes("mapping"))
                  commit("triggerMappingRefresh");
                if (effects.includes("product"))
                  commit("triggerProductRefresh");
              }
            }
            break;
          case "plan":
            commit("updateLastAssistantMessage", { plan: event.data });
            break;
          case "done": {
            commit("finalizeRunningTools");
            const patch = {
              done: true,
              modelName: event.data?.modelName,
            };
            if (typeof event.data?.ok === "boolean")
              patch.planFailed = !event.data.ok;
            commit("updateLastAssistantMessage", patch);
            break;
          }
          case "error":
            commit("finalizeRunningTools");
            if (event.data?.code) {
              commit("setLastAssistantRequestError", {
                code: event.data.code,
                status: Number.isInteger(event.data.status) ? event.data.status : null,
                reason: "",
              });
            } else {
              commit("updateLastAssistantMessage", {
                delta: `\n\n${event.data?.message}`,
              });
            }
            break;
        }
      } catch {
        // ignore parse errors
      }
    }
  }
}

export default {
  namespaced: true,

  state: () => ({
    isOpen: false,
    messages: [],
    sessionId: newSessionId(),
    conversationTitle: null,
    isLoading: false,
    pageContext: null,
    panelWidth: parseInt(localStorage.getItem("ai-panel-width") || "400"),
    mappingRefreshToken: 0,
    productRefreshToken: 0,
    fabsHidden: localStorage.getItem("fabs-hidden") === "1",
    selectedModelId: null,
    attachedFiles: [],
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
    setSessionId(state, id) {
      state.sessionId = id;
    },
    setConversationTitle(state, title) {
      state.conversationTitle = title;
    },
    applyDbIds(state, saved) {
      let idx = 0;
      for (const msg of state.messages) {
        if (!msg.dbId && idx < saved.length) {
          msg.dbId = saved[idx++].dbId;
        }
      }
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
        requestError,
      },
    ) {
      const last = state.messages[state.messages.length - 1];
      if (!last || last.role !== "assistant") return;

      if (resume) last.done = false;
      if (delta !== undefined) last.content += delta;
      if (delta) last.timeline = appendTextDelta(last.timeline, delta);
      if (modelName) last.modelName = modelName;
      if (dbId !== undefined) last.dbId = dbId;
      if (planFailed === true) last.planFailed = true;
      if (planFailed === false) last.planFailed = false;
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

      if (plan) last.plan = plan;
      if (done) {
        last.done = true;
        if (last.planConfirmed && !last.planCancelled) last.planExecuted = true;
      }
    },
    finalizeRunningTools(state) {
      const last = state.messages[state.messages.length - 1];
      if (!last) return;
      finalizeTurnTools(last);
    },
    updateToolCall(state, { id, name, status, summary, result }) {
      const last = state.messages[state.messages.length - 1];
      if (!last) return;
      const tc = last.toolCalls?.find(
        (t) =>
          (id && t.id === id) ||
          (!id && t.name === name && t.status === "running"),
      );
      if (tc) {
        tc.status = status;
        if (summary) tc.summary = summary;
        if (result !== undefined && result !== null) {
          try {
            tc.result =
              typeof result === "string" ? JSON.parse(result) : result;
          } catch {
            tc.result = result;
          }
        }
        if (
          last.planConfirmed &&
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
    },
    clearMessages(state) {
      state.messages = [];
      state.sessionId = newSessionId();
      state.conversationTitle = null;
      state.attachedFiles = [];
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
      let last = state.messages.at(-1);
      if (!last || last.role !== "assistant") {
        last = emptyAssistantMessage();
        state.messages.push(last);
      }
      last.requestError = error;
      last.done = true;
    },
    setFormReply(state, { messageId, formId, answers }) {
      const id = String(formId ?? "");
      if (!id) return;
      const msg =
        state.messages.find((m) => m.id === messageId) ||
        [...state.messages].reverse().find((m) => m.role === "assistant");
      if (!msg || msg.role !== "assistant") return;
      msg.formReplies = {
        ...(msg.formReplies || {}),
        [id]: {
          answers: answers && typeof answers === "object" ? answers : {},
        },
      };
    },
    setLastPlanCancelled(state) {
      const planMsg = [...state.messages]
        .reverse()
        .find((m) => m.plan && !m.planConfirmed && !m.planSuperseded);
      if (planMsg) planMsg.planCancelled = true;
    },
    supersedePendingPlans(state) {
      for (const m of state.messages) {
        if (isOpenPlan(m)) m.planSuperseded = true;
      }
    },
  },

  actions: {
    async startNewConversation({ state, commit, dispatch }) {
      if (state.isLoading) await dispatch("stopGeneration");
      commit("clearMessages");
    },

    async loadLastConversation({ commit, dispatch }) {
      try {
        const { http } = await import("@/main");
        const res = await http.get("/ai/conversations/latest");
        const data = res.data;
        if (!data) return;

        commit("setSessionId", data.sessionId);
        if (data.title) commit("setConversationTitle", data.title);

        const messages = hydrateConversation(data.messages || []);
        commit("setMessages", messages);
        await dispatch("maybeAttachLiveStream", data);
      } catch {
        /* silent */
      }
    },

    maybeAttachLiveStream({ dispatch, state }, data) {
      if (!data?.generating) return;
      const last = state.messages.at(-1);
      if (
        last?.role === "assistant" &&
        last.done &&
        !(last.plan && last.planConfirmed && !last.planExecuted)
      )
        return;
      return dispatch("attachLiveStream");
    },

    async reloadConversationMessages({ state, commit }) {
      if (!state.sessionId) return;
      const sessionId = state.sessionId;
      try {
        const { http } = await import("@/main");
        const res = await http.get(
          `/ai/conversations/${encodeURIComponent(sessionId)}`,
        );
        const data = res.data;
        if (!data || state.sessionId !== sessionId) return;
        if (data.title) commit("setConversationTitle", data.title);
        commit("setMessages", hydrateConversation(data.messages || []));
      } catch {
        /* silent */
      }
    },

    async attachLiveStream({ state, commit, dispatch }) {
      if (!state.sessionId) return;
      const sessionId = state.sessionId;
      const last = state.messages.at(-1);
      if (last?.role === "assistant" && last.done && !last.plan) return;

      userStopped = false;
      commit("setLoading", true);
      abortController = new AbortController();
      const { signal } = abortController;

      try {
        const response = await fetchWithAuth(
          `/api/ai/chat/stream?sessionId=${encodeURIComponent(sessionId)}`,
          { method: "GET", headers: { "X-CSRF": "1" }, signal },
        );
        if (response.status === 404) {
          await dispatch("reloadConversationMessages");
          return;
        }
        if (!response.ok) throw await responseError(response);

        const current = state.messages.at(-1);
        if (current?.role === "assistant") {
          if (current.done && !current.plan) return;
          if (current.plan && current.planConfirmed && !current.planExecuted) {
            commit("updateLastAssistantMessage", { resume: true });
          } else if (!current.plan) {
            commit("replaceLastAssistant", emptyAssistantMessage());
          }
        } else {
          commit("addMessage", emptyAssistantMessage());
        }
        await consumeSse(response, commit);
        if (state.sessionId === sessionId) {
          if (userStopped) commit("updateLastAssistantMessage", { done: true });
          await dispatch("reloadConversationMessages");
        }
      } catch (err) {
        if (state.sessionId !== sessionId) return;
        if (err.name === "AbortError") {
          if (userStopped) {
            commit("updateLastAssistantMessage", { done: true });
          }
        } else {
          await dispatch("reloadConversationMessages");
          commit("setLastAssistantRequestError", requestErrorDetails(err));
        }
      } finally {
        abortController = null;
        if (state.sessionId === sessionId) commit("setLoading", false);
      }
    },

    async sendMessage({ state, commit, dispatch }, payload) {
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
      const attached = [...(state.attachedFiles || [])];
      const attachments = attachmentMetaList(attached);
      const text =
        content || (attachments.length ? t("ai.attachedFilesMessage") : "");
      if (!text) return;

      if (!formReply && !isAiControlMessage(text))
        commit("supersedePendingPlans");

      userStopped = false;
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
      commit("setAttachedFiles", []);

      if (attached.length) {
        try {
          const staged = await uploadChatAttachments(state.sessionId, attached);
          commit(
            "patchLastUserAttachments",
            applyStagedIds(attachments, staged),
          );
        } catch (err) {
          commit("removeLastUserMessage");
          commit("setAttachedFiles", attached);
          commit("setLoading", false);
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
      if (state.sessionId !== activeSessionId) {
        commit("setLoading", false);
        return;
      }

      abortController = new AbortController();

      const history = buildHistory(state.messages);

      try {
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
          signal: abortController.signal,
        });

        if (!response.ok) throw await responseError(response);
        await consumeSse(response, commit);
        if (state.sessionId === activeSessionId) {
          if (userStopped) commit("updateLastAssistantMessage", { done: true });
          await dispatch("reloadConversationMessages");
        }
      } catch (err) {
        // If the session was switched while streaming, don't corrupt the new conversation's messages
        if (state.sessionId !== activeSessionId) return;

        if (err.name === "AbortError") {
          if (userStopped) {
            commit("updateLastAssistantMessage", { done: true });
          }
        } else {
          await dispatch("reloadConversationMessages");
          commit("setLastAssistantRequestError", requestErrorDetails(err));
        }
      } finally {
        abortController = null;
        if (state.sessionId === activeSessionId) {
          const last = state.messages.at(-1);
          if (!userStopped && last?.role === "assistant" && !last.done) {
            await dispatch("attachLiveStream");
          } else {
            commit("setLoading", false);
          }
        }
      }
    },

    stopGeneration({ state }) {
      userStopped = true;
      abortController?.abort();
      if (state.sessionId) {
        fetchWithAuth("/api/ai/stop", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF": "1" },
          body: JSON.stringify({ sessionId: state.sessionId }),
        }).catch(() => {});
      }
    },

    async confirmPlan({ state, commit, dispatch }) {
      const planMsg = [...state.messages].reverse().find((m) => isOpenPlan(m));
      const steps = planMsg?.plan?.steps || [];
      const proposalId = planMsg?.plan?.proposalId;
      const executable =
        proposalId && steps.length > 0 && steps.every((s) => s.tool && s.args);

      if (
        !planMsg ||
        planMsg.planConfirming ||
        planMsg.planConfirmationError ||
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
      userStopped = false;
      const activeSessionId = state.sessionId;
      commit("setLoading", true);
      abortController = new AbortController();
      let responseAccepted = false;

      try {
        const response = await fetchWithAuth("/api/ai/execute-plan", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF": "1" },
          body: JSON.stringify({
            proposalId,
            sessionId: activeSessionId,
          }),
          signal: abortController.signal,
        });
        if (!response.ok) {
          throw await responseError(response);
        }
        responseAccepted = true;
        commit("setPlanConfirmed", proposalId);
        await consumeSse(response, commit);
        if (state.sessionId === activeSessionId) {
          if (userStopped) commit("updateLastAssistantMessage", { done: true });
          await dispatch("reloadConversationMessages");
        }
      } catch (err) {
        if (state.sessionId !== activeSessionId) return;
        if (err.name === "AbortError") {
          await dispatch("reloadConversationMessages");
          if (userStopped) {
            commit("updateLastAssistantMessage", { done: true });
          }
        } else {
          await dispatch("reloadConversationMessages");
          commit("setPlanConfirmationError", {
            proposalId,
            error: {
              stage: responseAccepted ? "execution" : "confirmation",
              ...requestErrorDetails(err),
            },
          });
        }
      } finally {
        abortController = null;
        commit("setPlanConfirming", { proposalId, confirming: false });
        if (state.sessionId === activeSessionId) {
          const last = state.messages.at(-1);
          if (!userStopped && last?.role === "assistant" && !last.done) {
            await dispatch("attachLiveStream");
          } else {
            commit("setLoading", false);
          }
        }
      }
    },

    async syncHistory({ state, commit, dispatch }) {
      // Include completed assistant messages and all user messages; skip empty assistant placeholders
      const msgs = state.messages.filter(
        (m) =>
          (m.role === "user" && !isAiControlMessage(m)) ||
          (m.role === "assistant" &&
            (m.done ||
              (m.plan &&
                (m.planConfirmed || m.planCancelled || m.planSuperseded))) &&
            (m.content || m.toolCalls?.length || m.plan)),
      );
      if (msgs.length < 1) return;
      const modelName =
        [...msgs].reverse().find((m) => m.modelName)?.modelName ?? null;
      try {
        const { http } = await import("@/main");
        const body = {
          sessionId: state.sessionId,
          modelName,
          messages: msgs.map((m) => ({
            role: m.role,
            content: m.content || "",
            toolCallsJson: serializeToolCallsJson(m),
            createdAt: new Date().toISOString(),
            dbId: m.dbId || undefined,
          })),
        };
        const res = await http.post("/ai/conversations/messages", body);
        const saved = res.data?.saved ?? [];
        if (saved.length > 0) {
          commit("applyDbIds", saved);
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

    async loadConversation({ state, commit, dispatch }, sessionId) {
      // Stop any ongoing generation before switching conversations
      if (state.isLoading) await dispatch("stopGeneration");
      try {
        const { http } = await import("@/main");
        const res = await http.get(`/ai/conversations/${sessionId}`);
        const data = res.data;
        if (!data) return;
        commit("setSessionId", data.sessionId);
        commit("setConversationTitle", data.title ?? null);
        const messages = hydrateConversation(data.messages || []);
        commit("setMessages", messages);
        await dispatch("maybeAttachLiveStream", data);
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
      try {
        const { http } = await import("@/main");
        const res = await http.post(
          `/ai/conversations/${state.sessionId}/generate-title`,
          { firstUserMessage },
        );
        if (res.data?.title) commit("setConversationTitle", res.data.title);
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

    cancelPlan({ commit, dispatch }) {
      commit("setLastPlanCancelled");
      dispatch("syncHistory");
    },
  },
};
