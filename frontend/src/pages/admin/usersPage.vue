<script>

import PageSection from "@/components/pageSection.vue";

import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
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
import { Check, Copy, KeyRound, Loader2, Pencil, Plus, Save, Trash2, UserCheck, UserPlus, Users } from "lucide-vue-next";
import { hasPermission, PERMS } from "@/lib/permissions";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { confirm } from "@/composables/useConfirm";
import { EmptyState } from "@/components/ui/empty-state";

export default {
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { formatStoreDateTime };
  },
  components: {
    PageSection,
    Button,
    Input,
    Label,
    Switch,
    Checkbox,
    Badge,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
    EmptyState,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Check,
    Copy,
    KeyRound,
    Loader2,
    Pencil,
    Plus,
    Save,
    Trash2,
    UserCheck,
    UserPlus,
    Users,
  },
  data() {
    return {
      users: [],
      me: null,
      editingUser: null,
      editForm: {
        userName: "",
        password: "",
        isAdmin: false,
        isActive: true,
        roles: [],
        permissionGrants: [],
      },
      permissionsFromRoles: [],
      roleCatalog: [],
      permissionCatalog: [],
      isEditDialogOpen: false,
      isLoading: false,
      savingUserId: null,
      canManageRoles: false,
      isInviteDialogOpen: false,
      tokens: [],
      tokensLoading: false,
      isCreatingToken: false,
      revokingTokenId: null,
      lastCreatedToken: null,
      copiedToken: null,
    };
  },
  computed: {
    activeUsersCount() {
      return this.users.filter((user) => user.isActive).length;
    },
    editingSelf() {
      return Boolean(this.editingUser && this.me && this.editingUser.id === this.me.id);
    },
    canToggleOwner() {
      return Boolean(this.me?.isAdmin);
    },
  },
  methods: {
    ...mapActions([
      "getMe",
      "getAdminUsers",
      "updateAdminUser",
      "getRolesCatalog",
      "getPermissionsCatalog",
      "getUserAccess",
      "putUserAccess",
      "getAllTokens",
      "createToken",
      "revokeToken",
    ]),

    roleLabel(user) {
      if (user.isAdmin) return this.$t("pages.usersPage.roleOwner");
      const roles = user.roles || [];
      if (roles.length) return roles.map((r) => this.localizedRoleName(r)).join(", ");
      return this.$t("nav.user");
    },

    localizedRoleName(roleName) {
      const key = `pages.usersPage.roleNames.${roleName}`;
      const label = this.$t(key);
      return label === key ? roleName : label;
    },

    localizedPermissionLabel(perm) {
      const key = `pages.usersPage.permissionLabels.${perm}`;
      const label = this.$t(key);
      return label === key ? perm : label;
    },

    isPermissionFromRole(perm) {
      return this.permissionsFromRoles.includes(perm);
    },

    isPermissionChecked(perm) {
      return this.isPermissionFromRole(perm) || this.editForm.permissionGrants.includes(perm);
    },

    toggleRole(roleName, enabled) {
      const set = new Set(this.editForm.roles);
      if (enabled) set.add(roleName);
      else set.delete(roleName);
      this.editForm.roles = Array.from(set);
      this.recomputePermissionsFromRoles();
    },

    recomputePermissionsFromRoles() {
      const fromRoles = new Set();
      for (const role of this.roleCatalog) {
        if (!this.editForm.roles.includes(role.name)) continue;
        for (const p of role.permissions || []) fromRoles.add(p);
      }
      this.permissionsFromRoles = Array.from(fromRoles);
      this.editForm.permissionGrants = this.editForm.permissionGrants.filter(
        (p) => !fromRoles.has(p),
      );
    },

    togglePermissionGrant(perm, enabled) {
      if (this.isPermissionFromRole(perm)) return;
      const set = new Set(this.editForm.permissionGrants);
      if (enabled) set.add(perm);
      else set.delete(perm);
      this.editForm.permissionGrants = Array.from(set);
    },

    async fetchUsers() {
      this.isLoading = true;
      await this.getAdminUsers({
        onSuccess: (response) => {
          this.users = response.data.value ?? response.data ?? [];
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
          this.me = response.data.value ?? response.data;
          this.canManageRoles = hasPermission(this.me, PERMS.RolesManage);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchCatalogs() {
      if (!this.canManageRoles) return;
      await this.getRolesCatalog({
        onSuccess: (response) => {
          this.roleCatalog = response.data.value ?? response.data ?? [];
        },
        onError: displaySonnerError,
      });
      await this.getPermissionsCatalog({
        onSuccess: (response) => {
          const data = response.data.value ?? response.data ?? {};
          this.permissionCatalog = data.assignable || data.all || [];
        },
        onError: displaySonnerError,
      });
    },

    async openEditDialog(user) {
      this.editingUser = user;
      this.editForm = {
        userName: user.userName || "",
        password: "",
        isAdmin: user.isAdmin,
        isActive: user.isActive,
        roles: [...(user.roles || [])],
        permissionGrants: [],
      };
      this.permissionsFromRoles = [];
      this.isEditDialogOpen = true;

      if (this.canManageRoles) {
        await this.getUserAccess({
          userId: user.id,
          onSuccess: (response) => {
            const access = response.data.value ?? response.data;
            this.editForm.roles = [...(access.roles || [])];
            this.editForm.permissionGrants = [...(access.permissionGrants || [])];
            this.permissionsFromRoles = [...(access.permissionsFromRoles || [])];
          },
          onError: displaySonnerError,
        });
      }
    },

    closeEditDialog() {
      this.isEditDialogOpen = false;
      this.editingUser = null;
    },

    async openInviteDialog() {
      this.isInviteDialogOpen = true;
      this.lastCreatedToken = null;
      await this.fetchTokens();
    },

    closeInviteDialog() {
      this.isInviteDialogOpen = false;
      this.lastCreatedToken = null;
      this.copiedToken = null;
    },

    async fetchTokens() {
      this.tokensLoading = true;
      await this.getAllTokens({
        onSuccess: (response) => {
          this.tokens = response.data.value ?? response.data ?? [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.tokensLoading = false;
    },

    async handleCreateToken() {
      this.isCreatingToken = true;
      await this.createToken({
        onSuccess: (response) => {
          const payload = response.data.value ?? response.data;
          this.lastCreatedToken = payload?.token ?? null;
          displaySonnerSuccess(this.$t("pages.usersPage.invite.tokenCreated"));
          this.fetchTokens();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreatingToken = false;
    },

    async copyToken(token) {
      try {
        await navigator.clipboard.writeText(token);
        this.copiedToken = token;
        displaySonnerSuccess(this.$t("pages.usersPage.invite.tokenCopied"));
        setTimeout(() => {
          if (this.copiedToken === token) this.copiedToken = null;
        }, 2000);
      } catch {
        displaySonnerError(this.$t("common.copyFailed"));
      }
    },

    async handleRevokeToken(token) {
      if (!token?.id || token.used) return;
      const ok = await confirm({
        title: this.$t("pages.usersPage.invite.revokeTitle"),
        description: this.$t("pages.usersPage.invite.revokeDescription"),
        confirmLabel: this.$t("pages.usersPage.invite.revokeAction"),
      });
      if (!ok) return;

      this.revokingTokenId = token.id;
      await this.revokeToken({
        tokenId: token.id,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.usersPage.invite.tokenRevoked"));
          if (this.lastCreatedToken && this.lastCreatedToken === token.token) {
            this.lastCreatedToken = null;
          }
          this.fetchTokens();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.revokingTokenId = null;
    },

    formatTokenDate(dateString) {
      if (!dateString) return "-";
      const formatted = this.formatStoreDateTime(dateString);
      return formatted === "-" ? "-" : formatted;
    },

    tokenHint(token) {
      const value = token?.token || "";
      if (value.length < 12) return value || "-";
      return `${value.slice(0, 8)}…${value.slice(-4)}`;
    },

    async saveIdentityChanges() {
      if (!this.editingUser) return;

      const userName = this.editForm.userName.trim();
      const password = this.editForm.password.trim();

      if (!userName) {
        displaySonnerError(this.$t("pages.usersPage.userNameEmptyError"));
        return;
      }

      this.savingUserId = this.editingUser.id;
      const payload = {
        userName,
        isActive: this.editForm.isActive,
      };
      if (this.canToggleOwner) {
        payload.isAdmin = this.editForm.isAdmin;
      }
      if (password) {
        payload.password = password;
      }

      await this.updateAdminUser({
        userId: this.editingUser.id,
        data: payload,
        onSuccess: async () => {
          if (this.canManageRoles) {
            await this.putUserAccess({
              userId: this.editingUser.id,
              data: {
                roles: this.editForm.roles,
                permissionGrants: this.editForm.permissionGrants,
              },
              onSuccess: () => {},
              onError: displaySonnerError,
            });
          }
          try {
            const { invalidateMeCache } = await import("@/main");
            invalidateMeCache();
          } catch { /* ignore */ }
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
  async mounted() {
    await this.fetchMe();
    await this.fetchCatalogs();
    await this.fetchUsers();
  },
};
</script>

<template>
  <div>
    <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
      <Button @click="openInviteDialog">
        <UserPlus class="size-4 mr-2" />
        {{ $t("pages.usersPage.invite.button") }}
      </Button>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="users.length === 0"
      :empty-icon="Users"
      :empty-title="$t('pages.usersPage.emptyTitle')"
      :empty-description="$t('pages.usersPage.emptyDescription')"
    >
      <template #empty-action>
        <Button @click="openInviteDialog">
          <UserPlus class="size-4 mr-2" />
          {{ $t("pages.usersPage.invite.button") }}
        </Button>
      </template>

      <div class="overflow-hidden rounded-lg border">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead class="w-24">ID</TableHead>
                <TableHead>{{ $t("common.name") }}</TableHead>
                <TableHead class="w-48">{{ $t("pages.usersPage.columnRole") }}</TableHead>
                <TableHead class="w-32">{{ $t("common.status") }}</TableHead>
                <TableHead class="w-32" />
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
                    {{ roleLabel(user) }}
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

    <Dialog
      :open="isInviteDialogOpen"
      @update:open="(v) => { if (!v) closeInviteDialog(); else isInviteDialogOpen = true; }"
    >
      <DialogContent size="3xl" class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-3xl">
        <DialogHeader class="shrink-0 space-y-1.5 border-b px-6 py-4 text-left">
          <DialogTitle>{{ $t("pages.usersPage.invite.title") }}</DialogTitle>
          <DialogDescription>
            {{ $t("pages.usersPage.invite.description") }}
          </DialogDescription>
        </DialogHeader>

        <div class="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5">
          <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <p class="text-sm text-muted-foreground sm:max-w-xl">
              {{ $t("pages.usersPage.invite.hint") }}
            </p>
            <Button class="shrink-0" :disabled="isCreatingToken" @click="handleCreateToken">
              <Loader2 v-if="isCreatingToken" class="mr-2 size-4 animate-spin" />
              <Plus v-else class="mr-2 size-4" />
              {{ $t("pages.usersPage.invite.createToken") }}
            </Button>
          </div>

          <div
            v-if="lastCreatedToken"
            class="flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3"
          >
            <div class="text-sm font-medium">
              {{ $t("pages.usersPage.invite.newTokenCreated") }}
            </div>
            <div class="flex flex-wrap items-center gap-2">
              <code class="min-w-0 flex-1 break-all font-mono text-xs">
                {{ lastCreatedToken }}
              </code>
              <Button variant="outline" size="sm" @click="copyToken(lastCreatedToken)">
                <Check v-if="copiedToken === lastCreatedToken" class="mr-1.5 size-3.5" />
                <Copy v-else class="mr-1.5 size-3.5" />
                {{ $t("common.copy") }}
              </Button>
            </div>
          </div>

          <EmptyState
            v-if="tokensLoading"
            loading
            size="sm"
          />

          <EmptyState
            v-else-if="tokens.length === 0"
            size="sm"
            :title="$t('pages.usersPage.invite.emptyTitle')"
            :description="$t('pages.usersPage.invite.emptyDescription')"
          >
            <template #icon>
              <KeyRound />
            </template>
            <Button class="mt-2" :disabled="isCreatingToken" @click="handleCreateToken">
              <Loader2 v-if="isCreatingToken" class="mr-2 size-4 animate-spin" />
              <Plus v-else class="mr-2 size-4" />
              {{ $t("pages.usersPage.invite.createToken") }}
            </Button>
          </EmptyState>

          <div v-else class="overflow-hidden rounded-lg border">
            <div class="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ $t("pages.usersPage.invite.columnToken") }}</TableHead>
                    <TableHead class="w-32">{{ $t("pages.usersPage.invite.columnStatus") }}</TableHead>
                    <TableHead class="w-40">{{ $t("pages.usersPage.invite.columnCreated") }}</TableHead>
                    <TableHead class="w-40">{{ $t("pages.usersPage.invite.columnUsed") }}</TableHead>
                    <TableHead>{{ $t("pages.usersPage.invite.columnUsedBy") }}</TableHead>
                    <TableHead class="w-24 text-right" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow v-for="token in tokens" :key="token.id">
                    <TableCell>
                      <code class="font-mono text-xs text-muted-foreground">{{ tokenHint(token) }}</code>
                    </TableCell>
                    <TableCell>
                      <Badge v-if="token.used" variant="secondary">
                        <UserCheck class="mr-1 size-3" />
                        {{ $t("pages.usersPage.invite.statusUsed") }}
                      </Badge>
                      <Badge v-else variant="default">
                        {{ $t("pages.usersPage.invite.statusActive") }}
                      </Badge>
                    </TableCell>
                    <TableCell class="text-sm text-muted-foreground">
                      {{ formatTokenDate(token.createdAt) }}
                    </TableCell>
                    <TableCell class="text-sm text-muted-foreground">
                      {{ formatTokenDate(token.usedAt) }}
                    </TableCell>
                    <TableCell>
                      {{ token.usedByUserName || "-" }}
                    </TableCell>
                    <TableCell class="text-right">
                      <div v-if="!token.used" class="inline-flex items-center justify-end gap-0.5">
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8"
                          :title="$t('common.copy')"
                          @click="copyToken(token.token)"
                        >
                          <Check v-if="copiedToken === token.token" class="size-4" />
                          <Copy v-else class="size-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8 text-destructive hover:text-destructive"
                          :disabled="revokingTokenId === token.id"
                          :title="$t('pages.usersPage.invite.revokeAction')"
                          @click="handleRevokeToken(token)"
                        >
                          <Loader2 v-if="revokingTokenId === token.id" class="size-4 animate-spin" />
                          <Trash2 v-else class="size-4" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          </div>
        </div>

        <DialogFooter class="shrink-0 border-t px-6 py-4 sm:justify-end">
          <Button variant="outline" @click="closeInviteDialog">
            {{ $t("common.close") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="isEditDialogOpen">
      <DialogContent size="2xl">
        <DialogHeader>
          <DialogTitle>{{ $t("pages.usersPage.editUser") }}</DialogTitle>
          <DialogDescription>
            {{ $t("pages.usersPage.editUserDescription") }}
          </DialogDescription>
        </DialogHeader>

        <div class="grid max-h-[70vh] gap-4 overflow-y-auto pr-1">
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
                <div class="text-sm font-medium">{{ $t("pages.usersPage.ownerLabel") }}</div>
                <div class="text-xs text-muted-foreground">{{ $t("pages.usersPage.ownerHint") }}</div>
              </div>
              <Switch
                :model-value="editForm.isAdmin"
                :disabled="!canToggleOwner || editingSelf || savingUserId === editingUser?.id"
                @update:model-value="(v) => (editForm.isAdmin = v)"
              />
            </div>
            <div class="flex items-center justify-between gap-4">
              <div>
                <div class="text-sm font-medium">{{ $t("pages.usersPage.activeAccountLabel") }}</div>
                <div class="text-xs text-muted-foreground">{{ $t("pages.usersPage.activeAccountHint") }}</div>
              </div>
              <Switch
                :model-value="editForm.isActive"
                :disabled="editingSelf || savingUserId === editingUser?.id"
                @update:model-value="(v) => (editForm.isActive = v)"
              />
            </div>
          </div>

          <div v-if="canManageRoles" class="grid gap-3 rounded-lg border p-3">
            <h3 class="text-sm font-semibold">{{ $t("pages.usersPage.rolesTitle") }}</h3>
            <div class="grid gap-2">
              <label
                v-for="role in roleCatalog"
                :key="role.name"
                class="flex cursor-pointer items-center gap-2 text-sm"
              >
                <Checkbox
                  :model-value="editForm.roles.includes(role.name)"
                  :disabled="savingUserId === editingUser?.id || (!canToggleOwner && role.name === 'Owner')"
                  @update:model-value="(v) => toggleRole(role.name, v)"
                />
                <span>{{ localizedRoleName(role.name) }}</span>
              </label>
            </div>
          </div>

          <div v-if="canManageRoles" class="grid gap-3 rounded-lg border p-3">
            <h3 class="text-sm font-semibold">{{ $t("pages.usersPage.permissionsTitle") }}</h3>
            <p class="text-xs text-muted-foreground">{{ $t("pages.usersPage.permissionsHint") }}</p>
            <div class="grid gap-2 sm:grid-cols-2">
              <label
                v-for="perm in permissionCatalog"
                :key="perm"
                class="flex cursor-pointer items-start gap-2 text-sm"
              >
                <Checkbox
                  class="mt-0.5"
                  :model-value="isPermissionChecked(perm)"
                  :disabled="isPermissionFromRole(perm) || savingUserId === editingUser?.id"
                  @update:model-value="(v) => togglePermissionGrant(perm, v)"
                />
                <span>
                  <span>{{ localizedPermissionLabel(perm) }}</span>
                  <span
                    v-if="isPermissionFromRole(perm)"
                    class="block text-xs text-muted-foreground"
                  >{{ $t("pages.usersPage.fromRole") }}</span>
                </span>
              </label>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" @click="closeEditDialog">{{ $t("common.cancel") }}</Button>
          <Button :disabled="savingUserId === editingUser?.id" @click="saveIdentityChanges">
            <Loader2 v-if="savingUserId === editingUser?.id" class="mr-2 size-4 animate-spin" />
            <Save v-else class="mr-2 size-4" />
            {{ $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
