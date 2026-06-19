<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
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
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
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
      drafts: {},
      editingUser: null,
      editForm: {
        userName: "",
        password: "",
      },
      isEditDialogOpen: false,
      isLoading: false,
      savingUserId: null,
      breadcrumbs: [{ label: "Пользователи" }],
    };
  },
  computed: {
    activeUsersCount() {
      return this.users.filter((user) => user.isActive).length;
    },
    hasIdentityChanges() {
      if (!this.editingUser) return false;
      return (
        this.editForm.userName.trim() !== (this.editingUser.userName || "") ||
        this.editForm.password.trim().length > 0
      );
    },
  },
  methods: {
    ...mapActions(["getAdminUsers", "updateAdminUser"]),

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
      };
      this.isEditDialogOpen = true;
    },

    closeEditDialog() {
      this.isEditDialogOpen = false;
      this.editingUser = null;
      this.editForm = {
        userName: "",
        password: "",
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
          displaySonnerSuccess("Пользователь обновлён");
        },
        onError: (error) => {
          displaySonnerError(error);
          this.createDrafts(this.users);
        },
      });
      this.savingUserId = null;
    },

    async saveIdentityChanges() {
      if (!this.editingUser || !this.hasIdentityChanges) return;

      const userName = this.editForm.userName.trim();
      const password = this.editForm.password.trim();

      if (!userName) {
        displaySonnerError("Имя пользователя не может быть пустым");
        return;
      }

      this.savingUserId = this.editingUser.id;
      const payload = { userName };
      if (password) {
        payload.password = password;
      }

      await this.updateAdminUser({
        userId: this.editingUser.id,
        data: payload,
        onSuccess: async (response) => {
          await this.fetchUsers();
          this.closeEditDialog();
          displaySonnerSuccess(password ? "Пользователь и пароль обновлены" : "Имя пользователя обновлено");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.savingUserId = null;
    },
  },
  mounted() {
    this.fetchUsers();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Пользователи"
      :description="`Активных пользователей: ${activeUsersCount}`"
    />

    <PageSection
      :loading="isLoading"
      :empty="users.length === 0"
      :empty-icon="Users"
      empty-title="Нет пользователей"
      empty-description="Пользователи появятся здесь после регистрации по приглашению"
    >
      <div class="overflow-hidden rounded-lg border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-24">ID</TableHead>
              <TableHead>Имя</TableHead>
              <TableHead class="w-32">Роль</TableHead>
              <TableHead class="w-36">Админ</TableHead>
              <TableHead class="w-36">Активен</TableHead>
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
                <Badge :variant="drafts[user.id].isAdmin ? 'default' : 'secondary'">
                  {{ drafts[user.id].isAdmin ? "Админ" : "Пользователь" }}
                </Badge>
              </TableCell>
              <TableCell>
                <Switch
                  :model-value="drafts[user.id].isAdmin"
                  :disabled="savingUserId === user.id"
                  @update:model-value="drafts[user.id].isAdmin = $event"
                />
              </TableCell>
              <TableCell>
                <Switch
                  :model-value="drafts[user.id].isActive"
                  :disabled="savingUserId === user.id"
                  @update:model-value="drafts[user.id].isActive = $event"
                />
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
                    <span class="sr-only">Редактировать пользователя</span>
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="savingUserId === user.id || !hasRoleChanges(user)"
                    @click="saveRoleChanges(user)"
                  >
                    <Loader2
                      v-if="savingUserId === user.id"
                      class="size-4 animate-spin"
                    />
                    <Save v-else class="size-4" />
                    <span class="sr-only">Сохранить роль и статус</span>
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </PageSection>

    <Dialog v-model:open="isEditDialogOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Редактировать пользователя</DialogTitle>
          <DialogDescription>
            Измените имя пользователя или задайте новый пароль.
          </DialogDescription>
        </DialogHeader>

        <div class="grid gap-4">
          <div class="grid gap-2">
            <Label for="admin-user-name">Имя пользователя</Label>
            <Input
              id="admin-user-name"
              v-model="editForm.userName"
              :disabled="savingUserId === editingUser?.id"
              autocomplete="off"
            />
          </div>

          <div class="grid gap-2">
            <Label for="admin-user-password">Новый пароль</Label>
            <div class="relative">
              <KeyRound class="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                id="admin-user-password"
                v-model="editForm.password"
                :disabled="savingUserId === editingUser?.id"
                class="pl-9"
                type="password"
                autocomplete="new-password"
                placeholder="Оставьте пустым, если менять не нужно"
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
            Отмена
          </Button>
          <Button
            :disabled="savingUserId === editingUser?.id || !hasIdentityChanges"
            @click="saveIdentityChanges"
          >
            <Loader2
              v-if="savingUserId === editingUser?.id"
              class="size-4 animate-spin"
            />
            <span v-else>Сохранить</span>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
