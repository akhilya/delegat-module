<script setup>
import { reactiveOmit } from "@vueuse/core";
import { DialogContent, DialogPortal, useForwardPropsEmits } from "reka-ui";
import { cn } from "@/lib/utils";
import { isConfirmOverlayActive } from "@/composables/useConfirm";
import DialogCloseButton from "@/components/ui/dialog/DialogCloseButton.vue";
import SheetOverlay from "./SheetOverlay.vue";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  class: { type: null, required: false },
  side: { type: String, required: false, default: "right" },
  forceMount: { type: Boolean, required: false },
  disableOutsidePointerEvents: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  /** False when the header already has an in-flow close (bar sheets). */
  showClose: { type: Boolean, default: true },
});
const emits = defineEmits([
  "escapeKeyDown",
  "pointerDownOutside",
  "focusOutside",
  "interactOutside",
  "openAutoFocus",
  "closeAutoFocus",
]);

const delegatedProps = reactiveOmit(props, "class", "side", "showClose");

const forwarded = useForwardPropsEmits(delegatedProps, emits);

function isPortaledOverlayTarget(target) {
  if (!(target instanceof Element)) return false;
  return Boolean(
    target.closest("[data-slot='popover-content']") ||
    target.closest("[data-slot='select-content']") ||
    target.closest("[data-slot='dropdown-menu-content']") ||
    target.closest("[data-slot='combobox-list']") ||
    target.closest("[data-slot='alert-dialog-content']") ||
    target.closest("[data-slot='alert-dialog-overlay']") ||
    target.closest("[data-slot='dialog-content']") ||
    target.closest("[data-reka-popper-content-wrapper]"),
  );
}

function onEscapeKeyDown(event) {
  if (isConfirmOverlayActive()) event.preventDefault();
}

function onPointerDownOutside(event) {
  if (isConfirmOverlayActive()) {
    event.preventDefault();
    return;
  }
  const originalEvent = event.detail?.originalEvent;
  const target = originalEvent?.target ?? event.target;
  if (isPortaledOverlayTarget(target)) {
    event.preventDefault();
    return;
  }
  if (originalEvent && target instanceof HTMLElement) {
    if (
      originalEvent.offsetX > target.clientWidth ||
      originalEvent.offsetY > target.clientHeight
    ) {
      event.preventDefault();
    }
  }
}

function onFocusOutside(event) {
  if (isConfirmOverlayActive()) {
    event.preventDefault();
    return;
  }
  const target = event.detail?.originalEvent?.target ?? event.target;
  if (isPortaledOverlayTarget(target)) {
    event.preventDefault();
  }
}

function onInteractOutside(event) {
  if (isConfirmOverlayActive()) {
    event.preventDefault();
    return;
  }
  const target = event.detail?.originalEvent?.target ?? event.target;
  if (isPortaledOverlayTarget(target)) {
    event.preventDefault();
  }
}
</script>

<template>
  <DialogPortal>
    <SheetOverlay />
    <DialogContent
      data-slot="sheet-content"
      :class="
        cn(
          'bg-background data-[state=open]:animate-in data-[state=closed]:animate-out fixed z-[var(--z-modal)] flex flex-col gap-4 shadow-none ease-[var(--ease-out-quart)] data-[state=closed]:duration-[var(--duration-drawer-out)] data-[state=open]:duration-[var(--duration-drawer)]',
          side === 'right' &&
            'data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right inset-y-0 right-0 h-full w-3/4 border-l sm:max-w-sm',
          side === 'left' &&
            'data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left inset-y-0 left-0 h-full w-3/4 border-r sm:max-w-sm',
          side === 'top' &&
            'data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top inset-x-0 top-0 h-auto border-b',
          side === 'bottom' &&
            'data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom inset-x-0 bottom-0 h-auto border-t',
          props.class,
        )
      "
      v-bind="{ ...$attrs, ...forwarded }"
      @escape-key-down="onEscapeKeyDown"
      @pointer-down-outside="onPointerDownOutside"
      @focus-outside="onFocusOutside"
      @interact-outside="onInteractOutside"
    >
      <slot />

      <DialogCloseButton
        v-if="showClose"
        class="top-4 right-6 size-8 rounded-md"
      />
    </DialogContent>
  </DialogPortal>
</template>
