<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Wand2, AlertTriangle } from "lucide-vue-next";
import { MODIFIER_CATALOG } from "../../modifierCatalog.js";
import {
  modifierOutputType,
  modifierIsReversible,
  overridableParams,
  pinColor,
} from "../graphModel.js";
import NodePreview from "./NodePreview.vue";

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const catalog = computed(() => MODIFIER_CATALOG[props.data.modifierType]);
const title = computed(
  () => catalog.value?.displayName || props.data.modifierType || "Модификатор",
);
const summary = computed(() =>
  catalog.value ? catalog.value.summary(props.data.params || {}) : "",
);
const unknown = computed(() => !catalog.value);
// Lossy transforms (truncate, replace, split…) can't be reversed on import — warn on the block.
const irreversible = computed(
  () => !unknown.value && !modifierIsReversible(props.data.modifierType),
);
const outType = computed(() =>
  props.data.modifierType === "forceType"
    ? props.data.params?.type || "any"
    : modifierOutputType(props.data.modifierType, "any"),
);
const collapsed = computed(() => !!props.data.collapsed);

// Operand params exposed as optional override pins (wire overrides the in-block default).
const paramPins = computed(() => overridableParams(props.data.modifierType));
const hasPins = computed(() => paramPins.value.length > 0);
const paramColor = (t) => pinColor(t === "number" ? "number" : "string");

// Pixel-aligned input rows: row 0 = the value input, rows 1.. = param override pins.
const HEADER = 26;
const ROW = 18;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;
</script>

<template>
  <div
    :class="[
      'vf-card',
      'vf-card--modifier',
      unknown && 'vf-card--error',
      selected && 'vf-card--selected',
      hasPins && 'vf-card--haspins',
      collapsed && !hasPins && 'vf-card--collapsed',
    ]"
  >
    <template v-if="hasPins">
      <Handle
        id="in"
        type="target"
        :position="Position.Left"
        :style="{ top: topFor(0) }"
      />
      <Handle
        v-for="(p, i) in paramPins"
        :key="p.key"
        :id="`p:${p.key}`"
        type="target"
        :position="Position.Left"
        :style="{ top: topFor(i + 1), background: paramColor(p.type) }"
      />
    </template>
    <Handle v-else id="in" type="target" :position="Position.Left" />

    <div class="vf-card__head">
      <Wand2 class="size-3.5 text-muted-foreground" />
      <span class="vf-card__kind">{{ title }}</span>
      <AlertTriangle
        v-if="irreversible"
        class="vf-card__warn"
        title="Необратимо при импорте: исходное значение не восстановить — поле будет записано как есть."
      />
    </div>

    <div v-if="hasPins" class="vf-mod__pins">
      <div class="vf-mod__pin-row vf-mod__pin-row--value">значение</div>
      <div v-for="p in paramPins" :key="p.key" class="vf-mod__pin-row">
        {{ p.label }}
      </div>
    </div>

    <div v-if="!collapsed" :class="hasPins && 'vf-mod__body'">
      <div v-if="summary" class="vf-card__title vf-card__title--sm">
        {{ summary }}
      </div>
      <div v-if="!hasPins" class="vf-card__pin vf-card__pin--right">
        <span class="vf-dot" :style="{ background: pinColor(outType) }" />
      </div>
      <NodePreview :value="data.previewValue" />
    </div>

    <Handle
      id="out"
      type="source"
      :position="Position.Right"
      :style="{ background: pinColor(outType) }"
    />
  </div>
</template>
