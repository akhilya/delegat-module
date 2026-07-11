<script setup>
import { ArrowRight } from "lucide-vue-next";

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: "" },
  to: { type: String, required: true },
  icon: { type: [Object, Function], default: null },
});
</script>

<template>
  <RouterLink
    :to="to"
    class="group flex items-start gap-3 rounded-lg border border-border/60 bg-card px-4 py-3 transition-colors hover:border-border hover:bg-muted/30"
  >
    <component
      :is="icon"
      v-if="icon"
      class="mt-0.5 size-4 shrink-0 text-muted-foreground group-hover:text-foreground"
    />
    <div class="min-w-0 flex-1">
      <div class="text-sm font-medium">{{ title }}</div>
      <p v-if="description" class="mt-0.5 text-sm text-muted-foreground">{{ description }}</p>
    </div>
    <ArrowRight class="mt-0.5 size-4 shrink-0 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100" />
  </RouterLink>
</template>