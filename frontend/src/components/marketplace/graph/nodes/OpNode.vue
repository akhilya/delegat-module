<script setup>
import { computed } from "vue";
import { Handle, Position, useVueFlow } from "@vue-flow/core";
import {
  Braces,
  List,
  GitBranch,
  Binary,
  Equal,
  Plus,
  Minus,
} from "lucide-vue-next";
import {
  OP_SPECS,
  opSummary,
  opLabel,
  opInputLabels,
  pinColor,
} from "../graphModel.js";
import NodePreview from "./NodePreview.vue";
import { Button } from "@/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { t } from "@/i18n";

const props = defineProps({
  id: String,
  type: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});

const { updateNodeData } = useVueFlow();

const spec = computed(() => OP_SPECS[props.type] || {});
const isMulti = computed(() => spec.value.inputs === "multi");
// A fixed number of inputs (2 = pair, 3 = ifElse, …).
const isFixed = computed(
  () => typeof spec.value.inputs === "number" && spec.value.inputs >= 2,
);
const fixedCount = computed(() => (isFixed.value ? spec.value.inputs : 0));
const fixedInputs = computed(() =>
  Array.from({ length: fixedCount.value }, (_, i) => i),
);
const count = computed(() => Math.max(1, props.data.inputCount || 2));
const multiInputs = computed(() =>
  Array.from({ length: count.value }, (_, i) => i),
);
const summary = computed(() => opSummary(props.type, props.data));
const outColor = computed(() => pinColor(spec.value.output || "string"));
const label = computed(() => opLabel(props.type));
const inputLabels = computed(() => opInputLabels(props.type));

const ICONS = { Braces, List, GitBranch, Binary, Equal };
const icon = computed(() => ICONS[spec.value.icon] || Braces);

const topMulti = (i) => `${((i + 1) / (count.value + 1)) * 100}%`;
const topFixed = (i) => `${((i + 1) / (fixedCount.value + 1)) * 100}%`;
const addInput = () =>
  updateNodeData(props.id, { inputCount: count.value + 1 });
const removeInput = () =>
  updateNodeData(props.id, { inputCount: Math.max(1, count.value - 1) });
const collapsed = computed(() => !!props.data.collapsed);
</script>

<template>
  <TooltipProvider :delay-duration="150">
    <div
      :class="[
        'vf-card',
        'vf-card--combine',
        selected && 'vf-card--selected',
        collapsed && 'vf-card--collapsed',
        collapsed && (isMulti || isFixed) && 'vf-card--collapsed-multi',
      ]"
    >
      <template v-if="isMulti">
        <Handle
          v-for="i in multiInputs"
          :key="i"
          :id="`in-${i}`"
          type="target"
          :position="Position.Left"
          :style="{ top: topMulti(i) }"
        />
      </template>
      <template v-else-if="isFixed">
        <Handle
          v-for="i in fixedInputs"
          :key="i"
          :id="`in-${i}`"
          type="target"
          :position="Position.Left"
          :style="{ top: topFixed(i) }"
        />
      </template>
      <Handle v-else id="in" type="target" :position="Position.Left" />

      <div class="vf-card__head">
        <component :is="icon" class="size-3.5 text-muted-foreground" />
        <Tooltip>
          <TooltipTrigger as-child>
            <span class="vf-card__kind">{{
              collapsed && summary ? summary : label
            }}</span>
          </TooltipTrigger>
          <TooltipContent>
            {{ summary || label }}
          </TooltipContent>
        </Tooltip>
      </div>
      <template v-if="!collapsed">
        <div v-if="summary" class="vf-card__title vf-card__title--sm">
          {{ summary }}
        </div>
        <div v-if="isFixed && inputLabels.length" class="mt-1 flex flex-col">
          <span
            v-for="(inputLabel, i) in inputLabels"
            :key="i"
            class="vf-pin-label"
            >{{ inputLabel }}</span
          >
        </div>

        <div v-if="isMulti" class="vf-combine__inputs">
          <span class="vf-pin-label">{{
            t("marketplace.graph.inputsCount", { count })
          }}</span>
          <Tooltip>
            <TooltipTrigger as-child>
              <Button
                variant="ghost"
                size="icon-xs"
                class="nodrag size-[20px] rounded-[var(--radius-xs)] border border-border p-0 text-muted-foreground"
                :aria-label="t('marketplace.graph.removeInput')"
                @mousedown.stop
                @click="removeInput"
              >
                <Minus class="size-3" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              {{ t("marketplace.graph.removeInput") }}
            </TooltipContent>
          </Tooltip>
          <Tooltip>
            <TooltipTrigger as-child>
              <Button
                variant="ghost"
                size="icon-xs"
                class="nodrag size-[20px] rounded-[var(--radius-xs)] border border-border p-0 text-muted-foreground"
                :aria-label="t('marketplace.graph.addInput')"
                @mousedown.stop
                @click="addInput"
              >
                <Plus class="size-3" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              {{ t("marketplace.graph.addInput") }}
            </TooltipContent>
          </Tooltip>
        </div>

        <NodePreview :value="data.previewValue" />
      </template>

      <Handle
        id="out"
        type="source"
        :position="Position.Right"
        :style="{ background: outColor }"
      />
    </div>
  </TooltipProvider>
</template>
