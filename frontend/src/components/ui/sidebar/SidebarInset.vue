<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <main data-slot="sidebar-inset" :class="cn('flex min-h-screen min-w-0 flex-1 flex-col', props.class)">
    <slot />
  </main>
</template>
