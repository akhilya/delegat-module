<script setup>
import { cn } from "@/lib/utils";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  class: {
    type: [Boolean, null, String, Object, Array],
    required: false,
    skipCheck: true,
  },
});
</script>

<template>
  <td
    data-slot="table-cell"
    v-bind="$attrs"
    :class="
      cn(
        'p-2 align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0 *:[[role=checkbox]]:translate-y-0.5',
        props.class,
      )
    "
  >
    <slot />
  </td>
</template>
