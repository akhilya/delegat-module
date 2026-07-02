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
import { OP_SPECS, opSummary, pinColor } from "../graphModel.js";
import NodePreview from "./NodePreview.vue";

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
      <span class="vf-card__kind">{{ spec.label }}</span>
    </div>
    <template v-if="!collapsed">
      <div v-if="summary" class="vf-card__title vf-card__title--sm">
        {{ summary }}
      </div>
      <div
        v-if="isFixed && spec.inputLabels?.length"
        class="vf-pin-label"
        style="margin-top: 4px"
      >
        {{ spec.inputLabels.join(" · ") }}
      </div>

      <div v-if="isMulti" class="vf-combine__inputs">
        <span class="vf-pin-label">входов: {{ count }}</span>
        <button
          type="button"
          class="vf-iconbtn nodrag"
          title="Убрать вход"
          @mousedown.stop
          @click="removeInput"
        >
          <Minus class="size-3" />
        </button>
        <button
          type="button"
          class="vf-iconbtn nodrag"
          title="Добавить вход"
          @mousedown.stop
          @click="addInput"
        >
          <Plus class="size-3" />
        </button>
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
</template>
