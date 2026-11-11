<script setup>
import { computed, toRef, useAttrs } from "vue";
import { reactiveOmit } from "@vueuse/core";
import { DialogContent, DialogPortal, useForwardPropsEmits } from "reka-ui";
import { cn } from "@/lib/utils";
import { isConfirmOverlayActive } from "@/composables/useConfirm";
import {
  SHEET_RESIZE_MAX,
  SHEET_RESIZE_MIN,
  SHEET_WIDTH_DEFAULTS,
  useSheetResize,
} from "@/composables/useSheetResize";
import DialogCloseButton from "@/components/ui/dialog/DialogCloseButton.vue";
import SheetOverlay from "./SheetOverlay.vue";
import SheetResizeHandle from "./SheetResizeHandle.vue";

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
  /**
   * Float the panel slightly off the viewport (8px) with rounded corners.
   * Implied when `resizable`.
   */
  inset: { type: Boolean, default: false },
  /**
   * Capsule drag handle on the free edge; width is stored per `storageKey`
   * or controlled via `width` / `update:width`.
   */
  resizable: { type: Boolean, default: false },
  width: { type: Number, required: false },
  defaultWidth: { type: Number, default: SHEET_WIDTH_DEFAULTS.ai },
  storageKey: { type: String, required: false, default: undefined },
  minWidth: { type: Number, default: SHEET_RESIZE_MIN },
  maxWidth: { type: Number, default: SHEET_RESIZE_MAX },
  /**
   * Platform overlay scrim (`bg-black/50` + blur). Off for a companion sheet
   * that must leave the page usable (AI chat).
   */
  overlay: { type: Boolean, default: true },
  /**
   * Dismiss when the pointer/focus leaves the panel. Off for AI so catalog
   * work can continue with the assistant open.
   */
  closeOnOutside: { type: Boolean, default: true },
  /**
   * Extra pixels from the inset edge. Companion sheets stack without overlap.
   */
  edgeOffset: { type: Number, default: 0 },
});
const emits = defineEmits([
  "escapeKeyDown",
  "pointerDownOutside",
  "focusOutside",
  "interactOutside",
  "openAutoFocus",
  "closeAutoFocus",
  "update:width",
]);

const attrs = useAttrs();
const isInset = computed(() => props.inset || props.resizable);

const {
  width: liveWidth,
  isResizing,
  startResize,
} = useSheetResize({
  enabled: toRef(props, "resizable"),
  width: toRef(props, "width"),
  setWidth: (w) => emits("update:width", w),
  storageKey: props.storageKey,
  defaultWidth: props.defaultWidth,
  minWidth: props.minWidth,
  maxWidth: props.maxWidth,
  side: toRef(props, "side"),
});

const mergedStyle = computed(() => {
  const fromAttrs = attrs.style;
  const layout = {};
  if (props.resizable) {
    layout.width = `${liveWidth.value}px`;
    layout.maxWidth = "calc(100vw - 1rem)";
  }
  if (isInset.value && props.side === "right") {
    layout.right = `${8 + (Number(props.edgeOffset) || 0)}px`;
  } else if (isInset.value && props.side === "left") {
    layout.left = `${8 + (Number(props.edgeOffset) || 0)}px`;
  }
  if (Object.keys(layout).length === 0) return fromAttrs;
  if (!fromAttrs) return layout;
  if (typeof fromAttrs === "string") return [layout, fromAttrs];
  return { ...fromAttrs, ...layout };
});

const delegatedProps = reactiveOmit(
  props,
  "class",
  "side",
  "showClose",
  "inset",
  "resizable",
  "width",
  "defaultWidth",
  "storageKey",
  "minWidth",
  "maxWidth",
  "overlay",
  "closeOnOutside",
  "disableOutsidePointerEvents",
  "edgeOffset",
);

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
    target.closest("[data-slot='sheet-content']") ||
    target.closest("[data-companion-actions]") ||
    target.closest("[data-reka-popper-content-wrapper]"),
  );
}

