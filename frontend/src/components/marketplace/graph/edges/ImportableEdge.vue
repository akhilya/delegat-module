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
});

const path = computed(
  () =>
    getBezierPath({
      sourceX: props.sourceX,
      sourceY: props.sourceY,
      sourcePosition: props.sourcePosition,
      targetX: props.targetX,
      targetY: props.targetY,
      targetPosition: props.targetPosition,
    })[0],
);
</script>

<template>
  <g>
    <!-- Faint solid base line -->
    <path :d="path" fill="none" class="vf-bidi-base" />
    <!-- Dashes flowing source → target (forward) -->
    <path :d="path" fill="none" class="vf-bidi-fwd" />
    <!-- Dashes flowing target → source (backward), offset by half period to interleave -->
    <path :d="path" fill="none" class="vf-bidi-bwd" />
  </g>
</template>
