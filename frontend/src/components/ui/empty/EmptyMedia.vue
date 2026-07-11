<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  // "icon": muted circle around a lucide icon; "default": bare media (image, illustration)
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
          'grid size-12 place-items-center rounded-full border bg-muted/40 text-muted-foreground [&_svg]:size-6',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
