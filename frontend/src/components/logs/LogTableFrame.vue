<script setup>
import { cn } from "@/lib/utils";
import PageDockFooter from "@/components/PageDockFooter.vue";

defineProps({
  class: { type: null, required: false },
  /**
   * Fill the remaining pane and pin the footer. Rows scroll inside.
   * Use on nested panels (logs), not full catalog/order pages.
   */
  fill: { type: Boolean, default: false },
  /**
   * Use the page footer bar (under the last row, or docked when the page overflows).
   * The table grows with the page scroller.
   */
  dockFooter: { type: Boolean, default: false },
});
</script>

<template>
  <div
    :class="
      cn(
        'flex w-full flex-col',
        fill && 'min-h-0 flex-1 overflow-hidden',
        $props.class,
      )
    "
  >
    <div
      data-slot="table-frame"
      :class="
        fill
          ? 'min-h-0 flex-1 overflow-auto'
          : dockFooter
            ? 'min-w-0'
            : 'overflow-x-auto'
      "
    >
      <slot />
    </div>
    <PageDockFooter v-if="dockFooter && $slots.footer">
      <div class="flex min-w-0 flex-1 items-center justify-between gap-3">
        <slot name="footer" />
      </div>
    </PageDockFooter>
    <div
      v-else-if="$slots.footer"
      data-slot="table-actions-footer"
      class="flex shrink-0 items-center justify-between gap-3 bg-background px-3 py-2 text-sm"
    >
      <slot name="footer" />
    </div>
  </div>
</template>
