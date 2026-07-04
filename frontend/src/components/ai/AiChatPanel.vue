<template>
  <div>
    <!-- Toggle button -->
    <button
      class="ai-fab"
      :class="{ 'ai-fab--loading': isLoading }"
      @click="toggle"
      title="AI Ассистент"
    >
      <svg v-if="!isLoading" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M12 2L2 7l10 5 10-5-10-5z"/>
        <path d="M2 17l10 5 10-5"/>
        <path d="M2 12l10 5 10-5"/>
      </svg>
      <span v-else class="ai-spinner" />
    </button>

    <!-- Chat panel -->
    <Transition name="ai-panel">
      <div v-if="isOpen" class="ai-panel">
        <div class="ai-panel__header">
          <span class="ai-panel__title">AI Ассистент</span>
          <div class="ai-panel__header-actions">
            <button class="ai-panel__btn" @click="clearMessages" title="Очистить историю">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/>
                <path d="M10 11v6"/><path d="M14 11v6"/>
              </svg>
            </button>
            <button class="ai-panel__btn" @click="toggle" title="Свернуть">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>
        </div>

        <div ref="messagesEl" class="ai-panel__messages">
          <div v-if="messages.length === 0" class="ai-panel__empty">
            <p>Привет! Я могу помочь управлять товарами, категориями, маппингами и экспортом.</p>
            <p class="ai-panel__hints">Примеры: «покажи статистику магазина», «увеличь цену всем товарам на 100»</p>
          </div>

          <div
            v-for="msg in messages"
            :key="msg.id"
            class="ai-message"
            :class="`ai-message--${msg.role}`"
          >
            <!-- Plan card -->
            <template v-if="msg.role === 'assistant' && msg.plan">
              <AiPlanCard
                :plan="msg.plan"
                :confirmed="msg.planConfirmed"
                :cancelled="msg.planCancelled"
                @confirm="confirmPlan"
                @cancel="cancelPlan"
              />
              <div v-if="msg.content" class="ai-message__text" v-html="renderMarkdown(msg.content)" />
            </template>

            <!-- Regular assistant message -->
            <template v-else-if="msg.role === 'assistant'">
              <div v-if="msg.toolCalls?.length" class="ai-tool-calls">
                <div v-for="tc in msg.toolCalls" :key="tc.name" class="ai-tool-call" :class="`ai-tool-call--${tc.status}`">
                  <span class="ai-tool-call__icon">{{ tc.status === 'running' ? '⏳' : '✓' }}</span>
                  <span class="ai-tool-call__name">{{ tc.name }}</span>
                  <span v-if="tc.summary" class="ai-tool-call__summary">{{ tc.summary }}</span>
                </div>
              </div>
              <div v-if="msg.content" class="ai-message__text" v-html="renderMarkdown(msg.content)" />
              <span v-if="!msg.done && isLoading && msg === lastAssistant" class="ai-cursor" />
            </template>

            <!-- User message -->
            <div v-else class="ai-message__text">{{ msg.content }}</div>
          </div>
        </div>

        <div class="ai-panel__input">
          <textarea
            ref="inputEl"
            v-model="inputText"
            class="ai-panel__textarea"
            placeholder="Введите запрос..."
            rows="1"
            :disabled="isLoading"
            @keydown.enter.exact.prevent="submit"
            @input="autoResize"
          />
          <button
            class="ai-panel__send"
            :disabled="isLoading || !inputText.trim()"
            @click="submit"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
              <line x1="22" y1="2" x2="11" y2="13"/>
              <polygon points="22 2 15 22 11 13 2 9 22 2"/>
            </svg>
          </button>
        </div>
      </div>
    </Transition>
  </div>
</template>

<script setup>
import { ref, computed, nextTick, watch } from "vue";
import { useStore } from "vuex";
import AiPlanCard from "./AiPlanCard.vue";

const store = useStore();
const messagesEl = ref(null);
const inputEl = ref(null);
const inputText = ref("");

const isOpen = computed(() => store.state.aiChat.isOpen);
const isLoading = computed(() => store.state.aiChat.isLoading);
const messages = computed(() => store.state.aiChat.messages);
const lastAssistant = computed(() =>
  [...messages.value].reverse().find((m) => m.role === "assistant")
);

function toggle() { store.commit("aiChat/toggle"); }
function clearMessages() { store.commit("aiChat/clearMessages"); }
function confirmPlan() { store.dispatch("aiChat/confirmPlan"); }
function cancelPlan() { store.dispatch("aiChat/cancelPlan"); }

async function submit() {
  const text = inputText.value.trim();
  if (!text || isLoading.value) return;
  inputText.value = "";
  await nextTick();
  if (inputEl.value) inputEl.value.style.height = "auto";
  await store.dispatch("aiChat/sendMessage", text);
}

function autoResize() {
  if (!inputEl.value) return;
  inputEl.value.style.height = "auto";
  inputEl.value.style.height = Math.min(inputEl.value.scrollHeight, 120) + "px";
}

