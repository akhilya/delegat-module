<script>
// NavigationBar - Page-level toolbar with mobile sidebar trigger, center slot, and user menu.
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import Button from "./ui/button/Button.vue";
import { SidebarTrigger } from "@/components/ui/sidebar";

import { http } from "@/main";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { useDark, useToggle } from "@vueuse/core";
import {
  FolderOpen,
  Globe,
  House,
  Key,
  Layers,
  Loader2,
  LogOut,
  Moon,
  Plus,
  Puzzle,
  ShoppingBag,
  Store,
  Sun,
  Trash2,
  UserRound,
  Users,
} from "lucide-vue-next";
import {
  PAGE_ADMIN_STORES,
  PAGE_ADMIN_TOKENS,
  PAGE_ADMIN_USERS,
  PAGE_CATEGORIES,
  PAGE_MARKETPLACE,
  PAGE_MARKETPLACE_PLUGINS,
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_PRODUCTS,
  PAGE_STORE_ATTRIBUTES,
} from "@/router";

export default {
  components: {
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    SidebarTrigger,
    Button,
    FolderOpen,
    Globe,
    House,
    Key,
    Layers,
    Loader2,
    LogOut,
    Moon,
    Plus,
    Puzzle,
    ShoppingBag,
    Store,
    Sun,
    Trash2,
    UserRound,
    Users,
  },
  setup() {
    const isDark = useDark();
    const toggleDark = useToggle(isDark);
    return { isDark, toggleDark };
  },
  data() {
    return {
      me: undefined,
      isLoading: false,
      platformVersion: null,
    };
  },
  methods: {
    ...mapActions(["getMe", "postLogOut"]),
    goToAdminStores() {
      this.$router.push(PAGE_ADMIN_STORES);
    },
    goToAdminUsers() {
      this.$router.push(PAGE_ADMIN_USERS);
    },
    goToAdminTokens() {
      this.$router.push(PAGE_ADMIN_TOKENS);
    },
    goToProducts() {
      this.$router.push(PAGE_PRODUCTS);
    },
    goToProductCreation() {
      this.$router.push(PAGE_PRODUCT_CREATION);
    },
    goToMarketplace() {
      this.$router.push(PAGE_MARKETPLACE);
    },
    goToPlugins() {
      this.$router.push(PAGE_MARKETPLACE_PLUGINS);
    },
    goToCategories() {
      this.$router.push(PAGE_CATEGORIES);
    },
    goToProductGroups() {
      this.$router.push(PAGE_PRODUCT_GROUPS);
    },
    goToStoreAttributes() {
      this.$router.push(PAGE_STORE_ATTRIBUTES);
    },
    goToTrash() {
      this.$router.push({ path: PAGE_PRODUCTS, query: { trash: "1" } });
    },
  },
  async mounted() {
    this.isLoading = true;
    await this.getMe({
      onSuccess: (response) => {
        this.me = response.data.value;
      },
      onError: (error) => {
        displaySonnerError(error);
      },
    });

    this.isLoading = false;

    http.get("/health").then(r => {
      this.platformVersion = r.data?.version;
    }).catch(() => {});
  },
};
</script>

<template>
  <div class="w-full sticky top-0 z-30 bg-background">
    <template v-if="!isLoading">
      <div class="flex items-center h-14 gap-2">
        <div class="md:hidden shrink-0">
          <SidebarTrigger />
        </div>

        <div class="hidden sm:flex flex-1 items-center justify-center">
          <slot name="center" />
        </div>

        <div class="ml-auto shrink-0">
          <DropdownMenu>
            <DropdownMenuTrigger as-child>
              <Button variant="ghost" class="h-9 gap-1.5">
                <UserRound class="size-4" />
                <span class="hidden sm:inline max-w-[120px] truncate">{{ me?.userName }}</span>
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuLabel>{{ me?.userName }}</DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem @select="goToProducts">
                <House class="size-4 mr-2" />
                Товары
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToProductCreation">
                <Plus class="size-4 mr-2" />
                Добавить товар
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToCategories">
                <FolderOpen class="size-4 mr-2" />
                Категории
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToProductGroups">
                <Layers class="size-4 mr-2" />
                Группы товаров
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToStoreAttributes">
                <Globe class="size-4 mr-2" />
                Глобальные атрибуты
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToTrash">
                <Trash2 class="size-4 mr-2" />
                Корзина товаров
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem @select="goToMarketplace">
                <ShoppingBag class="size-4 mr-2" />
                Адаптеры
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToPlugins">
                <Puzzle class="size-4 mr-2" />
                Плагины
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <template v-if="me?.isAdmin">
                <DropdownMenuItem @select="goToAdminStores">
                  <Store class="size-4 mr-2" />
                  Магазины
                </DropdownMenuItem>
                <DropdownMenuItem @select="goToAdminUsers">
                  <Users class="size-4 mr-2" />
                  Пользователи
                </DropdownMenuItem>
                <DropdownMenuItem @select="goToAdminTokens">
                  <Key class="size-4 mr-2" />
                  Токены
                </DropdownMenuItem>
                <DropdownMenuSeparator />
              </template>
              <DropdownMenuItem @select="toggleDark()">
                <Sun v-if="isDark" class="size-4 mr-2" />
                <Moon v-else class="size-4 mr-2" />
                {{ isDark ? "Светлая тема" : "Тёмная тема" }}
              </DropdownMenuItem>
              <DropdownMenuItem @select="postLogOut">
                <LogOut class="size-4 mr-2" />
                Выйти
              </DropdownMenuItem>
              <template v-if="platformVersion">
                <DropdownMenuSeparator />
                <div class="px-2 py-1 text-[10px] text-muted-foreground text-center select-none">
                  v{{ platformVersion }}
                </div>
              </template>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      <div class="sm:hidden pb-1.5">
        <slot name="center" />
      </div>
    </template>

    <template v-else>
      <div class="flex items-center justify-center h-14">
        <Loader2 class="size-4 animate-spin" />
      </div>
    </template>
  </div>
</template>
