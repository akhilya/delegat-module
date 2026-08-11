<script setup>
import { reactiveOmit } from "@vueuse/core";
import {
  DialogContent,
  DialogPortal,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import DialogOverlay from "./DialogOverlay.vue";
import DialogCloseButton from "./DialogCloseButton.vue";

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  trapFocus: { type: Boolean, required: false },
  disableOutsidePointerEvents: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  // Standard width ladder. Pass size="2xl" instead of class="sm:max-w-2xl".
  // Any max-w passed via `class` still wins (it merges after the size class).
  size: { type: String, default: "lg" },
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

const delegatedProps = reactiveOmit(props, "class", "size");

const forwarded = useForwardPropsEmits(delegatedProps, emits);

const sizeClasses = {
  xs: "sm:max-w-xs",
  sm: "sm:max-w-sm",
  md: "sm:max-w-md",
  lg: "sm:max-w-lg",
  xl: "sm:max-w-xl",
  "2xl": "sm:max-w-2xl",
  "3xl": "sm:max-w-3xl",
  "4xl": "sm:max-w-4xl",
  "5xl": "sm:max-w-5xl",
  "6xl": "sm:max-w-6xl",
};

/**
 * Select / MultiSelect / Combobox portal their menus to <body>. A click there is
 * "outside" DialogContent and would dismiss the dialog mid-selection — exactly
 * what broke bulk-edit for select & multiselect fields. Confirm AlertDialog is
 * also portaled above dialogs; treat known overlay slots (and reka popper
 * wrappers) as inside the dialog.
 */
function isPortaledOverlayTarget(target) {
  if (!(target instanceof Element)) return false;
  return Boolean(
    target.closest("[data-slot='popover-content']") ||
      target.closest("[data-slot='select-content']") ||
      target.closest("[data-slot='dropdown-menu-content']") ||
      target.closest("[data-slot='combobox-list']") ||
      target.closest("[data-slot='alert-dialog-content']") ||
      target.closest("[data-slot='alert-dialog-overlay']") ||
      target.closest("[data-reka-popper-content-wrapper]"),
  );
}

function onPointerDownOutside(event) {
  const originalEvent = event.detail?.originalEvent;
  const target = originalEvent?.target ?? event.target;
  if (isPortaledOverlayTarget(target)) {
    event.preventDefault();
    return;
  }
  // Ignore scrollbar clicks on the overlay scroller (same pattern as DialogScrollContent).
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
  const target = event.detail?.originalEvent?.target ?? event.target;
  if (isPortaledOverlayTarget(target)) {
    event.preventDefault();
  }
}

function onInteractOutside(event) {
  const target = event.detail?.originalEvent?.target ?? event.target;
  if (isPortaledOverlayTarget(target)) {
    event.preventDefault();
  }
}
</script>

<template>
  <DialogPortal>
    <DialogOverlay
      class="flex items-start justify-center overflow-y-auto p-4 py-10"
    >
      <DialogContent
        data-slot="dialog-content"
        v-bind="forwarded"
        :class="
          cn(
            'bg-background data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 relative z-[var(--z-modal)] my-auto grid w-full max-w-[calc(100%-2rem)] gap-4 rounded-lg border p-6 shadow-lg duration-200',
            sizeClasses[props.size] ?? sizeClasses.lg,
            props.class,
          )
        "
        @pointer-down-outside="onPointerDownOutside"
        @focus-outside="onFocusOutside"
        @interact-outside="onInteractOutside"
      >
        <slot />

        <DialogCloseButton />
      </DialogContent>
    </DialogOverlay>
  </DialogPortal>
</template>
