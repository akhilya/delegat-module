<script>
import { Spinner } from "@/components/ui/spinner";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogHeaderClose,
  DialogTitle,
} from "@/components/ui/dialog";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  AlertTriangle,
  Check,
  Copy,
  KeyRound,
  Plus,
  RefreshCw,
  Save,
  Trash2,
  User,
  UserCheck,
  UserCog,
  UserPlus,
  X,
} from "lucide-vue-next";
import { ASSIGNABLE_PERMISSIONS, hasPermission, mergeAssignableCatalog, PERMS } from "@/lib/permissions";
import { isOwnerRoleLocked, isUserAccessRowLocked } from "@/lib/userAccessEdit";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { newPasswordFieldsIssue } from "@/lib/authValidation";
import { buildInviteSignUpUrl } from "@/lib/inviteSignUpUrl";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { confirm, isConfirmOverlayActive } from "@/composables/useConfirm";

export default {
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } =
      useStoreDateTime();
    loadMemberships();
    return { formatStoreDateTime };
  },
  components: {
    Spinner,
    PageSection,
    Button,
    Input,
    PasswordInput,
    FieldGroup,
    SettingsFormField,
    Switch,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    Dialog,
    DialogClose,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogHeaderClose,
    DialogTitle,
    Check,
    Copy,
    KeyRound,
    Plus,
    RefreshCw,
    Save,
    Trash2,
    User,
    UserCheck,
    UserCog,
    UserPlus,
    X,
  },
  data() {
    return {
      users: [],
      loadError: false,
      me: null,
      FIELD_LIMITS,
      editingUser: null,
      editForm: {
        userName: "",
        password: "",
        confirmPassword: "",
        isAdmin: false,
        isActive: true,
        roles: [],
        permissionGrants: [],
      },
      permissionsFromRoles: [],
      roleCatalog: [],
      permissionCatalog: [...ASSIGNABLE_PERMISSIONS],
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
    editingSelf() {
      return Boolean(
        this.editingUser && this.me && this.editingUser.id === this.me.id,
      );
    },
    canToggleOwner() {
      return Boolean(this.me?.isAdmin);
    },
    isSavingEdit() {
      return Boolean(
        this.editingUser && this.savingUserId === this.editingUser.id,
      );
    },
    editPasswordIssue() {
      const password = this.editForm.password.trim();
      const confirm = this.editForm.confirmPassword.trim();
      if (!password && !confirm) return null;
      return newPasswordFieldsIssue(password, confirm, this.$t, {
        optional: true,
        requirePolicy: true,
      });
    },
    ownerToggleLocked() {
      return !this.canToggleOwner || this.editingSelf;
    },
    activeToggleLocked() {
      return this.editingSelf;
    },
    accessEditLocked() {
      return isUserAccessRowLocked({
        canManageRoles: this.canManageRoles,
        editingSelf: this.editingSelf,
        saving: this.isSavingEdit,
      });
    },
    activeTokens() {
      return this.tokens.filter((token) => !token.used);
    },
    usedTokens() {
      return this.tokens.filter((token) => token.used);
    },
    emptyListIcon() {
      return this.loadError ? AlertTriangle : UserPlus;
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
      if (roles.length)
        return roles.map((r) => this.localizedRoleName(r)).join(", ");
      return this.$t("nav.user");
    },

    userCaption(user) {
      const status = user.isActive
        ? this.$t("pages.usersPage.statusActive")
        : this.$t("pages.usersPage.statusDisabled");
      const base = `${this.roleLabel(user)} · ${status}`;
      return this.isSelf(user)
        ? `${base} · ${this.$t("pages.usersPage.you")}`
        : base;
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
      return (
        this.isPermissionFromRole(perm) ||
        this.editForm.permissionGrants.includes(perm)
      );
    },

    isSelf(user) {
      return Boolean(this.me && user?.id === this.me.id);
    },

    toggleRole(roleName, enabled) {
      if (this.isRoleRowDisabled(roleName)) return;
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
      if (this.accessEditLocked || this.isPermissionFromRole(perm)) return;
      const set = new Set(this.editForm.permissionGrants);
      if (enabled) set.add(perm);
      else set.delete(perm);
      this.editForm.permissionGrants = Array.from(set);
    },

    toggleOwner() {
      if (this.ownerToggleLocked || this.isSavingEdit) return;
      this.editForm.isAdmin = !this.editForm.isAdmin;
      if (this.editForm.isAdmin) {
        const set = new Set(this.editForm.roles);
        set.add("Owner");
        this.editForm.roles = Array.from(set);
        this.recomputePermissionsFromRoles();
      }
    },

    toggleActive() {
      if (this.activeToggleLocked || this.isSavingEdit) return;
      this.editForm.isActive = !this.editForm.isActive;
    },

    isRoleRowDisabled(roleName) {
      if (this.accessEditLocked) return true;
      if (roleName !== "Owner") return false;
      return isOwnerRoleLocked({
        editingSelf: this.editingSelf,
        canToggleOwner: this.canToggleOwner,
        targetIsAdmin: this.editForm.isAdmin,
      });
    },

    async fetchUsers() {
      this.isLoading = true;
      this.loadError = false;
      await this.getAdminUsers({
        onSuccess: (response) => {
          const list = response.data.value ?? response.data ?? [];
          this.users = Array.isArray(list) ? list : [];
        },
        onError: (error) => {
          this.users = [];
          this.loadError = true;
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
          this.permissionCatalog = mergeAssignableCatalog(
            data.assignable || data.all || [],
          );
        },
        onError: (error) => {
          this.permissionCatalog = mergeAssignableCatalog([]);
          displaySonnerError(error);
        },
      });
    },

    async openEditDialog(user) {
      this.editingUser = user;
      this.editForm = {
        userName: user.userName || "",
        password: "",
        confirmPassword: "",
        isAdmin: user.isAdmin,
        isActive: user.isActive,
        roles: [...(user.roles || [])],
        permissionGrants: [],
      };
      this.permissionsFromRoles = [];
      this.isEditDialogOpen = true;
      this.$nextTick(() => document.getElementById("admin-user-name")?.focus());

      if (this.canManageRoles) {
        await this.getUserAccess({
          userId: user.id,
          onSuccess: (response) => {
            const access = response.data.value ?? response.data;
            this.editForm.roles = [...(access.roles || [])];
            this.editForm.permissionGrants = [
              ...(access.permissionGrants || []),
            ];
            this.permissionsFromRoles = [
              ...(access.permissionsFromRoles || []),
            ];
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
      if (isConfirmOverlayActive()) return;
      this.isInviteDialogOpen = false;
      this.lastCreatedToken = null;
      this.copiedToken = null;
    },

    onInviteDialogOpenChange(open) {
      if (!open && isConfirmOverlayActive()) {
        this.isInviteDialogOpen = true;
        return;
      }
      if (!open) this.closeInviteDialog();
      else this.isInviteDialogOpen = true;
    },

    async fetchTokens() {
      const silent = this.tokens.length > 0 || Boolean(this.lastCreatedToken);
      if (!silent) this.tokensLoading = true;
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

    inviteLink(token) {
      return buildInviteSignUpUrl(window.location.origin, token);
    },

    async copyToken(token) {
      try {
        await navigator.clipboard.writeText(this.inviteLink(token) || token);
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
      if (!dateString) return "";
      const formatted = this.formatStoreDateTime(dateString);
      return formatted === "-" ? "" : formatted;
    },

    tokenHint(token) {
      const value = token?.token || "";
      if (value.length < 12) return value || "—";
      return `${value.slice(0, 8)}…${value.slice(-4)}`;
    },

    tokenCaption(token) {
      if (token.used) {
        const who = token.usedByUserName || this.$t("common.emDash");
        const when = this.formatTokenDate(token.usedAt);
        return when
          ? this.$t("pages.usersPage.invite.usedCaption", {
              name: who,
              date: when,
            })
          : this.$t("pages.usersPage.invite.usedBy", { name: who });
      }
      const when = this.formatTokenDate(token.createdAt);
      return when
        ? this.$t("pages.usersPage.invite.createdCaption", { date: when })
        : this.$t("pages.usersPage.invite.statusActive");
    },

    async saveIdentityChanges() {
      if (!this.editingUser) return;

      const userName = this.editForm.userName.trim();
      const password = this.editForm.password.trim();
      const confirmPassword = this.editForm.confirmPassword.trim();

      const nameIssue = fieldLimitIssue(userName, FIELD_LIMITS.userNameStored);
      if (nameIssue === "empty") {
        displaySonnerError(this.$t("pages.usersPage.userNameEmptyError"));
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.userNameStored }),
        );
        return;
      }
      if (password || confirmPassword) {
        const issue = newPasswordFieldsIssue(
          password,
          confirmPassword,
          this.$t,
          { optional: true, requirePolicy: true },
        );
        if (issue) {
          displaySonnerError(issue.password || issue.confirm);
          return;
        }
      }
      if (
        password &&
        fieldLimitIssue(password, FIELD_LIMITS.password, {
          required: false,
        }) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.password }),
        );
        return;
      }

      this.savingUserId = this.editingUser.id;
      const payload = {
        userName,
        isActive: this.editForm.isActive,
      };
      if (this.canToggleOwner && !this.editingSelf) {
        payload.isAdmin = this.editForm.isAdmin;
      }
      if (password) {
        payload.password = password;
      }

      await this.updateAdminUser({
        userId: this.editingUser.id,
        data: payload,
        onSuccess: async () => {
          if (this.canManageRoles && !this.editingSelf) {
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
          } catch {
            /* ignore */
          }
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
  <div class="flex w-full max-w-xl flex-col pb-10">
    <div
      v-if="!isLoading && users.length > 0"
      class="mb-4 flex shrink-0 flex-wrap items-center justify-end gap-2"
    >
      <Button @click="openInviteDialog">
        <UserPlus class="size-4 mr-2" />
        {{ $t("pages.usersPage.invite.button") }}
      </Button>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="loadError || users.length === 0"
      :empty-icon="emptyListIcon"
      :empty-title="
        loadError
          ? $t('pages.usersPage.loadErrorTitle')
          : $t('pages.usersPage.emptyTitle')
      "
      :empty-description="
        loadError
          ? $t('pages.usersPage.loadErrorDescription')
          : $t('pages.usersPage.emptyDescription')
      "
    >
      <template #empty-action>
        <Button v-if="loadError" variant="outline" @click="fetchUsers">
          <RefreshCw class="mr-2 size-4" />
          {{ $t("common.retry") }}
        </Button>
        <Button v-else @click="openInviteDialog">
          <UserPlus class="mr-2 size-4" />
          {{ $t("pages.usersPage.invite.button") }}
        </Button>
      </template>

      <InsetGroup>
        <InsetGroupItem
          v-for="user in users"
          :key="user.id"
          show-chevron
          :disabled="savingUserId === user.id"
          :class="!user.isActive ? 'text-muted-foreground' : ''"
          @click="openEditDialog(user)"
        >
          <template #leading>
            <UserCog v-if="user.isAdmin" class="size-4" />
            <User v-else class="size-4" />
          </template>
          <p class="truncate">{{ user.userName }}</p>
          <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
            {{ userCaption(user) }}
          </p>
        </InsetGroupItem>
      </InsetGroup>
    </PageSection>

    <Dialog :open="isInviteDialogOpen" @update:open="onInviteDialogOpenChange">
      <DialogContent
        size="lg"
        :show-close="false"
        class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <div class="flex min-h-8 items-center gap-2">
            <DialogTitle class="min-w-0 flex-1 truncate">
              {{ $t("pages.usersPage.invite.title") }}
            </DialogTitle>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              class="size-8 shrink-0 text-muted-foreground"
              :aria-label="$t('common.close')"
              @click="closeInviteDialog"
            >
              <X class="size-4" />
            </Button>
          </div>
          <DialogDescription>
            {{ $t("pages.usersPage.invite.description") }}
          </DialogDescription>
        </DialogHeader>

        <div class="min-h-0 flex-1 space-y-6 overflow-y-auto px-6 py-5">
          <InsetGroup>
            <InsetGroupItem
              :disabled="isCreatingToken"
              @click="handleCreateToken"
            >
              <template #leading>
                <Spinner v-if="isCreatingToken" />
                <Plus v-else class="size-4" />
              </template>
              {{ $t("pages.usersPage.invite.createToken") }}
            </InsetGroupItem>
          </InsetGroup>

          <section v-if="lastCreatedToken">
            <InsetGroupLabel>{{
              $t("pages.usersPage.invite.newTokenCreated")
            }}</InsetGroupLabel>
            <InsetGroup>
              <InsetGroupItem as="div">
                <template #leading>
                  <KeyRound class="size-4" />
                </template>
                <code class="block break-all font-mono text-xs">
                  {{ inviteLink(lastCreatedToken) }}
                </code>
                <template #trailing>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    @click.stop="copyToken(lastCreatedToken)"
                  >
                    <Check
                      v-if="copiedToken === lastCreatedToken"
                      class="mr-1.5 size-3.5"
                    />
                    <Copy v-else class="mr-1.5 size-3.5" />
                    {{ $t("common.copy") }}
                  </Button>
                </template>
              </InsetGroupItem>
            </InsetGroup>
          </section>

          <div
            v-if="tokensLoading"
            class="flex min-h-32 items-center justify-center"
          >
            <Spinner size="lg" class="text-muted-foreground" />
          </div>

          <template v-else-if="tokens.length > 0">
            <section v-if="activeTokens.length">
              <InsetGroupLabel>{{
                $t("pages.usersPage.invite.tokensActiveLabel")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem
                  v-for="token in activeTokens"
                  :key="token.id"
                  as="div"
                >
                  <template #leading>
                    <KeyRound class="size-4" />
                  </template>
                  <p class="truncate font-mono text-xs" :title="token.token">
                    {{ tokenHint(token) }}
                  </p>
                  <p
                    class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
                  >
                    {{ tokenCaption(token) }}
                  </p>
                  <template #trailing>
                    <div class="flex items-center gap-0.5">
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        class="size-8"
                        :title="$t('common.copy')"
                        :aria-label="$t('common.copy')"
                        @click.stop="copyToken(token.token)"
                      >
                        <Check
                          v-if="copiedToken === token.token"
                          class="size-4"
                        />
                        <Copy v-else class="size-4" />
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                        :disabled="revokingTokenId === token.id"
                        :title="$t('pages.usersPage.invite.revokeAction')"
                        :aria-label="$t('pages.usersPage.invite.revokeAction')"
                        @click.stop="handleRevokeToken(token)"
                      >
                        <Spinner v-if="revokingTokenId === token.id" />
                        <Trash2 v-else class="size-4" />
                      </Button>
                    </div>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="usedTokens.length">
              <InsetGroupLabel>{{
                $t("pages.usersPage.invite.tokensUsedLabel")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem
                  v-for="token in usedTokens"
                  :key="token.id"
                  as="div"
                >
                  <template #leading>
                    <KeyRound class="size-4 text-muted-foreground" />
                  </template>
                  <p class="truncate font-mono text-xs text-muted-foreground">
                    {{ tokenHint(token) }}
                  </p>
                  <p
                    class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
                  >
                    {{ tokenCaption(token) }}
                  </p>
                </InsetGroupItem>
              </InsetGroup>
            </section>
          </template>
        </div>

        <DialogFooter variant="bar">
          <Button type="button" variant="outline" @click="closeInviteDialog">
            {{ $t("common.close") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="isEditDialogOpen">
      <DialogContent
        size="lg"
        :show-close="false"
        class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <div class="flex min-h-8 items-center gap-2">
            <DialogTitle class="min-w-0 flex-1 truncate">
              {{ $t("pages.usersPage.editUser") }}
            </DialogTitle>
            <DialogHeaderClose />
          </div>
          <DialogDescription>
            {{ $t("pages.usersPage.editUserDescription") }}
          </DialogDescription>
        </DialogHeader>

        <form
          id="edit-user-form"
          class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
          @submit.prevent="saveIdentityChanges"
        >
          <div class="flex flex-col gap-6">
            <FieldGroup class="gap-5">
              <SettingsFormField
                :label="$t('auth.userName')"
                label-for="admin-user-name"
              >
                <Input
                  id="admin-user-name"
                  v-model="editForm.userName"
                  :disabled="isSavingEdit"
                  autocomplete="off"
                  :maxlength="FIELD_LIMITS.userNameStored"
                />
              </SettingsFormField>

              <SettingsFormField
                :label="$t('pages.usersPage.newPasswordLabel')"
                label-for="admin-user-password"
                :error="editPasswordIssue?.password"
              >
                <PasswordInput
                  id="admin-user-password"
                  v-model="editForm.password"
                  :disabled="isSavingEdit"
                  autocomplete="new-password"
                  :placeholder="$t('pages.usersPage.newPasswordPlaceholder')"
                  :maxlength="FIELD_LIMITS.password"
                />
              </SettingsFormField>

              <SettingsFormField
                :label="$t('auth.confirmPassword')"
                label-for="admin-user-password-confirm"
                :error="editPasswordIssue?.confirm"
              >
                <PasswordInput
                  id="admin-user-password-confirm"
                  v-model="editForm.confirmPassword"
                  :disabled="isSavingEdit"
                  autocomplete="new-password"
                  :placeholder="$t('pages.usersPage.newPasswordPlaceholder')"
                  :maxlength="FIELD_LIMITS.password"
                />
              </SettingsFormField>
            </FieldGroup>

            <InsetGroup>
              <InsetGroupItem
                :as="ownerToggleLocked ? 'div' : 'button'"
                :disabled="!ownerToggleLocked && isSavingEdit"
                :aria-pressed="editForm.isAdmin"
                @click="toggleOwner"
              >
                <template #leading>
                  <UserCog class="size-4" />
                </template>
                <p>{{ $t("pages.usersPage.ownerLabel") }}</p>
                <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                  {{
                    editingSelf
                      ? $t("pages.usersPage.cannotChangeSelfAdmin")
                      : $t("pages.usersPage.ownerHint")
                  }}
                </p>
                <template #trailing>
                  <Switch
                    :model-value="editForm.isAdmin"
                    :disabled="ownerToggleLocked || isSavingEdit"
                    tabindex="-1"
                    class="pointer-events-none"
                  />
                </template>
              </InsetGroupItem>
              <InsetGroupItem
                :as="activeToggleLocked ? 'div' : 'button'"
                :disabled="!activeToggleLocked && isSavingEdit"
                :aria-pressed="editForm.isActive"
                @click="toggleActive"
              >
                <template #leading>
                  <UserCheck class="size-4" />
                </template>
                <p>{{ $t("pages.usersPage.activeAccountLabel") }}</p>
                <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                  {{ $t("pages.usersPage.activeAccountHint") }}
                </p>
                <template #trailing>
                  <Switch
                    :model-value="editForm.isActive"
                    :disabled="activeToggleLocked || isSavingEdit"
                    tabindex="-1"
                    class="pointer-events-none"
                  />
                </template>
              </InsetGroupItem>
            </InsetGroup>

            <section v-if="canManageRoles">
              <InsetGroupLabel>{{
                $t("pages.usersPage.rolesTitle")
              }}</InsetGroupLabel>
              <p
                v-if="editingSelf"
                class="mb-2 px-1 text-xs text-muted-foreground"
              >
                {{ $t("pages.usersPage.cannotChangeSelfAccess") }}
              </p>
              <InsetGroup>
                <InsetGroupItem
                  v-for="role in roleCatalog"
                  :key="role.name"
                  :as="isRoleRowDisabled(role.name) ? 'div' : 'button'"
                  :disabled="isRoleRowDisabled(role.name)"
                  :aria-pressed="editForm.roles.includes(role.name)"
                  @click="
                    toggleRole(role.name, !editForm.roles.includes(role.name))
                  "
                >
                  {{ localizedRoleName(role.name) }}
                  <template #trailing>
                    <Check
                      v-if="editForm.roles.includes(role.name)"
                      class="size-4 text-primary"
                    />
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </section>

            <section v-if="canManageRoles">
              <InsetGroupLabel>{{
                $t("pages.usersPage.permissionsTitle")
              }}</InsetGroupLabel>
              <InsetGroup>
                <InsetGroupItem
                  v-for="perm in permissionCatalog"
                  :key="perm"
                  :as="
                    accessEditLocked || isPermissionFromRole(perm)
                      ? 'div'
                      : 'button'
                  "
                  :disabled="accessEditLocked || isPermissionFromRole(perm)"
                  :aria-pressed="isPermissionChecked(perm)"
                  @click="
                    togglePermissionGrant(perm, !isPermissionChecked(perm))
                  "
                >
                  <p>{{ localizedPermissionLabel(perm) }}</p>
                  <p
                    v-if="isPermissionFromRole(perm)"
                    class="mt-0.5 text-xs font-normal text-muted-foreground"
                  >
                    {{ $t("pages.usersPage.fromRole") }}
                  </p>
                  <template #trailing>
                    <Check
                      v-if="isPermissionChecked(perm)"
                      class="size-4"
                      :class="
                        isPermissionFromRole(perm)
                          ? 'text-muted-foreground'
                          : 'text-primary'
                      "
                    />
                  </template>
                </InsetGroupItem>
              </InsetGroup>
              <p class="mt-2 px-1 text-xs text-muted-foreground">
                {{ $t("pages.usersPage.permissionsHint") }}
              </p>
            </section>
          </div>
        </form>

        <DialogFooter variant="bar">
          <DialogClose as-child>
            <Button type="button" variant="outline">{{
              $t("common.cancel")
            }}</Button>
          </DialogClose>
          <Button type="submit" form="edit-user-form" :disabled="isSavingEdit || Boolean(editPasswordIssue)">
            <Spinner v-if="isSavingEdit" class="mr-2" />
            <Save v-else class="mr-2 size-4" />
            {{ $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
