<script setup>
import { computed } from "vue";
import { useStore } from "vuex";
import { Activity } from "lucide-vue-next";

const store = useStore();

const activeCount = computed(() => store.getters["operations/activeCount"]);
const hasOperations = computed(() => store.getters["operations/hasOperations"]);

function toggle() {
  store.dispatch("operations/togglePanel");
}
</script>

<template>
  <Teleport to="body">
    <button
      type="button"
      class="fixed bottom-6 right-6 z-[9996] size-12 rounded-full bg-primary text-primary-foreground shadow-lg flex items-center justify-center cursor-pointer hover:bg-primary/90 transition-colors"
      @click="toggle"
    >
      <Activity class="size-5" />

      <!-- Badge -->
      <span
        v-if="activeCount > 0"
        class="absolute -top-1 -right-1 size-5 rounded-full bg-error text-error-foreground text-[10px] font-bold flex items-center justify-center"
      >
        {{ activeCount > 9 ? "9+" : activeCount }}
      </span>

      <!-- Pulse ring -->
      <span
        v-if="activeCount > 0"
        class="absolute inset-0 rounded-full bg-primary/30 animate-ping"
      />
    </button>
  </Teleport>
</template>
