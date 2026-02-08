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

import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { UserRound, Loader2, Store, Key, LogOut, AlertCircle, House, Plus, ShoppingBag, Puzzle, FolderOpen, Globe, Layers } from "lucide-vue-next";
import { PAGE_SIGN_IN, PAGE_ADMIN_STORES, PAGE_ADMIN_TOKENS, PAGE_PRODUCTS, PAGE_PRODUCT_CREATION, PAGE_MARKETPLACE, PAGE_MARKETPLACE_PLUGINS, PAGE_CATEGORIES, PAGE_PRODUCT_GROUPS, PAGE_STORE_ATTRIBUTES } from "@/router";

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
  },
  data() {
    return {
      me: undefined,
      isLoading: false,
      memberships: [],
    };
  },
  methods: {
    ...mapActions([
      "getMe",
      "postLogOut",
      "getMyMemberships",
      "postSelectStore",
      "getIsAuthenticated",
    ]),
    selectStore(storeId) {
      this.postSelectStore({
        storeId,
        onSuccess: (response) => {
          window.location.href = response.data.value.next;
        },
        onError: (error) => {
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
  },
  async mounted() {
    this.isLoading = true;
    await this.getIsAuthenticated({
      onSuccess: (response) => {
        const isAuthenticated = response.data.value.isAuthenticated;
        if (!isAuthenticated) this.$router.push(PAGE_SIGN_IN);
      },
      onError: (error) => {
        displaySonnerError(error);
      },
    });
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
    this.isLoading = false;
  },
};
</script>

<template>
  <div class="w-screen h-20 backdrop-blur-lg fixed left-0 top-0 z-20" />
  <div class="w-full h-20 sticky top-0 left-0 grid grid-cols-3 z-20">
    <template v-if="!isLoading">
      <div class="flex items-center justify-start gap-2">
        <template v-if="memberships.length > 0">
          <Select
            :model-value="String(me?.selectedStoreId ?? '')"
            @update:model-value="selectStore(Number($event))"
          >
            <SelectTrigger class="bg-primary-foreground w-48">
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
          <div class="flex items-center gap-2 text-xs text-muted-foreground bg-muted px-2 py-1.5 rounded-md w-48">
            <AlertCircle class="w-3 h-3 shrink-0" />
            <span class="truncate">Нет магазинов</span>
          </div>
        </template>
      </div>
      <div class="flex items-center justify-center">
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
              <House class="w-4 h-4 mr-2" />
              Товары
            </DropdownMenuItem>
            <DropdownMenuItem @select="goToProductCreation">
              <Plus class="w-4 h-4 mr-2" />
              Добавить товар
            </DropdownMenuItem>
            <DropdownMenuItem @select="goToCategories">
              <FolderOpen class="w-4 h-4 mr-2" />
              Категории
            </DropdownMenuItem>
            <DropdownMenuItem @select="goToProductGroups">
              <Layers class="w-4 h-4 mr-2" />
              Группы товаров
            </DropdownMenuItem>
            <DropdownMenuItem @select="goToStoreAttributes">
              <Globe class="w-4 h-4 mr-2" />
              Глобальные атрибуты
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem @select="goToMarketplace">
              <ShoppingBag class="w-4 h-4 mr-2" />
              Маркетплейсы
            </DropdownMenuItem>
            <DropdownMenuItem @select="goToPlugins">
              <Puzzle class="w-4 h-4 mr-2" />
              Плагины
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <template v-if="me?.isAdmin">
              <DropdownMenuItem @select="goToAdminStores">
                <Store class="w-4 h-4 mr-2" />
                Магазины
              </DropdownMenuItem>
              <DropdownMenuItem @select="goToAdminTokens">
                <Key class="w-4 h-4 mr-2" />
                Токены
              </DropdownMenuItem>
              <DropdownMenuSeparator />
            </template>
            <DropdownMenuItem @select="postLogOut">
              <LogOut class="w-4 h-4 mr-2" />
              Выйти
            </DropdownMenuItem>
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
