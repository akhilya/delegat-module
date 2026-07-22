<script>
import {
  Sidebar,
  SidebarContent,
  SidebarHeader,
} from "@/components/ui/sidebar";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import AppSidebarStoreSwitcher from "@/components/sidebar/AppSidebarStoreSwitcher.vue";
import AppSidebarNavCollapsible from "@/components/sidebar/AppSidebarNavCollapsible.vue";
import AppSidebarFooter from "@/components/sidebar/AppSidebarFooter.vue";
import { buildSidebarGroups } from "@/config/sidebarNav";
import { mapActions } from "vuex";
import { displaySonnerError, extractResponseData } from "@/store/sonnerHelper";
import { http } from "@/main";
import { hasAnyPermission, PERMS } from "@/lib/permissions";
import { ME_UPDATED_EVENT } from "@/lib/meCache";

export default {
  components: {
    Sidebar,
    SidebarContent,
    SidebarHeader,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AppSidebarStoreSwitcher,
    AppSidebarNavCollapsible,
    AppSidebarFooter,
  },
  data() {
    return {
      me: null,
      memberships: [],
      platformStoreCount: null,
      connections: [],
      adapters: [],
      platformVersion: null,
      appVersion: import.meta.env.VITE_APP_VERSION || null,
      confirmDialog: { open: false, title: "", message: "", resolve: null },
      _onMeUpdated: null,
    };
  },
  computed: {
    selectedStoreMembership() {
      return this.memberships.find((store) => store.id === this.me?.selectedStoreId);
    },
    canManageSelectedStore() {
      return hasAnyPermission(this.me, [
        PERMS.StoreSettingsManage,
        PERMS.StoreMembersManage,
        PERMS.CatalogManage,
      ]);
    },
    hasStore() {
      return this.memberships.length > 0;
    },
    canCreateStores() {
      return Boolean(this.me?.isAdmin) || hasAnyPermission(this.me, [PERMS.StoresCreate]);
    },
    storeSwitcherEmptyMode() {
      if (this.hasStore) return null;
      // Admins who can create stores: wait for platform count, then show create hint if empty.
      if (this.canCreateStores) {
        if (this.platformStoreCount === null) return null;
        if (this.platformStoreCount === 0) return "no-stores";
      }
      return "no-membership";
    },
    sidebarContext() {
      return {
        me: this.me,
        canManageStore: this.canManageSelectedStore,
        isAdmin: Boolean(this.me?.isAdmin),
        hasStore: this.hasStore,
        hasPermission: (p) => hasAnyPermission(this.me, [p]),
        connections: this.connections,
        adapters: this.adapters,
      };
    },
    navGroups() {
      return buildSidebarGroups(this.sidebarContext);
    },
    displayVersion() {
      return this.appVersion || this.platformVersion || "";
    },
  },
  watch: {
    "$store.state.appKey"() {
      this.loadShellData(true);
    },
    "$route.path"(path) {
      if (path.startsWith("/adapter")) {
        this.fetchConnections();
        this.fetchAdapters();
      }
    },
  },
  methods: {
    ...mapActions([
      "getMe",
      "getMyMemberships",
      "getAllStores",
      "getConnections",
      "getAvailableAdapters",
      "postSelectStore",
    ]),
    showConfirm(title, message) {
      return new Promise((r) => { this.confirmDialog = { open: true, title, message, resolve: r }; });
    },
    async refreshPlatformStoreCount() {
      if (this.memberships.length > 0 || !this.canCreateStores) {
        this.platformStoreCount = null;
        return;
      }
      await this.getAllStores({
        onSuccess: (response) => {
          const payload = response.data?.value ?? response.data;
          this.platformStoreCount = Number(payload?.total ?? payload?.items?.length ?? 0);
        },
        onError: () => {
          this.platformStoreCount = null;
        },
      });
    },
    async fetchConnections() {
      if (!this.me?.selectedStoreId) {
        this.connections = [];
        return;
      }
      await this.getConnections({
        onSuccess: (response) => {
          this.connections = extractResponseData(response, []);
        },
        onError: () => {
          this.connections = [];
        },
      });
    },
    async fetchAdapters() {
      await this.getAvailableAdapters({
        onSuccess: (response) => {
          this.adapters = extractResponseData(response, []);
        },
        onError: () => {
          this.adapters = [];
        },
      });
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

      await this.refreshPlatformStoreCount();

      http.get("/health").then((response) => {
        this.platformVersion = response.data?.version;
      }).catch(() => {});

      if (autoSelectStore && this.memberships.length > 0 && (
        !this.me?.selectedStoreId ||
        !this.memberships.some((store) => store.id === this.me.selectedStoreId)
      )) {
        await this.selectStore(this.memberships[0].id);
        return;
      }

      if (this.memberships.length === 0) {
        this.connections = [];
        const path = this.$route.path;
        const isStoreSurface =
          path === "/" ||
          path.startsWith("/products") ||
          path.startsWith("/categories") ||
          path.startsWith("/product-groups") ||
          path.startsWith("/storage") ||
          path.startsWith("/adapter") ||
          path.startsWith("/tools") ||
          path.startsWith("/store-attributes");
        if (isStoreSurface) {
          await this.$router.replace("/settings/user");
        }
        return;
      }

      this.fetchConnections();
      this.fetchAdapters();
    },
    async selectStore(storeId) {
      const hasUploads = this.$store.getters["operations/hasLocalActiveUploads"];
      if (hasUploads) {
        const confirmed = await this.showConfirm(
          this.$t("components.appSidebar.switchStoreTitle"),
          this.$t("components.appSidebar.switchStoreUploadWarning")
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
    this._onMeUpdated = (event) => {
      if (event?.detail && typeof event.detail === "object") {
        this.me = event.detail;
      }
    };
    window.addEventListener(ME_UPDATED_EVENT, this._onMeUpdated);
    this.loadShellData();
  },
  beforeUnmount() {
    if (this._onMeUpdated) {
      window.removeEventListener(ME_UPDATED_EVENT, this._onMeUpdated);
    }
  },
};
</script>

<template>
  <Sidebar collapsible="icon">
    <SidebarHeader class="border-b p-2">
      <AppSidebarStoreSwitcher
        :memberships="memberships"
        :selected-store-id="me?.selectedStoreId"
        :empty-mode="storeSwitcherEmptyMode"
        @select-store="selectStore"
      />
    </SidebarHeader>

    <SidebarContent class="gap-0.5">
      <AppSidebarNavCollapsible
        v-for="group in navGroups"
        :key="group.id"
        :group-id="group.id"
        :label="group.label"
        :icon="group.icon"
        :items="group.resolvedItems"
        :default-open="group.defaultOpen"
      />
    </SidebarContent>

    <AppSidebarFooter
      :user-name="me?.userName"
      :version="displayVersion"
    />
  </Sidebar>

  <AlertDialog
    :open="confirmDialog.open"
    @update:open="(v) => { if (!v) confirmDialog.resolve?.(false); confirmDialog.open = v; }"
  >
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ confirmDialog.title }}</AlertDialogTitle>
        <AlertDialogDescription>{{ confirmDialog.message }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">{{ $t('common.cancel') }}</AlertDialogCancel>
        <AlertDialogAction @click="confirmDialog.resolve(true); confirmDialog.open = false">{{ $t('app.continue') }}</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>