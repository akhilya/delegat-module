<script setup>
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <fieldset
    data-slot="field-set"
    :class="
      cn(
        'm-0 min-w-0 border-0 p-0',
        'flex flex-col gap-6',
        'has-[>[data-slot=checkbox-group]]:gap-3 has-[>[data-slot=radio-group]]:gap-3',
        props.class,
      )
    "
  >
    <slot />
  </fieldset>
</template>
