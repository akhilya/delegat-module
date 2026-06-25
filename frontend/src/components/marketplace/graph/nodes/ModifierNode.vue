<script setup>
import { computed } from "vue";
import { Handle, Position } from "@vue-flow/core";
import { Wand2, AlertTriangle } from "lucide-vue-next";
import { MODIFIER_CATALOG } from "../../modifierCatalog.js";
import {
  modifierOutputType,
  modifierIsReversible,
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
</script>

<template>
  <div
    :class="[
      'vf-card',
      'vf-card--modifier',
      unknown && 'vf-card--error',
      selected && 'vf-card--selected',
    ]"
  >
    <Handle id="in" type="target" :position="Position.Left" />
    <div class="vf-card__head">
      <Wand2 class="size-3.5 text-muted-foreground" />
      <span class="vf-card__kind">{{ title }}</span>
      <AlertTriangle
        v-if="irreversible"
        class="vf-card__warn"
        title="Необратимо при импорте: исходное значение не восстановить — поле будет записано как есть."
      />
    </div>
    <div v-if="summary" class="vf-card__title vf-card__title--sm">
      {{ summary }}
    </div>
    <div class="vf-card__pin vf-card__pin--right">
      <span class="vf-dot" :style="{ background: pinColor(outType) }" />
    </div>
    <NodePreview :value="data.previewValue" />
    <Handle
      id="out"
      type="source"
      :position="Position.Right"
      :style="{ background: pinColor(outType) }"
    />
  </div>
</template>
