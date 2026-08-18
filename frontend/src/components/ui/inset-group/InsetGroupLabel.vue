<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <p
    data-slot="inset-group-label"
    :class="cn('mb-2 px-1 text-xs font-medium text-muted-foreground', props.class)"
  >
    <slot />
  </p>
</template>
