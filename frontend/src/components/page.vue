<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <!--
    min-h-full: page fills the inset scroller and can grow with rows.
    Flex column so AppPageChrome can expand empty states without a nested table scroll.
  -->
  <div :class="cn('flex min-h-full flex-col bg-background adaptive-padding', props.class)">
    <slot />
  </div>
</template>
