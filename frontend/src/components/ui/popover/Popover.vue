<script setup>
import { PopoverRoot, useForwardPropsEmits } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  defaultOpen: { type: Boolean, required: false },
  open: { type: Boolean, required: false },
  modal: { type: Boolean, required: false },
  class: { type: null, required: false },
});
const emits = defineEmits(["update:open"]);

const forwarded = useForwardPropsEmits(props, emits);
</script>

<template>
  <PopoverRoot data-slot="popover" v-bind="forwarded" :class="cn('block w-full min-w-0', props.class)">
    <slot />
  </PopoverRoot>
</template>
