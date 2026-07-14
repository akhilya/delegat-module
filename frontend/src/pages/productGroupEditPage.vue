<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";

import Badge from "@/components/ui/badge/Badge.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
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
  CardContent,
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
  Loader2,
  Save,
  Plus,
  Trash2,
  GripVertical,
  Package,
  Upload,
  RefreshCw,
  CheckCircle,
  XCircle,
  Clock,
  Search,
  X,
  ExternalLink,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_PRODUCT_GROUPS, setPageTitle } from "@/router";
import draggable from "vuedraggable";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Textarea,
    Switch,
    Label,

    Badge,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
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
    CardContent,
    CardHeader,
    CardTitle,
    TableHeader,
    TableHead,
    TableRow,
    TableCell,
    Checkbox,
    Loader2,
    Save,
    Plus,
    Trash2,
    GripVertical,
    Package,
    Upload,
    RefreshCw,
    CheckCircle,
    XCircle,
    Clock,
    Search,
    X,
    ExternalLink,
    draggable,
    EmptyState,
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
      availableProducts: [],
      isSearchingProducts: false,
      selectedProductsToAdd: [],
      PAGE_PRODUCT_GROUPS,
    };
  },
  computed: {
    breadcrumbs() {
      const items = [{ label: "Группы товаров", to: PAGE_PRODUCT_GROUPS }];
      if (this.isCreateMode) {
        items.push({ label: "Новая группа" });
      } else if (this.group?.name) {
        items.push({ label: this.group.name });
      } else {
        items.push({ label: "Редактирование" });
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
      if (grouping.fieldBased) return "Через поле карточки";
      if (grouping.endpointBased) return "Через API слияния";
      return "Поддерживается";
    },

    getExportButtonLabel(connection) {
      const linked = this.getSyncStatusForConnection(connection.id);
      const grouping = this.connectionGrouping[connection.id];
      if (linked) return "Обновить привязку";
      if (grouping?.fieldBased) return "Привязать группу";
      if (grouping?.endpointBased) return "Объединить карточки";
      return "Экспортировать";
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
        displaySonnerError("Введите название группы");
        return;
      }

      this.isSaving = true;

      if (this.isCreateMode) {
        await this.createGroup({
          name: this.group.name.trim(),
          description: this.group.description?.trim() || null,
          productIds: [],
          onSuccess: (response) => {
            displaySonnerSuccess("Группа создана");
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
            displaySonnerSuccess("Группа сохранена");
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      }

      this.isSaving = false;
    },

    async searchProducts() {
      this.isSearchingProducts = true;
      await this.getAllProducts({
        searchQuery: this.productSearchQuery,
        startIndex: 0,
        pageSize: 50,
        onSuccess: (response) => {
          this.availableProducts = response.data.value.items || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSearchingProducts = false;
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

    async handleAddSelectedProducts() {
      if (this.selectedProductsToAdd.length === 0) return;

      this.isAddingProducts = true;
      const productIds = this.selectedProductsToAdd.map((p) => p.id);

      await this.addProductsToGroup({
        groupId: this.groupId,
        productIds,
        onSuccess: () => {
          displaySonnerSuccess(`Добавлено ${productIds.length} товаров`);
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
          displaySonnerSuccess("Товар удалён из группы");
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
      displaySonnerSuccess("Порядок сохранён");
    },

    async handleExportGroup(connectionId) {
      this.isExporting = true;
      await this.exportGroup({
        groupId: this.groupId,
        connectionId,
        onSuccess: (response) => {
          const data = response.data.value;
          const msg = data.message || data.externalGroupId || "Готово";
          if (data.isFieldBased) {
            displaySonnerSuccess(`Привязка создана. ${msg}`);
          } else {
            displaySonnerSuccess(`Карточки объединены на маркетплейсе. ${msg}`);
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
          return "Синхронизировано";
        case "Pending":
          return "Ожидает";
        case "Error":
          return "Ошибка";
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
      this.selectedProductsToAdd = [];
      this.searchProducts();
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
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageSection :loading="isLoading" class="flex flex-col">
      <div v-if="group" class="grow flex flex-col">
        <!-- Group Info Section -->
        <div class="space-y-4">
          <h2 class="text-lg font-semibold">Основные данные</h2>
          <div class="grid gap-4 max-w-xl">
            <div class="space-y-2">
              <Label>Название группы</Label>
              <Input v-model="group.name" placeholder="Введите название" />
            </div>
            <div class="space-y-2">
              <Label>Описание</Label>
              <Textarea
                v-model="group.description"
                placeholder="Описание группы (необязательно)"
                class="h-24"
              />
            </div>
            <Button
              @click="handleSaveGroup"
              :disabled="isSaving"
              class="w-fit"
            >
              <Loader2 v-if="isSaving" class="size-4 mr-2 animate-spin" />
              <Save v-else class="size-4 mr-2" />
              {{ isCreateMode ? 'Создать группу' : 'Сохранить' }}
            </Button>
          </div>
        </div>

        <template v-if="!isCreateMode">
          <!-- Products Section -->
          <div class="mt-8 space-y-4">
            <div class="flex justify-between items-center">
              <h2 class="text-lg font-semibold flex items-center gap-2">
                <Package class="size-5" />
                Товары в группе
                <span class="text-sm font-normal text-muted-foreground">({{ members.length }})</span>
              </h2>
              <Button @click="openProductSearchDialog" variant="outline">
                <Plus class="size-4 mr-2" />
                Добавить товары
              </Button>
            </div>

            <EmptyState
              v-if="members.length === 0"
              title="Нет товаров"
              description="Добавьте товары, которые должны объединяться на маркетплейсе в одну карточку"
            >
              <template #icon>
                <Package />
              </template>
            </EmptyState>

            <div
              v-else
              class="overflow-hidden rounded-lg border bg-card"
            >
              <div class="max-h-[min(28rem,50vh)] overflow-y-auto thin-scrollbar">
                <table class="w-full caption-bottom text-sm">
                  <TableHeader class="sticky top-0 z-10 bg-card [&_tr]:border-b">
                    <TableRow class="hover:bg-transparent">
                      <TableHead class="w-9 px-2" />
                      <TableHead class="w-9 px-2 text-center">#</TableHead>
                      <TableHead class="w-14 px-2" />
                      <TableHead>Название</TableHead>
                      <TableHead class="w-36 hidden sm:table-cell">SKU</TableHead>
                      <TableHead class="hidden md:table-cell">Категория</TableHead>
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
                        <TableCell class="w-9 px-2">
                          <GripVertical
                            class="drag-handle size-4 text-muted-foreground/70 cursor-grab active:cursor-grabbing hover:text-foreground"
                          />
                        </TableCell>
                        <TableCell class="w-9 px-2 text-center text-xs text-muted-foreground tabular-nums">
                          {{ index + 1 }}
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
                        <TableCell class="max-w-0">
                          <router-link
                            :to="`/products/${member.product.id}/edit`"
                            class="font-medium truncate block hover:underline"
                          >
                            {{ member.product.name }}
                          </router-link>
                          <div class="text-xs text-muted-foreground truncate sm:hidden">
                            <span v-if="member.product.sku">{{ member.product.sku }}</span>
                          </div>
                        </TableCell>
                        <TableCell class="hidden sm:table-cell font-mono text-xs text-muted-foreground">
                          {{ member.product.sku || "—" }}
                        </TableCell>
                        <TableCell class="hidden md:table-cell text-muted-foreground">
                          {{ member.product.category?.name || "—" }}
                        </TableCell>
                        <TableCell class="w-12 px-2">
                          <div class="opacity-0 group-hover:opacity-100 transition-opacity">
                            <AlertDialog>
                              <AlertDialogTrigger as-child>
                                <Button variant="ghost" size="icon" class="size-8">
                                  <Trash2 class="size-4 text-destructive" />
                                </Button>
                              </AlertDialogTrigger>
                              <AlertDialogContent>
                                <AlertDialogHeader>
                                  <AlertDialogTitle>Удалить товар из группы?</AlertDialogTitle>
                                  <AlertDialogDescription>
                                    Товар останется в каталоге, но будет исключён из группы.
                                  </AlertDialogDescription>
                                </AlertDialogHeader>
                                <AlertDialogFooter>
                                  <AlertDialogCancel>Отмена</AlertDialogCancel>
                                  <AlertDialogAction
                                    @click="handleRemoveProduct(member.product.id)"
                                    :disabled="isRemovingProduct"
                                    class="bg-destructive text-white hover:bg-destructive/90"
                                  >
                                    <Loader2
                                      v-if="isRemovingProduct"
                                      class="size-4 mr-2 animate-spin"
                                    />
                                    Удалить
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
          </div>

          <!-- Marketplace links -->
          <div class="mt-8 space-y-4">
            <div class="flex justify-between items-start gap-3">
              <div class="space-y-1">
                <h2 class="text-lg font-semibold flex items-center gap-2">
                  <Upload class="size-5" />
                  Объединение на маркетплейсах
                </h2>
                <p class="text-sm text-muted-foreground max-w-2xl">
                  Группа в каталоге — список товаров для одной карточки. Ozon объединяет через поле в
                  карточке (после привязки переэкспортируйте товары). Wildberries — через API слияния
                  карточек, если товары уже экспортированы.
                </p>
              </div>
              <Button
                variant="ghost"
                size="icon"
                class="size-8 shrink-0"
                title="Обновить статусы"
                @click="fetchSyncStatus"
                :disabled="isLoadingSyncStatus"
              >
                <RefreshCw :class="['size-4', isLoadingSyncStatus && 'animate-spin']" />
              </Button>
            </div>

            <div v-if="connections.length === 0" class="text-sm text-muted-foreground">
              Нет подключённых адаптеров
            </div>

            <div
              v-else-if="isLoadingConnectionGrouping"
              class="flex items-center gap-2 text-sm text-muted-foreground"
            >
              <Loader2 class="size-4 animate-spin" />
              Проверка возможностей адаптеров…
            </div>

            <div v-else class="space-y-6">
              <section v-if="supportedConnections.length" class="space-y-3">
                <h3 class="text-sm font-medium">
                  Поддерживают объединение
                  <span class="font-normal text-muted-foreground">
                    ({{ supportedConnections.length }})
                  </span>
                </h3>
                <Card
                  v-for="connection in supportedConnections"
                  :key="connection.id"
                >
                  <CardContent class="py-4">
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
                          <ExternalLink
                            v-else
                            class="size-5 text-muted-foreground"
                          />
                        </div>
                        <div class="min-w-0 space-y-1">
                          <div class="flex flex-wrap items-center gap-2">
                            <span class="font-medium">{{ connection.name }}</span>
                            <Badge variant="secondary" class="font-normal">
                              {{ getGroupingTypeLabel(connection.id) }}
                            </Badge>
                          </div>
                          <div class="text-sm text-muted-foreground">
                            {{ connection.adapterKey }}
                          </div>
                        </div>
                      </div>

                      <div class="flex items-center gap-3 shrink-0">
                        <template v-if="getSyncStatusForConnection(connection.id)">
                          <div class="text-right">
                            <Badge
                              :variant="
                                getSyncStatusVariant(
                                  getSyncStatusForConnection(connection.id).status,
                                )
                              "
                            >
                              {{
                                getSyncStatusLabel(
                                  getSyncStatusForConnection(connection.id).status,
                                )
                              }}
                            </Badge>
                            <div
                              v-if="
                                getSyncStatusForConnection(connection.id).externalGroupId
                              "
                              class="text-xs text-muted-foreground mt-1"
                            >
                              ID:
                              {{
                                getSyncStatusForConnection(connection.id).externalGroupId
                              }}
                            </div>
                          </div>
                        </template>

                        <Button
                          @click="handleExportGroup(connection.id)"
                          :disabled="isExporting || members.length === 0"
                          size="sm"
                        >
                          <Loader2
                            v-if="isExporting"
                            class="size-4 mr-2 animate-spin"
                          />
                          <Upload v-else class="size-4 mr-2" />
                          {{ getExportButtonLabel(connection) }}
                        </Button>
                      </div>
                    </div>

                    <div
                      v-if="
                        getSyncStatusForConnection(connection.id)?.lastErrorMessage
                      "
                      class="mt-3 rounded-md bg-destructive/10 p-2 text-sm text-destructive"
                    >
                      {{
                        getSyncStatusForConnection(connection.id).lastErrorMessage
                      }}
                    </div>
                  </CardContent>
                </Card>
              </section>

              <section v-if="unsupportedConnections.length" class="space-y-3">
                <h3 class="text-sm font-medium text-muted-foreground">
                  Без поддержки объединения
                  <span class="font-normal">
                    ({{ unsupportedConnections.length }})
                  </span>
                </h3>
                <Card
                  v-for="connection in unsupportedConnections"
                  :key="connection.id"
                  class="border-dashed bg-muted/20"
                >
                  <CardContent class="py-4">
                    <div class="flex items-start justify-between gap-4">
                      <div class="flex items-start gap-3 min-w-0">
                        <div
                          class="size-10 rounded-lg bg-muted/60 flex items-center justify-center shrink-0 opacity-70"
                        >
                          <img
                            v-if="connection.logoUrl"
                            :src="connection.logoUrl"
                            :alt="connection.name"
                            class="size-6 opacity-70"
                          />
                          <ExternalLink
                            v-else
                            class="size-5 text-muted-foreground/70"
                          />
                        </div>
                        <div class="min-w-0 space-y-1">
                          <div class="font-medium text-muted-foreground">
                            {{ connection.name }}
                          </div>
                          <div class="text-sm text-muted-foreground">
                            {{ connection.adapterKey }}
                          </div>
                          <p class="text-xs text-muted-foreground pt-0.5">
                            Адаптер не умеет объединять товары в одну карточку на
                            маркетплейсе.
                          </p>
                        </div>
                      </div>
                      <Badge
                        variant="outline"
                        class="shrink-0 font-normal text-muted-foreground"
                      >
                        Не поддерживается
                      </Badge>
                    </div>
                  </CardContent>
                </Card>
              </section>
            </div>
          </div>
        </template>
      </div>
    </PageSection>

    <!-- Product Search Dialog -->
    <Dialog v-model:open="showProductSearch">
      <DialogContent size="2xl" class="max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Добавить товары</DialogTitle>
        </DialogHeader>

        <div class="flex flex-col gap-4 grow overflow-hidden">
          <div class="flex gap-2">
            <Input
              v-model="productSearchQuery"
              placeholder="Поиск по названию или SKU..."
              @keyup.enter="searchProducts"
              class="grow"
            />
            <Button @click="searchProducts" :disabled="isSearchingProducts" variant="outline" size="icon">
              <Loader2 v-if="isSearchingProducts" class="size-4 animate-spin" />
              <Search v-else class="size-4" />
            </Button>
          </div>

          <div class="grow overflow-auto border rounded-lg">
            <div v-if="isSearchingProducts" class="flex justify-center py-8">
              <Loader2 class="size-6 animate-spin text-muted-foreground" />
            </div>

            <EmptyState
              v-else-if="availableProductsFiltered.length === 0"
              size="sm"
              title="Нет доступных товаров"
              description="Все товары уже в группе, или поиск не дал результатов."
            >
              <template #icon>
                <Package />
              </template>
            </EmptyState>

            <div v-else class="divide-y">
              <label
                v-for="product in availableProductsFiltered"
                :key="product.id"
                class="p-3 flex items-center gap-3 cursor-pointer hover:bg-accent/50 transition-colors"
              >
                <Checkbox
                  :model-value="isProductSelected(product.id)"
                  @update:model-value="toggleProductSelection(product, $event)"
                />
                <div class="w-10 h-10 rounded-md overflow-hidden bg-muted shrink-0">
                  <img
                    v-if="getProductImageUrl(product)"
                    :src="getProductImageUrl(product)"
                    :alt="product.name"
                    class="w-full h-full object-cover"
                  />
                  <div v-else class="w-full h-full flex items-center justify-center">
                    <Package class="size-5 text-muted-foreground" />
                  </div>
                </div>
                <div class="grow min-w-0">
                  <div class="font-medium truncate">{{ product.name }}</div>
                  <div class="text-sm text-muted-foreground">{{ product.sku }}</div>
                </div>
              </label>
            </div>
          </div>
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button
            @click="handleAddSelectedProducts"
            :disabled="isAddingProducts || selectedProductsToAdd.length === 0"
          >
            <Loader2 v-if="isAddingProducts" class="size-4 mr-2 animate-spin" />
            <template v-else>
              Добавить
              <span v-if="selectedProductsToAdd.length > 0" class="ml-1">({{ selectedProductsToAdd.length }})</span>
            </template>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
