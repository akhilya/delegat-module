<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { loadMe } from "@/main";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { resolveSettingsBreadcrumb } from "@/config/settingsNav";
import { PAGE_SETTINGS, PAGE_SETTINGS_USER } from "@/router";
import { t } from "@/i18n";
import { canAccessPath, fallbackPathForUser } from "@/lib/routeAccess";
import { hasPermission, PERMS } from "@/lib/permissions";

const route = useRoute();
const router = useRouter();
const me = ref(null);

const canPlatformSettings = computed(() => hasPermission(me.value, PERMS.SettingsPlatform));

const breadcrumbs = computed(() => {
  const path = route.path;
  const label = resolveSettingsBreadcrumb(path);
  if (!label || path === PAGE_SETTINGS) return [{ label: t("nav.settings") }];
  const settingsRoot = canPlatformSettings.value ? PAGE_SETTINGS : PAGE_SETTINGS_USER;
  return [{ label: t("nav.settings"), to: settingsRoot }, { label }];
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

onMounted(async () => {
  try {
    me.value = await loadMe();
  } catch {}
});
</script>

<template>
  <div class="flex flex-col">
    <Breadcrumbs :items="breadcrumbs" />
    <slot />
  </div>
</template>
