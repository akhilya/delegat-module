<script setup>
import { computed, onMounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { http } from "@/main";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { resolveSettingsBreadcrumb } from "@/config/settingsNav";
import { PAGE_SETTINGS, PAGE_SETTINGS_USER } from "@/router";
import { t } from "@/i18n";

const route = useRoute();
const router = useRouter();
const me = ref(null);

const isAdmin = computed(() => me.value?.isAdmin ?? false);

const breadcrumbs = computed(() => {
  const path = route.path;
  const label = resolveSettingsBreadcrumb(path);
  if (!label || path === PAGE_SETTINGS) return [{ label: t("nav.settings") }];
  const settingsRoot = isAdmin.value ? PAGE_SETTINGS : PAGE_SETTINGS_USER;
  return [{ label: t("nav.settings"), to: settingsRoot }, { label }];
});

watch(
  [() => route.path, isAdmin, () => me.value],
  () => {
    if (me.value === null) return;
    if (route.path === PAGE_SETTINGS && !isAdmin.value) {
      router.replace(PAGE_SETTINGS_USER);
    }
  },
  { immediate: true },
);

onMounted(async () => {
  try {
    const meResponse = await http.get("/auth/me", { cache: false });
    me.value = meResponse.data?.value ?? meResponse.data;
  } catch {}
});
</script>

<template>
  <div class="flex flex-col">
    <Breadcrumbs :items="breadcrumbs" />
    <slot />
  </div>
</template>