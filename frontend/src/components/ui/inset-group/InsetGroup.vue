<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div
    data-slot="inset-group"
    :class="
      cn(
        'flex flex-col gap-0.5 overflow-hidden rounded-xl bg-card p-1 ring-1 ring-border/70',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
