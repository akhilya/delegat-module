<script setup>
// Shared close affordance for Dialog, DialogScrollContent and Sheet.
// One source of truth so every modal's "X" looks and focuses identically:
// 28px hit target, muted resting state, accent hover, visible keyboard ring.
import { X } from "lucide-vue-next";
import { DialogClose } from "reka-ui";
import { cn } from "@/lib/utils";

const props = defineProps({
  class: { type: null, required: false },
});
</script>

<template>
  <DialogClose
    data-slot="dialog-close-button"
    :class="
      cn(
        'absolute top-3.5 right-3.5 inline-flex size-7 items-center justify-center rounded-sm text-muted-foreground opacity-70 ring-offset-background transition-opacity hover:bg-accent hover:opacity-100 focus-visible:opacity-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:pointer-events-none [&_svg]:pointer-events-none [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <X class="size-4" />
    <span class="sr-only">Закрыть</span>
  </DialogClose>
</template>
