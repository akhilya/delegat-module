<script>

import PageSection from "@/components/pageSection.vue";


import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { PageHeader } from "@/components/ui/page-header";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { KeyRound, Loader2, Pencil, Save, Users } from "lucide-vue-next";

export default {
  components: {
    PageSection,
    Button,
    Input,
    Label,
    Switch,
    Badge,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
    PageHeader,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    KeyRound,
    Loader2,
    Pencil,
    Save,
    Users,
  },
  data() {
    return {
      users: [],
      me: null,
      drafts: {},
      editingUser: null,
      editForm: {
        userName: "",
        password: "",
        isAdmin: false,
        isActive: true,
      },
      isEditDialogOpen: false,

      isLoading: false,
      savingUserId: null,
    };
  },
  computed: {
    activeUsersCount() {
      return this.users.filter((user) => user.isActive).length;
    },
    hasEditChanges() {
      if (!this.editingUser) return false;
      return (
        this.editForm.userName.trim() !== (this.editingUser.userName || "") ||
        this.editForm.password.trim().length > 0 ||
        this.editForm.isAdmin !== this.editingUser.isAdmin ||
        this.editForm.isActive !== this.editingUser.isActive
      );
    },
    editingSelf() {
      return Boolean(this.editingUser && this.me && this.editingUser.id === this.me.id);
    },
  },
  methods: {
    ...mapActions(["getMe", "getAdminUsers", "updateAdminUser"]),

    createDrafts(users) {
      this.drafts = users.reduce((acc, user) => {
        acc[user.id] = {
          isAdmin: user.isAdmin,
          isActive: user.isActive,
        };
        return acc;
      }, {});
    },

    async fetchUsers() {
      this.isLoading = true;
      await this.getAdminUsers({
        onSuccess: (response) => {
          this.users = response.data.value;
          this.createDrafts(this.users);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async fetchMe() {
      await this.getMe({
        onSuccess: (response) => {
          this.me = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    applyUpdatedUser(updatedUser) {
      const index = this.users.findIndex((item) => item.id === updatedUser.id);
      if (index !== -1) {
        this.users.splice(index, 1, updatedUser);
      }
      this.createDrafts(this.users);
    },

    hasRoleChanges(user) {
      const draft = this.drafts[user.id];
      if (!draft) return false;

      return (
        draft.isAdmin !== user.isAdmin ||
        draft.isActive !== user.isActive
      );
    },

    openEditDialog(user) {
      this.editingUser = user;
      this.editForm = {
        userName: user.userName || "",
        password: "",
        isAdmin: user.isAdmin,
        isActive: user.isActive,
      };
      this.isEditDialogOpen = true;
    },

    closeEditDialog() {
      this.isEditDialogOpen = false;
      this.editingUser = null;
      this.editForm = {
        userName: "",
        password: "",
        isAdmin: false,
        isActive: true,
      };
    },

    async saveRoleChanges(user) {
      const draft = this.drafts[user.id];
      if (!draft || !this.hasRoleChanges(user)) return;

      this.savingUserId = user.id;
      await this.updateAdminUser({
        userId: user.id,
        data: {
          isAdmin: draft.isAdmin,
          isActive: draft.isActive,
        },
        onSuccess: (response) => {
          this.applyUpdatedUser(response.data.value);
          displaySonnerSuccess(this.$t("pages.usersPage.userUpdated"));
        },
        onError: (error) => {
          displaySonnerError(error);
          this.createDrafts(this.users);
        },
      });
      this.savingUserId = null;
    },

    async saveIdentityChanges() {
      if (!this.editingUser || !this.hasEditChanges) return;

      const userName = this.editForm.userName.trim();
      const password = this.editForm.password.trim();

      if (!userName) {
        displaySonnerError(this.$t("pages.usersPage.userNameEmptyError"));
        return;
      }

      this.savingUserId = this.editingUser.id;
      const payload = {
        userName,
        isAdmin: this.editForm.isAdmin,
        isActive: this.editForm.isActive,
      };
      if (password) {
        payload.password = password;
      }

      await this.updateAdminUser({
        userId: this.editingUser.id,
        data: payload,
        onSuccess: async (response) => {
          await this.fetchUsers();
          this.closeEditDialog();
          displaySonnerSuccess(this.$t("pages.usersPage.userUpdated"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.savingUserId = null;
    },
  },
  mounted() {
    this.fetchMe();
    this.fetchUsers();
  },
};
</script>

<template>
  <div>
    <PageHeader
      :title="$t('routes.users')"
      :description="$t('pages.usersPage.activeUsersDescription', { count: activeUsersCount })"
    />

    <PageSection
      :loading="isLoading"
      :empty="users.length === 0"
      :empty-icon="Users"
      :empty-title="$t('pages.usersPage.emptyTitle')"
      :empty-description="$t('pages.usersPage.emptyDescription')"
    >
      <div class="rounded-lg border overflow-hidden">
      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-24">ID</TableHead>
              <TableHead>{{ $t("common.name") }}</TableHead>
              <TableHead class="w-32">{{ $t("pages.usersPage.columnRole") }}</TableHead>
              <TableHead class="w-32">{{ $t("common.status") }}</TableHead>
              <TableHead class="w-32"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow
              v-for="user in users"
              :key="user.id"
              :class="!user.isActive ? 'bg-muted/30 text-muted-foreground' : ''"
            >
              <TableCell class="text-muted-foreground tabular-nums">
                {{ user.id }}
              </TableCell>
              <TableCell>
                <div class="font-medium">{{ user.userName }}</div>
              </TableCell>
              <TableCell>
                <Badge :variant="user.isAdmin ? 'default' : 'secondary'">
                  {{ user.isAdmin ? $t("pages.usersPage.roleAdmin") : $t("nav.user") }}
                </Badge>
              </TableCell>
              <TableCell>
                <Badge :variant="user.isActive ? 'secondary' : 'outline'">
                  {{ user.isActive ? $t("pages.usersPage.statusActive") : $t("pages.usersPage.statusDisabled") }}
                </Badge>
              </TableCell>
              <TableCell>
                <div class="flex justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="savingUserId === user.id"
                    @click="openEditDialog(user)"
                  >
                    <Pencil class="size-4" />
                    <span class="sr-only">{{ $t("pages.usersPage.editUser") }}</span>
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
      </div>
    </PageSection>

    <Dialog v-model:open="isEditDialogOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t("pages.usersPage.editUser") }}</DialogTitle>
          <DialogDescription>
            {{ $t("pages.usersPage.editUserDescription") }}
          </DialogDescription>
        </DialogHeader>

        <div class="grid gap-4">
          <div class="grid gap-2">
            <Label for="admin-user-name">{{ $t("auth.userName") }}</Label>
            <Input
              id="admin-user-name"
              v-model="editForm.userName"
              :disabled="savingUserId === editingUser?.id"
              autocomplete="off"
            />
          </div>

          <div class="grid gap-2">
            <Label for="admin-user-password">{{ $t("pages.usersPage.newPasswordLabel") }}</Label>
            <div class="relative">
              <KeyRound class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="admin-user-password"
                v-model="editForm.password"
                :disabled="savingUserId === editingUser?.id"
                class="pl-9"
                type="password"
                autocomplete="new-password"
                :placeholder="$t('pages.usersPage.newPasswordPlaceholder')"
              />
            </div>
          </div>

          <div class="grid gap-3 rounded-lg border p-3">
            <div class="flex items-center justify-between gap-4">
              <div>
                <div class="text-sm font-medium">{{ $t("pages.usersPage.globalAdminLabel") }}</div>
                <div class="text-xs text-muted-foreground">{{ $t("pages.usersPage.globalAdminHint") }}</div>
              </div>
              <Switch
                :model-value="editForm.isAdmin"
                :disabled="savingUserId === editingUser?.id || editingSelf"
                @update:model-value="editForm.isAdmin = $event"
              />
            </div>
            <div v-if="editingSelf" class="text-xs text-muted-foreground">
              {{ $t("pages.usersPage.cannotChangeSelfAdmin") }}
            </div>
            <div class="flex items-center justify-between gap-4">
              <div>
                <div class="text-sm font-medium">{{ $t("pages.usersPage.statusActive") }}</div>
                <div class="text-xs text-muted-foreground">{{ $t("pages.usersPage.disabledUserHint") }}</div>
              </div>
              <Switch
                :model-value="editForm.isActive"
                :disabled="savingUserId === editingUser?.id"
                @update:model-value="editForm.isActive = $event"
              />
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button
            variant="outline"
            :disabled="savingUserId === editingUser?.id"
            @click="closeEditDialog"
          >
            {{ $t("common.cancel") }}
          </Button>
          <Button
            :disabled="savingUserId === editingUser?.id || !hasEditChanges"
            @click="saveIdentityChanges"
          >
            <Loader2
              v-if="savingUserId === editingUser?.id"
              class="size-4 animate-spin"
            />
            <span v-else>{{ $t("common.save") }}</span>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
