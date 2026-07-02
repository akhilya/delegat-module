<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Target } from "lucide-vue-next";
import { pinColor } from "../graphModel.js";

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const fields = computed(() => props.data.fields || []);
const preview = computed(() => props.data.preview || {});
const avail = computed(() => props.data.availableSources || {});
const productFields = computed(() => avail.value.productFields || []);
const categoryAttributes = computed(() => avail.value.categoryAttributes || []);
const storeAttributes = computed(() => avail.value.storeAttributes || []);

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

// Pill background/border derived from pin type color (12% / 28% opacity as hex suffix).
function pillStyle(type) {
  const c = pinColor(type);
  return {
    background: `${c}1f`,
    borderColor: `${c}47`,
    color: c,
  };
}

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
          class="vf-adapter__pill"
          :style="pillStyle(f.outputType)"
          :title="`Источник: ${boundLabel(f)}`"
        >{{ boundLabel(f) }}</span>
        <span
          v-if="preview[f.fieldKey] != null"
          class="vf-adapter__preview"
          :title="String(preview[f.fieldKey])"
        >{{ preview[f.fieldKey] }}</span>
      </div>
    </div>
  </div>
</template>
