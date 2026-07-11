<script setup>
import { ref, computed, onMounted, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { http } from "@/main";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import {
  resolveSettingsBreadcrumb,
} from "@/config/settingsNav";
import { PAGE_SETTINGS, PAGE_SETTINGS_USER, PAGE_SETTINGS_STORE } from "@/router";

const route = useRoute();
const router = useRouter();
const me = ref(null);
const memberships = ref([]);

const selectedStore = computed(() =>
  memberships.value.find((store) => store.id === me.value?.selectedStoreId),
);
const canManageStore = computed(() => me.value?.isAdmin || selectedStore.value?.isStoreAdmin);
const isAdmin = computed(() => me.value?.isAdmin ?? false);

const breadcrumbs = computed(() => {
  const path = route.path;
  const label = resolveSettingsBreadcrumb(path);
  if (!label || path === PAGE_SETTINGS) return [{ label: "Настройки" }];
  const settingsRoot = isAdmin.value ? PAGE_SETTINGS : PAGE_SETTINGS_USER;
  return [{ label: "Настройки", to: settingsRoot }, { label }];
});

watch(
  [() => route.path, isAdmin, canManageStore, () => me.value],
  () => {
    if (me.value === null) return;
    if (route.path === PAGE_SETTINGS && !isAdmin.value) {
      router.replace(PAGE_SETTINGS_USER);
      return;
    }
    if (route.path === PAGE_SETTINGS_STORE && !canManageStore.value) {
      router.replace(PAGE_SETTINGS_USER);
    }
  },
  { immediate: true },
);

onMounted(async () => {
  try {
    const [meResponse, membershipsResponse] = await Promise.all([
      http.get("/auth/me", { cache: false }),
      http.get("/users/me/memberships", { cache: false }),
    ]);
    me.value = meResponse.data?.value ?? meResponse.data;
    memberships.value = membershipsResponse.data?.value ?? membershipsResponse.data ?? [];
  } catch {}
});
</script>

<template>
  <div class="adaptive-padding flex min-h-0 flex-1 flex-col">
    <Breadcrumbs :items="breadcrumbs" />
    <div class="min-w-0 flex-1 pb-6">
      <slot />
    </div>
  </div>
</template>