<script setup>
import { Spinner } from "@/components/ui/spinner";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";

defineProps({
  open: { type: Boolean, default: false },
  loading: { type: Boolean, default: false },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  ariaLabel: { type: String, default: "" },
});

defineEmits(["update:open"]);
</script>

<template>
  <Sheet :open="open" @update:open="$emit('update:open', $event)">
    <SheetContent
      side="right"
      :show-close="false"
      class="flex w-full flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl"
      :aria-label="ariaLabel || title || undefined"
    >
      <SheetHeader variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <SheetTitle class="min-w-0 flex-1 truncate">
            <slot name="title">{{ title }}</slot>
          </SheetTitle>
          <div
            v-if="$slots.actions"
            class="flex shrink-0 items-center gap-1"
          >
            <slot name="actions" />
          </div>
          <SheetHeaderClose />
        </div>
        <SheetDescription
          v-if="description || $slots.description"
          class="break-all"
        >
          <slot name="description">{{ description }}</slot>
        </SheetDescription>
      </SheetHeader>

      <div
        v-if="loading"
        class="flex flex-1 items-center justify-center px-6 py-12 text-muted-foreground"
      >
        <Spinner size="lg" />
      </div>
      <div
        v-else
        class="flex min-h-0 flex-1 flex-col overflow-hidden"
      >
        <slot />
      </div>
    </SheetContent>
  </Sheet>
</template>
