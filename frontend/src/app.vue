<script>
import { Spinner } from "@/components/ui/spinner";
import { RouterView, useRoute } from "vue-router";
import { Toaster } from "@/components/ui/sonner";
import "vue-sonner/style.css";
import OperationsPanel from "@/components/operations/OperationsPanel.vue";
import DocsSheet from "@/components/docs/DocsSheet.vue";
import LoadingBar from "@/components/LoadingBar.vue";
import AppSidebar from "@/components/appSidebar.vue";
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";

import { computed, defineAsyncComponent, ref, Transition } from "vue";
import { Button } from "@/components/ui/button";
import { Monitor } from "lucide-vue-next";
import { ConfirmDialogHost } from "@/components/ui/confirm-dialog";
import { useI18n } from "vue-i18n";
import { brandName } from "@/brand";
import { loadMe } from "@/main";
import { ME_UPDATED_EVENT } from "@/lib/meCache";
import { resolveAiChromeEnabled } from "@/lib/ai/chrome";

const AiChatPanel = defineAsyncComponent(
  () => import("@/components/ai/AiChatPanel.vue"),
);

export default {
  components: {
    Spinner,
    RouterView,
    Transition,
    Toaster,
    OperationsPanel,
    DocsSheet,
    LoadingBar,
    AppSidebar,
    SidebarInset,
    SidebarProvider,
    Button,
    Monitor,
    AiChatPanel,
    ConfirmDialogHost,
  },
  setup() {
    const { t } = useI18n();
    const route = useRoute();
    const isPublicPage = computed(() => !!route.meta?.public);
    const isBareLayout = computed(
      () => !!route.meta?.public || !!route.meta?.bareLayout,
    );
    // Default false until /auth/me confirms AI_ENABLED — avoids loading AI chunk on free tier.
    const aiEnabled = ref(false);

    // Mobile warning: show once, remember dismissal in localStorage
    const mobileWarningDismissed = ref(
      localStorage.getItem("mobile-warning-dismissed") === "1",
    );
    const isMobileScreen = ref(window.innerWidth < 640);

    function dismissMobileWarning() {
      mobileWarningDismissed.value = true;
      localStorage.setItem("mobile-warning-dismissed", "1");
    }

    return {
      t,
      brandName,
      isPublicPage,
      isBareLayout,
      mobileWarningDismissed,
      isMobileScreen,
      dismissMobileWarning,
      aiEnabled,
    };
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
      return (
        this.isMobileScreen &&
        !this.mobileWarningDismissed &&
        !this.isBareLayout
      );
    },
  },
  watch: {
    isBareLayout() {
      this.syncAiChrome();
    },
  },
  mounted() {
    this.$store.dispatch("operations/initSignalR");
    window.addEventListener("beforeunload", this._onBeforeUnload);
    this._onMeUpdated = (event) => {
      this.applyAiChrome(event.detail);
    };
    window.addEventListener(ME_UPDATED_EVENT, this._onMeUpdated);
    this.syncAiChrome();
  },
  beforeUnmount() {
    this.$store.dispatch("operations/destroySignalR");
    window.removeEventListener("beforeunload", this._onBeforeUnload);
    window.removeEventListener(ME_UPDATED_EVENT, this._onMeUpdated);
  },
  methods: {
    applyAiChrome(me) {
      this.aiEnabled = resolveAiChromeEnabled({
        isBareLayout: this.isBareLayout,
        me,
      });
    },
    async syncAiChrome() {
      if (this.isBareLayout) {
        this.aiEnabled = false;
        return;
      }
      try {
        const me = await loadMe();
        this.applyAiChrome(me);
      } catch {
        this.aiEnabled = false;
      }
    },
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
  <RouterView v-if="isBareLayout" v-slot="{ Component, route }">
    <div class="h-svh overflow-y-auto">
      <Transition enter-active-class="page-region-in">
        <component
          :is="Component"
          :key="(route.meta.stableKey || route.path) + '-' + appKey"
        />
      </Transition>
    </div>
  </RouterView>
  <SidebarProvider v-else :open="true" class="h-svh overflow-hidden">
    <AppSidebar />
    <SidebarInset class="flex min-h-0 min-w-0 flex-1 flex-col overflow-hidden">
      <div
        id="app-navbar-host"
        data-slot="app-navbar-host"
        class="z-30 w-full min-w-0 shrink-0 bg-background"
      />
      <div
        data-slot="page-scroll"
        class="min-h-0 min-w-0 flex-1 overflow-y-auto scroll-pb-[var(--page-scroll-end)]"
      >
        <RouterView v-slot="{ Component, route }">
          <Transition enter-active-class="page-region-in">
            <component
              :is="Component"
              :key="(route.meta.stableKey || route.path) + '-' + appKey"
            />
          </Transition>
        </RouterView>
      </div>
      <div
        id="page-footer-dock"
        data-slot="page-footer-dock"
        class="shrink-0 empty:hidden"
      />
    </SidebarInset>
  </SidebarProvider>
  <Toaster />
  <ConfirmDialogHost />
  <DocsSheet v-if="!isPublicPage" />
  <template v-if="!isBareLayout">
    <OperationsPanel />
    <AiChatPanel v-if="aiEnabled" :key="appKey" />
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
          <h2 class="text-lg font-semibold mb-2">
            {{ t("app.mobileWarningTitle") }}
          </h2>
          <p class="text-sm text-muted-foreground leading-relaxed">
            {{ t("app.mobileWarningBody", { brand: brandName }) }}
          </p>
        </div>
        <Button class="w-full" @click="dismissMobileWarning">
          {{ t("app.continue") }}
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
        <Spinner size="xl" class="text-muted-foreground" />
        <span class="text-sm text-muted-foreground">{{
          t("app.switchingStore")
        }}</span>
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
