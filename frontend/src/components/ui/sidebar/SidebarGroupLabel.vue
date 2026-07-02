<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div
    data-slot="sidebar-group-label"
    :class="cn('flex h-8 shrink-0 items-center px-2 text-xs font-medium text-sidebar-foreground/60', props.class)"
  >
    <slot />
  </div>
</template>
