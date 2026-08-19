<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  variant: { type: String, default: "legend" },
});
</script>

<template>
  <legend
    data-slot="field-legend"
    :data-variant="variant"
    :class="
      cn(
        'float-none mb-2 w-full p-0 px-1 text-xs font-medium leading-4 text-muted-foreground',
        props.class,
      )
    "
  >
    <slot />
  </legend>
</template>
