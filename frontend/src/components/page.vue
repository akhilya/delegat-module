<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <!-- min-h-svh only: no max-height / overflow scroll — SidebarInset scrolls the page. -->
  <div :class="cn('bg-background adaptive-padding min-h-svh', props.class)">
    <slot />
  </div>
</template>
