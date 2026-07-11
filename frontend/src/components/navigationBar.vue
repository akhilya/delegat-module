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
import { Button } from "@/components/ui/button";
import { SidebarTrigger } from "@/components/ui/sidebar";

import { http } from "@/main";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { useDark, useToggle } from "@vueuse/core";
import {
  House,
  Layers,
  Loader2,
  LogOut,
  Moon,
  Plus,
  Settings2,
  Sun,
  Trash2,
  UserCog,
  UserRound,
} from "lucide-vue-next";
import {
  PAGE_PRODUCT_CREATION,
  PAGE_PRODUCT_GROUPS,
  PAGE_PRODUCTS,
  PAGE_SETTINGS,
  PAGE_SETTINGS_STORE,
  PAGE_SETTINGS_USER,
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
    House,
    Layers,
    Loader2,
    LogOut,
    Moon,
    Plus,
    Settings2,
    Sun,
    Trash2,
    UserCog,
    UserRound,
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
    goToProducts() {
      this.$router.push(PAGE_PRODUCTS);
    },
    goToProductCreation() {
      this.$router.push(PAGE_PRODUCT_CREATION);
    },
    goToProductGroups() {
      this.$router.push(PAGE_PRODUCT_GROUPS);
    },
    goToTrash() {
      this.$router.push({ path: PAGE_PRODUCTS, query: { trash: "1" } });
    },
    goToUserSettings() {
      this.$router.push(PAGE_SETTINGS_USER);
    },
    goToStoreSettings() {
      this.$router.push(PAGE_SETTINGS_STORE);
    },
    goToAdminSettings() {
      this.$router.push(PAGE_SETTINGS);
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

    http
      .get("/health")
      .then((r) => {
        this.platformVersion = r.data?.version;
      })
      .catch(() => {});
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

        <div v-if="$slots.left" class="min-w-0 shrink">
          <slot name="left" />
        </div>

        <div class="hidden sm:flex flex-1 items-center justify-center">
          <slot name="center" />
        </div>

        <div class="ml-auto shrink-0">
          <DropdownMenu>
            <DropdownMenuTrigger as-child>
              <Button variant="ghost" class="h-9 gap-1.5">
                <UserRound class="size-4" />
                <span class="hidden sm:inline max-w-[120px] truncate">{{
                  me?.userName
                }}</span>
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
              <DropdownMenuItem @select="goToProductGroups">
                <Layers class="size-4 mr-2" />
                Группы товаров
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToTrash">
                <Trash2 class="size-4 mr-2" />
                Корзина товаров
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem @select="goToUserSettings">
                <UserCog class="size-4 mr-2" />
                Мои настройки
              </DropdownMenuItem>
              <template v-if="me?.isAdmin">
                <DropdownMenuItem @select="goToStoreSettings">
                  <Settings2 class="size-4 mr-2" />
                  Настройки магазина
                </DropdownMenuItem>
                <DropdownMenuItem @select="goToAdminSettings">
                  <Settings2 class="size-4 mr-2" />
                  Общие настройки
                </DropdownMenuItem>
              </template>
              <DropdownMenuSeparator />
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
                <div
                  class="px-2 py-1 text-[10px] text-muted-foreground text-center select-none"
                >
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
