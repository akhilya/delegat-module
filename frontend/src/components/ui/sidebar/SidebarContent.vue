<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div data-slot="sidebar-content" :class="cn('flex min-h-0 flex-1 flex-col gap-2 overflow-auto p-2', props.class)">
    <slot />
  </div>
</template>
