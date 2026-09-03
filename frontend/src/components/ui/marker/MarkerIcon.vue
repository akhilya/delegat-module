<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: {
    type: [Boolean, null, String, Object, Array],
    required: false,
    skipCheck: true,
  },
});
</script>

<template>
  <span
    data-slot="marker-icon"
    aria-hidden="true"
    :class="
      cn(`size-4 [&_svg:not([class*='size-'])]:size-4 shrink-0`, props.class)
    "
  >
    <slot />
  </span>
</template>
