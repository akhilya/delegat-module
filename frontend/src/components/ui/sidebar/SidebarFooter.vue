<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div data-slot="sidebar-footer" :class="cn('flex flex-col gap-2 border-t p-3', props.class)">
    <slot />
  </div>
</template>
