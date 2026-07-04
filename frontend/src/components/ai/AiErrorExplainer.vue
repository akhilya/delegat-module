<template>
  <div class="ai-error-explainer">
    <button
      v-if="!explanation && !isLoading"
      class="ai-error-explainer__btn"
      @click="explain"
    >
      ✦ Объяснить ошибку
    </button>

    <div v-if="isLoading" class="ai-error-explainer__loading">
      AI анализирует...
    </div>

    <div v-if="explanation" class="ai-error-explainer__result">
      <div class="ai-error-explainer__result-header">
        <span>✦ AI: объяснение ошибки</span>
        <button class="ai-error-explainer__close" @click="explanation = null">✕</button>
      </div>
      <p class="ai-error-explainer__text">{{ explanation }}</p>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { http } from "@/main";

const props = defineProps({
  errorMessage: { type: String, required: true },
  context: { type: String, default: "" },
  productId: { type: Number, default: null },
  connectionId: { type: Number, default: null },
});

const isLoading = ref(false);
const explanation = ref(null);

async function explain() {
  if (isLoading.value) return;
  isLoading.value = true;
  try {
    const r = await http.post("/api/ai/explain-error", {
      errorMessage: props.errorMessage,
      context: props.context,
      productId: props.productId,
      connectionId: props.connectionId,
    });
    explanation.value = r.data.explanation;
  } catch {
    explanation.value = "Не удалось получить объяснение от AI.";
  } finally {
    isLoading.value = false;
  }
}
</script>

<style scoped>
.ai-error-explainer { display: inline-block; }

.ai-error-explainer__btn {
  font-size: 11px;
  color: var(--color-primary, #5b6ee1);
  background: none;
  border: none;
  cursor: pointer;
  padding: 0;
  text-decoration: underline;
}
.ai-error-explainer__btn:hover { opacity: 0.7; }

.ai-error-explainer__loading {
  font-size: 11px;
  color: var(--color-text-secondary, #64748b);
  font-style: italic;
}

.ai-error-explainer__result {
  margin-top: 6px;
  background: #f0f4ff;
  border: 1px solid #c7d2fe;
  border-radius: 8px;
  padding: 10px 12px;
  max-width: 400px;
}
.ai-error-explainer__result-header {
  display: flex; align-items: center; justify-content: space-between;
  font-size: 11px; font-weight: 600; color: #4338ca; margin-bottom: 6px;
}
.ai-error-explainer__close {
  border: none; background: none; cursor: pointer; font-size: 12px; color: #6366f1;
}
.ai-error-explainer__text {
  font-size: 12px; line-height: 1.5; color: #1e293b; margin: 0;
  white-space: pre-wrap;
}
</style>
