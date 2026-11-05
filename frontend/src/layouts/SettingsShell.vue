<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { RouterView, useRoute, useRouter } from "vue-router";
import AppPageChrome from "@/components/AppPageChrome.vue";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { loadMe } from "@/main";
import { getCachedMe, ME_UPDATED_EVENT } from "@/lib/meCache";
import {
  PAGE_ADMIN_API,
  PAGE_ADMIN_ERROR_LOGS,
  PAGE_ADAPTER_LOGS,
  PAGE_SETTINGS_USER,
} from "@/router";
import { t } from "@/i18n";
import { canAccessPath, fallbackPathForUser } from "@/lib/routeAccess";
import { cn } from "@/lib/utils";

const route = useRoute();
const router = useRouter();
const me = ref(getCachedMe() ?? null);

const fillHeight = computed(() => route.meta.fillHeight === true);
/** Skip the first child paint — the shell already fades in from the app RouterView. */
const nestedEnter = ref(false);
const isApiPage = computed(() => route.path === PAGE_ADMIN_API);

const apiTab = computed(() => (route.query.tab === "logs" ? "logs" : "keys"));
const hasListTools = computed(() =>
  [PAGE_ADMIN_ERROR_LOGS, PAGE_ADAPTER_LOGS].includes(route.path),
);

function onApiTabChange(value) {
  const query = { ...route.query };
  if (value === "logs") query.tab = "logs";
  else delete query.tab;
  router.replace({ path: route.path, query });
}

watch(
  () => route.path,
  (path) => {
    if (path !== PAGE_ADMIN_API && route.query.tab) {
      const query = { ...route.query };
      delete query.tab;
      router.replace({ path, query });
    }
  },
);

watch(
  [() => route.path, () => me.value],
  () => {
    if (me.value === null) return;
    if (!canAccessPath(me.value, route.path)) {
      router.replace(fallbackPathForUser(me.value) || PAGE_SETTINGS_USER);
    }
  },
  { immediate: true },
);

function onMeUpdated(event) {
  if (event?.detail && typeof event.detail === "object") {
    me.value = event.detail;
  }
}

onMounted(async () => {
  window.addEventListener(ME_UPDATED_EVENT, onMeUpdated);
  requestAnimationFrame(() => {
    nestedEnter.value = true;
  });
  try {
    me.value = await loadMe();
  } catch {
    // The cached identity remains available if this refresh fails.
  }
});

onUnmounted(() => {
  window.removeEventListener(ME_UPDATED_EVENT, onMeUpdated);
});
</script>

<template>
  <!-- Fill-height routes use meta.fillHeight; chrome owns Page + Nav + Breadcrumbs. -->
  <AppPageChrome
    :fill-viewport="fillHeight"
    :has-nav-center="isApiPage || hasListTools"
    :content-class="cn(fillHeight && 'min-h-0 overflow-hidden')"
  >
    <template #nav-center>
      <Tabs
        v-if="isApiPage"
        :model-value="apiTab"
        @update:model-value="onApiTabChange"
      >
        <TabsList>
          <TabsTrigger value="keys">{{
            t("pages.apiPage.tabKeys")
          }}</TabsTrigger>
          <TabsTrigger value="logs">{{
            t("pages.apiPage.tabLogs")
          }}</TabsTrigger>
        </TabsList>
      </Tabs>
      <div v-else id="settings-navbar-tools" class="w-full min-w-0" />
    </template>
    <RouterView v-slot="{ Component, route: childRoute }">
      <Transition
        :enter-active-class="nestedEnter ? 'page-region-in' : undefined"
      >
        <component :is="Component" :key="childRoute.path" />
      </Transition>
    </RouterView>
  </AppPageChrome>
</template>
