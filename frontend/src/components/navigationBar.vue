<script>
import { Spinner } from "@/components/ui/spinner";
// NavigationBar - Page-level toolbar with mobile sidebar trigger, center slot, and user menu.
// Height is locked to --app-navbar-height (single row) so breadcrumbs share one Y-offset on every page.
import { SidebarTrigger } from "@/components/ui/sidebar";
import UserAccountMenu from "@/components/UserAccountMenu.vue";

import { http } from "@/main";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { normalizeAppVersion, resolveDisplayVersion } from "@/lib/appVersion";


export default {
  props: {
    sticky: { type: Boolean, default: true },
    /** When true, render the center slot (search/tabs). Do not infer from $slots alone. */
    hasCenter: { type: Boolean, default: false },
  },
  components: {
    Spinner,
    SidebarTrigger,
    UserAccountMenu
  },
  data() {
    return {
      me: undefined,
      isLoading: false,
      platformVersion: null,
      appVersion: import.meta.env.VITE_APP_VERSION || null,
    };
  },
  computed: {
    displayVersion() {
      return resolveDisplayVersion(this.platformVersion, this.appVersion);
    },
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
        this.platformVersion = normalizeAppVersion(r.data?.version);
      })
      .catch(() => {});
  },
};
</script>

<template>
  <div
    :class="[
      'h-[var(--app-navbar-height)] w-full shrink-0 overflow-hidden bg-background',
      sticky && 'sticky top-0 z-30 supports-[backdrop-filter]:bg-background/95 supports-[backdrop-filter]:backdrop-blur-sm',
    ]"
  >
    <template v-if="!isLoading">
      <div class="flex h-full items-center gap-2">
        <div class="md:hidden shrink-0">
          <SidebarTrigger />
        </div>

        <div v-if="$slots.left" class="min-w-0 shrink">
          <slot name="left" />
        </div>

        <div
          v-if="hasCenter"
          class="flex min-w-0 flex-1 items-center justify-center"
        >
          <slot name="center" />
        </div>

        <div
          v-if="$slots.right"
          class="mr-1 hidden shrink-0 items-center gap-2 md:flex"
        >
          <slot name="right" />
        </div>

        <div class="ml-auto shrink-0">
          <UserAccountMenu
            variant="navbar"
            :user-name="me?.userName"
            :version="displayVersion"
          />
        </div>
      </div>
    </template>

    <template v-else>
      <div class="flex h-full items-center justify-center">
        <Spinner />
      </div>
    </template>
  </div>
</template>
