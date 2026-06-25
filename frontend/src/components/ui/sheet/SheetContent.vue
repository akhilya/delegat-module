<script setup>
import { reactiveOmit } from "@vueuse/core";
import {
  DialogContent,
  DialogPortal,
  useForwardPropsEmits,
} from "reka-ui";
import { cn } from "@/lib/utils";
import SheetOverlay from "./SheetOverlay.vue";
import DialogCloseButton from "../dialog/DialogCloseButton.vue";

const props = defineProps({
  forceMount: { type: Boolean, required: false },
  trapFocus: { type: Boolean, required: false },
  disableOutsidePointerEvents: { type: Boolean, required: false },
  asChild: { type: Boolean, required: false },
  as: { type: null, required: false },
  class: { type: null, required: false },
  side: { type: String, default: "right" },
});
const emits = defineEmits([
  "escapeKeyDown",
  "pointerDownOutside",
  "focusOutside",
  "interactOutside",
  "openAutoFocus",
  "closeAutoFocus",
]);

const delegatedProps = reactiveOmit(props, "class", "side");
const forwarded = useForwardPropsEmits(delegatedProps, emits);

const sideClasses = {
  right:
    "inset-y-0 right-0 h-full w-3/4 sm:max-w-md border-l data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right",
  left:
    "inset-y-0 left-0 h-full w-3/4 sm:max-w-md border-r data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left",
  top:
    "inset-x-0 top-0 border-b data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-top data-[state=open]:slide-in-from-top",
  bottom:
    "inset-x-0 bottom-0 border-t data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:slide-out-to-bottom data-[state=open]:slide-in-from-bottom",
};
</script>

<template>
  <DialogPortal>
    <SheetOverlay />
    <DialogContent
      data-slot="sheet-content"
      v-bind="forwarded"
      :class="
        cn(
          'bg-background fixed z-[var(--z-modal)] flex flex-col gap-4 p-6 shadow-lg transition ease-in-out duration-300 data-[state=closed]:duration-200',
          sideClasses[props.side],
          props.class,
        )
      "
    >
      <slot />

      <DialogCloseButton />
    </DialogContent>
  </DialogPortal>
</template>
