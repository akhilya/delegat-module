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
        'mb-0 flex min-w-0 w-full shrink-0 flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center',
        $props.class,
      )
    "
  >
    <div class="flex min-w-0 flex-1 flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
      <slot />
    </div>
    <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2 self-end sm:self-auto">
      <slot name="actions" />
    </div>
  </div>
</template>
