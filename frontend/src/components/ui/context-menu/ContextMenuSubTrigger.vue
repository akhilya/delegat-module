<script setup>
import { reactiveOmit } from "@vueuse/core";
import { ChevronRight } from "lucide-vue-next";
import { ContextMenuSubTrigger, useForwardProps } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM } from "@/lib/menuSurface";

const props = defineProps({
  disabled: { type: Boolean, required: false },
  textValue: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
  inset: { type: Boolean, required: false },
});

const delegatedProps = reactiveOmit(props, "class", "inset");

const forwardedProps = useForwardProps(delegatedProps);
</script>

<template>
  <ContextMenuSubTrigger
    data-slot="context-menu-sub-trigger"
    :data-inset="inset ? '' : undefined"
    v-bind="forwardedProps"
    :class="
      cn(
        MENU_ITEM,
        props.class,
      )
    "
  >
    <slot />
    <ChevronRight class="ml-auto size-4" />
  </ContextMenuSubTrigger>
</template>
