<script setup>
import { reactiveOmit } from "@vueuse/core";
import { Check } from "lucide-vue-next";
import {
  ContextMenuCheckboxItem,
  ContextMenuItemIndicator,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM_CHECK, MENU_ITEM_INDICATOR } from "@/lib/menuSurface";

const props = defineProps({
  modelValue: { type: [Boolean, String], required: false },
  disabled: { type: Boolean, required: false },
  textValue: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});
const emits = defineEmits(["select", "update:modelValue"]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <ContextMenuCheckboxItem
    data-slot="context-menu-checkbox-item"
    v-bind="forwarded"
    :class="cn(MENU_ITEM_INDICATOR, props.class)"
  >
    <slot />
    <span :class="MENU_ITEM_CHECK">
      <ContextMenuItemIndicator>
        <slot name="indicator-icon">
          <Check class="size-4 text-foreground" />
        </slot>
      </ContextMenuItemIndicator>
    </span>
  </ContextMenuCheckboxItem>
</template>
