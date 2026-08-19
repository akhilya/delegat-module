<script setup>
import { reactiveOmit } from "@vueuse/core";
import { PopoverContent, PopoverPortal, useForwardPropsEmits } from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ANIMATION, MENU_SURFACE } from "@/lib/menuSurface";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  side: { type: null, required: false },
  sideOffset: { type: Number, required: false, default: 4 },
  align: { type: null, required: false, default: "center" },
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
  disableOutsidePointerEvents: { type: Boolean, required: false },
  class: { type: null, required: false },
});
const emits = defineEmits([
  "escapeKeyDown",
  "pointerDownOutside",
  "focusOutside",
  "interactOutside",
  "openAutoFocus",
  "closeAutoFocus",
]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <PopoverPortal>
    <PopoverContent
      data-slot="popover-content"
      v-bind="{ ...forwarded, ...$attrs }"
      :class="
        cn(
          'z-[var(--z-popover)] w-auto min-w-0 max-w-[calc(100vw-1rem)] origin-(--reka-popover-content-transform-origin) p-4 outline-hidden',
          MENU_SURFACE,
          MENU_ANIMATION,
          props.class,
        )
      "
    >
      <slot />
    </PopoverContent>
  </PopoverPortal>
</template>
