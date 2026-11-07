<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  // Icons stay bare; media never adds a decorative tile or outline.
  variant: { type: String, default: "default" },
  class: { type: null, required: false },
});
</script>

<template>
  <div
    data-slot="empty-media"
    :class="
      cn(
        'mb-1 flex shrink-0 items-center justify-center',
        variant === 'icon' &&
          'grid size-8 place-items-center text-muted-foreground [&_svg]:size-6',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
