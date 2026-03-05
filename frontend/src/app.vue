<script>
import { RouterView } from "vue-router";
import { Toaster } from "vue-sonner";
import "vue-sonner/style.css";
import ImportProgressPanel from "@/components/ImportProgressPanel.vue";
import ExportProgressPanel from "@/components/ExportProgressPanel.vue";
import LoadingBar from "@/components/LoadingBar.vue";
import { Loader2 } from "lucide-vue-next";

export default {
  components: {
    RouterView,
    Toaster,
    ImportProgressPanel,
    ExportProgressPanel,
    LoadingBar,
    Loader2,
  },
  computed: {
    appKey() {
      return this.$store.state.appKey;
    },
    isSwitchingStore() {
      return this.$store.state.isSwitchingStore;
    },
  },
};
</script>

<template>
  <LoadingBar ref="loadingBar" />
  <RouterView v-slot="{ Component, route }">
    <component :is="Component" :key="route.path + '-' + appKey" />
  </RouterView>
  <Toaster />
  <ImportProgressPanel />
  <ExportProgressPanel />

  <!-- Fullscreen overlay during store switch -->
  <Transition name="store-switch">
    <div
      v-if="isSwitchingStore"
      class="fixed inset-0 z-50 flex items-center justify-center bg-background"
    >
      <div class="flex flex-col items-center gap-3">
        <Loader2 class="size-6 animate-spin text-muted-foreground" />
        <span class="text-sm text-muted-foreground">Переключение магазина...</span>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.store-switch-enter-active {
  transition: opacity 0.2s ease;
}
.store-switch-enter-from {
  opacity: 0;
}
.store-switch-enter-to {
  opacity: 1;
}
</style>
