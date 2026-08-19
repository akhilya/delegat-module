<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  /**
   * default — sits inside SheetContent padding.
   * bar — bordered bottom chrome for SheetContent with `p-0` / `gap-0`.
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
    data-slot="sheet-footer"
    :data-variant="variant"
    :class="
      cn(
        'flex flex-col-reverse gap-2 sm:flex-row sm:justify-end',
        variant === 'default' && 'mt-auto p-4',
        variant === 'bar' && 'mt-0 shrink-0 border-t border-border px-6 py-4',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
