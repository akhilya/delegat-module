<script setup>
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { loadMe } from "@/main";
import { ME_UPDATED_EVENT } from "@/lib/meCache";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { resolveSettingsBreadcrumb } from "@/config/settingsNav";
import { PAGE_SETTINGS, PAGE_SETTINGS_USER } from "@/router";
import { t } from "@/i18n";
import { canAccessPath, fallbackPathForUser } from "@/lib/routeAccess";
import { hasPermission, PERMS } from "@/lib/permissions";
import { cn } from "@/lib/utils";

defineProps({
  class: { type: null, required: false },
});

const route = useRoute();
const router = useRouter();
const me = ref(null);

const canPlatformSettings = computed(() => hasPermission(me.value, PERMS.SettingsPlatform));

const fillHeight = computed(() => route.meta.fillHeight === true);

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
  <div :class="cn('flex flex-col', fillHeight && 'min-h-0', $props.class)">
    <Breadcrumbs class="shrink-0" :items="breadcrumbs" />
    <div
      :class="
        cn(
          'flex flex-col',
          fillHeight
            ? 'h-[calc(100svh-var(--app-navbar-height)-var(--app-breadcrumbs-height))] min-h-0 overflow-hidden'
            : 'pb-4',
        )
      "
    >
      <slot />
    </div>
  </div>
</template>
