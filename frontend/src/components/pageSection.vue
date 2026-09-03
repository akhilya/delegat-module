<script setup>
import { cn } from "@/lib/utils";
import { EmptyState } from "@/components/ui/empty-state";

const props = defineProps({
  loading: { type: Boolean, default: false },
  loadingMessage: { type: String, default: "" },
  empty: { type: Boolean, default: false },
  emptyIcon: { type: null, default: null },
  emptyTitle: { type: String, default: "" },
  emptyDescription: { type: String, default: "" },
  emptyIconClass: { type: String, default: "" },
  class: { type: null, required: false },
});
</script>

<template>
  <section
    :class="
      cn(
        'flex w-full flex-col',
        (loading || empty) ? 'min-h-0 flex-1' : 'mb-4',
        props.class,
      )
    "
  >
    <EmptyState
      v-if="loading"
      loading
      :title="loadingMessage"
      class="min-h-0 flex-1"
    />
    <EmptyState
      v-else-if="empty"
      class="min-h-0 flex-1 page-region-in"
      :title="emptyTitle"
      :description="emptyDescription"
    >
      <template v-if="emptyIcon" #icon>
        <component :is="emptyIcon" :class="emptyIconClass" />
      </template>
      <slot name="empty-action" />
    </EmptyState>
    <div v-else class="page-region-in flex min-h-0 w-full flex-1 flex-col">
      <slot />
    </div>
  </section>
</template>
