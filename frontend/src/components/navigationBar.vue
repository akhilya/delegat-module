<script>
import { Spinner } from "@/components/ui/spinner";
// NavigationBar - Page-level toolbar. Below lg, search/tabs sit on a second
// row so the title and actions stay usable on a phone.
import { SidebarTrigger } from "@/components/ui/sidebar";
import UserAccountMenu from "@/components/UserAccountMenu.vue";
import FabGroup from "@/components/FabGroup.vue";
import { CircleHelp } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { pageHelpSlug } from "@/lib/docsHelp";

import { http } from "@/main";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { normalizeAppVersion, resolveDisplayVersion } from "@/lib/appVersion";


export default {
  props: {
    /** When true, render the center slot (search/tabs). Do not infer from $slots alone. */
    hasCenter: { type: Boolean, default: false },
  },
  components: {
    Spinner,
    SidebarTrigger,
    UserAccountMenu,
    FabGroup,
    CircleHelp,
    Button,
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
    pageHelpSlug() {
      return pageHelpSlug(
        this.$route.path,
        this.$store.state.docs?.catalog,
      );
    },
  },
  methods: {
    ...mapActions(["getMe"]),
    openPageHelp() {
      this.$store.dispatch("docs/openForRoute", {
        path: this.$route.path,
        connId: this.$route.params.connId,
      });
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
        this.platformVersion = normalizeAppVersion(r.data?.version);
      })
      .catch(() => {});
  },
};
</script>

<template>
  <div
    data-slot="app-navbar"
    class="min-h-[var(--app-navbar-row)] min-w-0 w-full shrink-0 overflow-hidden bg-background"
  >
    <template v-if="!isLoading">
        <div
          :class="
            hasCenter
              ? 'grid w-full grid-cols-[minmax(0,1fr)_auto] grid-rows-[var(--app-navbar-row)_auto] lg:h-[var(--app-navbar-row)] lg:grid-cols-[minmax(0,1fr)_minmax(0,min(32rem,42vw))_max-content] lg:grid-rows-[var(--app-navbar-row)] lg:items-center lg:gap-x-3'
              : 'flex h-[var(--app-navbar-row)] items-center gap-2'
          "
        >
          <div
            :class="
              hasCenter
                ? 'col-start-1 row-start-1 flex h-[var(--app-navbar-row)] min-w-0 items-center gap-1.5'
                : 'flex min-w-0 flex-1 items-center gap-1.5'
            "
          >
            <div class="md:hidden shrink-0">
              <SidebarTrigger />
            </div>
            <div v-if="$slots.left" class="min-w-0 flex-1">
              <slot name="left" />
            </div>
          </div>

          <div
            v-if="hasCenter"
            class="col-span-2 row-start-2 flex min-w-0 w-full items-center pb-2.5 lg:col-span-1 lg:col-start-2 lg:row-start-1 lg:w-full lg:min-w-0 lg:max-w-full lg:justify-self-stretch lg:pb-0"
          >
            <div
              class="flex min-w-0 w-full items-center justify-start lg:justify-center lg:[&_[data-slot=nav-search]]:w-full lg:[&_[data-slot=nav-search]]:min-w-0 lg:[&_[data-slot=nav-search]]:max-w-64"
            >
              <slot name="center" />
            </div>
          </div>

          <div
            :class="
              hasCenter
                ? 'col-start-2 row-start-1 flex h-[var(--app-navbar-row)] shrink-0 items-center justify-end gap-0.5 lg:col-start-3 lg:justify-self-end'
                : 'ml-auto flex shrink-0 items-center gap-0.5'
            "
          >
            <div
              v-if="$slots.right"
              class="mr-1 hidden shrink-0 items-center gap-2 md:flex"
            >
              <slot name="right" />
            </div>
            <Button
              v-if="pageHelpSlug"
              variant="ghost"
              class="size-9 shrink-0 gap-1.5 p-0 xl:h-9 xl:w-auto xl:px-3"
              :title="$t('components.docsSheet.needHelp')"
              :aria-label="$t('components.docsSheet.needHelp')"
              @click="openPageHelp"
            >
              <CircleHelp class="size-4" />
              <span class="hidden xl:inline">{{
                $t("components.docsSheet.needHelp")
              }}</span>
            </Button>
            <FabGroup
              placement="navbar"
              :ai-enabled="me?.aiEnabled === true"
            />
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
