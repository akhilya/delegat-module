<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import NavSearchField from "@/components/NavSearchField.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";

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
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
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
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import { setPageTitle } from "@/router";
import draggable from "vuedraggable";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import ProductImageCell from "@/components/products/ProductImageCell.vue";

export default {
  components: {
    Spinner,
    AppPageChrome,
    NavSearchField,
    PageSection,
    Button,
    Input,
    Textarea,
    SettingsFormField,
    SettingsFormSection,
    AdapterIcon,
    ProductImageCell,
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
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
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
      FIELD_LIMITS,
    };
  },
  computed: {
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
      const nameIssue = fieldLimitIssue(
        this.group.name,
        FIELD_LIMITS.groupName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(this.$t("pages.productGroupEditPage.toast.nameRequired"));
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.groupName }),
        );
        return;
      }
      if (
        fieldLimitIssue(
          this.group.description,
          FIELD_LIMITS.groupDescription,
          { required: false },
        ) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.groupDescription }),
        );
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

    getProductThumbnailUrl(product) {
      if (product.images && product.images.length > 0) {
        return product.images[0].thumbnailUrl ?? product.images[0].url ?? null;
      }
      return product.primaryImageThumbnail ?? product.primaryImage ?? null;
    },

    getProductFullImageUrl(product) {
      if (product.images && product.images.length > 0) {
        return product.images[0].url ?? product.images[0].thumbnailUrl ?? null;
      }
      return product.primaryImage ?? product.primaryImageThumbnail ?? null;
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
  <AppPageChrome>
    <PageSection :loading="isLoading" class="!min-h-0">
      <div v-if="group" class="max-w-2xl space-y-1">
        <SettingsFormSection>
          <SettingsFormField :label="$t('common.name')" label-for="group-name">
            <Input
              id="group-name"
              v-model="group.name"
              :placeholder="$t('pages.productGroupEditPage.fields.name.placeholder')"
              :maxlength="FIELD_LIMITS.groupName"
            />
          </SettingsFormField>
          <SettingsFormField :label="$t('common.description')" label-for="group-description">
            <Textarea
              id="group-description"
              v-model="group.description"
              :placeholder="$t('pages.productGroupEditPage.fields.description.placeholder')"
              class="min-h-24 resize-none"
              :maxlength="FIELD_LIMITS.groupDescription"
            />
          </SettingsFormField>
          <template #footer>
            <Button
              @click="handleSaveGroup"
              :disabled="isSaving"
            >
              <Spinner v-if="isSaving" class="mr-2" />
              <Save v-else class="size-4 mr-2" />
              {{ isCreateMode ? $t("pages.productGroupEditPage.actions.createGroup") : $t("common.save") }}
            </Button>
          </template>
        </SettingsFormSection>

        <template v-if="!isCreateMode">
          <SettingsFormSection
            class="!max-w-none"
            :title="$t('pages.productGroupEditPage.sections.groupProducts.title')"
            :description="$t('pages.productGroupEditPage.sections.groupProducts.description', {
              count: members.length,
              unit: membersCountLabel,
            })"
          >
            <template #actions>
              <Button
                variant="outline"
                size="sm"
                @click="openProductSearchDialog"
              >
                <Plus class="size-4 mr-2" />
                {{ $t("common.add") }}
              </Button>
            </template>
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
            <InsetGroup v-else scrollable class="max-h-[min(42rem,70vh)]">
              <draggable
                v-model="members"
                item-key="id"
                handle=".drag-handle"
                ghost-class="opacity-50"
                class="flex flex-col gap-0.5"
                tag="div"
                @end="handleReorder"
              >
                <template #item="{ element: member, index }">
                  <div>
                    <InsetGroupItem as="div">
                      <template #leading>
                        <span
                          class="drag-handle pointer-events-auto flex size-8 cursor-grab items-center justify-center active:cursor-grabbing"
                          @click.stop
                        >
                          <GripVertical class="size-4 text-muted-foreground/70" />
                        </span>
                      </template>
                      <p class="truncate">
                        <router-link
                          :to="`/products/${member.product.id}/edit`"
                          class="font-medium hover:underline"
                        >
                          {{ member.product.name }}
                        </router-link>
                      </p>
                      <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
                        {{ index + 1 }}
                        · {{ member.product.sku || $t("common.emDash") }}
                        <template v-if="member.product.category?.name">
                          · {{ member.product.category.name }}
                        </template>
                      </p>
                      <template #trailing>
                        <div class="flex items-center gap-2">
                          <ProductImageCell
                            :thumbnail-url="getProductThumbnailUrl(member.product)"
                            :image-url="getProductFullImageUrl(member.product)"
                            image-class="size-8 shrink-0 rounded-md"
                          />
                          <AlertDialog>
                            <AlertDialogTrigger as-child>
                              <Button
                                variant="ghost"
                                size="icon"
                                reveal-on-row-hover
                                class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                                :aria-label="$t('common.delete')"
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
                                  :disabled="isRemovingProduct"
                                  @click="handleRemoveProduct(member.product.id)"
                                >
                                  <Spinner v-if="isRemovingProduct" class="mr-2" />
                                  {{ $t("common.delete") }}
                                </AlertDialogAction>
                              </AlertDialogFooter>
                            </AlertDialogContent>
                          </AlertDialog>
                        </div>
                      </template>
                    </InsetGroupItem>
                  </div>
                </template>
              </draggable>
            </InsetGroup>
          </SettingsFormSection>

          <SettingsFormSection
            class="!max-w-none"
            :title="$t('pages.productGroupEditPage.sections.marketplaceMerge.title')"
            :description="$t('pages.productGroupEditPage.sections.marketplaceMerge.description')"
          >
            <template #actions>
              <Button
                variant="ghost"
                size="icon"
                class="size-8"
                :title="$t('pages.productGroupEditPage.sections.marketplaceMerge.refreshStatuses')"
                :disabled="isLoadingSyncStatus"
                @click="fetchSyncStatus"
              >
                <RefreshCw :class="['size-4', isLoadingSyncStatus && 'animate-spin']" />
              </Button>
            </template>
            <p
              v-if="connections.length === 0"
              class="px-1 text-xs text-muted-foreground"
            >
              {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.noConnections") }}
            </p>

            <div
              v-else-if="isLoadingConnectionGrouping"
              class="flex items-center gap-2 px-1 py-2 text-sm text-muted-foreground"
            >
              <Spinner />
              {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.checkingCapabilities") }}
            </div>

            <template v-else>
              <div v-if="supportedConnections.length" class="flex flex-col gap-3">
                <InsetGroupLabel class="mb-0">
                  {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.supportedTitle") }}
                  · {{ supportedConnections.length }}
                </InsetGroupLabel>
                <InsetGroup>
                  <InsetGroupItem
                    v-for="connection in supportedConnections"
                    :key="connection.id"
                    as="div"
                  >
                    <template #leading>
                      <AdapterIcon :adapter-key="connection.adapterKey" class="size-4" />
                    </template>
                    <p class="truncate">{{ connection.name }}</p>
                    <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
                      {{ getGroupingTypeLabel(connection.id) }}
                      · {{ connection.adapterKey }}
                      <template v-if="getSyncStatusForConnection(connection.id)">
                        · {{ getSyncStatusLabel(getSyncStatusForConnection(connection.id).status) }}
                        <template v-if="getSyncStatusForConnection(connection.id).externalGroupId">
                          · {{ getSyncStatusForConnection(connection.id).externalGroupId }}
                        </template>
                      </template>
                    </p>
                    <p
                      v-if="getSyncStatusForConnection(connection.id)?.lastErrorMessage"
                      class="mt-0.5 text-xs text-destructive"
                    >
                      {{ getSyncStatusForConnection(connection.id).lastErrorMessage }}
                    </p>
                    <template #trailing>
                      <Button
                        size="sm"
                        :disabled="isExporting || members.length === 0"
                        @click="handleExportGroup(connection.id)"
                      >
                        <Spinner v-if="isExporting" class="mr-2" />
                        <Upload v-else class="size-4 mr-2" />
                        {{ getExportButtonLabel(connection) }}
                      </Button>
                    </template>
                  </InsetGroupItem>
                </InsetGroup>
              </div>

              <div v-if="unsupportedConnections.length" class="flex flex-col gap-3">
                <InsetGroupLabel class="mb-0">
                  {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.unsupportedTitle") }}
                  · {{ unsupportedConnections.length }}
                </InsetGroupLabel>
                <InsetGroup>
                  <InsetGroupItem
                    v-for="connection in unsupportedConnections"
                    :key="connection.id"
                    as="div"
                  >
                    <template #leading>
                      <AdapterIcon
                        :adapter-key="connection.adapterKey"
                        class="size-4 opacity-70"
                      />
                    </template>
                    <p class="truncate text-muted-foreground">{{ connection.name }}</p>
                    <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
                      {{ connection.adapterKey }}
                    </p>
                    <template #trailing>
                      <span class="text-xs text-muted-foreground">
                        {{ $t("pages.productGroupEditPage.sections.marketplaceMerge.notSupported") }}
                      </span>
                    </template>
                  </InsetGroupItem>
                </InsetGroup>
              </div>
            </template>
          </SettingsFormSection>
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

        <div class="shrink-0 space-y-3 px-6 py-5">
          <div class="flex gap-2">
            <NavSearchField
              v-model="productSearchQuery"
              class="min-w-0 flex-1"
              :placeholder="$t('pages.productGroupEditPage.addProductsDialog.searchPlaceholder')"
              @submit="searchProducts(true)"
            />
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
        </div>

        <div class="flex min-h-0 flex-1 flex-col overflow-hidden px-6 pb-4">
          <div v-if="isSearchingProducts" class="flex h-full items-center justify-center gap-2 py-16 text-sm text-muted-foreground">
            <Spinner size="xl" />
            {{ $t("pages.productGroupEditPage.addProductsDialog.searching") }}
          </div>

          <EmptyState
            v-else-if="availableProductsFiltered.length === 0"
            size="sm"
            :title="$t('pages.productGroupEditPage.addProductsDialog.noAvailableProductsTitle')"
            :description="$t('pages.productGroupEditPage.addProductsDialog.noAvailableProductsDescription')"
            class="h-full py-12"
          >
            <template #icon>
              <Package />
            </template>
          </EmptyState>

          <InsetGroup v-else scrollable>
            <InsetGroupItem as="div" @click="toggleSelectAllVisible(!allVisibleProductsSelected)">
              <template #leading>
                <span @click.stop>
                  <Checkbox
                    :model-value="allVisibleProductsSelected"
                    :indeterminate="someVisibleProductsSelected"
                    :aria-label="$t('pages.productGroupEditPage.addProductsDialog.selectAllOnPage')"
                    @update:model-value="toggleSelectAllVisible"
                  />
                </span>
              </template>
              <p class="text-muted-foreground text-sm font-normal">
                {{ $t("pages.productGroupEditPage.addProductsDialog.selectAllOnPage") }}
              </p>
            </InsetGroupItem>
            <InsetGroupItem
              v-for="product in availableProductsFiltered"
              :key="product.id"
              as="div"
              @click="toggleProductSelection(product, !isProductSelected(product.id))"
            >
              <template #leading>
                <span @click.stop>
                  <Checkbox
                    :model-value="isProductSelected(product.id)"
                    @update:model-value="toggleProductSelection(product, $event)"
                  />
                </span>
              </template>
              <p class="truncate">{{ product.name }}</p>
              <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
                {{ product.sku || $t("common.emDash") }}
                <template v-if="getProductCategoryName(product)">
                  · {{ getProductCategoryName(product) }}
                </template>
              </p>
              <template #trailing>
                <ProductImageCell
                  :thumbnail-url="getProductThumbnailUrl(product)"
                  :image-url="getProductFullImageUrl(product)"
                  image-class="size-8 shrink-0 rounded-md"
                  :stop-events="false"
                />
              </template>
            </InsetGroupItem>
          </InsetGroup>
        </div>

        <div
          class="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t bg-background px-6 py-2"
        >
          <div class="flex min-w-0 flex-wrap items-center gap-2">
            <p
              v-if="productSearchTotalCount > 0"
              class="text-sm text-muted-foreground"
            >
              {{ $t("pages.productGroupEditPage.addProductsDialog.totalInCatalog", { count: productSearchTotalCount }) }}
              <span v-if="availableProductsFiltered.length !== availableProducts.length">
                · {{ $t("pages.productGroupEditPage.addProductsDialog.availableOnPage", { count: availableProductsFiltered.length }) }}
              </span>
            </p>
            <template v-if="selectedProductsToAdd.length > 0">
              <div class="h-4 w-px bg-border" />
              <span class="text-sm font-medium">
                {{ selectedProductsToAdd.length }}
                {{ $t("pages.productGroupEditPage.addProductsDialog.selectedToAdd", { unit: selectedProductsCountLabel }) }}
              </span>
              <Button
                variant="ghost"
                size="sm"
                class="h-8"
                @click="clearSelectedProducts"
              >
                <X class="h-4 w-4" />
              </Button>
            </template>
          </div>
          <div
            v-if="!isSearchingProducts && productSearchTotalCount > 0"
            class="flex items-center gap-2"
          >
            <p class="mr-2 text-sm text-muted-foreground">
              {{ productSearchPage }} / {{ productSearchLastPage }}
            </p>
            <Button
              variant="outline"
              size="sm"
              class="h-8 w-8 p-0"
              :disabled="productSearchIsFirstPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.firstPage')"
              @click="goToProductSearchPage(1)"
            >
              <ChevronsLeft class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="h-8 w-8 p-0"
              :disabled="productSearchIsFirstPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.prevPage')"
              @click="goToProductSearchPage(productSearchPage - 1)"
            >
              <ChevronLeft class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="h-8 w-8 p-0"
              :disabled="productSearchIsLastPage || isSearchingProducts"
              :aria-label="$t('pages.productGroupEditPage.addProductsDialog.nextPage')"
              @click="goToProductSearchPage(productSearchPage + 1)"
            >
              <ChevronRight class="size-4" />
            </Button>
            <Button
              variant="outline"
              size="sm"
              class="h-8 w-8 p-0"
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
