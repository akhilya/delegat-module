<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { PageHeader } from "@/components/ui/page-header";
import { EmptyState } from "@/components/ui/empty-state";
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
import { Loader2, Save, Users } from "lucide-vue-next";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Switch,
    Badge,
    PageHeader,
    EmptyState,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Loader2,
    Save,
    Users,
  },
  data() {
    return {
      users: [],
      drafts: {},
      isLoading: false,
      savingUserId: null,
      breadcrumbs: [{ label: "Пользователи" }],
    };
  },
  computed: {
    activeUsersCount() {
      return this.users.filter((user) => user.isActive).length;
    },
  },
  methods: {
    ...mapActions(["getAdminUsers", "updateAdminUser"]),

    createDrafts(users) {
      this.drafts = users.reduce((acc, user) => {
        acc[user.id] = {
          userName: user.userName || "",
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

    hasChanges(user) {
      const draft = this.drafts[user.id];
      if (!draft) return false;

      return (
        draft.userName.trim() !== (user.userName || "") ||
        draft.isAdmin !== user.isAdmin ||
        draft.isActive !== user.isActive
      );
    },

    async saveUser(user) {
      const draft = this.drafts[user.id];
      if (!draft || !this.hasChanges(user)) return;

      if (!draft.userName.trim()) {
        displaySonnerError("Имя пользователя не может быть пустым");
        return;
      }

      this.savingUserId = user.id;
      await this.updateAdminUser({
        userId: user.id,
        data: {
          userName: draft.userName.trim(),
          isAdmin: draft.isAdmin,
          isActive: draft.isActive,
        },
        onSuccess: (response) => {
          const updatedUser = response.data.value;
          const index = this.users.findIndex((item) => item.id === updatedUser.id);
          if (index !== -1) {
            this.users.splice(index, 1, updatedUser);
          }
          this.createDrafts(this.users);
          displaySonnerSuccess("Пользователь обновлён");
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

    <div class="border rounded-lg h-full w-full grow mb-4 p-4">
      <EmptyState v-if="isLoading" loading />
      <EmptyState
        v-else-if="users.length === 0"
        title="Нет пользователей"
        description="Пользователи появятся здесь после регистрации по приглашению"
      >
        <template #icon>
          <Users class="size-12 mb-4 text-muted-foreground" />
        </template>
      </EmptyState>

      <div v-else class="overflow-hidden rounded-lg border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-24">ID</TableHead>
              <TableHead>Имя</TableHead>
              <TableHead class="w-32">Роль</TableHead>
              <TableHead class="w-36">Админ</TableHead>
              <TableHead class="w-36">Активен</TableHead>
              <TableHead class="w-24"></TableHead>
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
                <Input
                  v-model="drafts[user.id].userName"
                  :disabled="savingUserId === user.id"
                  class="max-w-sm"
                />
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
              <TableCell class="text-right">
                <Button
                  variant="ghost"
                  size="sm"
                  :disabled="savingUserId === user.id || !hasChanges(user)"
                  @click="saveUser(user)"
                >
                  <Loader2
                    v-if="savingUserId === user.id"
                    class="size-4 animate-spin"
                  />
                  <Save v-else class="size-4" />
                </Button>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>
  </Page>
</template>
