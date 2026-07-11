<script setup>
import { ref, computed, onMounted, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ShieldCheck, ChevronRight } from "lucide-vue-next";
import { http } from "@/main";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuItem,
  SidebarMenuButton,
  SidebarMenuSub,
  SidebarMenuSubItem,
  SidebarMenuSubButton,
} from "@/components/ui/sidebar";
import {
  getPersonalSettingsItems,
  getAdminSettingsItems,
  resolveSettingsBreadcrumb,
} from "@/config/settingsNav";
import { PAGE_SETTINGS, PAGE_SETTINGS_USER, PAGE_SETTINGS_STORE } from "@/router";
import { isActiveRoute } from "@/lib/isActiveRoute";

const route = useRoute();
const router = useRouter();
const me = ref(null);
const memberships = ref([]);
const adminOpen = ref(true);

const selectedStore = computed(() =>
  memberships.value.find((store) => store.id === me.value?.selectedStoreId),
);
const canManageStore = computed(() => me.value?.isAdmin || selectedStore.value?.isStoreAdmin);
const isAdmin = computed(() => me.value?.isAdmin ?? false);

const navContext = computed(() => ({
  me: me.value,
  canManageStore: canManageStore.value,
  isAdmin: isAdmin.value,
}));

const personalItems = computed(() => getPersonalSettingsItems(navContext.value));
const adminItems = computed(() => getAdminSettingsItems(navContext.value));

const breadcrumbs = computed(() => {
  const path = route.path;
  const label = resolveSettingsBreadcrumb(path);
  if (!label || path === PAGE_SETTINGS) return [{ label: "Настройки" }];
  const settingsRoot = isAdmin.value ? PAGE_SETTINGS : PAGE_SETTINGS_USER;
  return [{ label: "Настройки", to: settingsRoot }, { label }];
});

watch(
  () => route.path,
  (path) => {
    if (adminItems.value.some((item) => isActiveRoute(path, item.to))) {
      adminOpen.value = true;
    }
  },
  { immediate: true },
);

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
    <div class="flex min-h-0 flex-1 gap-8 pb-6">
      <div class="w-52 shrink-0">
        <div class="sticky top-4 space-y-1">
          <SidebarGroup class="p-0">
            <SidebarGroupLabel class="px-2">Настройки</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                <SidebarMenuItem v-for="item in personalItems" :key="item.id">
                  <SidebarMenuButton as-child :is-active="isActiveRoute(route, item.to)">
                    <RouterLink :to="item.to">
                      <component :is="item.icon" />
                      <span>{{ item.label }}</span>
                    </RouterLink>
                  </SidebarMenuButton>
                </SidebarMenuItem>
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>

          <SidebarGroup v-if="adminItems.length" class="p-0">
            <Collapsible v-model:open="adminOpen" class="group/collapsible">
              <CollapsibleTrigger as-child>
                <SidebarGroupLabel class="cursor-pointer select-none px-2 hover:text-sidebar-foreground">
                  <ShieldCheck class="mr-1.5 size-3 opacity-60" />
                  Администрирование
                  <ChevronRight
                    class="ml-auto size-3 opacity-60 transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90"
                  />
                </SidebarGroupLabel>
              </CollapsibleTrigger>
              <CollapsibleContent>
                <SidebarGroupContent>
                  <SidebarMenu>
                    <SidebarMenuItem>
                      <SidebarMenuSub>
                        <SidebarMenuSubItem v-for="item in adminItems" :key="item.id">
                          <SidebarMenuSubButton as-child :is-active="isActiveRoute(route, item.to)">
                            <RouterLink :to="item.to">
                              <component :is="item.icon" />
                              <span>{{ item.label }}</span>
                            </RouterLink>
                          </SidebarMenuSubButton>
                        </SidebarMenuSubItem>
                      </SidebarMenuSub>
                    </SidebarMenuItem>
                  </SidebarMenu>
                </SidebarGroupContent>
              </CollapsibleContent>
            </Collapsible>
          </SidebarGroup>
        </div>
      </div>

      <div class="min-w-0 flex-1">
        <slot />
      </div>
    </div>
  </div>
</template>