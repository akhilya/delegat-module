<script setup>
import { ref, computed, watch, nextTick, onMounted } from "vue";
import { ChevronDown, ChevronUp } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { t } from "@/i18n";

// Per-node preview chip. Long values (e.g. a product description) are clamped to a few lines
// with a "more" toggle so a single node can't grow arbitrarily tall.
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
    <TooltipProvider :delay-duration="300">
      <Tooltip>
        <TooltipTrigger as-child>
          <div
            ref="bodyEl"
            :class="[
              'vf-preview__body',
              expanded ? 'nowheel' : 'vf-preview__body--clamp',
            ]"
            @wheel.stop
          >
            {{ text }}
          </div>
        </TooltipTrigger>
        <TooltipContent class="max-w-72 whitespace-pre-wrap break-words">
          {{ text }}
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
    <Button
      v-if="overflowing || expanded"
      variant="link"
      size="xs"
      class="nodrag mt-[3px] h-auto gap-0.5 p-0 text-[10px] font-normal"
      @click.stop="expanded = !expanded"
      @mousedown.stop
    >
      <component :is="expanded ? ChevronUp : ChevronDown" class="size-3" />
      {{
        expanded
          ? t("marketplace.graph.previewLess")
          : t("marketplace.graph.previewMore")
      }}
    </Button>
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
</style>
