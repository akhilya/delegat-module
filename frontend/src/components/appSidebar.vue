<script>
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
} from "@/components/ui/sidebar";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import Button from "@/components/ui/button/Button.vue";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { http } from "@/main";
import { useDark, useToggle } from "@vueuse/core";
import {
  AlertTriangle,
  Boxes,
  FolderOpen,
  Globe,
  HardDrive,
  Key,
  Layers,
  LogOut,
  Moon,
  PackagePlus,
  Plug,
  Settings2,
  ShoppingBag,
  Store,
  Sun,
  Trash2,
  UserCog,
  Users,
  Wand2,
} from "lucide-vue-next";
import {
  PAGE_ADMIN_ERROR_LOGS,
  PAGE_ADMIN_PLUGINS,
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_TOKENS,
  PAGE_ADMIN_USERS,
  PAGE_CATEGORIES,
  PAGE_MARKETPLACE,
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_PRODUCTS,
  PAGE_SETTINGS,
  PAGE_SETTINGS_STORE,
  PAGE_SETTINGS_USER,
  PAGE_STORE_ATTRIBUTES,
  PAGE_STORE_STORAGE,
  PAGE_TOOLS,
} from "@/router";

export default {
  components: {
    Sidebar,
    SidebarContent,
    SidebarFooter,
    SidebarGroup,
    SidebarGroupContent,
    SidebarGroupLabel,
    SidebarHeader,
    SidebarMenu,
    SidebarMenuButton,
    SidebarMenuItem,
    SidebarMenuSub,
    SidebarMenuSubButton,
    SidebarMenuSubItem,
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectLabel,
    SelectTrigger,
    SelectValue,
    Button,
    AlertTriangle,
    Boxes,
    FolderOpen,
    Globe,
    HardDrive,
    Key,
    Layers,
    LogOut,
    Moon,
    PackagePlus,
    Plug,
    Settings2,
    ShoppingBag,
    Store,
    Sun,
    Trash2,
    UserCog,
    Users,
    Wand2,
  },
  setup() {
    const isDark = useDark();
    const toggleDark = useToggle(isDark);
    return {
      isDark,
      toggleDark,
      PAGE_SETTINGS,
      PAGE_SETTINGS_STORE,
    };
  },
  data() {
    return {
      me: null,
      memberships: [],
      platformVersion: null,
    };
  },
  computed: {
    selectedStoreMembership() {
      return this.memberships.find((store) => store.id === this.me?.selectedStoreId);
    },
    canManageSelectedStore() {
      return this.me?.isAdmin || this.selectedStoreMembership?.isStoreAdmin;
    },
    primaryItems() {
      return [
        { label: "Товары", to: PAGE_PRODUCTS, icon: Boxes },
        { label: "Новый товар", to: PAGE_PRODUCT_CREATION, icon: PackagePlus },
        { label: "Группы товаров", to: PAGE_PRODUCT_GROUPS, icon: Layers },
        { label: "Хранилище", to: PAGE_STORE_STORAGE, icon: HardDrive },
        { label: "Корзина товаров", to: `${PAGE_PRODUCTS}?trash=1`, icon: Trash2 },
      ];
    },
    userSettingsItems() {
      return [
        { label: "Мои настройки", to: PAGE_SETTINGS_USER, icon: UserCog },
      ];
    },
    storeSettingsSubItems() {
      if (!this.canManageSelectedStore) return [];
      return [
        { label: "Адаптеры", to: PAGE_MARKETPLACE, icon: ShoppingBag },
        { label: "Категории", to: PAGE_CATEGORIES, icon: FolderOpen },
        { label: "Глобальные атрибуты", to: PAGE_STORE_ATTRIBUTES, icon: Globe },
        { label: "Инструменты", to: PAGE_TOOLS, icon: Wand2 },
      ];
    },
    adminSettingsSubItems() {
      if (!this.me?.isAdmin) return [];
      return [
        { label: "Магазины", to: PAGE_ADMIN_STORES, icon: Store },
        { label: "Пользователи", to: PAGE_ADMIN_USERS, icon: Users },
        { label: "Токены", to: PAGE_ADMIN_TOKENS, icon: Key },
        { label: "Плагины", to: PAGE_ADMIN_PLUGINS, icon: Plug },
        { label: "Логи ошибок", to: PAGE_ADMIN_ERROR_LOGS, icon: AlertTriangle },
      ];
    },
  },
  methods: {
    ...mapActions([
      "getMe",
      "getMyMemberships",
      "postLogOut",
      "postSelectStore",
    ]),
    isActive(to) {
      const [path, query] = to.split("?");
      if (query) return this.$route.path === path && this.$route.fullPath.includes(query);
      return this.$route.path === path || this.$route.path.startsWith(`${path}/`);
    },
    async loadShellData(autoSelectStore = true) {
      await this.getMe({
        onSuccess: (response) => {
          this.me = response.data.value;
        },
        onError: () => {},
      });

      await this.getMyMemberships({
        onSuccess: (response) => {
          this.memberships = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      http.get("/health").then((response) => {
        this.platformVersion = response.data?.version;
      }).catch(() => {});

      if (autoSelectStore && this.memberships.length > 0 && (
        !this.me?.selectedStoreId ||
        !this.memberships.some((store) => store.id === this.me.selectedStoreId)
      )) {
        await this.selectStore(this.memberships[0].id);
      }
    },
    async selectStore(storeId) {
      const hasUploads = this.$store.getters["operations/hasLocalActiveUploads"];
      if (hasUploads) {
        const confirmed = window.confirm(
          "Идёт загрузка файлов. При переключении магазина загрузка будет прервана. Продолжить?"
        );
        if (!confirmed) return;
      }

      this.$store.commit("setSwitchingStore", true);
      await this.postSelectStore({
        storeId,
        onSuccess: async (response) => {
          await this.loadShellData(false);
          this.$store.dispatch("operations/reconnectSignalR");
          const next = response.data.value.next;
          const targetPath = new URL(next, window.location.origin).pathname;
          if (this.$route.path !== targetPath) {
            await this.$router.push(targetPath);
          }
          this.$store.commit("incrementAppKey");
          this.$store.commit("setSwitchingStore", false);
        },
        onError: (error) => {
          this.$store.commit("setSwitchingStore", false);
          displaySonnerError(error);
        },
      });
    },
  },
  mounted() {
    this.loadShellData();
  },
};
</script>

<template>
  <Sidebar>
    <SidebarHeader class="border-b">
      <div class="flex items-center gap-2 px-2 py-1">
        <div class="flex size-8 items-center justify-center rounded-md bg-primary text-primary-foreground">
          <Settings2 class="size-4" />
        </div>
        <div class="min-w-0">
          <div class="truncate text-sm font-semibold">Delegat Dashboard</div>
          <div class="truncate text-xs text-muted-foreground">{{ me?.userName || "Пользователь" }}</div>
        </div>
      </div>

      <Select
        v-if="memberships.length > 0"
        :model-value="String(me?.selectedStoreId ?? '')"
        @update:model-value="selectStore(Number($event))"
      >
        <SelectTrigger class="h-9 w-full bg-background">
          <SelectValue placeholder="Магазин" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectLabel>Магазины</SelectLabel>
            <SelectItem
              v-for="store in memberships"
              :key="store.id"
              :value="String(store.id)"
            >
              {{ store.name }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
    </SidebarHeader>

    <SidebarContent>
      <SidebarGroup>
        <SidebarGroupLabel>Магазин</SidebarGroupLabel>
        <SidebarGroupContent>
          <SidebarMenu>
            <SidebarMenuItem v-for="item in primaryItems" :key="item.label">
              <SidebarMenuButton as-child :is-active="isActive(item.to)">
                <RouterLink :to="item.to">
                  <component :is="item.icon" />
                  <span>{{ item.label }}</span>
                </RouterLink>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroupContent>
      </SidebarGroup>

      <SidebarGroup>
        <SidebarGroupLabel>Пользователь</SidebarGroupLabel>
        <SidebarGroupContent>
          <SidebarMenu>
            <SidebarMenuItem v-for="item in userSettingsItems" :key="item.label">
              <SidebarMenuButton as-child :is-active="isActive(item.to)">
                <RouterLink :to="item.to">
                  <component :is="item.icon" />
                  <span>{{ item.label }}</span>
                </RouterLink>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroupContent>
      </SidebarGroup>

      <SidebarGroup v-if="canManageSelectedStore">
        <SidebarGroupLabel>Настройки магазина</SidebarGroupLabel>
        <SidebarGroupContent>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton as-child :is-active="isActive(PAGE_SETTINGS_STORE)">
                <RouterLink :to="PAGE_SETTINGS_STORE">
                  <Store />
                  <span>Настройки магазина</span>
                </RouterLink>
              </SidebarMenuButton>
              <SidebarMenuSub>
                <SidebarMenuSubItem v-for="item in storeSettingsSubItems" :key="item.label">
                  <SidebarMenuSubButton as-child :is-active="isActive(item.to)">
                    <RouterLink :to="item.to">
                      <component :is="item.icon" />
                      {{ item.label }}
                    </RouterLink>
                  </SidebarMenuSubButton>
                </SidebarMenuSubItem>
              </SidebarMenuSub>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroupContent>
      </SidebarGroup>

      <SidebarGroup v-if="me?.isAdmin">
        <SidebarGroupLabel>Администрирование</SidebarGroupLabel>
        <SidebarGroupContent>
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton as-child :is-active="isActive(PAGE_SETTINGS)">
                <RouterLink :to="PAGE_SETTINGS">
                  <Settings2 />
                  <span>Настройки администратора</span>
                </RouterLink>
              </SidebarMenuButton>
              <SidebarMenuSub>
                <SidebarMenuSubItem v-for="item in adminSettingsSubItems" :key="item.label">
                  <SidebarMenuSubButton as-child :is-active="isActive(item.to)">
                    <RouterLink :to="item.to">
                      <component :is="item.icon" />
                      {{ item.label }}
                    </RouterLink>
                  </SidebarMenuSubButton>
                </SidebarMenuSubItem>
              </SidebarMenuSub>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroupContent>
      </SidebarGroup>
    </SidebarContent>

    <SidebarFooter>
      <Button variant="ghost" class="justify-start gap-2" @click="toggleDark()">
        <Sun v-if="isDark" class="size-4" />
        <Moon v-else class="size-4" />
        {{ isDark ? "Светлая тема" : "Тёмная тема" }}
      </Button>
      <Button variant="ghost" class="justify-start gap-2" @click="postLogOut">
        <LogOut class="size-4" />
        Выйти
      </Button>
      <div v-if="platformVersion" class="px-2 text-[10px] text-muted-foreground">
        v{{ platformVersion }}
      </div>
    </SidebarFooter>
  </Sidebar>
</template>
