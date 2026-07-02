<script setup>
import { cn } from "@/lib/utils";
import { EmptyState } from "@/components/ui/empty-state";

const props = defineProps({
  loading: { type: Boolean, default: false },
  empty: { type: Boolean, default: false },
  emptyIcon: { type: null, default: null },
  emptyTitle: { type: String, default: "" },
  emptyDescription: { type: String, default: "" },
  emptyIconClass: { type: String, default: "" },
  class: { type: null, required: false },
});
</script>

<template>
  <section :class="cn('w-full grow min-h-[calc(100vh-8.5rem)] mb-4', props.class)">
    <div v-if="loading" class="flex min-h-[inherit] grow items-center justify-center">
      <EmptyState loading />
    </div>
    <div v-else-if="empty" class="flex min-h-[inherit] grow items-center justify-center">
      <EmptyState :title="emptyTitle" :description="emptyDescription">
        <template v-if="emptyIcon" #icon>
          <component
            :is="emptyIcon"
            :class="cn('mb-4 size-12 text-muted-foreground', emptyIconClass)"
          />
        </template>
        <slot name="empty-action" />
      </EmptyState>
    </div>
    <slot v-else />
  </section>
</template>
