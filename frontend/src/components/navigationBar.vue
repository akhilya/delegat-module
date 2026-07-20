<script>
// NavigationBar - Page-level toolbar with mobile sidebar trigger, center slot, and user menu.
import { SidebarTrigger } from "@/components/ui/sidebar";
import UserAccountMenu from "@/components/UserAccountMenu.vue";

import { http } from "@/main";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { Loader2 } from "lucide-vue-next";

export default {
  props: {
    sticky: { type: Boolean, default: true },
  },
  components: {
    SidebarTrigger,
    Loader2,
    UserAccountMenu,
  },
  data() {
    return {
      me: undefined,
      isLoading: false,
      platformVersion: null,
    };
  },
  methods: {
    ...mapActions(["getMe"]),
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
  <div :class="['w-full bg-background', sticky && 'sticky top-0 z-30 supports-[backdrop-filter]:bg-background/95 supports-[backdrop-filter]:backdrop-blur-sm']">
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

        <div v-if="$slots.right" class="hidden md:flex items-center gap-2 shrink-0 mr-1">
          <slot name="right" />
        </div>

        <div class="ml-auto shrink-0">
          <UserAccountMenu
            variant="navbar"
            :user-name="me?.userName"
            :version="platformVersion"
          />
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
