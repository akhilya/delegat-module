<script setup>
defineProps({
  title: { type: String, required: true },
  description: { type: String, default: "" },
});
</script>

<template>
  <header class="mb-8 border-b border-border/60 pb-6">
    <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
    <p v-if="description" class="mt-1.5 max-w-2xl text-sm text-muted-foreground">
      {{ description }}
    </p>
    <div v-if="$slots.actions" class="mt-4 flex flex-wrap items-center gap-2">
      <slot name="actions" />
    </div>
  </header>
</template>