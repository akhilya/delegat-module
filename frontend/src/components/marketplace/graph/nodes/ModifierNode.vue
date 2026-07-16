<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Wand2, AlertTriangle } from "lucide-vue-next";
import { MODIFIER_CATALOG } from "../../modifierCatalog.js";
import {
  modifierOutputType,
  modifierIsReversible,
  overridableParams,
  fieldPinsFor,
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
const irreversible = computed(
  () => !unknown.value && !modifierIsReversible(props.data.modifierType),
);
const outType = computed(() =>
  props.data.modifierType === "forceType"
    ? props.data.params?.type || "any"
    : modifierOutputType(props.data.modifierType, "any"),
);
const collapsed = computed(() => !!props.data.collapsed);

const paramPins = computed(() => overridableParams(props.data.modifierType));
const fieldPins = computed(() => fieldPinsFor(props.data));
const hasFieldPins = computed(() => fieldPins.value.length > 0);
const hasPins = computed(() => paramPins.value.length > 0 || hasFieldPins.value);
const paramColor = (t) => pinColor(t === "number" ? "number" : "string");

const HEADER = 26;
const ROW = 18;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;

const fieldPinRowOffset = computed(() => 1);
const paramPinRowOffset = computed(
  () => fieldPinRowOffset.value + fieldPins.value.length,
);
</script>

<template>
  <div
    :class="[
      'vf-card',
      'vf-card--modifier',
      unknown && 'vf-card--error',
      selected && 'vf-card--selected',
      hasPins && !collapsed && 'vf-card--haspins',
      collapsed && 'vf-card--collapsed',
    ]"
  >
    <template v-if="hasPins">
      <Handle
        id="in"
        type="target"
        :position="Position.Left"
        :style="{ top: collapsed ? '50%' : topFor(0) }"
      />
      <Handle
        v-for="(pin, i) in fieldPins"
        :key="pin.id"
        :id="`field:${pin.id}`"
        type="target"
        :position="Position.Left"
        :style="{ top: collapsed ? '50%' : topFor(fieldPinRowOffset + i) }"
      />
      <Handle
        v-for="(p, i) in paramPins"
        :key="p.key"
        :id="`p:${p.key}`"
        type="target"
        :position="Position.Left"
        :style="{ top: collapsed ? '50%' : topFor(paramPinRowOffset + i), background: paramColor(p.type) }"
      />
    </template>
    <Handle v-else id="in" type="target" :position="Position.Left" />

    <div class="vf-card__head">
      <Wand2 class="size-3.5 text-muted-foreground" />
      <span class="vf-card__kind" :title="summary || title">{{
        collapsed && summary ? summary : title
      }}</span>
      <AlertTriangle
        v-if="irreversible"
        class="vf-card__warn cursor-help"
        title="Необратимо при импорте: исходное значение не восстановить — поле будет записано как есть."
      />
    </div>

    <div v-if="hasPins && !collapsed" class="vf-mod__pins">
      <div class="vf-mod__pin-row vf-mod__pin-row--value">значение</div>
      <div
        v-for="pin in fieldPins"
        :key="pin.id"
        class="vf-mod__pin-row"
        :title="`{{pin:${pin.id}}}`"
      >
        {{ pin.label || pin.id }}
      </div>
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