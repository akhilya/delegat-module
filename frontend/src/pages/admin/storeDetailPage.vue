<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Store, UserPlus, UserMinus, Loader2, Users } from "lucide-vue-next";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PAGE_ADMIN_STORES } from "@/router";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    Store,
    UserPlus,
    UserMinus,
    Loader2,
    Users,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectLabel,
    SelectTrigger,
    SelectValue,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
  },
  data() {
    return {
      store: null,
      allUsers: [],
      isLoading: false,
      isAddingMember: false,
      removingUserId: null,
      showAddDialog: false,
      selectedUserId: null,
    };
  },
  computed: {
    storeId() {
      return parseInt(this.$route.params.id);
    },
    availableUsers() {
      if (!this.store || !this.allUsers.length) return [];
      const memberIds = this.store.members.map(m => m.id);
      return this.allUsers.filter(u => !memberIds.includes(u.id));
    },
    breadcrumbs() {
      const items = [
        { label: "Магазины", to: PAGE_ADMIN_STORES },
      ];
      if (this.store) {
        items.push({ label: this.store.name });
      }
      return items;
    },
  },
  methods: {
    ...mapActions(["getStoreDetail", "getAllUsers", "addMember", "removeMember"]),

    async fetchStore() {
      this.isLoading = true;
      await this.getStoreDetail({
        id: this.storeId,
        onSuccess: (response) => {
          this.store = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async fetchUsers() {
      await this.getAllUsers({
        onSuccess: (response) => {
          this.allUsers = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async handleAddMember() {
      if (!this.selectedUserId) {
        displaySonnerError("Выберите пользователя");
        return;
      }

      this.isAddingMember = true;
      await this.addMember({
        storeId: this.storeId,
        userId: parseInt(this.selectedUserId),
        onSuccess: (response) => {
          this.store = response.data.value;
          displaySonnerSuccess("Участник добавлен");
          this.showAddDialog = false;
          this.selectedUserId = null;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isAddingMember = false;
    },

    async handleRemoveMember(userId) {
      this.removingUserId = userId;
      await this.removeMember({
        storeId: this.storeId,
        userId,
        onSuccess: (response) => {
          this.store = response.data.value;
          displaySonnerSuccess("Участник удалён");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.removingUserId = null;
    },
  },
  mounted() {
    this.fetchStore();
    this.fetchUsers();
  },
};
</script>

<template>
  <Page>
    <NavigationBar />
    <div class="">
      <Breadcrumbs :items="breadcrumbs" />

      <div v-if="isLoading" class="flex justify-center py-12">
        <Loader2 class="w-8 h-8 animate-spin text-muted-foreground" />
      </div>

      <div v-else-if="store" class="space-y-6">
        <div class="flex justify-between items-center">
          <h2 class="text-lg font-semibold flex items-center gap-2">
            <Users class="w-5 h-5" />
            Участники ({{ store.members.length }})
          </h2>

          <Dialog v-model:open="showAddDialog">
            <DialogTrigger as-child>
              <Button :disabled="availableUsers.length === 0">
                <UserPlus class="w-4 h-4 mr-2" />
                Добавить
              </Button>
            </DialogTrigger>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Добавить участника</DialogTitle>
                <DialogDescription>
                  Выберите пользователя для добавления в магазин
                </DialogDescription>
              </DialogHeader>
              <div class="py-4">
                <Select v-model="selectedUserId">
                  <SelectTrigger>
                    <SelectValue placeholder="Выберите пользователя..." />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectLabel>Пользователи</SelectLabel>
                      <SelectItem
                        v-for="user in availableUsers"
                        :key="user.id"
                        :value="String(user.id)"
                      >
                        {{ user.userName }}
                      </SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </div>
              <DialogFooter>
                <DialogClose as-child>
                  <Button variant="outline">Отмена</Button>
                </DialogClose>
                <Button @click="handleAddMember" :disabled="isAddingMember || !selectedUserId">
                  <Loader2 v-if="isAddingMember" class="w-4 h-4 mr-2 animate-spin" />
                  Добавить
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>

        <div v-if="store.members.length === 0" class="text-center py-12 text-muted-foreground border rounded-lg">
          <Users class="w-12 h-12 mx-auto mb-4 opacity-50" />
          <p>Нет участников</p>
          <p class="text-sm">Добавьте первого участника</p>
        </div>

        <div v-else class="border rounded-lg">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>ID</TableHead>
                <TableHead>Имя пользователя</TableHead>
                <TableHead class="w-[100px]"></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="member in store.members" :key="member.id">
                <TableCell class="text-muted-foreground">
                  {{ member.id }}
                </TableCell>
                <TableCell class="font-medium">
                  {{ member.userName }}
                </TableCell>
                <TableCell>
                  <Button
                    variant="ghost"
                    size="sm"
                    @click="handleRemoveMember(member.id)"
                    :disabled="removingUserId === member.id"
                  >
                    <Loader2 v-if="removingUserId === member.id" class="w-4 h-4 animate-spin" />
                    <UserMinus v-else class="w-4 h-4 text-destructive" />
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </div>
  </Page>
</template>
