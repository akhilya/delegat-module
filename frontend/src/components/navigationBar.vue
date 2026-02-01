<script>
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
import { UserRound, Loader2, Store, Key, LogOut, AlertCircle } from "lucide-vue-next";
import { PAGE_SIGN_IN, PAGE_ADMIN_STORES, PAGE_ADMIN_TOKENS } from "@/router";

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
        <slot name="pre-left" />
        <template v-if="memberships.length > 0">
          <Select
            :model-value="me?.selectedStoreId"
            @update:model-value="selectStore($event)"
          >
            <SelectTrigger class="bg-primary-foreground">
              <SelectValue placeholder="Выберите магазин..." />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectLabel>Магазины</SelectLabel>
                <SelectItem
                  as="button"
                  v-for="store in memberships"
                  :key="store.id"
                  :value="store.id"
                >
                  {{ store.name }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </template>
        <template v-else>
          <div class="flex items-center gap-2 text-sm text-muted-foreground bg-muted px-3 py-2 rounded-md">
            <AlertCircle class="w-4 h-4" />
            <span>Вы не состоите ни в одном магазине</span>
          </div>
        </template>
        <slot name="pro-left" />
      </div>
      <div class="flex items-center justify-center">
        <slot name="center" />
      </div>
      <div class="flex items-center justify-end gap-2">
        <slot name="pre-right" />
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
        <slot name="pro-right" />
      </div>
    </template>
    <template v-else>
      <div class="col-span-full flex items-center justify-center">
        <Loader2 class="size-4 animate-spin" />
      </div>
    </template>
  </div>
</template>
