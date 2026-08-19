<script setup>
import { SelectLabel } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_LABEL } from "@/lib/menuSurface";

const props = defineProps({
  for: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});
</script>

<template>
  <SelectLabel
    data-slot="select-label"
    :class="cn(MENU_LABEL, props.class)"
  >
    <slot />
  </SelectLabel>
</template>
