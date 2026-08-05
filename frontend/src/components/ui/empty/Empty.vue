<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <div
    data-slot="empty"
    :class="
      cn(
        'flex min-w-0 flex-col items-center justify-center gap-4 text-balance text-center',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
