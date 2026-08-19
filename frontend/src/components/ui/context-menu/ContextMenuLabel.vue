<script setup>
import { reactiveOmit } from "@vueuse/core";
import { ContextMenuLabel } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_LABEL } from "@/lib/menuSurface";

const props = defineProps({
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
  inset: { type: Boolean, required: false },
});

const delegatedProps = reactiveOmit(props, "class");
</script>

<template>
  <ContextMenuLabel
    data-slot="context-menu-label"
    :data-inset="inset ? '' : undefined"
    v-bind="delegatedProps"
    :class="cn(MENU_LABEL, props.class)"
  >
    <slot />
  </ContextMenuLabel>
</template>
