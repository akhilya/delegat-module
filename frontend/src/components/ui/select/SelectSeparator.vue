<script setup>
import { reactiveOmit } from "@vueuse/core";
import { SelectSeparator } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_SEPARATOR } from "@/lib/menuSurface";

const props = defineProps({
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});

const delegatedProps = reactiveOmit(props, "class");
</script>

<template>
  <SelectSeparator
    data-slot="select-separator"
    v-bind="delegatedProps"
    :class="cn(MENU_SEPARATOR, 'pointer-events-none', props.class)"
  />
</template>
