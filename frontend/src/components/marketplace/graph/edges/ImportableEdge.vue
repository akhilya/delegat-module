<script setup>
import { computed } from "vue";
import { getBezierPath } from "@vue-flow/core";

const props = defineProps({
  sourceX: Number,
  sourceY: Number,
  targetX: Number,
  targetY: Number,
  sourcePosition: String,
  targetPosition: String,
  markerEnd: String,
  selected: Boolean,
  debug: Boolean,
});

const bezier = computed(() =>
  getBezierPath({
    sourceX: props.sourceX,
    sourceY: props.sourceY,
    sourcePosition: props.sourcePosition,
    targetX: props.targetX,
    targetY: props.targetY,
    targetPosition: props.targetPosition,
  }),
);

const pathD = computed(() => bezier.value[0]);
const labelX = computed(() => bezier.value[1]);
const labelY = computed(() => bezier.value[2]);
</script>

<template>
  <g>
    <path :d="pathD" fill="none" class="vf-bidi-base" />
    <path :d="pathD" fill="none" class="vf-bidi-fwd" />
    <path :d="pathD" fill="none" class="vf-bidi-bwd" />
    <g v-if="debug">
      <rect :x="labelX - 23" :y="labelY - 9" width="46" height="17" rx="3" fill="rgba(0,0,0,0.85)" />
      <text
        :x="labelX" :y="labelY"
        fill="#4ade80" font-size="9" font-family="monospace"
        text-anchor="middle" dominant-baseline="middle"
        font-weight="bold"
      >IMPORT</text>
    </g>
  </g>
</template>
