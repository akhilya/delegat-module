<script setup>
import { computed } from "vue";
import { DropdownMenuTrigger, useForwardProps } from "reka-ui";

const props = defineProps({
  disabled: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
});

const forwardedProps = useForwardProps(props);
const triggerAttrs = computed(() =>
  props.asChild ? {} : { "data-slot": "dropdown-menu-trigger" },
);
</script>

<template>
  <DropdownMenuTrigger v-bind="{ ...triggerAttrs, ...forwardedProps }">
    <slot />
  </DropdownMenuTrigger>
</template>
