<script setup>
import { reactiveOmit } from "@vueuse/core";
import { DropdownMenuItem, useForwardProps } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM } from "@/lib/menuSurface";

const props = defineProps({
  disabled: { type: Boolean, required: false },
  textValue: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
  inset: { type: Boolean, required: false },
  variant: { type: String, required: false, default: "default" },
});

const delegatedProps = reactiveOmit(props, "inset", "variant", "class");

const forwardedProps = useForwardProps(delegatedProps);
</script>

<template>
  <DropdownMenuItem
    data-slot="dropdown-menu-item"
    :data-inset="inset ? '' : undefined"
    :data-variant="variant"
    v-bind="forwardedProps"
    :class="
      cn(
        MENU_ITEM,
        props.class,
      )
    "
  >
    <slot />
  </DropdownMenuItem>
</template>
