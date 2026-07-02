<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div
    data-slot="card"
    :class="
      cn(
        // Refined surface: no drop-shadow (generic AI tell), softer border tint, radius
        // matched to dialogs (8px instead of 12px). Tighter internal rhythm — gap-5/py-5
        // for dashboard density; px-6 in CardHeader/Content/Footer keeps form alignment.
        'bg-card text-card-foreground flex flex-col gap-5 rounded-lg border border-border/70 py-5',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