// Scroll to bottom when messages update
watch(messages, async () => {
  await nextTick();
  if (messagesEl.value) {
    messagesEl.value.scrollTop = messagesEl.value.scrollHeight;
  }
}, { deep: true });

function renderMarkdown(text) {
  if (!text) return "";
  return text
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
    .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
    .replace(/`(.+?)`/g, "<code>$1</code>")
    .replace(/\n/g, "<br>");
}
</script>

<style scoped>
.ai-fab {
  position: fixed;
  bottom: 84px;
  right: 24px;
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background: var(--color-primary, #5b6ee1);
  color: white;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 4px 16px rgba(0,0,0,0.2);
  z-index: 1000;
  transition: background 0.2s, transform 0.1s;
}
.ai-fab:hover { background: var(--color-primary-hover, #4a5cc5); transform: scale(1.05); }
.ai-fab--loading { opacity: 0.8; }

.ai-spinner {
  width: 20px; height: 20px;
  border: 2px solid rgba(255,255,255,0.3);
  border-top-color: white;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
  display: block;
}
@keyframes spin { to { transform: rotate(360deg); } }

.ai-panel {
  position: fixed;
  bottom: 144px;
  right: 24px;
  width: 380px;
  height: 520px;
  background: var(--color-surface, #fff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 12px;
  display: flex;
  flex-direction: column;
  box-shadow: 0 8px 32px rgba(0,0,0,0.15);
  z-index: 999;
  overflow: hidden;
}

.ai-panel-enter-active, .ai-panel-leave-active { transition: opacity 0.2s, transform 0.2s; }
.ai-panel-enter-from, .ai-panel-leave-to { opacity: 0; transform: translateY(16px) scale(0.97); }

.ai-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 14px;
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface-raised, #f8fafc);
}
.ai-panel__title { font-weight: 600; font-size: 14px; }
.ai-panel__header-actions { display: flex; gap: 4px; }
.ai-panel__btn {
  width: 28px; height: 28px;
  border: none; background: transparent; cursor: pointer;
  border-radius: 6px; display: flex; align-items: center; justify-content: center;
  color: var(--color-text-secondary, #64748b);
}
.ai-panel__btn:hover { background: var(--color-hover, #f1f5f9); }

.ai-panel__messages {
  flex: 1;
  overflow-y: auto;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.ai-panel__empty { color: var(--color-text-secondary, #64748b); font-size: 13px; text-align: center; padding: 20px 0; }
.ai-panel__hints { font-size: 11px; margin-top: 8px; opacity: 0.7; }

.ai-message { display: flex; flex-direction: column; max-width: 90%; }
.ai-message--user { align-self: flex-end; }
.ai-message--assistant { align-self: flex-start; }

.ai-message__text {
  padding: 8px 12px;
  border-radius: 10px;
  font-size: 13px;
  line-height: 1.5;
  word-break: break-word;
}
.ai-message--user .ai-message__text {
  background: var(--color-primary, #5b6ee1);
  color: white;
  border-bottom-right-radius: 2px;
}
.ai-message--assistant .ai-message__text {
  background: var(--color-surface-raised, #f1f5f9);
  color: var(--color-text, #1e293b);
  border-bottom-left-radius: 2px;
}

.ai-tool-calls { display: flex; flex-direction: column; gap: 3px; margin-bottom: 4px; }
.ai-tool-call {
  display: flex; align-items: center; gap: 6px;
  font-size: 11px; color: var(--color-text-secondary, #64748b);
  padding: 3px 8px; border-radius: 6px;
  background: var(--color-surface-raised, #f8fafc);
}
.ai-tool-call--done { opacity: 0.7; }
.ai-tool-call__name { font-family: monospace; }
.ai-tool-call__summary { opacity: 0.8; }

.ai-cursor {
  display: inline-block; width: 2px; height: 14px;
  background: currentColor; margin-left: 2px;
  animation: blink 1s step-end infinite;
}
@keyframes blink { 0%,100% { opacity: 1; } 50% { opacity: 0; } }

.ai-panel__input {
  display: flex; align-items: flex-end; gap: 8px;
  padding: 10px 12px;
  border-top: 1px solid var(--color-border, #e2e8f0);
}
.ai-panel__textarea {
  flex: 1;
  resize: none;
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 8px;
  padding: 8px 10px;
  font-size: 13px;
  font-family: inherit;
  outline: none;
  background: var(--color-surface, #fff);
  color: var(--color-text, #1e293b);
  max-height: 120px;
  min-height: 36px;
}
.ai-panel__textarea:focus { border-color: var(--color-primary, #5b6ee1); }
.ai-panel__send {
  width: 36px; height: 36px;
  border-radius: 8px;
  background: var(--color-primary, #5b6ee1);
  color: white; border: none; cursor: pointer;
  display: flex; align-items: center; justify-content: center;
  flex-shrink: 0;
  transition: opacity 0.15s;
}
.ai-panel__send:disabled { opacity: 0.4; cursor: not-allowed; }
.ai-panel__send:not(:disabled):hover { opacity: 0.85; }
</style>
