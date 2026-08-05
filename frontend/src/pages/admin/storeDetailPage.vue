<script>
import { Spinner } from "@/components/ui/spinner";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { loadMe, invalidateMeCache } from "@/main";
import { PAGE_ADMIN_STORES } from "@/router";
import {
  UserPlus,
  UserMinus,
  Users,
  Trash2,
} from "lucide-vue-next";
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
import { EmptyState } from "@/components/ui/empty-state";
import { setPageTitle } from "@/router";

export default {
  components: {
    Spinner,
    PageSection,
    Button,
    Input,
    UserPlus,
    UserMinus,
    Users,
    Trash2,
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
    EmptyState
  },
  data() {
    return {
      store: null,
      allUsers: [],
      me: null,
      isLoading: false,
      isAddingMember: false,
      removingUserId: null,
      showAddDialog: false,
      selectedUserId: null,
      showDeleteDialog: false,
      deleteConfirmName: "",
      isDeleting: false,
    };
  },
  computed: {
    storeId() {
      return parseInt(this.$route.params.id);
    },
    availableUsers() {
      if (!this.store || !this.allUsers.length) return [];
      const memberIds = this.store.members.map((m) => m.id);
      return this.allUsers.filter((u) => !memberIds.includes(u.id));
    },
    canDeleteStore() {
      return this.me?.isAdmin === true;
    },
    deleteNameMatches() {
      if (!this.store?.name) return false;
      return this.deleteConfirmName.trim() === this.store.name;
    },
  },
  methods: {
    ...mapActions(["getStoreDetail", "getAllUsers", "addMember", "removeMember", "deleteStore"]),

    async fetchMe() {
      try {
        this.me = await loadMe();
      } catch {
        this.me = null;
      }
    },

    async fetchStore() {
      this.isLoading = true;
      await this.getStoreDetail({
        id: this.storeId,
        onSuccess: (response) => {
          this.store = response.data.value;
          setPageTitle(this.store.name);
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
        displaySonnerError(this.$t("pages.storeDetailPage.selectUserError"));
        return;
      }

      this.isAddingMember = true;
      await this.addMember({
        storeId: this.storeId,
        userId: parseInt(this.selectedUserId),
        onSuccess: (response) => {
          this.store = response.data.value;
          displaySonnerSuccess(this.$t("pages.storeDetailPage.memberAdded"));
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
      const member = this.store?.members?.find((m) => m.id === userId);
      const ok = await confirm({
        title: this.$t("pages.storeDetailPage.removeMemberTitle"),
        description: member
          ? this.$t("pages.storeDetailPage.removeMemberDescriptionNamed", { name: member.userName })
          : this.$t("pages.storeDetailPage.removeMemberDescription"),
      });
      if (!ok) return;

      this.removingUserId = userId;
      await this.removeMember({
        storeId: this.storeId,
        userId,
        onSuccess: (response) => {
          this.store = response.data.value;
          displaySonnerSuccess(this.$t("pages.storeDetailPage.memberRemoved"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.removingUserId = null;
    },

    openDeleteDialog() {
      this.deleteConfirmName = "";
      this.showDeleteDialog = true;
    },

    async handleDeleteStore() {
      if (!this.deleteNameMatches || this.isDeleting) return;
      this.isDeleting = true;
      await this.deleteStore({
        storeId: this.storeId,
        confirmationName: this.deleteConfirmName.trim(),
        onSuccess: async () => {
          displaySonnerSuccess(this.$t("pages.storeDetailPage.deleteSuccess"));
          this.showDeleteDialog = false;
          invalidateMeCache();
          try {
            await loadMe();
          } catch {
            /* ignore */
          }
          // Remount shell / store switcher if current selection was wiped
          this.$store.commit("incrementAppKey");
          this.$router.push(PAGE_ADMIN_STORES);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
    },
  },
  mounted() {
    this.fetchMe();
    this.fetchStore();
    this.fetchUsers();
  },
};
</script>

<template>
  <div>
    <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
      <Dialog v-if="store" v-model:open="showAddDialog">
        <DialogTrigger as-child>
          <Button :disabled="availableUsers.length === 0">
            <UserPlus class="mr-2 size-4" />
            {{ $t("pages.storeDetailPage.addMember") }}
          </Button>
        </DialogTrigger>
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>{{ $t("pages.storeDetailPage.addMember") }}</DialogTitle>
            <DialogDescription>
              {{ $t("pages.storeDetailPage.addMemberDialogDescription") }}
            </DialogDescription>
          </DialogHeader>
          <div class="py-4">
            <Select v-model="selectedUserId">
              <SelectTrigger>
                <SelectValue :placeholder="$t('pages.storeDetailPage.selectUserPlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectLabel>{{ $t("pages.storeDetailPage.usersLabel") }}</SelectLabel>
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
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button @click="handleAddMember" :disabled="isAddingMember || !selectedUserId">
              <Spinner v-if="isAddingMember" class="mr-2" />
              {{ $t("common.add") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>

    <PageSection :loading="isLoading">
      <div v-if="store">
        <EmptyState
          v-if="store.members.length === 0"
          :title="$t('pages.storeDetailPage.emptyTitle')"
          :description="$t('pages.storeDetailPage.emptyDescription')"
        >
          <template #icon>
            <Users />
          </template>
        </EmptyState>

        <div v-else class="overflow-x-auto border-y">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>ID</TableHead>
                <TableHead>{{ $t("pages.storeDetailPage.columnUserName") }}</TableHead>
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
                    class="text-destructive hover:bg-destructive/10 hover:text-destructive"
                    @click="handleRemoveMember(member.id)"
                    :disabled="removingUserId === member.id"
                  >
                    <Spinner v-if="removingUserId === member.id" />
                    <UserMinus v-else class="size-4" />
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>

    <section
      v-if="store && canDeleteStore"
      class="mt-8 rounded-lg border border-destructive/40 p-4"
    >
      <h2 class="text-sm font-semibold text-destructive">
        {{ $t("pages.storeDetailPage.dangerZoneTitle") }}
      </h2>
      <p class="mt-1 max-w-2xl text-sm text-muted-foreground">
        {{ $t("pages.storeDetailPage.dangerZoneDescription") }}
      </p>
      <Button class="mt-4" variant="destructive" @click="openDeleteDialog">
        <Trash2 class="mr-2 size-4" />
        {{ $t("pages.storeDetailPage.deleteStoreButton") }}
      </Button>

      <Dialog v-model:open="showDeleteDialog">
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>{{ $t("pages.storeDetailPage.deleteDialogTitle") }}</DialogTitle>
            <DialogDescription>
              {{ $t("pages.storeDetailPage.deleteDialogDescription") }}
            </DialogDescription>
          </DialogHeader>
          <div class="grid gap-2 py-2">
            <label class="text-sm font-medium" for="store-delete-confirm">
              {{ $t("pages.storeDetailPage.deleteConfirmLabel", { name: store.name }) }}
            </label>
            <Input
              id="store-delete-confirm"
              v-model="deleteConfirmName"
              :placeholder="$t('pages.storeDetailPage.deleteConfirmPlaceholder')"
              autocomplete="off"
              @keydown.enter="handleDeleteStore"
            />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline" :disabled="isDeleting">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button
              variant="destructive"
              :disabled="!deleteNameMatches || isDeleting"
              @click="handleDeleteStore"
            >
              <Spinner v-if="isDeleting" class="mr-2" />
              {{ $t("pages.storeDetailPage.deleteForever") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  </div>
</template>
