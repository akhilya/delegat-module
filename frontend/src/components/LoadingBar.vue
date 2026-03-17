<script setup>
import { loadingBarState } from "@/composables/useLoadingBar";
</script>

<template>
  <div
    v-if="loadingBarState.isLoading"
    class="loading-bar"
    :style="loadingBarState.style"
  />
</template>

<style scoped>
.loading-bar {
  position: fixed;
  top: 0;
  left: 0;
  height: 3px;
  background: var(--color-primary, var(--primary));
  z-index: var(--z-overlay, 50);
  transition:
    width 2s cubic-bezier(0.1, 0.5, 0.3, 1),
    opacity 0.3s ease;
  pointer-events: none;
}
</style>
