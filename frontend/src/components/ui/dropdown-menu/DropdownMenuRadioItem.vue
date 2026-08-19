<script setup>
import { reactiveOmit } from "@vueuse/core";
import { Check } from "lucide-vue-next";
import {
  DropdownMenuItemIndicator,
  DropdownMenuRadioItem,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ITEM_CHECK, MENU_ITEM_INDICATOR } from "@/lib/menuSurface";

const props = defineProps({
  value: { type: String, required: true },
  disabled: { type: Boolean, required: false },
  textValue: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});

const emits = defineEmits(["select"]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <DropdownMenuRadioItem
    data-slot="dropdown-menu-radio-item"
    v-bind="forwarded"
    :class="cn(MENU_ITEM_INDICATOR, props.class)"
  >
    <slot />
    <span :class="MENU_ITEM_CHECK">
      <DropdownMenuItemIndicator>
        <Check class="size-4 text-foreground" />
      </DropdownMenuItemIndicator>
    </span>
  </DropdownMenuRadioItem>
</template>
