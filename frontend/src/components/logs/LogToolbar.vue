<script setup>
import { cn } from "@/lib/utils";
import Badge from "@/components/ui/badge/Badge.vue";
import { Activity, Check } from "lucide-vue-next";

defineProps({
  /** Short health line shown as a badge on the right of the toolbar */
  summary: { type: String, default: "" },
  /** When true, summary uses destructive styling */
  summaryAlert: { type: Boolean, default: false },
  class: { type: null, required: false },
});
</script>

<template>
  <div
    :class="
      cn(
        'mb-0 flex min-w-0 w-full flex-col gap-3 rounded-lg border bg-card p-3 sm:flex-row sm:flex-wrap sm:items-center shrink-0',
        $props.class,
      )
    "
  >
    <div class="flex min-w-0 flex-1 flex-col gap-3 sm:flex-row sm:flex-wrap sm:items-center">
      <slot />
    </div>
    <Badge
      v-if="summary"
      :variant="summaryAlert ? 'destructive' : 'secondary'"
      class="h-8 w-fit shrink-0 gap-1.5 px-3"
    >
      <Activity v-if="summaryAlert" class="size-3.5" />
      <Check v-else class="size-3.5" />
      {{ summary }}
    </Badge>
  </div>
</template>
