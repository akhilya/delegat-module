<script>
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/ui/page-header";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { UserPlus, UserMinus, Loader2, Users } from "lucide-vue-next";
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
    PageSection,
    Button,
    PageHeader,
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
    EmptyState,
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

  },
  methods: {
    ...mapActions(["getStoreDetail", "getAllUsers", "addMember", "removeMember"]),

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
  },
  mounted() {
    this.fetchStore();
    this.fetchUsers();
  },
};
</script>

<template>
  <div>
    <PageHeader
      :title="store?.name || $t('routes.store')"
      :description="store ? $t('pages.storeDetailPage.membersCountDescription', { count: store.members.length }) : $t('pages.storeDetailPage.membersSettingsDescription')"
    >
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
              <Loader2 v-if="isAddingMember" class="mr-2 size-4 animate-spin" />
              {{ $t("common.add") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

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
                    @click="handleRemoveMember(member.id)"
                    :disabled="removingUserId === member.id"
                  >
                    <Loader2 v-if="removingUserId === member.id" class="size-4 animate-spin" />
                    <UserMinus v-else class="size-4 text-destructive" />
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>
  </div>
</template>
