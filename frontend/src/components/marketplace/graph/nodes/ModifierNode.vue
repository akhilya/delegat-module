<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
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
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

const { t } = useI18n();

const props = defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const catalog = computed(() => MODIFIER_CATALOG[props.data.modifierType]);
const title = computed(
  () =>
    catalog.value?.displayName ||
    props.data.modifierType ||
    t("marketplace.graph.modifierFallback"),
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
const hasPins = computed(
  () => paramPins.value.length > 0 || hasFieldPins.value,
);
const paramColor = (t) => pinColor(t === "number" ? "number" : "string");
const pinRef = (id) => `{{pin:${id}}}`;

const HEADER = 26;
const ROW = 18;
const topFor = (i) => `${HEADER + i * ROW + ROW / 2}px`;

const fieldPinRowOffset = computed(() => 1);
const paramPinRowOffset = computed(
  () => fieldPinRowOffset.value + fieldPins.value.length,
);
</script>

<template>
  <TooltipProvider :delay-duration="300">
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
          :is-connectable="!collapsed"
          :class="{ 'invisible pointer-events-none': collapsed }"
          type="target"
          :position="Position.Left"
          :style="{ top: collapsed ? '50%' : topFor(fieldPinRowOffset + i) }"
        />
        <Handle
          v-for="(p, i) in paramPins"
          :key="p.key"
          :id="`p:${p.key}`"
          :is-connectable="!collapsed"
          :class="{ 'invisible pointer-events-none': collapsed }"
          type="target"
          :position="Position.Left"
          :style="{
            top: collapsed ? '50%' : topFor(paramPinRowOffset + i),
            background: paramColor(p.type),
          }"
        />
      </template>
      <Handle v-else id="in" type="target" :position="Position.Left" />

      <div class="vf-card__head">
        <Wand2 class="size-3.5 text-muted-foreground" />
        <Tooltip>
          <TooltipTrigger as-child>
            <span class="vf-card__kind">{{
              collapsed && summary ? summary : title
            }}</span>
          </TooltipTrigger>
          <TooltipContent>
            {{ summary || title }}
          </TooltipContent>
        </Tooltip>
        <Tooltip v-if="irreversible">
          <TooltipTrigger as-child>
            <AlertTriangle class="vf-card__warn cursor-help" />
          </TooltipTrigger>
          <TooltipContent>
            {{ t("marketplace.graph.irreversibleWarning") }}
          </TooltipContent>
        </Tooltip>
      </div>

      <div v-if="hasPins && !collapsed" class="vf-mod__pins">
        <div class="vf-mod__pin-row vf-mod__pin-row--value">
          {{ t("marketplace.graph.valueRowLabel") }}
        </div>
        <Tooltip v-for="pin in fieldPins" :key="pin.id">
          <TooltipTrigger as-child>
            <div class="vf-mod__pin-row">
              {{ pin.label || pin.id }}
            </div>
          </TooltipTrigger>
          <TooltipContent> {{ pinRef(pin.id) }} </TooltipContent>
        </Tooltip>
        <div v-for="p in paramPins" :key="p.key" class="vf-mod__pin-row">
          {{ p.label }}
        </div>
      </div>

      <div v-if="!collapsed" :class="hasPins && 'vf-mod__body'">
        <div v-if="summary" class="vf-card__title vf-card__title--sm">
          <span
            v-if="data.modifierType === 'unitConversion'"
            class="vf-mod__unit"
            >{{ summary }}</span
          >
          <template v-else>{{ summary }}</template>
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
  </TooltipProvider>
</template>
