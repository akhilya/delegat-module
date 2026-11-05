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
  <th
    data-slot="table-head"
    :class="
      cn(
        'text-muted-foreground h-10 px-2 text-left align-middle text-xs font-medium tracking-tight whitespace-nowrap',
        props.class,
      )
    "
  >
    <slot />
  </th>
</template>
