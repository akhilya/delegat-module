<script setup>
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Loader2 } from "lucide-vue-next";

defineProps({
  open: { type: Boolean, default: false },
  loading: { type: Boolean, default: false },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
});

defineEmits(["update:open"]);
</script>

<template>
  <Sheet :open="open" @update:open="$emit('update:open', $event)">
    <SheetContent
      side="right"
      class="flex w-full flex-col gap-0 overflow-hidden sm:max-w-xl"
    >
      <SheetHeader class="shrink-0 pr-10 text-left">
        <SheetTitle>
          <slot name="title">{{ title }}</slot>
        </SheetTitle>
        <SheetDescription
          v-if="description || $slots.description"
          class="break-all"
        >
          <slot name="description">{{ description }}</slot>
        </SheetDescription>
        <div v-if="$slots.actions" class="flex flex-wrap gap-2 pt-2">
          <slot name="actions" />
        </div>
      </SheetHeader>

      <div
        v-if="loading"
        class="flex flex-1 items-center justify-center px-4 py-12 text-muted-foreground"
      >
        <Loader2 class="size-5 animate-spin" />
      </div>
      <div
        v-else
        class="flex min-h-0 flex-1 flex-col overflow-hidden px-4 pb-4"
      >
        <slot />
      </div>
    </SheetContent>
  </Sheet>
</template>
