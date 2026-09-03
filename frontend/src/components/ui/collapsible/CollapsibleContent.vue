<script setup>
import { CollapsibleContent } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});
</script>

<template>
  <CollapsibleContent
    data-slot="collapsible-content"
    :force-mount="forceMount"
    :as-child="asChild"
    :as="as"
    :class="
      cn(
        'overflow-hidden data-[state=closed]:animate-collapsible-up data-[state=open]:animate-collapsible-down duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]',
        props.class,
      )
    "
  >
    <slot />
  </CollapsibleContent>
</template>
