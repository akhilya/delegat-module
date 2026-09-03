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
  <tr
    data-slot="table-row"
    :class="
      cn(
        'group border-b border-border/80 transition-colors hover:bg-muted/40 data-[state=selected]:bg-muted/70',
        props.class,
      )
    "
  >
    <slot />
  </tr>
</template>
