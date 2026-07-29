<script setup>
import { computed, ref, watch } from "vue";
import { useVueFlow } from "@vue-flow/core";

const props = defineProps({
  horizontal: { type: Number, default: undefined },
  vertical: { type: Number, default: undefined },
});

const { viewport, dimensions } = useVueFlow();
const canvasRef = ref(null);

const width = computed(() => dimensions.value.width || 0);
const height = computed(() => dimensions.value.height || 0);
const x = computed(() => viewport.value.x);
const y = computed(() => viewport.value.y);
const zoom = computed(() => viewport.value.zoom);

function paint() {
  const canvas = canvasRef.value;
  const ctx = canvas?.getContext("2d");
  if (!ctx || !canvas || !width.value || !height.value) return;

  const dpi = window.devicePixelRatio || 1;
  canvas.width = width.value * dpi;
  canvas.height = height.value * dpi;
  ctx.setTransform(dpi, 0, 0, dpi, 0, 0);
  ctx.clearRect(0, 0, width.value, height.value);

  const stroke =
    getComputedStyle(canvas).getPropertyValue("--vf-helper-line").trim()
    || getComputedStyle(document.documentElement).getPropertyValue("--muted-foreground").trim()
    || "#94a3b8";
  ctx.strokeStyle = stroke;
  ctx.globalAlpha = 0.35;
  ctx.lineWidth = 1;
  ctx.setLineDash([]);

  if (typeof props.vertical === "number") {
    const px = props.vertical * zoom.value + x.value;
    ctx.beginPath();
    ctx.moveTo(px, 0);
    ctx.lineTo(px, height.value);
    ctx.stroke();
  }

  if (typeof props.horizontal === "number") {
    const py = props.horizontal * zoom.value + y.value;
    ctx.beginPath();
    ctx.moveTo(0, py);
    ctx.lineTo(width.value, py);
    ctx.stroke();
  }

  ctx.globalAlpha = 1;
}

watch(
  [width, height, x, y, zoom, () => props.horizontal, () => props.vertical],
  paint,
  { immediate: true },
);
</script>

<template>
  <canvas ref="canvasRef" class="vf-helper-lines" aria-hidden="true" />
</template>

<style scoped>
.vf-helper-lines {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  z-index: 10;
  pointer-events: none;
}
</style>
