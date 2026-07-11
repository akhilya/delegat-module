<script setup>
defineProps({
  title: { type: String, default: "" },
  description: { type: String, default: "" },
});
</script>

<template>
  <section class="grid gap-6 border-b border-border/40 py-8 last:border-b-0 lg:grid-cols-[minmax(0,220px)_1fr]">
    <div v-if="title || description || $slots.aside" class="lg:pt-0.5">
      <slot name="aside">
        <h2 v-if="title" class="text-sm font-semibold">{{ title }}</h2>
        <p v-if="description" class="mt-1.5 text-sm text-muted-foreground leading-relaxed">
          {{ description }}
        </p>
      </slot>
    </div>
    <div class="min-w-0 space-y-4">
      <slot />
      <div v-if="$slots.footer" class="flex flex-wrap items-center justify-end gap-3 pt-2">
        <slot name="footer" />
      </div>
    </div>
  </section>
</template>