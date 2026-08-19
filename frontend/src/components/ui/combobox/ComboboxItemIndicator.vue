<script setup>
import { reactiveOmit } from "@vueuse/core";
import { ComboboxItemIndicator, useForwardProps } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM_CHECK } from "@/lib/menuSurface";

const props = defineProps({
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardProps(delegatedProps);
</script>

<template>
  <ComboboxItemIndicator
    data-slot="combobox-item-indicator"
    v-bind="forwarded"
    :class="cn(MENU_ITEM_CHECK, props.class)"
  >
    <slot />
  </ComboboxItemIndicator>
</template>
