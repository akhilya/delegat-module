<script setup>
import { Loader2 } from "lucide-vue-next";

defineProps({
  loading: { type: Boolean, default: false },
  title: { type: String, default: '' },
  description: { type: String, default: '' },
})
</script>

<template>
  <div v-if="loading" class="flex justify-center py-12">
    <Loader2 class="size-8 animate-spin text-muted-foreground" />
  </div>
  <div v-else class="flex flex-col items-center justify-center py-12 text-center">
    <slot name="icon" />
    <h3 v-if="title" class="font-medium mb-1">{{ title }}</h3>
    <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
    <slot />
  </div>
</template>
