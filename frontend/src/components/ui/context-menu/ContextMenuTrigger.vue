<script setup>
import { ContextMenuTrigger, useForwardProps } from "reka-ui";
import { openContextMenuFromTouchRelease } from "@/lib/contextMenuTouch";

const props = defineProps({
  disabled: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
});

const forwardedProps = useForwardProps(props);

let origin = null;

function onPointerDown(event) {
  if (props.disabled) {
    origin = null;
    return;
  }
  if (event.pointerType !== "touch" && event.pointerType !== "pen") {
    origin = null;
    return;
  }
  origin = { x: event.clientX, y: event.clientY };
}

function onPointerUp(event) {
  const start = origin;
  origin = null;
  if (props.disabled) return;
  openContextMenuFromTouchRelease(event, start);
}

function onPointerCancel() {
  origin = null;
}
</script>

<template>
  <ContextMenuTrigger
    data-slot="context-menu-trigger"
    v-bind="forwardedProps"
    @pointerdown="onPointerDown"
    @pointerup="onPointerUp"
    @pointercancel="onPointerCancel"
  >
    <slot />
  </ContextMenuTrigger>
</template>
