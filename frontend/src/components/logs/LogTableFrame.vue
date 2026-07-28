<script setup>
import { cn } from "@/lib/utils";

defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div
    :class="
      cn(
        'flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border bg-background',
        $props.class,
      )
    "
  >
    <div class="min-h-0 flex-1 overflow-auto">
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
