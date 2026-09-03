<script setup>
import { cn } from "@/lib/utils";

defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div :class="cn('flex w-full flex-col', $props.class)">
    <div class="overflow-x-auto">
      <slot />
    </div>
    <div
      v-if="$slots.footer"
      class="flex shrink-0 items-center justify-between gap-3 border-t bg-background px-3 py-2 text-sm text-muted-foreground"
    >
      <slot name="footer" />
    </div>
  </div>
</template>
