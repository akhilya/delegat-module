import { fetchWithAuth } from "@/utils/fetchWithAuth";
import { t } from "@/i18n";

/** Model-facing control messages - always English (system prompts are English). */
const AI_CONFIRM_PLAN = "Confirmed, proceed";
const AI_CANCEL_PLAN =
  "Cancelling the proposed plan - do NOT execute it. Do not roll anything back and do not call rollback_last_operation: the plan was never executed. Just confirm the cancellation.";

function newSessionId() { return crypto.randomUUID(); }

let abortController = null;

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
      const assistantContent = [];
      const toolResults = [];

      for (const block of m.apiBlocks) {
        if (block.role === "assistant") {
          assistantContent.push(...(block.content || []));
        } else if (block.role === "tool_result") {
          toolResults.push(block);
        }
      }

      if (assistantContent.length) {
        result.push({ role: "assistant", content: assistantContent });
      }
      if (toolResults.length) {
        result.push({ role: "user", content: toolResults });
      }

      if (m.plan) {
        const steps = (m.plan.steps || []).map((s) => `- ${s.description || s.tool}`).join("\n");
        const status = m.planConfirmed ? "confirmed by user" : m.planCancelled ? "cancelled by user" : "pending";
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
      const toolLines = m.toolCalls.map((tc) => {
        const input = (tc.input || "").length > 200 ? tc.input.slice(0, 200) + "..." : (tc.input || "");
        return `${tc.name}(${input})${tc.summary ? ` -> ${tc.summary}` : ""}`;
      }).join("\n");
      content = `[ALREADY EXECUTED IN PREVIOUS TURN - DO NOT REPEAT OR IMITATE:\n${toolLines}]\n${content}`;
    }

    if (m.plan) {
      const steps = (m.plan.steps || []).map((s) => `- ${s.description || s.tool}`).join("\n");
      const status = m.planConfirmed ? "confirmed by user" : m.planCancelled ? "cancelled by user" : "pending";
      content += `\n[Plan: "${m.plan.summary || m.plan.description || ""}" | Steps:\n${steps}\n| Status: ${status}]`;
    }

    result.push({ role: "assistant", content });
  }

  return result;
}

