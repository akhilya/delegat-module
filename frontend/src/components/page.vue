<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <!--
    min-h-svh: page fills the inset. Flex column so AppPageChrome can grow
    the region below the navbar without forcing extra scroll (nav + flex-1 = svh).
  -->
  <div :class="cn('flex min-h-svh flex-col bg-background adaptive-padding', props.class)">
    <slot />
  </div>
</template>
