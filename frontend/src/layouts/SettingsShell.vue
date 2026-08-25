<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import AppPageChrome from "@/components/AppPageChrome.vue";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { loadMe } from "@/main";
import { getCachedMe, ME_UPDATED_EVENT } from "@/lib/meCache";
import { resolveSettingsBreadcrumb } from "@/config/settingsNav";
import { PAGE_ADMIN_API, PAGE_SETTINGS, PAGE_SETTINGS_USER } from "@/router";
import { t } from "@/i18n";
import { canAccessPath, fallbackPathForUser } from "@/lib/routeAccess";
import { cn } from "@/lib/utils";

const route = useRoute();
const router = useRouter();
const me = ref(getCachedMe() ?? null);

const canPlatformSettings = computed(() =>
  canAccessPath(me.value, PAGE_SETTINGS),
);
const fillHeight = computed(() => route.meta.fillHeight === true);
const isApiPage = computed(() => route.path === PAGE_ADMIN_API);

const apiTab = computed(() => (route.query.tab === "logs" ? "logs" : "keys"));

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

const breadcrumbs = computed(() => {
  const path = route.path;
  const label = resolveSettingsBreadcrumb(path);
  if (path === PAGE_SETTINGS) {
    return [{ label: t("nav.overview") }];
  }
  if (!label) return [{ label: t("nav.overview") }];
  const settingsRoot = canPlatformSettings.value ? PAGE_SETTINGS : PAGE_SETTINGS_USER;
  return [{ label: t("nav.overview"), to: settingsRoot }, { label }];
});

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
  try {
    me.value = await loadMe();
  } catch {}
});

onUnmounted(() => {
  window.removeEventListener(ME_UPDATED_EVENT, onMeUpdated);
});
</script>

<template>
  <!-- Fill-height routes use meta.fillHeight; chrome owns Page + Nav + Breadcrumbs. -->
  <AppPageChrome
    :breadcrumb-items="breadcrumbs"
    :fill-viewport="fillHeight"
    :has-nav-center="isApiPage"
    :content-class="
      cn(fillHeight && 'min-h-0 overflow-hidden')
    "
  >
    <template v-if="isApiPage" #nav-center>
      <Tabs :model-value="apiTab" @update:model-value="onApiTabChange">
        <TabsList>
          <TabsTrigger value="keys">{{ t("pages.apiPage.tabKeys") }}</TabsTrigger>
          <TabsTrigger value="logs">{{ t("pages.apiPage.tabLogs") }}</TabsTrigger>
        </TabsList>
      </Tabs>
    </template>
    <RouterView />
  </AppPageChrome>
</template>
