<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  TableHead,
  TableHeader,
  TableRow,
  TableCell,
} from "@/components/ui/table";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Save,
  Plus,
  Trash2,
  GripVertical,
  Package,
  Upload,
  RefreshCw,
  Search,
  X,
  ExternalLink,
  Ban,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_PRODUCT_GROUPS, setPageTitle } from "@/router";
import draggable from "vuedraggable";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    Textarea,
    Badge,
    SettingsFormField,
    FieldGroup,
    AdapterIcon,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogFooter,
    DialogClose,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    Card,
    CardAction,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
    TableHeader,
    TableHead,
    TableRow,
    TableCell,
    Checkbox,
    Save,
    Plus,
    Trash2,
    GripVertical,
    Package,
    Upload,
    RefreshCw,
    Search,
    X,
    ExternalLink,
    Ban,
    ChevronLeft,
    ChevronRight,
    ChevronsLeft,
    ChevronsRight,
    draggable,
    EmptyState
  },
  data() {
    const groupId = this.$route.params.id ? parseInt(this.$route.params.id) : null;
    return {
      groupId,
      isCreateMode: !groupId,
      group: null,
      members: [],
      connections: [],
      connectionGrouping: {},
      isLoadingConnectionGrouping: false,
      syncStatuses: [],
      isLoading: false,
      isSaving: false,
      isAddingProducts: false,
      isRemovingProduct: false,
      isExporting: false,
      isLoadingSyncStatus: false,
      showProductSearch: false,
      productSearchQuery: "",
      productSearchStartIndex: 0,
      productSearchPageSize: 25,
      productSearchTotalCount: 0,
      availableProducts: [],
      isSearchingProducts: false,
      selectedProductsToAdd: [],
      PAGE_PRODUCT_GROUPS,
    };
  },
  computed: {
    breadcrumbs() {
      const items = [{ label: this.$t("routes.groups"), to: PAGE_PRODUCT_GROUPS }];
      if (this.isCreateMode) {
        items.push({ label: this.$t("pages.productGroupEditPage.breadcrumbNewGroup") });
      } else if (this.group?.name) {
        items.push({ label: this.group.name });
      } else {
        items.push({ label: this.$t("pages.productGroupEditPage.breadcrumbEditing") });
      }
      return items;
    },
    availableProductsFiltered() {
      const memberIds = new Set(this.members.map((m) => m.product.id));
      return this.availableProducts.filter((p) => !memberIds.has(p.id));
    },
    supportedConnections() {
      return this.connections.filter(
        (c) => this.connectionGrouping[c.id]?.supported,
      );
    },
    unsupportedConnections() {
      return this.connections.filter(
        (c) => !this.connectionGrouping[c.id]?.supported,
      );
    },
    membersCountLabel() {
      const n = this.members.length;
      const mod10 = n % 10;
      const mod100 = n % 100;
      if (mod100 >= 11 && mod100 <= 14) return this.$t("pages.productGroupEditPage.productsCount.formMany");
      if (mod10 === 1) return this.$t("pages.productGroupEditPage.productsCount.formOne");
      if (mod10 >= 2 && mod10 <= 4) return this.$t("pages.productGroupEditPage.productsCount.formFew");
      return this.$t("pages.productGroupEditPage.productsCount.formMany");
    },
    allVisibleProductsSelected() {
      const visible = this.availableProductsFiltered;
      return visible.length > 0
        && visible.every((product) => this.isProductSelected(product.id));
    },
    someVisibleProductsSelected() {
      const visible = this.availableProductsFiltered;
      const selectedCount = visible.filter((product) => this.isProductSelected(product.id)).length;
      return selectedCount > 0 && selectedCount < visible.length;
    },
    selectedProductsCountLabel() {
      const n = this.selectedProductsToAdd.length;
      const mod10 = n % 10;
      const mod100 = n % 100;
      if (mod100 >= 11 && mod100 <= 14) return this.$t("pages.productGroupEditPage.productsCount.formMany");
      if (mod10 === 1) return this.$t("pages.productGroupEditPage.productsCount.formOne");
      if (mod10 >= 2 && mod10 <= 4) return this.$t("pages.productGroupEditPage.productsCount.formFew");
      return this.$t("pages.productGroupEditPage.productsCount.formMany");
    },
    productSearchLastPage() {
      return Math.max(1, Math.ceil(this.productSearchTotalCount / this.productSearchPageSize));
    },
    productSearchPage() {
      return Math.floor(this.productSearchStartIndex / this.productSearchPageSize) + 1;
    },
    productSearchIsFirstPage() {
      return this.productSearchPage <= 1;
    },
    productSearchIsLastPage() {
      return this.productSearchPage >= this.productSearchLastPage;
    },
  },
  methods: {
    ...mapActions([
      "getGroup",
      "createGroup",
      "updateGroup",
      "addProductsToGroup",
      "removeProductFromGroup",
      "updateGroupMember",
      "exportGroup",
      "getGroupSyncStatus",
      "getAllProducts",
      "getConnections",
      "getCapabilities",
    ]),

    async fetchGroup() {
      if (this.isCreateMode) {
        this.group = { name: "", description: "" };
        return;
      }

      this.isLoading = true;
      await this.getGroup({
        id: this.groupId,
        onSuccess: (response) => {
          this.group = response.data.value;
          setPageTitle(this.group.name);
          this.members = this.group.members || [];
          this.syncStatuses = this.group.marketplaceLinks || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async fetchConnections() {
      await this.getConnections({
        onSuccess: (response) => {
          this.connections = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      await this.fetchConnectionGroupingCapabilities();
    },

    async fetchConnectionGroupingCapabilities() {
      if (!this.connections.length) {
        this.connectionGrouping = {};
        return;
      }

      this.isLoadingConnectionGrouping = true;
      const grouping = {};

      await Promise.all(
        this.connections.map(
          (conn) =>
            new Promise((resolve) => {
              this.getCapabilities({
                connectionId: conn.id,
                onSuccess: (response) => {
                  const data = response.data?.value ?? response.data;
                  const caps = data?.capabilities ?? data;
                  const fieldBased = !!caps?.fieldBasedGrouping;
                  const endpointBased = !!caps?.endpointBasedGrouping;
                  grouping[conn.id] = {
                    supported: fieldBased || endpointBased,
                    fieldBased,
                    endpointBased,
                  };
                  resolve();
                },
                onError: () => {
                  grouping[conn.id] = {
                    supported: false,
                    fieldBased: false,
                    endpointBased: false,
                  };
                  resolve();
                },
              });
            }),
        ),
      );

      this.connectionGrouping = grouping;
      this.isLoadingConnectionGrouping = false;
    },

    getSyncStatusForConnection(connectionId) {
      return this.syncStatuses.find((s) => s.connectionId === connectionId);
    },

    getGroupingTypeLabel(connectionId) {
      const grouping = this.connectionGrouping[connectionId];
      if (!grouping?.supported) return null;
      if (grouping.fieldBased) return this.$t("pages.productGroupEditPage.grouping.viaCardField");
      if (grouping.endpointBased) return this.$t("pages.productGroupEditPage.grouping.viaMergeApi");
      return this.$t("pages.productGroupEditPage.grouping.supported");
    },

    getExportButtonLabel(connection) {
      const linked = this.getSyncStatusForConnection(connection.id);
      const grouping = this.connectionGrouping[connection.id];
      if (linked) return this.$t("pages.productGroupEditPage.grouping.updateLink");
      if (grouping?.fieldBased) return this.$t("pages.productGroupEditPage.grouping.linkGroup");
      if (grouping?.endpointBased) return this.$t("pages.productGroupEditPage.grouping.mergeCards");
      return this.$t("pages.productGroupEditPage.grouping.exportAction");
    },

    async fetchSyncStatus() {
      if (this.isCreateMode) return;

      this.isLoadingSyncStatus = true;
      await this.getGroupSyncStatus({
        groupId: this.groupId,
        onSuccess: (response) => {
          this.syncStatuses = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingSyncStatus = false;
    },

    async handleSaveGroup() {
      if (!this.group.name.trim()) {
        displaySonnerError(this.$t("pages.productGroupEditPage.toast.nameRequired"));
        return;
      }

      this.isSaving = true;

      if (this.isCreateMode) {
        await this.createGroup({
          name: this.group.name.trim(),
          description: this.group.description?.trim() || null,
          productIds: [],
          onSuccess: (response) => {
            displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.created"));
            this.$router.push(`/product-groups/${response.data.value.id}/edit`);
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } else {
        await this.updateGroup({
          id: this.groupId,
          name: this.group.name.trim(),
          description: this.group.description?.trim() || null,
          onSuccess: () => {
            displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.saved"));
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      }

      this.isSaving = false;
    },

    async searchProducts(resetPage = true) {
      if (resetPage) {
        this.productSearchStartIndex = 0;
      }

      this.isSearchingProducts = true;
      await this.getAllProducts({
        searchQuery: this.productSearchQuery,
        startIndex: this.productSearchStartIndex,
        pageSize: this.productSearchPageSize,
        onSuccess: (response) => {
          const data = response.data.value;
          this.availableProducts = data.items || [];
          this.productSearchTotalCount = data.totalCount || 0;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSearchingProducts = false;
    },

    goToProductSearchPage(page) {
      const nextPage = Math.min(Math.max(1, page), this.productSearchLastPage);
      this.productSearchStartIndex = (nextPage - 1) * this.productSearchPageSize;
      this.searchProducts(false);
    },

    toggleProductSelection(product, checked) {
      const index = this.selectedProductsToAdd.findIndex((p) => p.id === product.id);
      if (checked && index < 0) {
        this.selectedProductsToAdd.push(product);
      } else if (!checked && index >= 0) {
        this.selectedProductsToAdd.splice(index, 1);
      }
    },

    isProductSelected(productId) {
      return this.selectedProductsToAdd.some((p) => p.id === productId);
    },

    toggleSelectAllVisible(checked) {
      if (checked) {
        for (const product of this.availableProductsFiltered) {
          if (!this.isProductSelected(product.id)) {
            this.selectedProductsToAdd.push(product);
          }
        }
        return;
      }

      const visibleIds = new Set(this.availableProductsFiltered.map((product) => product.id));
      this.selectedProductsToAdd = this.selectedProductsToAdd.filter(
        (product) => !visibleIds.has(product.id),
      );
    },

    clearSelectedProducts() {
      this.selectedProductsToAdd = [];
    },

    getProductCategoryName(product) {
      return product.category?.name || product.categoryName || "-";
    },

    async handleAddSelectedProducts() {
      if (this.selectedProductsToAdd.length === 0) return;

      this.isAddingProducts = true;
      const productIds = this.selectedProductsToAdd.map((p) => p.id);

      await this.addProductsToGroup({
        groupId: this.groupId,
        productIds,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.productsAdded", { count: productIds.length }));
          this.selectedProductsToAdd = [];
          this.showProductSearch = false;
          this.fetchGroup();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isAddingProducts = false;
    },

    async handleRemoveProduct(productId) {
      this.isRemovingProduct = true;
      await this.removeProductFromGroup({
        groupId: this.groupId,
        productId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.productRemoved"));
          this.fetchGroup();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isRemovingProduct = false;
    },

    async handleReorder() {
      // Update sort order for all members
      for (let i = 0; i < this.members.length; i++) {
        const member = this.members[i];
        if (member.sortOrder !== i) {
          await this.updateGroupMember({
            groupId: this.groupId,
            productId: member.product.id,
            sortOrder: i,
            onSuccess: () => {},
            onError: (error) => {
              displaySonnerError(error);
            },
          });
        }
      }
      displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.orderSaved"));
    },

    async handleExportGroup(connectionId) {
      this.isExporting = true;
      await this.exportGroup({
        groupId: this.groupId,
        connectionId,
        onSuccess: (response) => {
          const data = response.data.value;
          const msg = data.message || data.externalGroupId || this.$t("pages.productGroupEditPage.toast.exportDone");
          if (data.isFieldBased) {
            displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.linkCreated", { message: msg }));
          } else {
            displaySonnerSuccess(this.$t("pages.productGroupEditPage.toast.cardsMerged", { message: msg }));
          }
          this.fetchSyncStatus();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isExporting = false;
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
          return this.$t("pages.productGroupEditPage.syncStatus.synced");
        case "Pending":
          return this.$t("pages.productGroupEditPage.syncStatus.pending");
        case "Error":
          return this.$t("pages.productGroupEditPage.syncStatus.error");
        default:
          return status;
      }
    },

    getProductImageUrl(product) {
      // Handle GroupProductDTO structure (members in group)
      if (product.images && product.images.length > 0) {
        return product.images[0].thumbnailUrl ?? product.images[0].url;
      }
      // Handle ProductReviewDTO structure (product search results)
      if (product.primaryImageThumbnail ?? product.primaryImage) {
        return product.primaryImageThumbnail ?? product.primaryImage;
      }
      return null;
    },

    openProductSearchDialog() {
      this.showProductSearch = true;
      this.productSearchQuery = "";
      this.productSearchStartIndex = 0;
      this.productSearchTotalCount = 0;
      this.selectedProductsToAdd = [];
      this.searchProducts(true);
    },
  },
  mounted() {
    this.fetchGroup();
    this.fetchConnections();
    if (!this.isCreateMode) {
      this.fetchSyncStatus();
    }
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <PageSection :loading="isLoading" class="!min-h-0">
      <div v-if="group" class="space-y-3">
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0 flex-1 space-y-1">
            <h1 class="text-lg font-semibold">
              {{ isCreateMode ? $t("pages.productGroupEditPage.newGroupTitle") : group.name }}
            </h1>
            <p class="text-sm text-muted-foreground">
              <template v-if="isCreateMode">
                {{ $t("pages.productGroupEditPage.newGroupHint") }}
              </template>
              <template v-else>
                {{ $t("pages.productGroupEditPage.editGroupHint") }}
              </template>
            </p>
          </div>
          <Button
            class="shrink-0"
            @click="handleSaveGroup"
            :disabled="isSaving"
          >
            <Spinner v-if="isSaving" class="mr-2" />
            <Save v-else class="size-4 mr-2" />
            {{ isCreateMode ? $t("pages.productGroupEditPage.actions.createGroup") : $t("common.save") }}
          </Button>
        </div>

        <!-- Block 1: Settings -->
        <FieldGroup class="max-w-2xl gap-4">
          <SettingsFormField :label="$t('common.name')" label-for="group-name">
            <Input
              id="group-name"
              v-model="group.name"
              :placeholder="$t('pages.productGroupEditPage.fields.name.placeholder')"
            />
          </SettingsFormField>
          <SettingsFormField :label="$t('common.description')" label-for="group-description">
            <Textarea
              id="group-description"
              v-model="group.description"
              :placeholder="$t('pages.productGroupEditPage.fields.description.placeholder')"
              class="min-h-24 resize-none"
            />
          </SettingsFormField>
        </FieldGroup>

        <template v-if="!isCreateMode">
          <!-- Block 2: Products -->
          <Card class="overflow-hidden border-0 shadow-none !gap-3 !py-0">
            <CardHeader class="!px-0">
              <CardTitle class="text-base">
                {{ $t("pages.productGroupEditPage.sections.groupProducts.title") }}
              </CardTitle>
              <CardDescription>
                {{ $t("pages.productGroupEditPage.sections.groupProducts.description", {
                  count: members.length,
                  unit: membersCountLabel,
                }) }}
              </CardDescription>
              <CardAction>
                <Button
                  variant="outline"
                  size="sm"
                  @click="openProductSearchDialog"
                >
                  <Plus class="size-4 mr-2" />
                  {{ $t("common.add") }}
                </Button>
              </CardAction>
            </CardHeader>
            <CardContent class="!px-0 pb-6 pt-0">
              <EmptyState
                v-if="members.length === 0"
                :title="$t('pages.productGroupEditPage.membersEmpty.title')"
                :description="$t('pages.productGroupEditPage.membersEmpty.description')"
                class="py-10"
              >
                <template #icon>
                  <Package />
                </template>
                <Button variant="outline" @click="openProductSearchDialog">
                  <Plus class="size-4 mr-2" />
                  {{ $t("pages.productGroupEditPage.actions.addProducts") }}
                </Button>
              </EmptyState>

              <div
                v-else
                class="overflow-hidden rounded-lg border"
              >
                <div class="max-h-[min(42rem,70vh)] overflow-y-auto thin-scrollbar">
                <table class="w-full caption-bottom text-sm">
                  <TableHeader class="sticky top-0 z-10 bg-card [&_tr]:border-b">
                    <TableRow class="hover:bg-transparent">
                      <TableHead class="w-9 px-3" />
                      <TableHead class="w-9 px-2 text-center">#</TableHead>
                      <TableHead class="w-36 px-3">{{ $t("pages.productGroupEditPage.table.sku") }}</TableHead>
                      <TableHead class="w-14 px-2" />
                      <TableHead class="px-3">{{ $t("common.name") }}</TableHead>
                      <TableHead class="hidden md:table-cell px-3">{{ $t("pages.productGroupEditPage.table.category") }}</TableHead>
                      <TableHead class="w-12 px-2" />
                    </TableRow>
                  </TableHeader>
                  <draggable
                    v-model="members"
                    tag="tbody"
                    item-key="id"
                    handle=".drag-handle"
                    ghost-class="opacity-50"
                    @end="handleReorder"
                  >
                    <template #item="{ element: member, index }">
                      <TableRow class="group">
                        <TableCell class="w-9 px-3">
                          <GripVertical
                            class="drag-handle size-4 text-muted-foreground/70 cursor-grab active:cursor-grabbing hover:text-foreground"
                          />
                        </TableCell>
                        <TableCell class="w-9 px-2 text-center text-xs text-muted-foreground tabular-nums">
                          {{ index + 1 }}
                        </TableCell>
                        <TableCell class="w-36 px-3 font-mono text-xs text-muted-foreground">
                          {{ member.product.sku || "-" }}
                        </TableCell>
                        <TableCell class="w-14 px-2">
                          <div class="size-10 rounded-md overflow-hidden bg-muted">
                            <img
                              v-if="getProductImageUrl(member.product)"
                              :src="getProductImageUrl(member.product)"
                              :alt="member.product.name"
                              class="size-full object-cover"
                            />
                            <div
                              v-else
                              class="size-full flex items-center justify-center"
                            >
                              <Package class="size-4 text-muted-foreground" />
                            </div>
                          </div>
                        </TableCell>
                        <TableCell class="min-w-0 px-3">
                          <router-link
                            :to="`/products/${member.product.id}/edit`"
                            class="font-medium truncate block hover:underline"
                          >
                            {{ member.product.name }}
                          </router-link>
                        </TableCell>
                        <TableCell class="hidden md:table-cell px-3 text-muted-foreground">
                          {{ member.product.category?.name || "-" }}
                        </TableCell>
                        <TableCell class="w-12 px-2">
                          <div class="opacity-0 group-hover:opacity-100 transition-opacity">
                            <AlertDialog>
                              <AlertDialogTrigger as-child>
                                <Button
                                  variant="ghost"
                                  size="icon"
                                  class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                                >
                                  <Trash2 class="size-4" />
                                </Button>
                              </AlertDialogTrigger>
                              <AlertDialogContent>
                                <AlertDialogHeader>
                                  <AlertDialogTitle>{{ $t("pages.productGroupEditPage.removeProductConfirm.title") }}</AlertDialogTitle>
                                  <AlertDialogDescription>
                                    {{ $t("pages.productGroupEditPage.removeProductConfirm.description") }}
                                  </AlertDialogDescription>
                                </AlertDialogHeader>
                                <AlertDialogFooter>
                                  <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
                                  <AlertDialogAction
                                    variant="destructive"
                                    @click="handleRemoveProduct(member.product.id)"
                                    :disabled="isRemovingProduct"
                                  >
                                    <Spinner v-if="isRemovingProduct" class="mr-2" />
                                    {{ $t("common.delete") }}
                                  </AlertDialogAction>
                                </AlertDialogFooter>
                              </AlertDialogContent>
                            </AlertDialog>
                          </div>
                        </TableCell>
                      </TableRow>
                    </template>
                  </draggable>
                </table>
                </div>
              </div>
            </CardContent>
          </Card>

          <!-- Block 3: Marketplace merge -->
          <Card class="border-0 shadow-none !gap-3 !py-0">
            <CardHeader class="!px-0">
              <CardTitle class="text-base">
                {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.title") }}
              </CardTitle>
              <CardDescription>
                {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.description") }}
              </CardDescription>
              <CardAction>
                <Button
                  variant="outline"
                  size="icon"
                  class="size-8"
                  :title="$t('pages.productGroupEditPage.sections.marketplaceMerge.refreshStatuses')"
                  @click="fetchSyncStatus"
                  :disabled="isLoadingSyncStatus"
                >
                  <RefreshCw :class="['size-4', isLoadingSyncStatus && 'animate-spin']" />
                </Button>
              </CardAction>
            </CardHeader>
            <CardContent class="!px-0 space-y-4">
              <p
                v-if="connections.length === 0"
                class="text-sm text-muted-foreground"
              >
                {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.noConnections") }}
              </p>

              <div
                v-else-if="isLoadingConnectionGrouping"
                class="flex items-center gap-2 text-sm text-muted-foreground py-2"
              >
                <Spinner />
                {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.checkingCapabilities") }}
              </div>

              <template v-else>
                <section v-if="supportedConnections.length" class="space-y-2">
                  <p class="text-sm font-medium">
                    {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.supportedTitle") }}
                    <span class="font-normal text-muted-foreground">
                      · {{ supportedConnections.length }}
                    </span>
                  </p>
                  <div class="rounded-lg border overflow-hidden">
                    <ul class="divide-y divide-border/60">
                      <li
                        v-for="connection in supportedConnections"
                        :key="connection.id"
                        class="px-5 py-4 space-y-3"
                      >
                        <div class="flex items-start justify-between gap-4">
                          <div class="flex items-start gap-3 min-w-0">
                            <div
                              class="size-10 rounded-lg bg-muted flex items-center justify-center shrink-0"
                            >
                              <img
                                v-if="connection.logoUrl"
                                :src="connection.logoUrl"
                                :alt="connection.name"
                                class="size-6"
                              />
                              <AdapterIcon
                                v-else
                                :adapter-key="connection.adapterKey"
                                class="size-6"
                              />
                            </div>
                            <div class="min-w-0 space-y-1">
                              <div class="flex flex-wrap items-center gap-2">
                                <span class="font-medium">{{ connection.name }}</span>
                                <Badge variant="secondary" class="font-normal text-xs">
                                  {{ getGroupingTypeLabel(connection.id) }}
                                </Badge>
                              </div>
                              <p class="text-sm text-muted-foreground">
                                {{ connection.adapterKey }}
                              </p>
                            </div>
                          </div>

                          <div class="flex items-center gap-3 shrink-0">
                            <template v-if="getSyncStatusForConnection(connection.id)">
                              <div class="text-right space-y-1">
                                <Badge
                                  :variant="
                                    getSyncStatusVariant(
                                      getSyncStatusForConnection(connection.id).status,
                                    )
                                  "
                                  class="font-normal"
                                >
                                  {{
                                    getSyncStatusLabel(
                                      getSyncStatusForConnection(connection.id).status,
                                    )
                                  }}
                                </Badge>
                                <p
                                  v-if="
                                    getSyncStatusForConnection(connection.id).externalGroupId
                                  "
                                  class="text-xs text-muted-foreground font-mono"
                                >
                                  {{
                                    getSyncStatusForConnection(connection.id).externalGroupId
                                  }}
                                </p>
                              </div>
                            </template>

                            <Button
                              @click="handleExportGroup(connection.id)"
                              :disabled="isExporting || members.length === 0"
                              size="sm"
                            >
                              <Spinner v-if="isExporting" class="mr-2" />
                              <Upload v-else class="size-4 mr-2" />
                              {{ getExportButtonLabel(connection) }}
                            </Button>
                          </div>
                        </div>

                        <p
                          v-if="
                            getSyncStatusForConnection(connection.id)?.lastErrorMessage
                          "
                          class="rounded-md bg-destructive/10 px-3 py-2 text-sm text-destructive"
                        >
                          {{
                            getSyncStatusForConnection(connection.id).lastErrorMessage
                          }}
                        </p>
                      </li>
                    </ul>
                  </div>
                </section>

                <section v-if="unsupportedConnections.length" class="space-y-2">
                  <p class="text-sm font-medium text-muted-foreground">
                    {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.unsupportedTitle") }}
                    <span class="font-normal">
                      · {{ unsupportedConnections.length }}
                    </span>
                  </p>
                  <div class="rounded-lg border border-dashed bg-muted/20 overflow-hidden">
                    <ul class="divide-y divide-border/40">
                      <li
                        v-for="connection in unsupportedConnections"
                        :key="connection.id"
                        class="flex items-center gap-4 px-5 py-3.5"
                      >
                        <div
                          class="size-10 rounded-lg bg-muted/60 flex items-center justify-center shrink-0 opacity-70"
                        >
                          <img
                            v-if="connection.logoUrl"
                            :src="connection.logoUrl"
                            :alt="connection.name"
                            class="size-6 opacity-70"
                          />
                          <AdapterIcon
                            v-else
                            :adapter-key="connection.adapterKey"
                            class="size-6 opacity-70"
                          />
                        </div>
                        <div class="min-w-0 flex-1">
                          <div class="font-medium text-muted-foreground">
                            {{ connection.name }}
                          </div>
                          <p class="text-sm text-muted-foreground truncate">
                            {{ connection.adapterKey }}
                          </p>
                        </div>
                        <Badge
                          variant="outline"
                          class="shrink-0 font-normal text-muted-foreground"
                        >
                          {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.notSupported") }}
                        </Badge>
                      </li>
                    </ul>
                  </div>
                </section>
              </template>
            </CardContent>
          </Card>
        </template>
      </div>
    </PageSection>

    <!-- Product Search Dialog -->
    <Dialog v-model:open="showProductSearch">
      <DialogContent
        size="4xl"
        class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-5xl"
      >
        <DialogHeader variant="bar">
          <DialogTitle>
            {{ $t("pages.productGroupEditPage.addProductsDialog.title") }}
          </DialogTitle>
          <DialogDescription>
            {{ $t("pages.productGroupEditPage.addProductsDialog.description", { group: group?.name }) }}
          </DialogDescription>
        </DialogHeader>

        <div class="space-y-3 px-6 pb-4">
          <div class="flex gap-2">
            <div class="relative min-w-0 flex-1">
              <Search class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                v-model="productSearchQuery"
                :placeholder="$t('pages.productGroupEditPage.addProductsDialog.searchPlaceholder')"
                class="pl-9"
                @keyup.enter="searchProducts(true)"
              />
            </div>
            <Button
              variant="outline"
              :disabled="isSearchingProducts"
              @click="searchProducts(true)"
            >
              <Spinner v-if="isSearchingProducts" class="mr-2" />
              <Search v-else class="size-4 mr-2" />
              {{ $t("pages.productGroupEditPage.addProductsDialog.searchAction") }}
            </Button>
          </div>

          <div
            v-if="selectedProductsToAdd.length > 0"
            class="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-primary/20 bg-primary/5 px-3 py-2"
          >
            <div class="flex items-center gap-2 text-sm">
              <Badge variant="secondary" class="font-normal tabular-nums">
                {{ selectedProductsToAdd.length }}
              </Badge>
              <span>
                {{ $t("pages.productGroupEditPage.addProductsDialog.selectedToAdd", { unit: selectedProductsCountLabel }) }}
              </span>
            </div>
            <Button
              variant="ghost"
              size="sm"
              class="h-7 text-muted-foreground"
              @click="clearSelectedProducts"
            >
              {{ $t("pages.productGroupEditPage.addProductsDialog.clearSelection") }}
            </Button>
          </div>
        </div>

        <div class="flex min-h-0 flex-1 flex-col border-y">
          <div
            v-if="!isSearchingProducts && availableProductsFiltered.length > 0"
            class="grid shrink-0 grid-cols-[2.5rem_7.5rem_3.5rem_minmax(0,1fr)] items-center gap-3 border-b bg-muted/30 px-6 py-2 text-xs font-medium text-muted-foreground sm:grid-cols-[2.5rem_7.5rem_3.5rem_minmax(0,1fr)_9rem]"
          >
            <div class="flex justify-center">
              <Checkbox
                :model-value="allVisibleProductsSelected"
                :indeterminate="someVisibleProductsSelected"
                :aria-label="$t('pages.productGroupEditPage.addProductsDialog.selectAllOnPage')"
                @update:model-value="toggleSelectAllVisible"
              />
            </div>
            <span>{{ $t("pages.productGroupEditPage.table.sku") }}</span>
            <span />
            <span>{{ $t("common.name") }}</span>
            <span class="hidden sm:block">{{ $t("pages.productGroupEditPage.table.category") }}</span>
          </div>

          <div class="min-h-0 flex-1 overflow-y-auto thin-scrollbar">
            <div v-if="isSearchingProducts" class="flex items-center justify-center gap-2 py-16 text-sm text-muted-foreground">
              <Spinner size="xl" />
              {{ $t("pages.productGroupEditPage.addProductsDialog.searching") }}
            </div>

            <EmptyState
              v-else-if="availableProductsFiltered.length === 0"
              size="sm"
              :title="$t('pages.productGroupEditPage.addProductsDialog.noAvailableProductsTitle')"
              :description="$t('pages.productGroupEditPage.addProductsDialog.noAvailableProductsDescription')"
              class="py-12"
            >
              <template #icon>
                <Package />
              </template>
            </EmptyState>

            <div v-else class="divide-y divide-border/60">
              <label
                v-for="product in availableProductsFiltered"
                :key="product.id"
                class="grid cursor-pointer grid-cols-[2.5rem_7.5rem_3.5rem_minmax(0,1fr)] items-center gap-3 px-6 py-2.5 transition-colors hover:bg-accent/40 sm:grid-cols-[2.5rem_7.5rem_3.5rem_minmax(0,1fr)_9rem]"
                :class="isProductSelected(product.id) ? 'bg-primary/5' : ''"
              >
                <div class="flex justify-center">
                  <Checkbox
                    :model-value="isProductSelected(product.id)"
                    @update:model-value="toggleProductSelection(product, $event)"
                    @click.stop
                  />
                </div>
                <span class="truncate font-mono text-xs text-muted-foreground">
                  {{ product.sku || "-" }}
                </span>
                <div class="size-10 overflow-hidden rounded-md bg-muted">
                  <img
                    v-if="getProductImageUrl(product)"
                    :src="getProductImageUrl(product)"
                    :alt="product.name"
                    class="size-full object-cover"
                  />
                  <div
                    v-else
                    class="flex size-full items-center justify-center"
                  >
                    <Package class="size-4 text-muted-foreground" />
                  </div>
                </div>
                <span class="min-w-0 truncate text-sm font-medium">
                  {{ product.name }}
                </span>
                <span class="hidden truncate text-sm text-muted-foreground sm:block">
                  {{ getProductCategoryName(product) }}
                </span>
              </label>
            </div>
          </div>
        </div>

        <div
          v-if="!isSearchingProducts && productSearchTotalCount > 0"
          class="flex shrink-0 items-center justify-between gap-3 border-t bg-muted/20 px-6 py-2.5"
        >
          <p class="text-xs text-muted-foreground">
            {{ $t("pages.productGroupEditPage.addProductsDialog.totalInCatalog", { count: productSearchTotalCount }) }}
            <span v-if="availableProductsFiltered.length !== availableProducts.length">
              · {{ $t("pages.productGroupEditPage.addProductsDialog.availableOnPage", { count: availableProductsFiltered.length }) }}
            </span>
          </p>
          <div class="flex items-center gap-1.5">
            <span class="mr-1 text-sm tabular-nums text-muted-foreground">
              {{ productSearchPage }} / {{ productSearchLastPage }}
            </span>
            <Button
              variant="outline"
              size="sm"
              class="size-8 p-0"
              :disabled="productSearchIsFirstPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.firstPage')"
              @click="goToProductSearchPage(1)"
            >
              <ChevronsLeft class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="size-8 p-0"
              :disabled="productSearchIsFirstPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.prevPage')"
              @click="goToProductSearchPage(productSearchPage - 1)"
            >
              <ChevronLeft class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="size-8 p-0"
              :disabled="productSearchIsLastPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.nextPage')"
              @click="goToProductSearchPage(productSearchPage + 1)"
            >
              <ChevronRight class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="size-8 p-0"
              :disabled="productSearchIsLastPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.lastPage')"
              @click="goToProductSearchPage(productSearchLastPage)"
            >
              <ChevronsRight class="size-4" />
            </Button>
          </div>
        </div>

        <DialogFooter variant="bar" class="sm:justify-between">
          <p class="hidden text-xs text-muted-foreground sm:block">
            {{ $t("pages.productGroupEditPage.addProductsDialog.reorderHint") }}
          </p>
          <div class="flex w-full flex-col-reverse gap-2 sm:w-auto sm:flex-row sm:justify-end">
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button
              :disabled="isAddingProducts || selectedProductsToAdd.length === 0"
              @click="handleAddSelectedProducts"
            >
              <Spinner v-if="isAddingProducts" class="mr-2" />
              <Plus v-else class="size-4 mr-2" />
              <template v-if="selectedProductsToAdd.length > 0">
                {{ $t("pages.productGroupEditPage.addProductsDialog.addCount", { count: selectedProductsToAdd.length, unit: selectedProductsCountLabel }) }}
              </template>
              <template v-else>
                {{ $t("pages.productGroupEditPage.addProductsDialog.addToGroup") }}
              </template>
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </AppPageChrome>
</template>
