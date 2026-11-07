<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Spinner } from "@/components/ui/spinner";
import { Button } from "@/components/ui/button";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Layers, Trash2 } from "lucide-vue-next";
import { Checkbox } from "@/components/ui/checkbox";
import ListSelectAllRow from "@/components/lists/ListSelectAllRow.vue";
import ListBulkActions from "@/components/lists/ListBulkActions.vue";
import { canAct, PERMS } from "@/lib/permissions";
import { getCachedMe } from "@/lib/meCache";
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
import MergeProductGroupsDialog from "@/components/products/MergeProductGroupsDialog.vue";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";

export default {
  components: {
    AppPageChrome,
    PageSection,
    Spinner,
    Button,
    Plus,
    Trash2,
    ListSelectAllRow,
    ListBulkActions,
    Checkbox,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    InsetGroup,
    InsetGroupItem,
    ConfirmDialog,
    CreateProductGroupDialog,
    MergeProductGroupsDialog,
  },
  data() {
    return {
      groups: [],
      emptyListIcon: Layers,
      isLoading: false,
      isDeleting: false,
      isMerging: false,
      showMergeDialog: false,
      showCreateDialog: false,
      groupToDelete: null,
      selectedIds: {},
      isBulkDeleteDialogOpen: false,
    };
  },
  computed: {
    selectedGroups() {
      return this.groups.filter((group) => this.selectedIds[String(group.id)]);
    },
    canManageCatalog() {
      return canAct(getCachedMe(), PERMS.CatalogManage);
    },
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
      const n = this.groups.filter(
        (group) => this.selectedIds[String(group.id)],
      ).length;
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
      if (this.isDeleting || this.isMerging) return;
      this.isDeleting = true;
      await this.deleteGroup({
        id: groupId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.productGroupsPage.deleted"));
          this.removeFromSelection(groupId);
          this.groups = this.groups.filter((group) => group.id !== groupId);
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
      if (this.isDeleting || this.isMerging) return;
      const key = String(group.id);
      if (checked) {
        this.selectedIds = { ...this.selectedIds, [key]: true };
      } else {
        this.removeFromSelection(group.id);
      }
    },

    toggleAllPage(checked) {
      if (this.isDeleting || this.isMerging) return;
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
      if (this.isMerging) return;
      this.selectedIds = {};
    },

    async handleBulkDelete() {
      if (!this.canManageCatalog || this.isDeleting || this.isMerging) return;
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
            this.groups = this.groups.filter(
              (group) => String(group.id) !== id,
            );
            this.removeFromSelection(id);
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
      this.isBulkDeleteDialogOpen = false;
      this.isDeleting = false;
    },

    openGroup(groupId) {
      if (this.isMerging) return;
      this.$router.push(`/product-groups/${groupId}/edit`);
    },

    onGroupsMerged({ sourceGroupIds }) {
      const removed = new Set(sourceGroupIds.map(String));
      this.groups = this.groups.filter((group) => !removed.has(String(group.id)));
      this.selectedIds = {};
      this.fetchGroups();
    },

    groupMarketplaceNames(group) {
      return (group.marketplaceLinks || [])
        .map((link) => link.connectionName)
        .filter(Boolean)
        .join(" · ");
    },
  },
  mounted() {
    this.fetchGroups();
  },
};
</script>

<template>
  <AppPageChrome>
    <div class="flex w-full max-w-xl flex-col">
      <div class="mb-6 flex flex-wrap items-center justify-start gap-2">
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
        :empty-icon="emptyListIcon"
        :empty-title="$t('pages.productGroupsPage.emptyTitle')"
        :empty-description="$t('pages.productGroupsPage.emptyDescription')"
      >
        <template #empty-action>
          <Button @click="showCreateDialog = true">
            <Plus class="size-4 mr-2" />
            {{ $t("pages.productGroupsPage.createGroup") }}
          </Button>
        </template>

        <div class="flex flex-col gap-3">
          <InsetGroup>
            <ListSelectAllRow
              v-if="canManageCatalog"
              :model-value="
                allPageSelected
                  ? true
                  : somePageSelected
                    ? 'indeterminate'
                    : false
              "
              :disabled="isDeleting || isMerging"
              @update:model-value="toggleAllPage"
            />
            <InsetGroupItem
              v-for="group in groups"
              :key="group.id"
              show-chevron
              @click="openGroup(group.id)"
            >
              <template v-if="canManageCatalog" #leading>
                <span @click.stop>
                  <Checkbox
                    :model-value="isSelected(group)"
                    :disabled="isDeleting || isMerging"
                    :aria-label="group.name"
                    @update:model-value="(v) => toggleRow(group, v === true)"
                  />
                </span>
              </template>
              <p class="truncate">{{ group.name }}</p>
              <p
                v-if="group.description"
                class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
              >
                {{ group.description }}
              </p>
              <p
                class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
              >
                {{
                  $t("pages.productGroupsPage.rowMeta", {
                    count: group.membersCount || 0,
                  })
                }}
                <template v-if="groupMarketplaceNames(group)">
                  · {{ groupMarketplaceNames(group) }}
                </template>
              </p>
              <template v-if="canManageCatalog" #trailing>
                <ConfirmDialog
                  :title="$t('pages.productGroupsPage.deleteTitle')"
                  :description="
                    $t('pages.productGroupsPage.deleteDescription', {
                      name: group.name,
                    })
                  "
                  :confirm-label="$t('common.delete')"
                  :loading="isDeleting"
                  @confirm="handleDeleteGroup(group.id)"
                >
                  <template #trigger>
                    <Button
                      variant="ghost"
                      reveal-on-row-hover
                      :disabled="isDeleting"
                      size="icon"
                      class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                      :aria-label="$t('common.delete')"
                      @click.stop
                    >
                      <Trash2 class="size-4" />
                    </Button>
                  </template>
                </ConfirmDialog>
              </template>
            </InsetGroupItem>
          </InsetGroup>

          <ListBulkActions
            v-if="canManageCatalog"
            :count="selectedCount"
            :busy="isDeleting || isMerging"
            @clear="clearSelection"
            @delete="isBulkDeleteDialogOpen = true"
          >
            <Button
              type="button"
              variant="outline"
              size="sm"
              class="h-8"
              :disabled="selectedCount < 2 || selectedCount > 101 || isDeleting || isMerging"
              @click="showMergeDialog = true"
            >{{ $t("pages.productGroupsPage.mergeAction") }}</Button>
          </ListBulkActions>
        </div>
      </PageSection>

      <MergeProductGroupsDialog
        v-if="canManageCatalog"
        v-model:open="showMergeDialog"
        v-model:busy="isMerging"
        :groups="selectedGroups"
        @merged="onGroupsMerged"
      />

      <AlertDialog v-model:open="isBulkDeleteDialogOpen">
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{{
              $t("pages.productGroupsPage.bulkDeleteTitle")
            }}</AlertDialogTitle>
            <AlertDialogDescription>
              {{
                $t("pages.productGroupsPage.bulkDeleteDescription", {
                  count: selectedCount,
                })
              }}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel :disabled="isDeleting">{{
              $t("common.cancel")
            }}</AlertDialogCancel>
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
