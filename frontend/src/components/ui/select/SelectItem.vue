<script setup>
import { reactiveOmit } from "@vueuse/core";
import { Check } from "lucide-vue-next";
import {
  SelectItem,
  SelectItemIndicator,
  SelectItemText,
  useForwardProps,
} from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM_CHECK, MENU_ITEM_INDICATOR } from "@/lib/menuSurface";

const props = defineProps({
  value: { type: null, required: true },
  disabled: { type: Boolean, required: false },
  textValue: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});

const delegatedProps = reactiveOmit(props, "class");

const forwardedProps = useForwardProps(delegatedProps);
</script>

<template>
  <SelectItem
    data-slot="select-item"
    v-bind="forwardedProps"
    :class="cn(MENU_ITEM_INDICATOR, 'w-full', props.class)"
  >
    <SelectItemText>
      <slot />
    </SelectItemText>
    <span :class="MENU_ITEM_CHECK">
      <SelectItemIndicator>
        <Check class="size-4 text-foreground" />
      </SelectItemIndicator>
    </span>
  </SelectItem>
</template>
