<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Plus,
  Layers,
  Trash2,
} from "lucide-vue-next";
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
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    SettingsFormField,
    FieldGroup,
    Badge,
    Plus,
    Layers,
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
    ConfirmDialog
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
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <div class="flex min-h-0 flex-1 flex-col">
    <div class="mb-4 flex shrink-0 flex-wrap items-center justify-end gap-2">
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
          <FieldGroup class="gap-4 py-4">
            <SettingsFormField :label="$t('common.name')" label-for="new-group-name">
              <Input
                id="new-group-name"
                v-model="newGroupName"
                :placeholder="$t('pages.productGroupsPage.namePlaceholder')"
                @keyup.enter="handleCreateGroup"
              />
            </SettingsFormField>
            <SettingsFormField :label="$t('common.description')" label-for="new-group-description">
              <Input
                id="new-group-description"
                v-model="newGroupDescription"
                :placeholder="$t('pages.productGroupsPage.descriptionPlaceholder')"
              />
            </SettingsFormField>
          </FieldGroup>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button @click="handleCreateGroup" :disabled="isCreating">
              <Spinner v-if="isCreating" class="mr-2" />
              {{ $t("common.create") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
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
                        class="size-8 opacity-0 group-hover:opacity-100 transition-opacity text-destructive hover:bg-destructive/10 hover:text-destructive"
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
      </div>
    </PageSection>
    </div>
  </AppPageChrome>
</template>
