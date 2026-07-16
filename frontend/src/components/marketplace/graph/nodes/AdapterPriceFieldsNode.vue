<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Banknote, AlertTriangle, ArrowLeft, ArrowLeftRight } from "lucide-vue-next";
import { mergeProductFieldSources, pinColor } from "../graphModel.js";

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const fields = computed(() => props.data.fields || []);
const analysis = computed(() => props.data.analysis || {});

function aliasPrimaryLabel(f) {
  const primary = fields.value.find((x) => x.fieldKey === f.aliasOf);
  return primary?.label || f.aliasOf;
}

const avail = computed(() => props.data.availableSources || {});
const productFields = computed(() => mergeProductFieldSources(avail.value));
const categoryAttributes = computed(() => avail.value.categoryAttributes || []);
const storeAttributes = computed(() => avail.value.storeAttributes || []);

function boundLabel(f) {
  const s = f.source;
  if (!s) return "";
  if (s.type === "groupName") return "Название группы";
  if (s.type === "productField")
    return productFields.value.find((x) => x.key === s.field)?.displayName || s.field;
  if (s.type === "attribute")
    return categoryAttributes.value.find((x) => x.id === s.attributeId)?.name || `атрибут #${s.attributeId}`;
  if (s.type === "storeAttribute")
    return storeAttributes.value.find((x) => x.id === s.attributeId)?.name || `атрибут #${s.attributeId}`;
  return "";
}

function pillStyle(type) {
  const c = pinColor(type);
  return { background: `${c}1f`, borderColor: `${c}47`, color: c };
}

function fieldTooltip(f) {
  const parts = [];
  if (f.description) parts.push(String(f.description).trim());
  if (f.unit) parts.push(`Ед. изм.: ${f.unit}`);
  parts.push(`Ключ: ${f.fieldKey}`);
  return parts.filter(Boolean).join("\n");
}

function fieldLabel(f) {
  const base = f.label || f.fieldKey;
  if (f.unit && !String(base).toLowerCase().includes(String(f.unit).toLowerCase())) {
    return `${base}, ${f.unit}`;
  }
  return base;
}

const HEADER = 30;
const ROW = 24;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;
</script>

<template>
  <div :class="['vf-card', 'vf-card--adapter', selected && 'vf-card--selected']">
    <Handle
      v-for="(f, i) in fields"
      :key="f.fieldKey"
      :id="`f:${f.fieldKey}`"
      type="target"
      :position="Position.Left"
      :connectable="!f.source && !f.aliasOf"
      :style="{
        top: topFor(i),
        background: pinColor(f.outputType),
        opacity: f.source || f.aliasOf ? 0 : 1,
        pointerEvents: f.source || f.aliasOf ? 'none' : 'auto',
      }"
    />

    <div class="vf-adapter__head">
      <Banknote class="size-3.5 text-primary" />
      <span class="vf-card__kind">Поля цен</span>
      <span class="vf-pin-label">{{ fields.length }}</span>
    </div>

    <div class="vf-adapter__list">
      <div
        v-for="f in fields"
        :key="f.fieldKey"
        class="vf-adapter__row cursor-pointer"
        :title="fieldTooltip(f)"
        @click.stop="data.openField?.(f.fieldKey)"
      >
        <span class="vf-adapter__label" :title="fieldTooltip(f)">
          {{ fieldLabel(f) }}<span v-if="f.isRequired" class="vf-req"> *</span>
        </span>

        <span
          v-if="f.aliasOf"
          class="vf-adapter__pill vf-adapter__pill--alias"
          :title="`Объединено с «${aliasPrimaryLabel(f)}»`"
          @click.stop="data.openField?.(f.fieldKey)"
        >
          = {{ aliasPrimaryLabel(f) }}
        </span>

        <component
          v-if="f.source"
          :is="analysis[f.fieldKey]?.importable ? ArrowLeftRight : ArrowLeft"
          :class="['vf-adapter__dir', analysis[f.fieldKey]?.importable ? 'vf-adapter__dir--import' : 'vf-adapter__dir--export']"
        />

        <AlertTriangle
          v-if="analysis[f.fieldKey]?.fanOut"
          class="size-2.5 shrink-0 cursor-help text-amber-500"
          title="Источник уже используется другим полем цен."
        />

        <span
          v-if="f.source"
          class="vf-adapter__pill"
          :style="pillStyle(f.outputType)"
          :title="`${boundLabel(f)} — нажмите для настройки`"
          @click.stop="data.openField?.(f.fieldKey)"
        >{{ boundLabel(f) }}</span>
      </div>
    </div>
  </div>
</template>