<script setup>
import { reactiveOmit } from "@vueuse/core";
import {
  TooltipContent,
  TooltipPortal,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import { MENU_ANIMATION, MENU_SURFACE } from "@/lib/menuSurface";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  ariaLabel: { type: String, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  side: { type: null, required: false },
  sideOffset: { type: Number, required: false, default: 4 },
  align: { type: null, required: false },
  alignOffset: { type: Number, required: false },
  avoidCollisions: { type: Boolean, required: false },
  collisionBoundary: { type: null, required: false },
  collisionPadding: { type: [Number, Object], required: false },
  arrowPadding: { type: Number, required: false },
  sticky: { type: String, required: false },
  hideWhenDetached: { type: Boolean, required: false },
  positionStrategy: { type: String, required: false },
  updatePositionStrategy: { type: String, required: false },
  class: { type: null, required: false },
});

const emits = defineEmits(["escapeKeyDown", "pointerDownOutside"]);

const delegatedProps = reactiveOmit(props, "class");
const forwarded = useForwardPropsEmits(delegatedProps, emits);
</script>

<template>
  <TooltipPortal>
    <TooltipContent
      data-slot="tooltip-content"
      v-bind="{ ...forwarded, ...$attrs }"
      :class="
        cn(
          'z-[var(--z-popover)] w-fit px-2.5 py-1 text-xs text-balance',
          MENU_SURFACE,
          MENU_ANIMATION,
          props.class,
        )
      "
    >
      <slot />
    </TooltipContent>
  </TooltipPortal>
</template>
