<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div :class="cn('bg-background min-h-screen adaptive-padding', props.class)">
    <slot />
  </div>
</template>