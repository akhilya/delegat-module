<script setup>
import { reactiveOmit } from "@vueuse/core";
import {
  ContextMenuContent,
  ContextMenuPortal,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ANIMATION, MENU_SURFACE } from "@/lib/menuSurface";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  loop: { type: Boolean, required: false },
  sideFlip: { type: Boolean, required: false },
  alignOffset: { type: Number, required: false },
  alignFlip: { type: Boolean, required: false },
  avoidCollisions: { type: Boolean, required: false },
  collisionBoundary: { type: null, required: false },
  collisionPadding: { type: [Number, Object], required: false },
  hideShiftedArrow: { type: Boolean, required: false },
  sticky: { type: String, required: false },
  hideWhenDetached: { type: Boolean, required: false },
  positionStrategy: { type: String, required: false },
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
  "closeAutoFocus",
]);

const delegatedProps = reactiveOmit(props, "class");

const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <ContextMenuPortal>
    <ContextMenuContent
      data-slot="context-menu-content"
      v-bind="{ ...$attrs, ...forwarded }"
      :class="
        cn(
          'z-[var(--z-popover)] max-h-(--reka-context-menu-content-available-height) min-w-48 origin-(--reka-context-menu-content-transform-origin) overflow-x-hidden overflow-y-auto p-1',
          MENU_SURFACE,
          MENU_ANIMATION,
          props.class,
        )
      "
    >
      <slot />
    </ContextMenuContent>
  </ContextMenuPortal>
</template>
