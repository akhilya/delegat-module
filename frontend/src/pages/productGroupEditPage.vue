<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import Label from "@/components/ui/label/Label.vue";
import Separator from "@/components/ui/separator/Separator.vue";
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
import { PAGE_PRODUCT_GROUPS } from "@/router";
import draggable from "vuedraggable";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Textarea,
    Switch,
    Label,
    Separator,
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
  },
  data() {
    const groupId = this.$route.params.id ? parseInt(this.$route.params.id) : null;
    return {
      groupId,
      isCreateMode: !groupId,
      group: null,
      members: [],
      connections: [],
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
          if (data.isFieldBased) {
            displaySonnerSuccess(`Группа экспортирована (field-based): ${data.externalGroupId || 'ID назначен'}`);
          } else {
            displaySonnerSuccess(`Группа экспортирована: ${data.externalGroupId || 'ID назначен'}`);
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
        return product.images[0].url;
      }
      // Handle ProductReviewDTO structure (product search results)
      if (product.primaryImage) {
        return product.primaryImage;
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

    <div class="border rounded-lg h-full w-full grow mb-4 flex flex-col overflow-hidden">
      <div v-if="isLoading" class="grow flex items-center justify-center">
        <Loader2 class="text-primary size-8 animate-spin" />
      </div>

      <div v-else-if="group" class="grow flex flex-col overflow-auto">
        <!-- Group Info Section -->
        <div class="p-4 space-y-4">
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
          <Separator />

          <!-- Products Section -->
          <div class="p-4 space-y-4">
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

            <div v-if="members.length === 0" class="flex flex-col items-center justify-center py-12 text-center border rounded-lg">
              <Package class="size-12 mb-4 text-muted-foreground" />
              <h3 class="font-medium mb-1">Нет товаров</h3>
              <p class="text-sm text-muted-foreground">Добавьте товары в группу для синхронизации</p>
            </div>

            <draggable
              v-else
              v-model="members"
              item-key="id"
              handle=".drag-handle"
              ghost-class="opacity-50"
              class="space-y-2"
              @end="handleReorder"
            >
              <template #item="{ element: member }">
                <Card class="group">
                  <CardContent class="flex items-center gap-4">
                    <GripVertical class="drag-handle size-5 min-w-5 text-muted-foreground cursor-grab active:cursor-grabbing shrink-0" />

                    <!-- Product Image -->
                    <div class="w-12 h-12 rounded-md overflow-hidden bg-muted shrink-0">
                      <img
                        v-if="getProductImageUrl(member.product)"
                        :src="getProductImageUrl(member.product)"
                        :alt="member.product.name"
                        class="w-full h-full object-cover"
                      />
                      <div v-else class="w-full h-full flex items-center justify-center">
                        <Package class="size-6 text-muted-foreground" />
                      </div>
                    </div>

                    <!-- Product Info -->
                    <div class="grow min-w-0">
                      <div class="flex items-center gap-2">
                        <span class="font-medium truncate">{{ member.product.name }}</span>
                      </div>
                      <div class="text-sm text-muted-foreground flex items-center gap-2">
                        <span v-if="member.product.sku">SKU: {{ member.product.sku }}</span>
                        <span v-if="member.product.category">{{ member.product.category.name }}</span>
                      </div>
                    </div>

                    <!-- Actions -->
                    <div class="flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity shrink-0">
                      <AlertDialog>
                        <AlertDialogTrigger as-child>
                          <Button variant="ghost" size="sm">
                            <Trash2 class="size-4 text-destructive" />
                          </Button>
                        </AlertDialogTrigger>
                        <AlertDialogContent>
                          <AlertDialogHeader>
                            <AlertDialogTitle>Удалить товар из группы?</AlertDialogTitle>
                            <AlertDialogDescription>
                              Вы уверены что хотите удалить "{{ member.product.name }}" из группы?
                            </AlertDialogDescription>
                          </AlertDialogHeader>
                          <AlertDialogFooter>
                            <AlertDialogCancel>Отмена</AlertDialogCancel>
                            <AlertDialogAction
                              @click="handleRemoveProduct(member.product.id)"
                              :disabled="isRemovingProduct"
                            >
                              <Loader2 v-if="isRemovingProduct" class="size-4 mr-2 animate-spin" />
                              Удалить
                            </AlertDialogAction>
                          </AlertDialogFooter>
                        </AlertDialogContent>
                      </AlertDialog>
                    </div>
                  </CardContent>
                </Card>
              </template>
            </draggable>
          </div>

          <Separator />

          <!-- Marketplace Sync Section -->
          <div class="p-4 space-y-4">
            <div class="flex justify-between items-center">
              <h2 class="text-lg font-semibold flex items-center gap-2">
                <Upload class="size-5" />
                Синхронизация с маркетплейсами
              </h2>
              <Button
                variant="ghost"
                size="sm"
                @click="fetchSyncStatus"
                :disabled="isLoadingSyncStatus"
              >
                <RefreshCw :class="['size-4', isLoadingSyncStatus && 'animate-spin']" />
              </Button>
            </div>

            <div v-if="connections.length === 0" class="text-sm text-muted-foreground">
              Нет подключенных маркетплейсов
            </div>

            <div v-else class="space-y-3">
              <Card v-for="connection in connections" :key="connection.id">
                <CardContent>
                  <div class="flex items-center justify-between">
                    <div class="flex items-center gap-3">
                      <div class="w-10 h-10 rounded-lg bg-muted flex items-center justify-center">
                        <img
                          v-if="connection.logoUrl"
                          :src="connection.logoUrl"
                          :alt="connection.name"
                          class="w-6 h-6"
                        />
                        <ExternalLink v-else class="size-5 text-muted-foreground" />
                      </div>
                      <div>
                        <div class="font-medium">{{ connection.name }}</div>
                        <div class="text-sm text-muted-foreground">{{ connection.adapterKey }}</div>
                      </div>
                    </div>

                    <div class="flex items-center gap-3">
                      <!-- Sync Status -->
                      <template v-if="syncStatuses.find(s => s.connectionId === connection.id)">
                        <div class="text-right">
                          <Badge :variant="getSyncStatusVariant(syncStatuses.find(s => s.connectionId === connection.id).status)">
                            {{ getSyncStatusLabel(syncStatuses.find(s => s.connectionId === connection.id).status) }}
                          </Badge>
                          <div v-if="syncStatuses.find(s => s.connectionId === connection.id).externalGroupId" class="text-xs text-muted-foreground mt-1">
                            ID: {{ syncStatuses.find(s => s.connectionId === connection.id).externalGroupId }}
                          </div>
                        </div>
                      </template>

                      <Button
                        @click="handleExportGroup(connection.id)"
                        :disabled="isExporting || members.length === 0"
                        size="sm"
                      >
                        <Loader2 v-if="isExporting" class="size-4 mr-2 animate-spin" />
                        <Upload v-else class="size-4 mr-2" />
                        {{ syncStatuses.find(s => s.connectionId === connection.id) ? 'Обновить' : 'Экспортировать' }}
                      </Button>
                    </div>
                  </div>

                  <!-- Error message if exists -->
                  <div
                    v-if="syncStatuses.find(s => s.connectionId === connection.id)?.lastErrorMessage"
                    class="mt-3 p-2 bg-destructive/10 text-destructive text-sm rounded"
                  >
                    {{ syncStatuses.find(s => s.connectionId === connection.id).lastErrorMessage }}
                  </div>
                </CardContent>
              </Card>
            </div>
          </div>
        </template>
      </div>
    </div>

    <!-- Product Search Dialog -->
    <Dialog v-model:open="showProductSearch">
      <DialogContent class="max-w-xl max-h-[80vh] flex flex-col">
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

            <div v-else-if="availableProductsFiltered.length === 0" class="flex flex-col items-center justify-center py-8 text-center">
              <Package class="size-8 mb-2 text-muted-foreground" />
              <p class="text-sm text-muted-foreground">Нет доступных товаров</p>
            </div>

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

        <DialogFooter class="border-t pt-4">
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
