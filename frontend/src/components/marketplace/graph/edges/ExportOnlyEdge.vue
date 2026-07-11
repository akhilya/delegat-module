<script setup>
import { computed, ref, onMounted, nextTick } from "vue";
import { getBezierPath } from "@vue-flow/core";

const SPEED_PX_PER_S = 65;
const SPACING_PX = 48;

const props = defineProps({
  id: String,
  sourceX: Number,
  sourceY: Number,
  targetX: Number,
  targetY: Number,
  sourcePosition: String,
  targetPosition: String,
  markerEnd: String,
  selected: Boolean,
  debug: Boolean,
  animate: { type: Boolean, default: true },
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

const pathD  = computed(() => bezier.value[0]);
const labelX = computed(() => bezier.value[1]);
const labelY = computed(() => bezier.value[2]);

const pathId = computed(() => `vf-wp-${props.id}`);
const wire   = computed(() => props.selected ? "#3b82f6" : "#b1b1b7");
const baseOp = computed(() => props.selected ? 0.65 : 0.2);
const arrOp  = computed(() => props.selected ? 1 : 0.85);

const wireLength = computed(() =>
  Math.sqrt((props.targetX - props.sourceX) ** 2 + (props.targetY - props.sourceY) ** 2),
);
const dur      = computed(() => Math.max(0.8, wireLength.value / SPEED_PX_PER_S));
const fwdCount = computed(() => Math.max(1, Math.round(wireLength.value / SPACING_PX)));

const pathReady = ref(false);
onMounted(() => nextTick(() => { pathReady.value = true; }));
</script>

<template>
  <g>
    <!-- invisible track for animateMotion -->
    <path :id="pathId" :d="pathD" fill="none" stroke="none" />
    <!-- visible wire -->
    <path :d="pathD" fill="none" :stroke="wire" stroke-width="2.5" :opacity="baseOp" />
    <!-- rightward chevron arrows, count scales with wire length -->
    <path
      v-if="animate && pathReady"
      v-for="i in fwdCount"
      :key="i"
      d="M-4,-3 L3,0 L-4,3"
      fill="none"
      :stroke="wire"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      :opacity="arrOp"
    >
      <animateMotion
        :dur="`${dur}s`"
        repeatCount="indefinite"
        rotate="auto"
        :begin="`${-((i - 1) * dur / fwdCount)}s`"
      >
        <mpath :href="`#${pathId}`" />
      </animateMotion>
    </path>
    <!-- debug label -->
    <g v-if="debug">
      <rect :x="labelX - 23" :y="labelY - 9" width="46" height="17" rx="3" fill="rgba(0,0,0,0.85)" />
      <text
        :x="labelX"
        :y="labelY"
        fill="#fb923c"
        font-size="9"
        font-family="monospace"
        text-anchor="middle"
        dominant-baseline="middle"
        font-weight="bold"
      >EXPORT</text>
    </g>
  </g>
</template>
