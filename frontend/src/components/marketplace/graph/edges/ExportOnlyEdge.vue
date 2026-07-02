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
  <path :d="path" fill="none" class="vf-export-path" />
</template>
