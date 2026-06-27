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
const avail = computed(() => props.data.availableSources || {});
const productFields = computed(() => avail.value.productFields || []);
const categoryAttributes = computed(() => avail.value.categoryAttributes || []);
const storeAttributes = computed(() => avail.value.storeAttributes || []);

// Export-only marker: lanes whose source isn't an attribute (fan-in / op / storage) can't be read
// back on import. Irreversibility of individual transforms is shown on the modifier blocks instead.
function isExportOnly(fieldKey) {
  const a = analysis.value[fieldKey];
  return !!a && a.connected && !a.importable;
}

// Human name of a field's direct source binding (chosen in the block's settings).
function boundLabel(f) {
  const s = f.source;
  if (!s) return "";
  if (s.type === "groupName") return "Название группы";
  if (s.type === "productField")
    return (
      productFields.value.find((x) => x.key === s.field)?.displayName || s.field
    );
  if (s.type === "attribute")
    return (
      categoryAttributes.value.find((x) => x.id === s.attributeId)?.name ||
      `атрибут #${s.attributeId}`
    );
  if (s.type === "storeAttribute")
    return (
      storeAttributes.value.find((x) => x.id === s.attributeId)?.name ||
      `атрибут #${s.attributeId}`
    );
  return "";
}

// Pixel-aligned handle positions: header + row center. Bound fields (with a source) render no pin,
// but the row index `i` still drives `top`, so the remaining pins stay aligned to their rows.
const HEADER = 30;
const ROW = 24;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;
</script>

<template>
  <div
    :class="['vf-card', 'vf-card--adapter', selected && 'vf-card--selected']"
  >
    <template v-for="(f, i) in fields" :key="f.fieldKey">
      <Handle
        v-if="!f.source"
        :id="`f:${f.fieldKey}`"
        type="target"
        :position="Position.Left"
        :style="{ top: topFor(i), background: pinColor(f.outputType) }"
      />
    </template>

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
        <span
          v-if="f.source"
          class="vf-adapter__bound"
          :title="`Источник: ${boundLabel(f)}`"
          >← {{ boundLabel(f) }}</span
        >
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
