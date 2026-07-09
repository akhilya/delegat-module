<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Target, ArrowUpFromLine } from "lucide-vue-next";
import { pinColor, pinTypeLabel } from "../graphModel.js";

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const fields = computed(() => props.data.fields || []);
const preview = computed(() => props.data.preview || {});
const analysis = computed(() => props.data.analysis || {});

// Export-only marker: lanes whose source isn't an attribute (fan-in / op / storage) can't be read
// back on import. Irreversibility of individual transforms is shown on the modifier blocks instead.
function isExportOnly(fieldKey) {
  const a = analysis.value[fieldKey];
  return !!a && a.connected && !a.importable;
}

// Pixel-aligned handle positions: header + row center. Rows render at the same metrics
// so each input pin lines up with its field row, and Vue Flow reads the real handle rect.
const HEADER = 30;
const ROW = 24;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;
</script>

<template>
  <div
    :class="['vf-card', 'vf-card--adapter', selected && 'vf-card--selected']"
  >
    <Handle
      v-for="(f, i) in fields"
      :key="f.fieldKey"
      :id="`f:${f.fieldKey}`"
      type="target"
      :position="Position.Left"
      :style="{ top: topFor(i), background: pinColor(f.outputType) }"
    />

    <div class="vf-adapter__head">
      <Target class="size-3.5 text-primary" />
      <span class="vf-card__kind">Поля адаптера</span>
      <span class="vf-pin-label">{{ fields.length }}</span>
    </div>

    <div class="vf-adapter__list">
      <div v-for="f in fields" :key="f.fieldKey" class="vf-adapter__row">
        <span class="vf-adapter__label" :title="f.fieldKey">
          {{ f.label || f.fieldKey
          }}<span v-if="f.isRequired" class="vf-req"> *</span>
        </span>
        <ArrowUpFromLine
          v-if="isExportOnly(f.fieldKey)"
          class="vf-adapter__marker vf-adapter__marker--muted"
          title="Только экспорт: обратное чтение (импорт) для этого поля недоступно"
        />
        <span class="vf-adapter__type">{{ pinTypeLabel(f.outputType) }}</span>
        <span
          v-if="preview[f.fieldKey] != null"
          class="vf-adapter__preview"
          :title="String(preview[f.fieldKey])"
          >{{ preview[f.fieldKey] }}</span
        >
      </div>
    </div>
  </div>
</template>
