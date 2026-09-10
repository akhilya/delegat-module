<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Target, AlertTriangle, ArrowLeft, ArrowLeftRight, Plus } from "lucide-vue-next";
import {
  ADD_ADAPTER_FIELD_HANDLE,
  mergeProductFieldSources,
  pinColor,
} from "../graphModel.js";
import {
  localizeExternalFieldName,
  localizeStandardSourceName,
} from "../fieldLabels.js";
import { t } from "@/i18n";

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
  if (s.type === "groupName") return t("marketplace.graph.groupNameLabel");
  if (s.type === "productField")
    return localizeStandardSourceName(
      s.field,
      productFields.value.find((x) => x.key === s.field)?.displayName || s.field,
    );
  if (s.type === "attribute")
    return categoryAttributes.value.find((x) => x.id === s.attributeId)?.name || t("marketplace.graph.attributeFallback", { id: s.attributeId });
  if (s.type === "storeAttribute")
    return storeAttributes.value.find((x) => x.id === s.attributeId)?.name || t("marketplace.graph.attributeFallback", { id: s.attributeId });
  return "";
}

function pillStyle(type) {
  const c = pinColor(type);
  return { background: `${c}1f`, borderColor: `${c}47`, color: c };
}

function aliasTitle(f) {
  const primary = aliasPrimaryLabel(f);
  return t("marketplace.graph.aliasMergedTitle", { primary });
}

function fanOutTitle(f) {
  if (f.source) {
    return t("marketplace.graph.fanOutSourceUsed");
  }
  return t("marketplace.graph.fanOutImportDisabled");
}

function sourceTitle(f) {
  return t("marketplace.graph.sourceClickTitle", { label: boundLabel(f) });
}

/** Tooltip for adapter field: description, unit, key. */
function fieldTooltip(f) {
  const parts = [];
  if (f.description) parts.push(String(f.description).trim());
  if (f.unit) parts.push(t("marketplace.graph.unitLabel", { unit: f.unit }));
  if (f.minValue != null || f.maxValue != null) {
    const range = [
      f.minValue != null ? t("marketplace.graph.rangeFrom", { value: f.minValue }) : null,
      f.maxValue != null ? t("marketplace.graph.rangeTo", { value: f.maxValue }) : null,
    ]
      .filter(Boolean)
      .join(" ");
    if (range) parts.push(range);
  }
  if (f.maxLength != null) {
    parts.push(t("marketplace.graph.maxLengthLabel", { max: f.maxLength }));
  }
  if (f.maxValues != null) {
    parts.push(t("marketplace.graph.maxValuesLabel", { max: f.maxValues }));
  }
  if (f.allowCustomValues) {
    parts.push(t("marketplace.graph.allowCustomValuesLabel"));
  }
  parts.push(t("marketplace.graph.keyLabel", { key: f.fieldKey }));
  return parts.filter(Boolean).join("\n");
}

function fieldLabel(f) {
  const base = localizeExternalFieldName(f.fieldKey, f.label || f.fieldKey);
  if (f.unit && !String(base).toLowerCase().includes(String(f.unit).toLowerCase())) {
    return `${base}, ${f.unit}`;
  }
  return base;
}

function pinHidden(f) {
  return !!(f.source || f.aliasOf || f.omit);
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
      :connectable="!pinHidden(f)"
      :style="{
        top: topFor(i),
        background: pinColor(f.outputType),
        opacity: pinHidden(f) ? 0 : 1,
        pointerEvents: pinHidden(f) ? 'none' : 'auto',
      }"
    />

    <Handle
      :id="ADD_ADAPTER_FIELD_HANDLE"
      type="target"
      :position="Position.Left"
      :style="{
        top: topFor(fields.length),
        background: pinColor('any'),
      }"
    />

    <div class="vf-adapter__head">
      <Target class="size-3.5 text-primary" />
      <span class="vf-card__kind">{{ t("marketplace.graph.adapterHeader") }}</span>
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
          v-if="f.omit"
          class="vf-adapter__pill vf-adapter__pill--omit"
          :title="t('marketplace.graph.omitFieldTitle')"
          @click.stop="data.openField?.(f.fieldKey)"
        >{{ t("marketplace.graph.omitFieldShort") }}</span>

        <span
          v-if="f.aliasOf"
          class="vf-adapter__pill vf-adapter__pill--alias"
          :title="aliasTitle(f)"
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
          :title="fanOutTitle(f)"
        />

        <span
          v-if="f.source"
          class="vf-adapter__pill"
          :style="pillStyle(f.outputType)"
          :title="sourceTitle(f)"
          @click.stop="data.openField?.(f.fieldKey)"
        >{{ boundLabel(f) }}</span>
      </div>
      <div
        class="vf-adapter__row vf-adapter__row--add cursor-pointer"
        :title="t('marketplace.graph.addCustomFieldHint')"
        @click.stop="data.addCustomField?.()"
      >
        <Plus class="size-3 shrink-0 text-muted-foreground" />
        <span class="vf-adapter__label text-muted-foreground">{{
          t("marketplace.graph.addCustomField")
        }}</span>
      </div>
    </div>
  </div>
</template>