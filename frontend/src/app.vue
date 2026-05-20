<script>
import { RouterView, useRoute } from "vue-router";
import { Toaster } from "vue-sonner";
import "vue-sonner/style.css";
import OperationsPanel from "@/components/operations/OperationsPanel.vue";
import OperationsTriggerButton from "@/components/operations/OperationsTriggerButton.vue";
import LoadingBar from "@/components/LoadingBar.vue";
import { Loader2 } from "lucide-vue-next";
import { computed, ref } from "vue";
import Button from "@/components/ui/button/Button.vue";
import { Monitor } from "lucide-vue-next";

export default {
  components: {
    RouterView,
    Toaster,
    OperationsPanel,
    OperationsTriggerButton,
    LoadingBar,
    Loader2,
    Button,
    Monitor,
  },
  setup() {
    const route = useRoute();
    const isPublicPage = computed(() => !!route.meta?.public);

    // Mobile warning: show once, remember dismissal in localStorage
    const mobileWarningDismissed = ref(
      localStorage.getItem("mobile-warning-dismissed") === "1"
    );
    const isMobileScreen = ref(window.innerWidth < 640);

    function dismissMobileWarning() {
      mobileWarningDismissed.value = true;
      localStorage.setItem("mobile-warning-dismissed", "1");
    }

    return { isPublicPage, mobileWarningDismissed, isMobileScreen, dismissMobileWarning };
  },
  computed: {
    appKey() {
      return this.$store.state.appKey;
    },
    isSwitchingStore() {
      return this.$store.state.isSwitchingStore;
    },
    hasActiveUploads() {
      return this.$store.getters["operations/hasLocalActiveUploads"];
    },
    showMobileWarning() {
      return this.isMobileScreen && !this.mobileWarningDismissed && !this.isPublicPage;
    },
  },
  mounted() {
    this.$store.dispatch("operations/initSignalR");
    window.addEventListener("beforeunload", this._onBeforeUnload);
  },
  beforeUnmount() {
    this.$store.dispatch("operations/destroySignalR");
    window.removeEventListener("beforeunload", this._onBeforeUnload);
  },
  methods: {
    _onBeforeUnload(e) {
      if (this.hasActiveUploads) {
        e.preventDefault();
        e.returnValue = "";
      }
    },
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

  <!-- Mobile device warning -->
  <Teleport to="body">
    <div
      v-if="showMobileWarning"
      class="fixed inset-0 z-[99999] flex items-center justify-center bg-background/95 backdrop-blur-sm p-6"
    >
      <div class="max-w-sm w-full flex flex-col items-center text-center gap-5">
        <Monitor class="size-12 text-muted-foreground" />
        <div>
          <h2 class="text-lg font-semibold mb-2">Лучше с компьютера</h2>
          <p class="text-sm text-muted-foreground leading-relaxed">
            PrintO — панель управления товарами, оптимизированная для работы на компьютере.
            На мобильных устройствах часть функций может работать некорректно.
          </p>
        </div>
        <Button class="w-full" @click="dismissMobileWarning">
          Продолжить
        </Button>
      </div>
    </div>
  </Teleport>

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
