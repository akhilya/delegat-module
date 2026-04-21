<script>
// NavigationBar - Top-level navigation bar with store selector, user menu, and page links.
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import Button from "./ui/button/Button.vue";

import { http } from "@/main";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { useDark, useToggle } from "@vueuse/core";
import { UserRound, Loader2, Store, Key, LogOut, AlertCircle, House, Plus, ShoppingBag, Puzzle, FolderOpen, Globe, Layers, Trash2, Sun, Moon } from "lucide-vue-next";
import { PAGE_ADMIN_STORES, PAGE_ADMIN_TOKENS, PAGE_PRODUCTS, PAGE_PRODUCT_CREATION, PAGE_MARKETPLACE, PAGE_MARKETPLACE_PLUGINS, PAGE_CATEGORIES, PAGE_PRODUCT_GROUPS, PAGE_STORE_ATTRIBUTES } from "@/router";

export default {
  components: {
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectLabel,
    SelectTrigger,
    SelectValue,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    Button,
    UserRound,
    Loader2,
    Store,
    Key,
    LogOut,
    AlertCircle,
    House,
    Plus,
    ShoppingBag,
    Puzzle,
    FolderOpen,
    Globe,
    Layers,
    Trash2,
    Sun,
    Moon,
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
      memberships: [],
      platformVersion: null,
    };
  },
  methods: {
    ...mapActions([
      "getMe",
      "postLogOut",
      "getMyMemberships",
      "postSelectStore",
    ]),
    async selectStore(storeId) {
      // Block store switch if file uploads are in-flight
      const hasUploads = this.$store.state.operations.operations.some(
        (o) => o.type === "file-upload" && o.status === "running"
      );
      if (hasUploads) {
        const confirmed = window.confirm(
          "Идёт загрузка файлов. При переключении магазина загрузка будет прервана. Продолжить?"
        );
        if (!confirmed) return;
      }
      this.$store.commit("setSwitchingStore", true);
      const minDelay = new Promise(r => setTimeout(r, 500));
      this.postSelectStore({
        storeId,
        onSuccess: async (response) => {
          // Re-fetch user data (selectedStoreId changed on server)
          await this.getMe({
            onSuccess: (r) => { this.me = r.data.value; },
            onError: () => {},
          });
          // Reconnect SignalR to new store group
          this.$store.dispatch("operations/reconnectSignalR");
          // Navigate FIRST to avoid re-mounting the current page component
          // with a stale product/entity ID from the old store (causes 404 toasts).
          const next = response.data.value.next;
          const targetPath = new URL(next, window.location.origin).pathname;
          if (this.$route.path !== targetPath) {
            await this.$router.push(targetPath);
          }
          // Force re-render all page components via appKey AFTER navigation,
          // so components mount at the correct route with the new store cookie.
          this.$store.commit("incrementAppKey");
          // Ensure overlay is visible for at least 1 second
          await minDelay;
          this.$store.commit("setSwitchingStore", false);
        },
        onError: async (error) => {
          await minDelay;
          this.$store.commit("setSwitchingStore", false);
          displaySonnerError(error);
        },
      });
    },
    goToAdminStores() {
      this.$router.push(PAGE_ADMIN_STORES);
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
      this.$router.push({ path: PAGE_PRODUCTS, query: { trash: '1' } });
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

    await this.getMyMemberships({
      onSuccess: (response) => {
        this.memberships = response.data.value;
      },
      onError: (error) => {
        displaySonnerError(error);
      },
    });

    // Auto-select first store if current selection is invalid
    if (this.memberships.length > 0 && (
      !this.me?.selectedStoreId ||
      !this.memberships.some(s => s.id === this.me.selectedStoreId)
    )) {
      await this.selectStore(this.memberships[0].id);
    }

    this.isLoading = false;

    http.get("/health").then(r => {
      this.platformVersion = r.data?.version;
    }).catch(() => {});
  },
};
</script>

<template>
  <div class="w-screen h-20 backdrop-blur-lg fixed left-0 top-0 z-30" />
  <div class="w-full h-20 sticky top-0 left-0 grid grid-cols-3 z-30">
    <template v-if="!isLoading">
      <div class="flex items-center justify-start gap-2">
        <template v-if="memberships.length > 0">
          <Select
            :model-value="String(me?.selectedStoreId ?? '')"
            @update:model-value="selectStore(Number($event))"
          >
            <SelectTrigger class="bg-primary-foreground w-32 sm:w-48">
              <SelectValue placeholder="Выберите магазин..." />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectLabel>Магазины</SelectLabel>
                <SelectItem
                  as="button"
                  v-for="store in memberships"
                  :key="store.id"
                  :value="String(store.id)"
                >
                  {{ store.name }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </template>
        <template v-else>
          <div class="flex items-center gap-2 text-xs text-muted-foreground bg-muted px-2 py-1.5 rounded-md w-32 sm:w-48">
            <AlertCircle class="size-3 shrink-0" />
            <span class="truncate">Нет магазинов</span>
          </div>
        </template>
      </div>
      <div class="hidden sm:flex items-center justify-center">
        <slot name="center" />
      </div>
      <!-- Mobile: center slot rendered as a full-width row below nav -->
      <div class="sm:hidden col-span-3 pt-1">
        <slot name="center" />
      </div>
      <div class="flex items-center justify-end gap-2">
        <DropdownMenu>
          <DropdownMenuTrigger class="flex items-center gap-2">
            <Button variant="outline">
              <UserRound class="size-4" />
              {{ me?.userName }}
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
              Корзина
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem @select="goToMarketplace">
              <ShoppingBag class="size-4 mr-2" />
              Маркетплейсы
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
              <DropdownMenuItem @select="goToAdminTokens">
                <Key class="size-4 mr-2" />
                Токены
              </DropdownMenuItem>
              <DropdownMenuSeparator />
            </template>
            <DropdownMenuItem @select="toggleDark()">
              <Sun v-if="isDark" class="size-4 mr-2" />
              <Moon v-else class="size-4 mr-2" />
              {{ isDark ? 'Светлая тема' : 'Тёмная тема' }}
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
    </template>
    <template v-else>
      <div class="col-span-full flex items-center justify-center">
        <Loader2 class="size-4 animate-spin" />
      </div>
    </template>
  </div>

</template>
