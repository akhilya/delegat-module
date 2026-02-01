<script>
import { ChevronRight, Home } from "lucide-vue-next";

export default {
  components: {
    ChevronRight,
    Home,
  },
  props: {
    items: {
      type: Array,
      required: true,
      // Each item: { label: string, to?: string }
    },
  },
  methods: {
    navigate(to) {
      if (to) {
        this.$router.push(to);
      }
    },
  },
};
</script>

<template>
  <nav class="flex items-center gap-1 text-sm text-muted-foreground mb-6 border p-2 px-4 rounded-lg">
    <button
      @click="navigate('/')"
      class="flex items-center hover:text-foreground transition-colors"
    >
      <Home class="w-4 h-4" />
    </button>
    <template v-for="(item, index) in items" :key="index">
      <ChevronRight class="w-4 h-4" />
      <button
        v-if="item.to && index < items.length - 1"
        @click="navigate(item.to)"
        class="hover:text-foreground transition-colors"
      >
        {{ item.label }}
      </button>
      <span v-else class="text-foreground font-medium">
        {{ item.label }}
      </span>
    </template>
  </nav>
</template>
