<script setup>
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils";

defineProps({
  title: { type: String, required: true },
  description: { type: String, default: "" },
  class: { type: null, default: undefined },
});
</script>

<template>
  <Card :class="cn('gap-0 py-0 shadow-none', $props.class)">
    <CardContent class="flex items-center justify-between gap-4 px-4 py-3">
      <div class="min-w-0">
        <div class="text-sm font-medium">{{ title }}</div>
        <div v-if="description" class="text-xs text-muted-foreground">
          {{ description }}
        </div>
      </div>
      <div class="shrink-0">
        <slot />
      </div>
    </CardContent>
  </Card>
</template>
