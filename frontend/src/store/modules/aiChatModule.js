export default {
  namespaced: true,

  state: () => ({
    isOpen: false,
    messages: [],
    isLoading: false,
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
    updateLastAssistantMessage(state, { delta, toolCall, plan, done }) {
      const last = state.messages[state.messages.length - 1];
      if (!last || last.role !== "assistant") return;
      if (delta !== undefined) last.content += delta;
      if (toolCall) last.toolCalls.push(toolCall);
      if (plan) last.plan = plan;
      if (done) last.done = true;
    },
    updateToolCall(state, { name, status, summary }) {
      const last = state.messages[state.messages.length - 1];
      if (!last) return;
      const tc = last.toolCalls?.find((t) => t.name === name && t.status === "running");
      if (tc) { tc.status = status; if (summary) tc.summary = summary; }
    },
    setLoading(state, val) {
      state.isLoading = val;
    },
    clearMessages(state) {
      state.messages = [];
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
    async sendMessage({ state, commit }, text) {
      const userMsg = { id: Date.now(), role: "user", content: text };
      commit("addMessage", userMsg);
      commit("setLoading", true);

      const assistantMsg = { id: Date.now() + 1, role: "assistant", content: "", toolCalls: [], plan: null };
      commit("addMessage", assistantMsg);

      // Build messages array (exclude plan messages from history)
      const history = state.messages
        .filter((m) => m.role === "user" || (m.role === "assistant" && !m.plan))
        .slice(0, -1) // exclude the empty assistant message just added
        .map((m) => ({ role: m.role, content: m.content || "" }));

      try {
        const response = await fetch("/api/ai/chat", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({ messages: history }),
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);
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
                  commit("updateLastAssistantMessage", { delta: event.data?.delta ?? "" });
                  break;
                case "tool":
                  if (event.data?.status === "running") {
                    commit("updateLastAssistantMessage", { toolCall: { name: event.data.name, status: "running" } });
                  } else {
                    commit("updateToolCall", { name: event.data?.name, status: "done", summary: event.data?.summary });
                  }
                  break;
                case "plan":
                  commit("updateLastAssistantMessage", { plan: event.data });
                  break;
                case "done":
                  commit("updateLastAssistantMessage", { done: true });
                  break;
                case "error":
                  commit("updateLastAssistantMessage", { delta: `\n\n⚠️ ${event.data?.message}` });
                  break;
              }
            } catch {
              // ignore parse errors
            }
          }
        }
      } catch (err) {
        commit("updateLastAssistantMessage", { delta: `\n\n⚠️ Ошибка соединения: ${err.message}` });
      } finally {
        commit("setLoading", false);
      }
    },

    confirmPlan({ state, commit, dispatch }) {
      commit("setLastPlanConfirmed");
      dispatch("sendMessage", "Подтверждаю, выполняй");
    },

    cancelPlan({ commit, dispatch }) {
      commit("setLastPlanCancelled");
      dispatch("sendMessage", "Отмена");
    },
  },
};
