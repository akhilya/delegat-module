<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  /** split: two equal actions (Apple 2-button). stack: full-width column (3+). */
  layout: {
    type: String,
    required: false,
    default: "split",
  },
});
</script>

<template>
  <div
    data-slot="alert-dialog-footer"
    :data-layout="props.layout"
    :class="
      cn(
        props.layout === 'stack'
          ? 'flex flex-col gap-3'
          : 'flex flex-col-reverse gap-3 sm:grid sm:grid-cols-2',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