// Shared SSE consumer for /ai/chat and /ai/execute-plan
async function consumeSse(response, commit, dispatch) {
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
              const effects = event.data?.effects || [];
              if (effects.includes("mapping")) commit("triggerMappingRefresh");
              if (effects.includes("product")) commit("triggerProductRefresh");
            }
            break;
          case "plan":
            commit("updateLastAssistantMessage", { plan: event.data });
            break;
          case "done":
            commit("updateLastAssistantMessage", { done: true, modelName: event.data?.modelName });
            dispatch?.("syncHistory");
            break;
          case "error":
            commit("updateLastAssistantMessage", { delta: `\n\n${event.data?.message}` });
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
    setMessages(state, messages) {
      state.messages = messages;
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
    updateLastAssistantMessage(state, { delta, textBlock, toolCall, plan, done, modelName, dbId }) {
      const last = state.messages[state.messages.length - 1];
      if (!last || last.role !== "assistant") return;

      if (delta !== undefined) last.content += delta;
      if (modelName) last.modelName = modelName;
      if (dbId !== undefined) last.dbId = dbId;

      if (!last.apiBlocks) last.apiBlocks = [];

      if (textBlock && textBlock.length > 0) {
        const assistantBlock = last.apiBlocks.find((b) => b.role === "assistant");
        if (assistantBlock) {
          const textEntry = assistantBlock.content.find((c) => c.type === "text");
          if (textEntry) textEntry.text += textBlock;
          else assistantBlock.content.push({ type: "text", text: textBlock });
        } else {
          last.apiBlocks.push({ role: "assistant", content: [{ type: "text", text: textBlock }] });
        }
      }

      if (toolCall) {
        last.toolCalls.push(toolCall);

        let assistantBlock = last.apiBlocks.find((b) => b.role === "assistant");
        if (!assistantBlock) {
          assistantBlock = { role: "assistant", content: [] };
          last.apiBlocks.push(assistantBlock);
        }
        let inputObj = {};
        try { inputObj = JSON.parse(toolCall.input || "{}"); } catch {}
        assistantBlock.content.push({
          type: "tool_use",
          id: toolCall.id || toolCall.name,
          name: toolCall.name,
          input: inputObj,
        });
      }

      if (plan) last.plan = plan;
      if (done) last.done = true;
    },
    updateToolCall(state, { id, name, status, summary, result }) {
      const last = state.messages[state.messages.length - 1];
      if (!last) return;
      const tc = last.toolCalls?.find((t) =>
        (id && t.id === id) || (!id && t.name === name && t.status === "running")
      );
      if (tc) {
        tc.status = status;
        if (summary) tc.summary = summary;
        if (result !== undefined && result !== null) {
          try { tc.result = typeof result === "string" ? JSON.parse(result) : result; }
          catch { tc.result = result; }
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
    setLastPlanConfirmed(state) {
      const planMsg = [...state.messages].reverse().find((m) => m.plan && !m.planConfirmed);
      if (planMsg) planMsg.planConfirmed = true;
    },
    setLastPlanCancelled(state) {
      const planMsg = [...state.messages].reverse().find((m) => m.plan && !m.planConfirmed);
      if (planMsg) planMsg.planCancelled = true;
    },
  },

  actions: {
    async startNewConversation({ state, commit, dispatch }) {
      if (state.isLoading) await dispatch("stopGeneration");
      commit("clearMessages");
    },

    async loadLastConversation({ commit }) {
      try {
        const { http } = await import("@/main");
        const res = await http.get("/ai/conversations/latest");
        const data = res.data;
        if (!data) return;

        commit("setSessionId", data.sessionId);
        if (data.title) commit("setConversationTitle", data.title);

        const messages = (data.messages || []).map((m) => ({
          id: m.dbId,
          dbId: m.dbId,
          role: m.role,
          content: m.content || "",
          toolCalls: m.toolCallsJson
            ? JSON.parse(m.toolCallsJson).map((tc) => ({ ...tc, status: "done" }))
            : [],
          plan: null,
          apiBlocks: [],
          done: m.role === "assistant",
          feedbackRating: m.feedbackRating ?? null,
        }));
        commit("setMessages", messages);
      } catch { /* silent */ }
    },

    async sendMessage({ state, commit, dispatch }, text) {
      const activeSessionId = state.sessionId; // capture to detect session switches mid-stream

      const userMsg = { id: Date.now(), role: "user", content: text };
      commit("addMessage", userMsg);
      commit("setLoading", true);

      const assistantMsg = { id: Date.now() + 1, role: "assistant", content: "", toolCalls: [], plan: null, apiBlocks: [] };
      commit("addMessage", assistantMsg);

      // Save user message immediately so it's not lost if generation is interrupted
      dispatch("syncHistory");

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
            modelId: state.selectedModelId ? parseInt(state.selectedModelId) : null,
          }),
          signal: abortController.signal,
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        await consumeSse(response, commit, dispatch);
      } catch (err) {
        // If the session was switched while streaming, don't corrupt the new conversation's messages
        if (state.sessionId !== activeSessionId) return;

        if (err.name === "AbortError") {
          commit("updateLastAssistantMessage", { done: true });
          dispatch("syncHistory");
        } else if (err.message !== "Session expired") {
          commit("updateLastAssistantMessage", {
            delta: `\n\n${t("store.aiChatModule.connectionError", { message: err.message })}`,
            done: true,
          });
        }
      } finally {
        abortController = null;
        commit("setLoading", false);
      }
    },

    stopGeneration({ state }) {
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
      const planMsg = [...state.messages].reverse().find((m) => m.plan && !m.planConfirmed && !m.planCancelled);
      const steps = planMsg?.plan?.steps || [];
      const executable = steps.length > 0 && steps.every((s) => s.tool && s.args);

      commit("setLastPlanConfirmed");

      if (!executable) {
        dispatch("sendMessage", AI_CONFIRM_PLAN);
        return;
      }

      commit("addMessage", { id: Date.now(), role: "user", content: AI_CONFIRM_PLAN });
      commit("setLoading", true);
      commit("addMessage", { id: Date.now() + 1, role: "assistant", content: "", toolCalls: [], plan: null, apiBlocks: [] });
      abortController = new AbortController();

      try {
        const response = await fetchWithAuth("/api/ai/execute-plan", {
          method: "POST",
          headers: { "Content-Type": "application/json", "X-CSRF": "1" },
          body: JSON.stringify({ steps }),
          signal: abortController.signal,
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        await consumeSse(response, commit, dispatch);
      } catch (err) {
        if (err.name === "AbortError") {
          commit("updateLastAssistantMessage", { done: true });
        } else if (err.message !== "Session expired") {
          commit("updateLastAssistantMessage", {
            delta: `\n\n${t("store.aiChatModule.connectionError", { message: err.message })}`,
            done: true,
          });
        }
      } finally {
        abortController = null;
        commit("setLoading", false);
      }
    },

    async syncHistory({ state, commit, dispatch }) {
      // Include completed assistant messages and all user messages; skip empty assistant placeholders
      const msgs = state.messages.filter((m) =>
        m.role === "user" || (m.role === "assistant" && m.done && (m.content || m.toolCalls?.length))
      );
      if (msgs.length < 1) return;
      const modelName = [...msgs].reverse().find((m) => m.modelName)?.modelName ?? null;
      try {
        const { http } = await import("@/main");
        const body = {
          sessionId: state.sessionId,
          modelName,
          messages: msgs.map((m) => ({
            role: m.role,
            content: m.content || "",
            toolCallsJson: m.toolCalls?.length
              ? JSON.stringify(m.toolCalls.map((tc) => ({ name: tc.name, input: tc.input, summary: tc.summary })))
              : null,
            createdAt: new Date().toISOString(),
          })),
        };
        const res = await http.post("/ai/conversations/messages", body);
        const saved = res.data?.saved ?? [];
        if (saved.length > 0) {
          commit("applyDbIds", saved);
          if (!state.conversationTitle) {
            const firstUserMsg = state.messages.find((m) => m.role === "user");
            if (firstUserMsg?.content) {
              dispatch("generateTitle", firstUserMsg.content);
            }
          }
        }
      } catch { /* silent */ }
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
        const messages = (data.messages || []).map((m) => ({
          id: m.dbId,
          dbId: m.dbId,
          role: m.role,
          content: m.content || "",
          toolCalls: m.toolCallsJson
            ? JSON.parse(m.toolCallsJson).map((tc) => ({ ...tc, status: "done" }))
            : [],
          plan: null,
          apiBlocks: [],
          done: m.role === "assistant",
          feedbackRating: m.feedbackRating ?? null,
        }));
        commit("setMessages", messages);
      } catch { /* silent */ }
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

    async deleteConversation({ state, commit, dispatch }, sessionId) {
      try {
        const { http } = await import("@/main");
        await http.delete(`/ai/conversations/${sessionId}`);
        // If the deleted conversation is the active one, start a new one
        if (state.sessionId === sessionId) {
          commit("clearMessages");
        }
      } catch { /* silent */ }
    },

    async generateTitle({ state, commit }, firstUserMessage) {
      if (state.conversationTitle) return;
      try {
        const { http } = await import("@/main");
        const res = await http.post(`/ai/conversations/${state.sessionId}/generate-title`, { firstUserMessage });
        if (res.data?.title) commit("setConversationTitle", res.data.title);
      } catch { /* silent */ }
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
      dispatch("sendMessage", AI_CANCEL_PLAN);
    },
  },
};
