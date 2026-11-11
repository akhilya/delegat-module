<script setup>
import {
  computed,
  defineAsyncComponent,
  onMounted,
  onUnmounted,
  ref,
} from "vue";
import { loadMe } from "@/main";
import { Spinner } from "@/components/ui/spinner";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import QuotaUsagePanel from "@/components/settings/QuotaUsagePanel.vue";
import SettingsOverviewPage from "@/pages/settings/settingsOverviewPage.vue";
import { getCachedMe, ME_UPDATED_EVENT } from "@/lib/meCache";
import { hasPermission, PERMS } from "@/lib/permissions";
import { getAccessibleAdminSections } from "@/lib/adminSections";

const me = ref(getCachedMe() ?? null);

const sectionComponents = {
  stores: defineAsyncComponent(() => import("./storesPage.vue")),
  users: defineAsyncComponent(() => import("./usersPage.vue")),
  ai: defineAsyncComponent(() => import("@/pages/settings/AiModelsPage.vue")),
};

const canManagePlatform = computed(() =>
  hasPermission(me.value, PERMS.SettingsPlatform),
);
const sections = computed(() =>
  getAccessibleAdminSections(me.value).map((section) => ({
    ...section,
    component: sectionComponents[section.id],
  })),
);
const hasSections = computed(
  () => canManagePlatform.value || sections.value.length > 0,
);

function onMeUpdated(event) {
  me.value =
    event?.detail && typeof event.detail === "object"
      ? event.detail
      : (getCachedMe() ?? null);
}

onMounted(async () => {
  window.addEventListener(ME_UPDATED_EVENT, onMeUpdated);
  me.value = (await loadMe()) ?? getCachedMe() ?? null;
});

onUnmounted(() => window.removeEventListener(ME_UPDATED_EVENT, onMeUpdated));
</script>

<template>
  <div
    v-if="me && hasSections"
    class="flex w-full max-w-xl flex-col gap-8 pb-10"
  >
    <QuotaUsagePanel v-if="canManagePlatform" />

    <section v-for="section in sections" :key="section.id" class="space-y-2">
      <InsetGroupLabel>{{ $t(section.labelKey) }}</InsetGroupLabel>
      <component :is="section.component" />
    </section>

    <SettingsOverviewPage v-if="canManagePlatform" :include-quota="false" />
  </div>

  <div
    v-else
    class="flex min-h-32 flex-1 items-center justify-center"
    role="status"
  >
    <Spinner size="lg" class="text-muted-foreground" />
  </div>
</template>
