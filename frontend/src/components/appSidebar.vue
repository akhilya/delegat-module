<script>
import {
  Sidebar,
  SidebarContent,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import AppSidebarBrand from "@/components/sidebar/AppSidebarBrand.vue";
import AppSidebarStoreSwitcher from "@/components/sidebar/AppSidebarStoreSwitcher.vue";
import AppSidebarNavCollapsible from "@/components/sidebar/AppSidebarNavCollapsible.vue";
import AppSidebarFooter from "@/components/sidebar/AppSidebarFooter.vue";
import { buildSidebarGroups } from "@/config/sidebarNav";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { http } from "@/main";
import { useDark, useToggle } from "@vueuse/core";

export default {
  components: {
    Sidebar,
    SidebarContent,
    SidebarHeader,
    SidebarRail,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AppSidebarBrand,
    AppSidebarStoreSwitcher,
    AppSidebarNavCollapsible,
    AppSidebarFooter,
  },
  setup() {
    const isDark = useDark();
    const toggleDark = useToggle(isDark);
    return { isDark, toggleDark };
  },
  data() {
    return {
      me: null,
      memberships: [],
      connections: [],
      platformVersion: null,
      appVersion: import.meta.env.VITE_APP_VERSION || null,
      confirmDialog: { open: false, title: "", message: "", resolve: null },
    };
  },
  computed: {
    selectedStoreMembership() {
      return this.memberships.find((store) => store.id === this.me?.selectedStoreId);
    },
    canManageSelectedStore() {
      return this.me?.isAdmin || this.selectedStoreMembership?.isStoreAdmin;
    },
    sidebarContext() {
      return {
        me: this.me,
        canManageStore: this.canManageSelectedStore,
        isAdmin: Boolean(this.me?.isAdmin),
        connections: this.connections,
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
      this.fetchConnections();
    },
  },
  methods: {
    ...mapActions([
      "getMe",
      "getMyMemberships",
      "postLogOut",
      "postSelectStore",
    ]),
    showConfirm(title, message) {
      return new Promise((r) => { this.confirmDialog = { open: true, title, message, resolve: r }; });
    },
    fetchConnections() {
      if (!this.me?.selectedStoreId) {
        this.connections = [];
        return;
      }
      this.$store.dispatch("marketplace/getConnections", {
        onSuccess: (response) => {
          const data = response.data?.value ?? response.data;
          this.connections = Array.isArray(data) ? data : [];
        },
        onError: () => {
          this.connections = [];
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

      this.fetchConnections();
    },
    async selectStore(storeId) {
      const hasUploads = this.$store.getters["operations/hasLocalActiveUploads"];
      if (hasUploads) {
        const confirmed = await this.showConfirm(
          "Переключить магазин",
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
    <SidebarHeader class="border-b gap-2 py-2">
      <AppSidebarBrand :user-name="me?.userName || 'Пользователь'" />
      <AppSidebarStoreSwitcher
        :memberships="memberships"
        :selected-store-id="me?.selectedStoreId"
        @select-store="selectStore"
      />
    </SidebarHeader>

    <SidebarContent>
      <AppSidebarNavCollapsible
        v-for="group in navGroups"
        :key="group.id"
        :label="group.label"
        :icon="group.icon"
        :items="group.resolvedItems"
        :default-open="group.defaultOpen"
      />
    </SidebarContent>

    <AppSidebarFooter
      :is-dark="isDark"
      :version="displayVersion"
      @toggle-theme="toggleDark()"
      @logout="postLogOut"
    />

    <SidebarRail />

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
          <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">Отмена</AlertDialogCancel>
          <AlertDialogAction @click="confirmDialog.resolve(true); confirmDialog.open = false">Продолжить</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </Sidebar>
</template>