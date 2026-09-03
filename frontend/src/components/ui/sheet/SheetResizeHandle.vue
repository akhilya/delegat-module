<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { cn } from "@/lib/utils";

const props = defineProps({
  side: { type: String, default: "right" },
  resizing: { type: Boolean, default: false },
  /** Sit the capsule on the panel hairline. Sheets keep the inset capsule. */
  flush: { type: Boolean, default: false },
});

const { t } = useI18n();
const onLeftEdge = computed(() => props.side !== "left");
</script>

<template>
  <div
    data-slot="sheet-resize-handle"
    :data-flush="flush ? '' : undefined"
    :class="
      cn(
        'group absolute inset-y-0 z-20 flex w-4 cursor-col-resize touch-none items-center',
        onLeftEdge ? 'left-0 justify-start' : 'right-0 justify-end',
        flush
          ? 'overflow-visible'
          : cn(
              'justify-center overflow-hidden',
              onLeftEdge ? 'rounded-l-xl' : 'rounded-r-xl',
            ),
      )
    "
    role="separator"
    aria-orientation="vertical"
    :aria-label="t('common.resizeSheet')"
    :title="t('common.resizeSheet')"
  >
    <span
      class="pointer-events-none block h-12 w-1 rounded-full origin-center transition-[transform,background-color] duration-[var(--duration-press)] ease-[var(--ease-out-quart)]"
      :class="
        cn(
          resizing
            ? 'bg-foreground/45 scale-x-150 scale-y-110'
            : 'bg-muted-foreground/40 group-hover:bg-muted-foreground/65',
          flush && (onLeftEdge ? '-translate-x-1/2' : 'translate-x-1/2'),
        )
      "
    />
  </div>
</template>
