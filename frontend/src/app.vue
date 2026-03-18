<script>
import { RouterView, useRoute } from "vue-router";
import { Toaster } from "vue-sonner";
import "vue-sonner/style.css";
import OperationsPanel from "@/components/operations/OperationsPanel.vue";
import OperationsTriggerButton from "@/components/operations/OperationsTriggerButton.vue";
import LoadingBar from "@/components/LoadingBar.vue";
import { Loader2 } from "lucide-vue-next";
import { computed } from "vue";

export default {
  components: {
    RouterView,
    Toaster,
    OperationsPanel,
    OperationsTriggerButton,
    LoadingBar,
    Loader2,
  },
  setup() {
    const route = useRoute();
    const isPublicPage = computed(() => !!route.meta?.public);
    return { isPublicPage };
  },
  computed: {
    appKey() {
      return this.$store.state.appKey;
    },
    isSwitchingStore() {
      return this.$store.state.isSwitchingStore;
    },
  },
  mounted() {
    this.$store.dispatch("operations/initSignalR");
  },
  beforeUnmount() {
    this.$store.dispatch("operations/destroySignalR");
  },
};
</script>

<template>
  <LoadingBar ref="loadingBar" />
  <RouterView v-slot="{ Component, route }">
    <component :is="Component" :key="(route.meta.stableKey || route.path) + '-' + appKey" />
  </RouterView>
  <Toaster />
  <template v-if="!isPublicPage">
    <OperationsPanel />
    <OperationsTriggerButton />
  </template>

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
