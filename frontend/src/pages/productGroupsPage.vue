<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Plus,
  Layers,
  Trash2,
  X,
} from "lucide-vue-next";
import { Checkbox } from "@/components/ui/checkbox";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import CreateProductGroupDialog from "@/components/products/CreateProductGroupDialog.vue";
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
    AppPageChrome,
    PageSection,
    Button,
    Badge,
    Plus,
    Layers,
    Trash2,
    X,
    Checkbox,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    ConfirmDialog,
    CreateProductGroupDialog,
  },
  data() {
    return {
      groups: [],
      isLoading: false,
      isDeleting: false,
      showCreateDialog: false,
      groupToDelete: null,
      selectedIds: {},
      isBulkDeleteDialogOpen: false,
      breadcrumbs: [{ label: this.$t("pages.productGroupsPage.breadcrumb") }],
    };
  },
  computed: {
    selectedCount() {
      return Object.keys(this.selectedIds).length;
    },
    hasSelection() {
      return this.selectedCount > 0;
    },
    allPageSelected() {
      if (this.groups.length === 0) return false;
      return this.groups.every((group) => this.selectedIds[String(group.id)]);
    },
    somePageSelected() {
      if (this.groups.length === 0) return false;
      const n = this.groups.filter((group) => this.selectedIds[String(group.id)]).length;
      return n > 0 && n < this.groups.length;
    },
  },
  methods: {
    ...mapActions(["getAllGroups", "deleteGroup"]),

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

    onGroupCreated(group) {
      if (!group?.id) return;
      this.$router.push(`/product-groups/${group.id}/edit`);
    },

    async handleDeleteGroup(groupId) {
      this.isDeleting = true;
      await this.deleteGroup({
        id: groupId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.productGroupsPage.deleted"));
          this.removeFromSelection(groupId);
          this.fetchGroups();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
      this.groupToDelete = null;
    },

    isSelected(group) {
      return Boolean(this.selectedIds[String(group.id)]);
    },

    removeFromSelection(groupId) {
      const next = { ...this.selectedIds };
      delete next[String(groupId)];
      this.selectedIds = next;
    },

    toggleRow(group, checked) {
      const key = String(group.id);
      if (checked) {
        this.selectedIds = { ...this.selectedIds, [key]: true };
      } else {
        this.removeFromSelection(group.id);
      }
    },

    toggleAllPage(checked) {
      if (checked) {
        const next = { ...this.selectedIds };
        for (const group of this.groups) {
          next[String(group.id)] = true;
        }
        this.selectedIds = next;
      } else {
        const next = { ...this.selectedIds };
        for (const group of this.groups) {
          delete next[String(group.id)];
        }
        this.selectedIds = next;
      }
    },

    clearSelection() {
      this.selectedIds = {};
    },

    async handleBulkDelete() {
      const ids = Object.keys(this.selectedIds);
      if (ids.length === 0) return;

      this.isDeleting = true;
      let deleted = 0;
      let stopped = false;
      for (const id of ids) {
        await this.deleteGroup({
          id,
          onSuccess: () => {
            deleted += 1;
          },
          onError: (error) => {
            displaySonnerError(error);
            stopped = true;
          },
        });
        if (stopped) break;
      }
      if (deleted > 0) {
        displaySonnerSuccess(
          this.$t("pages.productGroupsPage.deletedCount", { count: deleted }),
        );
      }
      this.selectedIds = {};
      this.isBulkDeleteDialogOpen = false;
      this.isDeleting = false;
      await this.fetchGroups();
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
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <div class="flex min-h-0 flex-1 flex-col">
    <div class="mb-4 flex shrink-0 flex-wrap items-center justify-end gap-2">
      <Button @click="showCreateDialog = true">
        <Plus class="size-4 mr-2" />
        {{ $t("pages.productGroupsPage.createGroup") }}
      </Button>
      <CreateProductGroupDialog
        v-model:open="showCreateDialog"
        @created="onGroupCreated"
      />
    </div>

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

      <div class="overflow-hidden rounded-lg border">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead class="w-10">
                  <Checkbox
                    :model-value="allPageSelected"
                    :indeterminate="somePageSelected"
                    :aria-label="$t('pages.productGroupsPage.selectAll')"
                    @update:model-value="(v) => toggleAllPage(v === true)"
                  />
                </TableHead>
                <TableHead>{{ $t("common.name") }}</TableHead>
                <TableHead class="hidden md:table-cell w-[30%]">{{ $t("common.description") }}</TableHead>
                <TableHead class="w-28 text-right">{{ $t("pages.productGroupsPage.productsHeader") }}</TableHead>
                <TableHead class="hidden sm:table-cell w-48">{{ $t("pages.productGroupsPage.marketplacesHeader") }}</TableHead>
                <TableHead class="w-12" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="group in groups"
                :key="group.id"
                class="group cursor-pointer"
                :class="isSelected(group) ? 'bg-muted/30' : ''"
                @click="openGroup(group.id)"
              >
                <TableCell @click.stop>
                  <Checkbox
                    :model-value="isSelected(group)"
                    :aria-label="group.name"
                    @update:model-value="(v) => toggleRow(group, v === true)"
                  />
                </TableCell>
                <TableCell class="max-w-0 font-medium">
                  <span class="block truncate">{{ group.name }}</span>
                  <span
                    v-if="group.description"
                    class="text-muted-foreground block truncate text-xs md:hidden"
                  >
                    {{ group.description }}
                  </span>
                </TableCell>
                <TableCell class="text-muted-foreground hidden max-w-0 md:table-cell">
                  <span class="block truncate">{{ group.description || $t("common.emDash") }}</span>
                </TableCell>
                <TableCell class="text-muted-foreground text-right tabular-nums">
                  {{ group.membersCount || 0 }}
                </TableCell>
                <TableCell class="hidden whitespace-normal sm:table-cell">
                  <div
                    v-if="group.marketplaceLinks && group.marketplaceLinks.length > 0"
                    class="flex flex-wrap gap-1"
                  >
                    <Badge
                      v-for="link in group.marketplaceLinks"
                      :key="link.connectionId"
                      :variant="getSyncStatusVariant(link.status)"
                      class="max-w-full truncate text-[11px] font-normal"
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
                        class="text-destructive hover:bg-destructive/10 hover:text-destructive size-8 opacity-0 transition-opacity group-hover:opacity-100"
                        @click.stop
                      >
                        <Trash2 class="size-4" />
                      </Button>
                    </template>
                  </ConfirmDialog>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
        <div
          v-if="hasSelection"
          class="bg-background flex items-center justify-between border-t px-3 py-2"
        >
          <div class="flex items-center gap-2">
            <span class="text-sm font-medium">
              {{ $t("pages.productGroupsPage.selectedCount", { count: selectedCount }) }}
            </span>
            <Button variant="ghost" size="sm" class="h-8" @click="clearSelection">
              <X class="h-4 w-4" />
            </Button>
            <Button
              variant="destructive"
              size="sm"
              class="h-8"
              @click="isBulkDeleteDialogOpen = true"
            >
              <Trash2 class="mr-1 h-4 w-4" />
              {{ $t("common.delete") }}
            </Button>
          </div>
        </div>
      </div>
    </PageSection>

    <AlertDialog v-model:open="isBulkDeleteDialogOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t("pages.productGroupsPage.bulkDeleteTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t("pages.productGroupsPage.bulkDeleteDescription", { count: selectedCount }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="isDeleting">{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            :disabled="isDeleting"
            @click.prevent="handleBulkDelete"
          >
            <Spinner v-if="isDeleting" class="mr-2" />
            {{ $t("common.delete") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
    </div>
  </AppPageChrome>
</template>
