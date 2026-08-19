<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
  /**
   * Inner scroller. Never put overflow-hidden / overflow-y-auto on this
   * root — that clips the hairline ring. Ring is inset so a parent
   * overflow-hidden cannot shave the frame either.
   */
  scrollable: { type: Boolean, default: false },
});
</script>

<template>
  <div
    data-slot="inset-group"
    :class="
      cn(
        'flex flex-col rounded-xl bg-card p-1 ring-1 ring-inset ring-border/70',
        scrollable ? 'min-h-0 flex-1' : 'gap-0.5',
        props.class,
      )
    "
  >
    <div
      v-if="scrollable"
      class="flex min-h-0 flex-1 flex-col gap-0.5 overflow-y-auto"
    >
      <slot />
    </div>
    <slot v-else />
  </div>
</template>
