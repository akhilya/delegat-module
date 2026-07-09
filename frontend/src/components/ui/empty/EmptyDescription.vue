<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <p
    data-slot="empty-description"
    :class="cn('text-sm/relaxed text-muted-foreground', props.class)"
  >
    <slot />
  </p>
</template>
