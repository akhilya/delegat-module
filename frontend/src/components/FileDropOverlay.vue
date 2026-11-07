<script setup>
import { Upload } from "lucide-vue-next";

defineProps({
  active: { type: Boolean, default: false },
  title: { type: String, required: true },
  description: { type: String, default: "" },
});
</script>

<template>
  <Transition
    enter-active-class="transition-opacity duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
    enter-from-class="opacity-0"
    leave-active-class="transition-opacity duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]"
    leave-to-class="opacity-0"
  >
    <div
      v-if="active"
      data-slot="file-drop-overlay"
      class="pointer-events-none absolute inset-3 z-20 flex items-center justify-center rounded-lg border-2 border-dashed border-primary/40 bg-background/90 p-6 text-center"
      role="status"
      aria-live="polite"
      aria-atomic="true"
    >
      <div class="flex max-w-xs flex-col items-center gap-3">
        <Upload class="size-8 text-muted-foreground" aria-hidden="true" />
        <div class="space-y-1">
          <p class="text-sm font-medium">{{ title }}</p>
          <p v-if="description" class="text-xs text-muted-foreground">
            {{ description }}
          </p>
        </div>
      </div>
    </div>
  </Transition>
</template>
