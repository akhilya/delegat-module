<script setup>
import { computed, ref, onMounted, nextTick } from "vue";
import { EdgeLabelRenderer, getBezierPath } from "@vue-flow/core";
import UnitMismatchNotice from "../UnitMismatchNotice.vue";

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
  data: { type: Object, default: () => ({}) },
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

// Reversed bezier: same curve, opposite direction - used for leftward arrows so
// rotate="auto" with forward keyPoints correctly points the arrowhead left.
const revPathD = computed(() => {
  const nums = (pathD.value.match(/-?[\d.]+(?:e[+-]?\d+)?/g) || []).map(Number);
  if (nums.length < 8) return pathD.value;
  const [sx, sy, cp1x, cp1y, cp2x, cp2y, tx, ty] = nums;
  return `M ${tx},${ty} C ${cp2x},${cp2y} ${cp1x},${cp1y} ${sx},${sy}`;
});

const pathId = computed(() => `vf-wp-${props.id}`);
const revPathId = computed(() => `vf-wp-bwd-${props.id}`);
const wire = computed(() =>
  props.selected ? "var(--primary)" : "var(--muted-foreground)",
);
const baseOp = computed(() => (props.selected ? 0.65 : 0.2));
const fwdOp = computed(() => (props.selected ? 1 : 0.85));
const bwdOp = computed(() => (props.selected ? 0.75 : 0.5));

const wireLength = computed(() =>
  Math.sqrt(
    (props.targetX - props.sourceX) ** 2 + (props.targetY - props.sourceY) ** 2,
  ),
);
const dur = computed(() => Math.max(0.8, wireLength.value / SPEED_PX_PER_S));
const fwdCount = computed(() =>
  Math.max(1, Math.round(wireLength.value / SPACING_PX)),
);
const bwdCount = computed(() =>
  Math.max(1, Math.round(wireLength.value / (SPACING_PX * 1.5))),
);

const pathReady = ref(false);
onMounted(() =>
  nextTick(() => {
    pathReady.value = true;
  }),
);
</script>

<template>
  <g>
    <!-- invisible forward track -->
    <path :id="pathId" :d="pathD" fill="none" stroke="none" />
    <!-- invisible reversed track for leftward arrows -->
    <path :id="revPathId" :d="revPathD" fill="none" stroke="none" />
    <!-- visible wire -->
    <path
      :d="pathD"
      fill="none"
      :stroke="wire"
      stroke-width="2.5"
      :opacity="baseOp"
    />
    <!-- rightward arrows (export direction) -->
    <template v-if="animate && pathReady">
      <path
        v-for="i in fwdCount"
        :key="`f${i}`"
        d="M-4,-3 L3,0 L-4,3"
        fill="none"
        :stroke="wire"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        :opacity="fwdOp"
      >
        <animateMotion
          :dur="`${dur}s`"
          repeatCount="indefinite"
          rotate="auto"
          :begin="`${-(((i - 1) * dur) / fwdCount)}s`"
        >
          <mpath :href="`#${pathId}`" />
        </animateMotion>
      </path>
    </template>
    <!-- leftward arrows (import direction): use the reversed path so rotate="auto"
         naturally orients the chevron to the left without browser quirks -->
    <template v-if="animate && pathReady">
      <path
        v-for="i in bwdCount"
        :key="`b${i}`"
        d="M-4,-3 L3,0 L-4,3"
        fill="none"
        :stroke="wire"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        :opacity="bwdOp"
      >
        <animateMotion
          :dur="`${dur * 1.35}s`"
          repeatCount="indefinite"
          rotate="auto"
          :begin="`${-(((i - 1) * dur * 1.35) / bwdCount)}s`"
        >
          <mpath :href="`#${revPathId}`" />
        </animateMotion>
      </path>
    </template>
    <!-- debug label -->
    <g v-if="debug">
      <rect
        :x="labelX - 23"
        :y="labelY - 9"
        width="46"
        height="17"
        rx="3"
        fill="rgba(0,0,0,0.85)"
      />
      <text
        :x="labelX"
        :y="labelY"
        fill="#4ade80"
        font-size="9"
        font-family="monospace"
        text-anchor="middle"
        dominant-baseline="middle"
        font-weight="bold"
        >IMPORT</text
      >
    </g>
  </g>
  <EdgeLabelRenderer v-if="data?.unitWarning">
    <div
      class="pointer-events-auto absolute z-20"
      :style="{
        transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
      }"
    >
      <UnitMismatchNotice :warning="data.unitWarning" />
    </div>
  </EdgeLabelRenderer>
</template>
