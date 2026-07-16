<script setup>
defineProps({
  items: {
    type: Array,
    default: () => [],
  },
});
</script>

<template>
  <div class="space-y-2 text-sm">
    <div
      v-for="(item, i) in items"
      :key="i"
      class="flex items-start justify-between gap-3"
    >
      <span class="shrink-0 text-muted-foreground">{{ item.label }}</span>
      <div class="min-w-0 text-right">
        <slot :name="`item-${i}`" :item="item">
          <span class="break-all">{{ item.value ?? "—" }}</span>
        </slot>
      </div>
    </div>
  </div>
</template>