function onEscapeKeyDown(event) {
  if (isConfirmOverlayActive()) event.preventDefault();
}

function shouldIgnoreOutside(event) {
  if (isConfirmOverlayActive() || !props.closeOnOutside) return true;
  const target = event.detail?.originalEvent?.target ?? event.target;
  return isPortaledOverlayTarget(target);
}

function onPointerDownOutside(event) {
  if (shouldIgnoreOutside(event)) {
    event.preventDefault();
    return;
  }
  const originalEvent = event.detail?.originalEvent;
  const target = originalEvent?.target ?? event.target;
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
  if (shouldIgnoreOutside(event)) event.preventDefault();
}

function onInteractOutside(event) {
  if (shouldIgnoreOutside(event)) event.preventDefault();
}

function insetSideClass() {
  if (props.resizable) {
    if (props.side === "right") {
      return "data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right top-2 right-2 bottom-2 h-auto !left-auto";
    }
    if (props.side === "left") {
      return "data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left top-2 left-2 bottom-2 h-auto !right-auto";
    }
  }
  if (props.side === "right") {
    return "data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right top-2 right-2 bottom-2 left-2 h-auto w-auto sm:left-auto sm:w-3/4 sm:max-w-sm";
  }
  if (props.side === "left") {
    return "data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left top-2 left-2 bottom-2 right-2 h-auto w-auto sm:right-auto sm:w-3/4 sm:max-w-sm";
  }
  return "";
}
</script>

<template>
  <DialogPortal>
    <SheetOverlay v-if="overlay" />
    <DialogContent
      data-slot="sheet-content"
      :data-inset="isInset ? '' : undefined"
      :data-resizable="resizable ? '' : undefined"
      :data-resizing="resizable && isResizing ? '' : undefined"
      :class="
        cn(
          'bg-background data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed z-[var(--z-modal)] flex flex-col gap-4 ease-[var(--ease-out-quart)] data-[state=closed]:duration-[var(--duration-drawer-out)] data-[state=open]:duration-[var(--duration-drawer)]',
          resizable &&
            (isResizing
              ? 'transition-none will-change-[width,right,left]'
              : 'transition-[width,right,left] duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]'),
          isInset ? 'min-h-0 rounded-xl border-0' : 'shadow-none',
          side === 'right' &&
            (isInset
              ? insetSideClass()
              : 'data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right inset-y-0 right-0 h-full w-3/4 border-l sm:max-w-sm'),
          side === 'left' &&
            (isInset
              ? insetSideClass()
              : 'data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left inset-y-0 left-0 h-full w-3/4 border-r sm:max-w-sm'),
          side === 'top' &&
            (isInset
              ? 'data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top left-2 right-2 top-2 h-auto'
              : 'data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top inset-x-0 top-0 h-auto border-b'),
          side === 'bottom' &&
            (isInset
              ? 'data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom left-2 right-2 bottom-2 h-auto'
              : 'data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom inset-x-0 bottom-0 h-auto border-t'),
          props.class,
        )
      "
      v-bind="{ ...$attrs, ...forwarded }"
      :style="mergedStyle"
      :disable-outside-pointer-events="closeOnOutside"
      @escape-key-down="onEscapeKeyDown"
      @pointer-down-outside="onPointerDownOutside"
      @focus-outside="onFocusOutside"
      @interact-outside="onInteractOutside"
    >
      <div
        :class="
          isInset
            ? 'relative flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden rounded-xl'
            : 'contents'
        "
      >
        <SheetResizeHandle
          v-if="resizable"
          class="max-lg:hidden"
          :side="side"
          :resizing="isResizing"
          @pointerdown="startResize"
        />
        <slot />

        <DialogCloseButton
          v-if="showClose"
          class="top-4 right-6 size-8 rounded-md"
        />
      </div>
    </DialogContent>
  </DialogPortal>
</template>
