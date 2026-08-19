<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  /**
   * default — sits inside SheetContent padding.
   * bar — bordered top chrome for SheetContent with `p-0` / `gap-0`.
   */
  variant: {
    type: String,
    default: "default",
    validator: (v) => ["default", "bar"].includes(v),
  },
});
</script>

<template>
  <div
    data-slot="sheet-header"
    :data-variant="variant"
    :class="
      cn(
        'flex flex-col gap-1.5 text-left',
        variant === 'default' && 'p-4 pr-14',
        variant === 'bar' && 'shrink-0 border-b border-border px-6 py-4',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
