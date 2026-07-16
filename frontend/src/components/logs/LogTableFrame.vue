<script setup>
import { cn } from "@/lib/utils";

defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div :class="cn('overflow-hidden rounded-lg border', $props.class)">
    <div class="overflow-x-auto">
      <slot />
    </div>
    <div
      v-if="$slots.footer"
      class="flex items-center justify-between gap-3 border-t bg-muted/20 px-3 py-2.5 text-sm text-muted-foreground"
    >
      <slot name="footer" />
    </div>
  </div>
</template>
