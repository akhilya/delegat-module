<script setup>
import { ref, computed, onMounted } from "vue";
import { useRoute } from "vue-router";
import { http } from "@/main";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
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
  PAGE_SETTINGS,
  PAGE_SETTINGS_USER,
  PAGE_SETTINGS_STORE,
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_USERS,
  PAGE_ADMIN_TOKENS,
  PAGE_ADMIN_PLUGINS,
  PAGE_ADMIN_ERROR_LOGS,
} from "@/router";
import {
  Settings2,
  User,
  Store,
  Users,
  Key,
  Plug,
  AlertTriangle,
  ShieldCheck,
  ChevronRight,
} from "lucide-vue-next";

const route = useRoute();
const me = ref(null);
const isAdmin = computed(() => me.value?.isAdmin ?? false);
const adminOpen = ref(true);

const ROUTE_LABELS = {
  [PAGE_SETTINGS]: "Общие",
  [PAGE_SETTINGS_USER]: "Пользователь",
  [PAGE_SETTINGS_STORE]: "Магазин",
  [PAGE_ADMIN_STORES]: "Магазины",
  [PAGE_ADMIN_USERS]: "Пользователи",
  [PAGE_ADMIN_TOKENS]: "Токены",
  [PAGE_ADMIN_PLUGINS]: "Плагины",
  [PAGE_ADMIN_ERROR_LOGS]: "Логи ошибок",
};

const breadcrumbs = computed(() => {
  const path = route.path;
  const label = ROUTE_LABELS[path];
  if (!label || path === PAGE_SETTINGS) return [{ label: "Настройки" }];
  return [{ label: "Настройки", to: PAGE_SETTINGS }, { label }];
});

const personalItems = [
  { label: "Пользователь", to: PAGE_SETTINGS_USER, icon: User },
  { label: "Магазин", to: PAGE_SETTINGS_STORE, icon: Store },
];

const adminItems = [
  { label: "Общие", to: PAGE_SETTINGS, icon: Settings2 },
  { label: "Магазины", to: PAGE_ADMIN_STORES, icon: Store },
  { label: "Пользователи", to: PAGE_ADMIN_USERS, icon: Users },
  { label: "Токены", to: PAGE_ADMIN_TOKENS, icon: Key },
  { label: "Плагины", to: PAGE_ADMIN_PLUGINS, icon: Plug },
  { label: "Логи ошибок", to: PAGE_ADMIN_ERROR_LOGS, icon: AlertTriangle },
];

function isActive(path) {
  return route.path === path;
}

onMounted(async () => {
  try {
    const res = await http.get("/auth/me", { cache: false });
    me.value = res.data?.value ?? res.data;
  } catch {}
});
</script>

<template>
  <div class="bg-background min-h-screen">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />
    <div class="adaptive-padding">
      <div class="flex gap-8 pb-6 min-h-[calc(100vh-7rem)]">

        <!-- Sidebar -->
        <aside class="w-44 shrink-0">
          <nav class="sticky top-20 space-y-1">

            <!-- Личные настройки -->
            <SidebarGroup class="p-0">
              <SidebarGroupLabel class="px-2">Настройки</SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  <SidebarMenuItem v-for="item in personalItems" :key="item.to">
                    <SidebarMenuButton
                      as="a"
                      :href="item.to"
                      :is-active="isActive(item.to)"
                      @click.prevent="$router.push(item.to)"
                    >
                      <component :is="item.icon" />
                      {{ item.label }}
                    </SidebarMenuButton>
                  </SidebarMenuItem>
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>

            <!-- Администрирование (collapsible) -->
            <SidebarGroup v-if="isAdmin" class="p-0">
              <SidebarGroupLabel
                class="px-2 cursor-pointer select-none hover:text-foreground transition-colors"
                @click="adminOpen = !adminOpen"
              >
                <ShieldCheck class="size-3 mr-1.5 opacity-60" />
                Администрирование
                <ChevronRight
                  class="ml-auto size-3 opacity-50 transition-transform duration-200"
                  :class="{ 'rotate-90': adminOpen }"
                />
              </SidebarGroupLabel>
              <SidebarGroupContent v-show="adminOpen">
                <SidebarMenu>
                  <SidebarMenuItem>
                    <SidebarMenuSub>
                      <SidebarMenuSubItem v-for="item in adminItems" :key="item.to">
                        <SidebarMenuSubButton
                          as="a"
                          :href="item.to"
                          :is-active="isActive(item.to)"
                          @click.prevent="$router.push(item.to)"
                        >
                          <component :is="item.icon" />
                          {{ item.label }}
                        </SidebarMenuSubButton>
                      </SidebarMenuSubItem>
                    </SidebarMenuSub>
                  </SidebarMenuItem>
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>

          </nav>
        </aside>

        <!-- Page content -->
        <div class="flex-1 min-w-0">
          <slot />
        </div>

      </div>
    </div>
  </div>
</template>
