<script setup>
import { reactiveOmit } from "@vueuse/core";
import { ComboboxItem, useForwardPropsEmits } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM_INDICATOR } from "@/lib/menuSurface";

const props = defineProps({
  textValue: { type: String, required: false },
  value: { type: null, required: true },
  disabled: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});
const emits = defineEmits(["select"]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <ComboboxItem
    data-slot="combobox-item"
    v-bind="forwarded"
    :class="
      cn(
        MENU_ITEM_INDICATOR,
        props.class,
      )
    "
  >
    <slot />
  </ComboboxItem>
</template>
