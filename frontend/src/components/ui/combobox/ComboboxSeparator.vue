<script setup>
import { reactiveOmit } from "@vueuse/core";
import { ComboboxSeparator } from "reka-ui";
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
  <ComboboxSeparator
    data-slot="combobox-separator"
    v-bind="delegatedProps"
    :class="cn(MENU_SEPARATOR, props.class)"
  >
    <slot />
  </ComboboxSeparator>
</template>
