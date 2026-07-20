<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Layers, Loader2, Trash2 } from "lucide-vue-next";
import { PAGE_PRODUCT_GROUP_CREATE, PAGE_PRODUCTS } from "@/router";
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
import { PageHeader } from "@/components/ui/page-header";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Badge,
    Plus,
    Layers,
    Loader2,
    Trash2,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    PageHeader,
    ConfirmDialog,
  },
  data() {
    return {
      groups: [],
      isLoading: false,
      isCreating: false,
      isDeleting: false,
      newGroupName: "",
      newGroupDescription: "",
      showCreateDialog: false,
      groupToDelete: null,
      breadcrumbs: [{ label: this.$t("pages.productGroupsPage.breadcrumb") }],
      PAGE_PRODUCTS,
    };
  },
  methods: {
    ...mapActions(["getAllGroups", "createGroup", "deleteGroup"]),

    async fetchGroups() {
      this.isLoading = true;
      await this.getAllGroups({
        onSuccess: (response) => {
          this.groups = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleCreateGroup() {
      if (!this.newGroupName.trim()) {
        displaySonnerError(this.$t("pages.productGroupsPage.nameRequired"));
        return;
      }

      this.isCreating = true;
      await this.createGroup({
        name: this.newGroupName.trim(),
        description: this.newGroupDescription.trim() || null,
        productIds: [],
        onSuccess: (response) => {
          displaySonnerSuccess(this.$t("pages.productGroupsPage.created"));
          this.newGroupName = "";
          this.newGroupDescription = "";
          this.showCreateDialog = false;
          // Navigate to edit page
          this.$router.push(`/product-groups/${response.data.value.id}/edit`);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleDeleteGroup(groupId) {
      this.isDeleting = true;
      await this.deleteGroup({
        id: groupId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.productGroupsPage.deleted"));
          this.fetchGroups();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
      this.groupToDelete = null;
    },

    openGroup(groupId) {
      this.$router.push(`/product-groups/${groupId}/edit`);
    },

    getSyncStatusVariant(status) {
      switch (status) {
        case "Synced":
          return "default";
        case "Pending":
          return "secondary";
        case "Error":
          return "destructive";
        default:
          return "outline";
      }
    },

    getSyncStatusLabel(status) {
      switch (status) {
        case "Synced":
          return this.$t("pages.productGroupsPage.syncStatus.synced");
        case "Pending":
          return this.$t("pages.productGroupsPage.syncStatus.pending");
        case "Error":
          return this.$t("pages.productGroupsPage.syncStatus.error");
        default:
          return status;
      }
    },
  },
  mounted() {
    this.fetchGroups();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader :title="$t('pages.productGroupsPage.title')" :description="$t('pages.productGroupsPage.description')">
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            {{ $t("pages.productGroupsPage.createGroup") }}
          </Button>
        </DialogTrigger>
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>{{ $t("pages.productGroupsPage.newGroupTitle") }}</DialogTitle>
            <DialogDescription>
              {{ $t("pages.productGroupsPage.newGroupDescription") }}
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <Input
              v-model="newGroupName"
              :placeholder="$t('pages.productGroupsPage.namePlaceholder')"
              @keyup.enter="handleCreateGroup"
            />
            <Input
              v-model="newGroupDescription"
              :placeholder="$t('pages.productGroupsPage.descriptionPlaceholder')"
            />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button @click="handleCreateGroup" :disabled="isCreating">
              <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
              {{ $t("common.create") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="groups.length === 0"
      :empty-icon="Layers"
      :empty-title="$t('pages.productGroupsPage.emptyTitle')"
      :empty-description="$t('pages.productGroupsPage.emptyDescription')"
    >
      <template #empty-action>
        <Button @click="showCreateDialog = true">
          <Plus class="size-4 mr-2" />
          {{ $t("pages.productGroupsPage.createGroup") }}
        </Button>
      </template>

      <div class="rounded-lg border overflow-hidden">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead>{{ $t("common.name") }}</TableHead>
                <TableHead class="hidden md:table-cell">{{ $t("common.description") }}</TableHead>
                <TableHead class="w-28 text-right">{{ $t("pages.productGroupsPage.productsHeader") }}</TableHead>
                <TableHead class="hidden sm:table-cell">{{ $t("pages.productGroupsPage.marketplacesHeader") }}</TableHead>
                <TableHead class="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="group in groups"
                :key="group.id"
                class="group cursor-pointer"
                @click="openGroup(group.id)"
              >
                <TableCell class="font-medium max-w-0">
                  <span class="truncate block">{{ group.name }}</span>
                  <span
                    v-if="group.description"
                    class="text-xs text-muted-foreground truncate block md:hidden"
                  >
                    {{ group.description }}
                  </span>
                </TableCell>
                <TableCell class="hidden md:table-cell text-muted-foreground max-w-xs truncate">
                  {{ group.description || $t("common.emDash") }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ group.membersCount || 0 }}
                </TableCell>
                <TableCell class="hidden sm:table-cell">
                  <div
                    v-if="group.marketplaceLinks && group.marketplaceLinks.length > 0"
                    class="flex flex-wrap gap-1"
                  >
                    <Badge
                      v-for="link in group.marketplaceLinks"
                      :key="link.connectionId"
                      :variant="getSyncStatusVariant(link.status)"
                      class="text-[11px] font-normal"
                    >
                      {{ link.connectionName }}
                    </Badge>
                  </div>
                  <span v-else class="text-muted-foreground">{{ $t("common.emDash") }}</span>
                </TableCell>
                <TableCell class="px-2">
                  <ConfirmDialog
                    :title="$t('pages.productGroupsPage.deleteTitle')"
                    :description="$t('pages.productGroupsPage.deleteDescription', { name: group.name })"
                    :confirm-label="$t('common.delete')"
                    :loading="isDeleting"
                    @confirm="handleDeleteGroup(group.id)"
                  >
                    <template #trigger>
                      <Button
                        variant="ghost"
                        size="icon"
                        class="size-8 opacity-0 group-hover:opacity-100 transition-opacity"
                        @click.stop
                      >
                        <Trash2 class="size-4 text-destructive" />
                      </Button>
                    </template>
                  </ConfirmDialog>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>
  </Page>
</template>
