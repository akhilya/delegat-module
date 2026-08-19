<script setup>
import { reactiveOmit } from "@vueuse/core";
import {
  DropdownMenuPortal,
  DropdownMenuSubContent,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ANIMATION, MENU_SURFACE } from "@/lib/menuSurface";

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  loop: { type: Boolean, required: false },
  sideOffset: { type: Number, required: false },
  alignOffset: { type: Number, required: false },
  avoidCollisions: { type: Boolean, required: false },
  collisionBoundary: { type: null, required: false },
  collisionPadding: { type: [Number, Object], required: false },
  arrowPadding: { type: Number, required: false },
  sticky: { type: String, required: false },
  hideWhenDetached: { type: Boolean, required: false },
  positionStrategy: { type: String, required: false },
  updatePositionStrategy: { type: String, required: false },
  disableUpdateOnLayoutShift: { type: Boolean, required: false },
  prioritizePosition: { type: Boolean, required: false },
  reference: { type: null, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
});
const emits = defineEmits([
  "escapeKeyDown",
  "pointerDownOutside",
  "focusOutside",
  "interactOutside",
  "entryFocus",
  "openAutoFocus",
  "closeAutoFocus",
]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <DropdownMenuPortal>
    <DropdownMenuSubContent
      data-slot="dropdown-menu-sub-content"
      v-bind="forwarded"
      :class="
        cn(
          'z-[var(--z-popover)] min-w-36 origin-(--reka-dropdown-menu-content-transform-origin) overflow-hidden p-1',
          MENU_SURFACE,
          MENU_ANIMATION,
          props.class,
        )
      "
    >
      <slot />
    </DropdownMenuSubContent>
  </DropdownMenuPortal>
</template>
