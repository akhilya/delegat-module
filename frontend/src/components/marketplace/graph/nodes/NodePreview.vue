<script setup>
import { ref, computed, watch, nextTick, onMounted } from "vue";
import { ChevronDown, ChevronUp } from "lucide-vue-next";

// Per-node preview chip. Long values (e.g. a product description) are clamped to a few lines
// with an "ещё" toggle so a single node can't grow arbitrarily tall.
const props = defineProps({
  value: { default: null },
});

const text = computed(() => (props.value == null ? "" : String(props.value)));
const expanded = ref(false);
const bodyEl = ref(null);
const overflowing = ref(false);

function measure() {
  const el = bodyEl.value;
  overflowing.value = !!el && el.scrollHeight > el.clientHeight + 1;
}
onMounted(() => nextTick(measure));
watch(text, () => {
  expanded.value = false;
  nextTick(measure);
});
</script>

<template>
  <div v-if="text" class="vf-preview">
    <div
      ref="bodyEl"
      :class="[
        'vf-preview__body',
        expanded ? 'nowheel' : 'vf-preview__body--clamp',
      ]"
      :title="text"
      @wheel.stop
    >
      {{ text }}
    </div>
    <button
      v-if="overflowing || expanded"
      type="button"
      class="vf-preview__toggle nodrag"
      @click.stop="expanded = !expanded"
      @mousedown.stop
    >
      <component :is="expanded ? ChevronUp : ChevronDown" class="size-3" />
      {{ expanded ? "свернуть" : "ещё" }}
    </button>
  </div>
</template>

<style scoped>
.vf-preview {
  margin-top: 6px;
  border-top: 1px dashed var(--border);
  padding-top: 4px;
}
.vf-preview__body {
  font-family: ui-monospace, monospace;
  font-size: 11px;
  line-height: 1.35;
  color: var(--muted-foreground);
  white-space: pre-wrap;
  word-break: break-word;
}
.vf-preview__body--clamp {
  display: -webkit-box;
  -webkit-line-clamp: 3;
  line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
.vf-preview__body:not(.vf-preview__body--clamp) {
  max-height: 160px;
  overflow-y: auto;
}
.vf-preview__toggle {
  display: inline-flex;
  align-items: center;
  gap: 2px;
  margin-top: 3px;
  font-size: 10px;
  color: var(--primary);
  cursor: pointer;
}
.vf-preview__toggle:hover {
  text-decoration: underline;
}
</style>
