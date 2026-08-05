<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  /**
   * default — sits inside DialogContent padding (p-6).
   * bar — bordered top chrome for DialogContent with `p-0` / `gap-0`
   *        (bulk editors, tall scroll dialogs).
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
    data-slot="dialog-header"
    :data-variant="variant"
    :class="
      cn(
        'flex flex-col gap-1 text-left',
        variant === 'bar' &&
          'shrink-0 border-b border-border px-6 py-4',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
