<template>
  <div class="ai-plan" :class="{ 'ai-plan--destructive': plan.isDestructive }">
    <div class="ai-plan__header">
      <span class="ai-plan__icon">{{ plan.isDestructive ? '⚠️' : '📋' }}</span>
      <span class="ai-plan__title">Вот что я собираюсь сделать:</span>
    </div>

    <p class="ai-plan__summary">{{ plan.summary }}</p>

    <ol v-if="plan.steps?.length" class="ai-plan__steps">
      <li v-for="(step, i) in plan.steps" :key="i" class="ai-plan__step">
        <code class="ai-plan__step-tool">{{ step.tool }}</code>
        <span>{{ step.description }}</span>
      </li>
    </ol>

    <div v-if="plan.affectedCount" class="ai-plan__meta">
      Затронуто объектов: <strong>{{ plan.affectedCount }}</strong>
    </div>

    <div v-if="!confirmed && !cancelled" class="ai-plan__actions">
      <button class="ai-plan__btn ai-plan__btn--confirm" @click="$emit('confirm')">
        Выполнить
      </button>
      <button class="ai-plan__btn ai-plan__btn--cancel" @click="$emit('cancel')">
        Отмена
      </button>
    </div>
    <div v-else-if="confirmed" class="ai-plan__status ai-plan__status--confirmed">
      ✓ Подтверждено — выполняю...
    </div>
    <div v-else-if="cancelled" class="ai-plan__status ai-plan__status--cancelled">
      ✕ Отменено
    </div>
  </div>
</template>

<script setup>
defineProps({
  plan: { type: Object, required: true },
  confirmed: { type: Boolean, default: false },
  cancelled: { type: Boolean, default: false },
});
defineEmits(["confirm", "cancel"]);
</script>

<style scoped>
.ai-plan {
  background: var(--color-surface-raised, #f8fafc);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 10px;
  padding: 12px;
  max-width: 340px;
  font-size: 13px;
}
.ai-plan--destructive { border-color: #f59e0b; background: #fffbeb; }

.ai-plan__header { display: flex; align-items: center; gap: 6px; margin-bottom: 6px; }
.ai-plan__title { font-weight: 600; font-size: 13px; }
.ai-plan__summary { color: var(--color-text, #1e293b); margin: 0 0 8px; line-height: 1.4; }

.ai-plan__steps {
  margin: 0 0 8px;
  padding-left: 20px;
  display: flex; flex-direction: column; gap: 4px;
}
.ai-plan__step { color: var(--color-text-secondary, #475569); line-height: 1.4; }
.ai-plan__step-tool {
  background: var(--color-surface, #fff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 4px;
  padding: 0 4px; font-size: 11px;
  margin-right: 6px;
  color: var(--color-primary, #5b6ee1);
}
.ai-plan__meta { font-size: 11px; color: var(--color-text-secondary, #64748b); margin-bottom: 10px; }

.ai-plan__actions { display: flex; gap: 8px; }
.ai-plan__btn {
  padding: 6px 14px; border-radius: 6px; border: none; cursor: pointer; font-size: 12px; font-weight: 500;
}
.ai-plan__btn--confirm { background: var(--color-primary, #5b6ee1); color: white; }
.ai-plan__btn--confirm:hover { opacity: 0.85; }
.ai-plan__btn--cancel { background: transparent; border: 1px solid var(--color-border, #e2e8f0); color: var(--color-text-secondary, #64748b); }
.ai-plan__btn--cancel:hover { background: var(--color-hover, #f1f5f9); }

.ai-plan__status { font-size: 12px; margin-top: 4px; }
.ai-plan__status--confirmed { color: #16a34a; }
.ai-plan__status--cancelled { color: var(--color-text-secondary, #64748b); }
</style>
