<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <h3
    data-slot="empty-title"
    :class="cn('font-medium tracking-tight', props.class)"
  >
    <slot />
  </h3>
</template>
