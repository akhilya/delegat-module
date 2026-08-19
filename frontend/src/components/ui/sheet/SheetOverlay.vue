<script setup>
import { reactiveOmit } from "@vueuse/core";
import { DialogOverlay } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});

const delegatedProps = reactiveOmit(props, "class");
</script>

<template>
  <DialogOverlay
    data-slot="sheet-overlay"
    :class="
      cn(
        'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:duration-[var(--duration-overlay-out)] data-[state=open]:duration-[var(--duration-overlay)] ease-[var(--ease-out-quart)] fixed inset-0 z-[var(--z-overlay)] bg-black/50 backdrop-blur-xs',
        props.class,
      )
    "
    v-bind="delegatedProps"
  >
    <slot />
  </DialogOverlay>
</template>
