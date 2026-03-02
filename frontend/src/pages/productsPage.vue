<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import ProductsTable from "@/components/products/productsTable.vue";
import ColumnSelector from "@/components/products/columnSelector.vue";
import BulkEditDialog from "@/components/products/bulkEditDialog.vue";
import ImportExcelDialog from "@/components/products/importExcelDialog.vue";
import {
  columns as baseProductColumns,
  defaultVisibleColumns,
  buildColumnsWithStoreAttributes,
  buildColumnsWithPreset,
  getDefaultVisibilityWithStoreAttributes,
  combinedColumnPresets,
} from "@/components/products/columns";
import { mapActions } from "vuex";
import { displaySonnerError } from "@/store/sonnerHelper";
import { toast } from "vue-sonner";
import Button from "@/components/ui/button/Button.vue";
import {
  Search,
  ChevronLeft,
  ChevronsLeft,
  ChevronRight,
  ChevronsRight,
  Pencil,
  Trash2,
  X,
  Download,
  Upload,
  ArrowUpDown,
  ArrowUp,
  ArrowDown,
  Filter,
  Check,
} from "lucide-vue-next";
import Input from "@/components/ui/input/Input.vue";
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
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
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

const COLUMN_VISIBILITY_KEY = "products-column-visibility";
const COLUMN_PRESET_KEY = "products-column-preset";
const COLUMN_ORDER_KEY = "products-column-order";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    ProductsTable,
    ColumnSelector,
    BulkEditDialog,
    ImportExcelDialog,
    Button,
    Search,
    ChevronLeft,
    ChevronsLeft,
    ChevronRight,
    ChevronsRight,
    Pencil,
    Trash2,
    X,
    Download,
    Upload,
    ArrowUpDown,
    ArrowUp,
    ArrowDown,
    Filter,
    Check,
    Input,
    Select,
    SelectContent,
    SelectGroup,
    SelectItem,
    SelectLabel,
    SelectTrigger,
    SelectValue,
    Popover,
    PopoverContent,
    PopoverTrigger,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
  },
  setup() {
    return { toast };
  },
  methods: {
    ...mapActions([
      "getAllProducts",
      "bulkDeleteProducts",
      "getAllStoreAttributes",
      "getCategoryAttributes",
      "getAllCategories",
      "exportProductsTemplate",
      "importProductsPreview",
      "importProductsApply",
    ]),

    async fetchProducts() {
      this.isProductsLoading = true;
      this.products = [];

      await this.getAllProducts({
        startIndex: this.startIndex,
        pageSize: this.pageSize,
        searchQuery: this.searchQuery,
        categoryId: this.filterCategoryId,
        sortBy: this.sortBy,
        sortDesc: this.sortDesc,
        onSuccess: (response) => {
          this.products = response.data.value.items.map((f) => f);
          this.lastPage = response.data.value.lastPage;
          this.totalCount = response.data.value.totalCount;
          // Sync cross-page selection cache with fresh product data
          for (const p of this.products) {
            if (this.rowSelection[String(p.id)]) {
              this.selectedProductsMap[p.id] = p;
            }
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isProductsLoading = false;
    },

    async fetchCategories() {
      await this.getAllCategories({
        onSuccess: (response) => {
          this.categories = response.data.value || [];
        },
        onError: () => {
          this.categories = [];
        },
      });
    },

    toggleSort(columnId) {
      if (this.sortBy === columnId) {
        if (this.sortDesc) {
          this.sortDesc = false;
        } else {
          this.sortBy = null;
          this.sortDesc = true;
        }
      } else {
        this.sortBy = columnId;
        this.sortDesc = true;
      }
      this.$router.replace({
        query: {
          ...this.$route.query,
          sortBy: this.sortBy ?? undefined,
          sortDesc: this.sortBy ? String(this.sortDesc) : undefined,
        },
      });
      if (this.page !== 1) this.page = 1;
      else this.fetchProducts();
    },

    async fetchStoreAttributes() {
      await this.getAllStoreAttributes({
        onSuccess: (response) => {
          this.storeAttributes = response.data.value || [];
        },
        onError: () => {
          this.storeAttributes = [];
        },
      });
    },

    clearSelection() {
      this.rowSelection = {};
      this.selectedProductsMap = {};
    },

    openBulkEditDialog() {
      this.isBulkEditDialogOpen = true;
    },

    async fetchCategoryAttributes(categoryId) {
      if (!categoryId || categoryId === this.loadedCategoryId) return;

      await this.getCategoryAttributes({
        categoryId,
        onSuccess: (response) => {
          this.loadedCategoryAttributes = response.data.value || [];
          this.loadedCategoryId = categoryId;
        },
        onError: () => {
          this.loadedCategoryAttributes = [];
          this.loadedCategoryId = null;
        },
      });
    },

    openBulkDeleteDialog() {
      this.isBulkDeleteDialogOpen = true;
    },

    async handleBulkDelete() {
      const productIds = this.selectedProductIds;
      this.isDeleting = true;

      await this.bulkDeleteProducts({
        productIds,
        onSuccess: ({ deletedCount }) => {
          toast.success(`Удалено товаров: ${deletedCount}`);
          this.isBulkDeleteDialogOpen = false;
          this.rowSelection = {};
          this.selectedProductsMap = {};
          this.fetchProducts();
        },
        onError: (error) => {
          toast.error(
            error.response?.data?.message || "Не удалось удалить товары",
          );
        },
      });

      this.isDeleting = false;
    },

    handleBulkEditSaved() {
      this.rowSelection = {};
      this.selectedProductsMap = {};
      this.fetchProducts();
    },

    async handleExportExcel() {
      this.isExporting = true;
      await this.exportProductsTemplate({
        productIds: this.selectedProductIds,
        onSuccess: () => {
          toast.success("Файл экспортирован");
        },
        onError: (error) => {
          toast.error(
            error.response?.data?.message || "Не удалось экспортировать",
          );
        },
      });
      this.isExporting = false;
    },

    openImportDialog() {
      this.isImportDialogOpen = true;
    },

    handleImportSaved() {
      this.clearSelection();
      this.fetchProducts();
    },

    saveColumnVisibility() {
      localStorage.setItem(
        COLUMN_VISIBILITY_KEY,
        JSON.stringify(this.columnVisibility),
      );
    },

    loadColumnVisibility() {
      const saved = localStorage.getItem(COLUMN_VISIBILITY_KEY);
      const defaults = getDefaultVisibilityWithStoreAttributes(
        this.storeAttributes || [],
      );
      if (saved) {
        try {
          // Merge saved settings with defaults (to include new attributes)
          return { ...defaults, ...JSON.parse(saved) };
        } catch {
          return defaults;
        }
      }
      return defaults;
    },

    onColumnVisibilityChange(newVisibility) {
      this.columnVisibility = newVisibility;
      this.saveColumnVisibility();
    },

    onPresetChange(preset) {
      this.selectedPreset = preset;
      localStorage.setItem(COLUMN_PRESET_KEY, preset);
    },

    onColumnOrderChange(newOrder) {
      this.columnOrder = newOrder;
      localStorage.setItem(COLUMN_ORDER_KEY, JSON.stringify(newOrder));
    },

    loadColumnOrder() {
      const saved = localStorage.getItem(COLUMN_ORDER_KEY);
      if (saved) {
        try {
          return JSON.parse(saved);
        } catch {
          return [];
        }
      }
      return [];
    },
  },
  data() {
    let pageSize = Number(this.$route.query.pageSize ?? 25);
    let startIndex = (Number(this.$route.query.page ?? 1) - 1) * pageSize;
    let searchQuery = this.$route.query.search ?? "";

    return {
      products: [],
      isProductsLoading: false,
      lastPage: false,
      startIndex,
      pageSize,
      totalCount: 0,
      searchQuery,
      prevSearchQuery: searchQuery,
      breadcrumbs: [{ label: "Товары" }],

      // Categories for filter (restore from query)
      categories: [],
      filterCategoryId: this.$route.query.categoryId != null
        ? Number(this.$route.query.categoryId)
        : null,

      // Sorting (restore from query)
      sortBy: this.$route.query.sortBy || null,
      sortDesc: this.$route.query.sortDesc != null
        ? this.$route.query.sortDesc === "true"
        : true,

      // Row selection
      rowSelection: {},

      // Column visibility
      columnVisibility: this.loadColumnVisibility
        ? this.loadColumnVisibility()
        : { ...defaultVisibleColumns },

      // Store attributes for bulk edit
      storeAttributes: [],

      // Category attributes for bulk edit (loaded dynamically)
      loadedCategoryAttributes: [],
      loadedCategoryId: null,

      // Dialogs
      isBulkEditDialogOpen: false,
      isBulkDeleteDialogOpen: false,
      isImportDialogOpen: false,
      isDeleting: false,
      isExporting: false,

      // Cross-page selection cache: { productId: productObject }
      selectedProductsMap: {},

      // Column preset and order
      selectedPreset: localStorage.getItem(COLUMN_PRESET_KEY) || "product",
      columnOrder: this.loadColumnOrder ? this.loadColumnOrder() : [],
    };
  },
  watch: {
    startIndex() {
      this.fetchProducts();
    },
    searchQuery(newVal, oldVal) {
      let queryReplace = newVal;
      if (newVal.length == 0) queryReplace = undefined;
      this.$router.replace({
        query: { ...this.$route.query, search: queryReplace },
      });
      if (this.page != 1) this.page = 1;
      else if (oldVal.length > 0 && newVal.length == 0) this.fetchProducts();
    },
    pageSize(newVal) {
      this.$router.replace({
        query: { ...this.$route.query, pageSize: newVal },
      });
      if (this.page != 1) this.page = 1;
      else this.fetchProducts();
    },
    rowSelection: {
      handler(sel) {
        // Add newly selected current-page products to cache
        for (const p of this.products) {
          if (sel[String(p.id)]) {
            this.selectedProductsMap[p.id] = p;
          } else {
            delete this.selectedProductsMap[p.id];
          }
        }
        // Remove deselected products from other pages
        for (const id of Object.keys(this.selectedProductsMap)) {
          if (!sel[id]) {
            delete this.selectedProductsMap[id];
          }
        }
      },
      deep: true,
    },
    filterCategoryId(newVal) {
      this.$router.replace({
        query: { ...this.$route.query, categoryId: newVal ?? undefined },
      });
      if (this.page !== 1) this.page = 1;
      else this.fetchProducts();
    },
    // Watch selected products to fetch category attributes
    selectedProducts: {
      handler(products) {
        if (products.length === 0) {
          this.loadedCategoryAttributes = [];
          this.loadedCategoryId = null;
          return;
        }
        const categoryIds = products.map((p) => p.categoryId);
        const uniqueCategories = [...new Set(categoryIds)];
        if (uniqueCategories.length === 1 && uniqueCategories[0] !== null) {
          this.fetchCategoryAttributes(uniqueCategories[0]);
        } else {
          this.loadedCategoryAttributes = [];
          this.loadedCategoryId = null;
        }
      },
      deep: true,
    },
  },
  computed: {
    // Dynamic columns including store attributes and preset
    productColumns() {
      return buildColumnsWithPreset(this.storeAttributes, this.selectedPreset);
    },
    lastPossiblePage() {
      return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
    },
    page: {
      get() {
        return this.startIndex / this.pageSize + 1;
      },
      set(value) {
        this.$router.replace({
          query: { ...this.$route.query, page: value },
        });
        this.startIndex = (value - 1) * this.pageSize;
      },
    },
    // Selected products
    selectedProductIds() {
      return Object.keys(this.rowSelection)
        .filter((key) => this.rowSelection[key])
        .map((id) => parseInt(id, 10));
    },
    selectedProducts() {
      return this.selectedProductIds
        .map((id) => this.selectedProductsMap[id])
        .filter(Boolean);
    },
    hasSelection() {
      return this.selectedProductIds.length > 0;
    },
    // Category attributes for selected products (only if same category)
    categoryAttributes() {
      if (this.selectedProducts.length === 0) return [];
      const categoryIds = this.selectedProducts.map((p) => p.categoryId);
      const uniqueCategories = [...new Set(categoryIds)];
      if (uniqueCategories.length !== 1 || uniqueCategories[0] === null)
        return [];
      // Return loaded category attributes
      return this.loadedCategoryAttributes;
    },
  },
  async mounted() {
    // Fetch store attributes and categories in parallel
    await Promise.all([
      this.fetchStoreAttributes(),
      this.fetchCategories(),
    ]);

    // Load column visibility from localStorage (now includes store attributes)
    this.columnVisibility = this.loadColumnVisibility();

    await this.fetchProducts();

    if (this.page > this.lastPossiblePage) this.page = this.lastPossiblePage;
  },
};
</script>

<template>
  <Page class="flex flex-col h-screen">
    <NavigationBar>
      <template #center>
        <div class="flex items-center gap-2 w-full">
          <div class="relative flex-1 h-9">
            <Input
              class="absolute w-full h-full"
              placeholder="Поиск..."
              v-model="searchQuery"
              @keypress.enter="fetchProducts"
            />
            <Button
              class="absolute top-1/2 -translate-y-1/2 right-0.5 size-8"
              variant="link"
              @click="fetchProducts"
            >
              <Search class="size-4" />
            </Button>
          </div>
          <Popover>
            <PopoverTrigger as-child>
              <Button
                variant="outline"
                size="icon"
                class="h-9 w-9 shrink-0"
                :class="filterCategoryId != null ? 'border-primary text-primary' : ''"
              >
                <Filter class="size-4" />
              </Button>
            </PopoverTrigger>
            <PopoverContent align="end" class="w-56 p-1">
              <div class="max-h-64 overflow-y-auto">
                <button
                  class="w-full text-left text-sm px-2 py-1.5 rounded-sm hover:bg-accent flex items-center justify-between"
                  :class="filterCategoryId == null ? 'bg-accent font-medium' : ''"
                  @click="filterCategoryId = null"
                >
                  Все категории
                  <Check v-if="filterCategoryId == null" class="size-4 shrink-0" />
                </button>
                <button
                  class="w-full text-left text-sm px-2 py-1.5 rounded-sm hover:bg-accent flex items-center justify-between"
                  :class="filterCategoryId === -1 ? 'bg-accent font-medium' : ''"
                  @click="filterCategoryId = -1"
                >
                  Без категории
                  <Check v-if="filterCategoryId === -1" class="size-4 shrink-0" />
                </button>
                <div class="h-px bg-border my-1" />
                <button
                  v-for="cat in categories"
                  :key="cat.id"
                  class="w-full text-left text-sm px-2 py-1.5 rounded-sm hover:bg-accent flex items-center justify-between gap-2"
                  :class="filterCategoryId === cat.id ? 'bg-accent font-medium' : ''"
                  @click="filterCategoryId = cat.id"
                >
                  <span class="truncate">{{ cat.name }}</span>
                  <Check v-if="filterCategoryId === cat.id" class="size-4 shrink-0" />
                </button>
              </div>
            </PopoverContent>
          </Popover>
        </div>
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />

    <div
      class="border rounded-lg w-full flex-1 min-h-0 mb-4 flex flex-col relative overflow-auto thin-scrollbar"
    >
      <ProductsTable
        :data="products"
        :columns="productColumns"
        :is-loading="isProductsLoading"
        :column-visibility="columnVisibility"
        :column-order="columnOrder"
        :row-selection="rowSelection"
        :start-index="startIndex"
        :sort-by="sortBy"
        :sort-desc="sortDesc"
        :sortable-columns="['name', 'sku', 'price', 'storageQuantity', 'category', 'createdAt', 'updatedAt']"
        @update:row-selection="rowSelection = $event"
        @update:column-visibility="onColumnVisibilityChange"
        @update:column-order="onColumnOrderChange"
        @sort="toggleSort"
        class="flex-1 min-h-0"
      >
        <template #headerActions>
          <ColumnSelector
            :column-visibility="columnVisibility"
            :store-attributes="storeAttributes"
            :selected-preset="selectedPreset"
            @update:column-visibility="onColumnVisibilityChange"
            @update:selected-preset="onPresetChange"
          />
        </template>
      </ProductsTable>
      <!-- Combined Footer -->
      <div
        class="flex items-center justify-between px-3 py-2 border-t bg-background"
        v-if="!isProductsLoading"
      >
        <!-- Left: Page size + Selection info -->
        <div class="flex items-center gap-4">
          <div class="flex items-center gap-2">
            <p class="text-sm text-muted-foreground">по</p>
            <Select
              :model-value="String(pageSize)"
              @update:model-value="(val) => pageSize = Number(val)"
            >
              <SelectTrigger class="h-8 w-[100px] bg-primary-foreground">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  <SelectItem as="button" value="10">10</SelectItem>
                  <SelectItem as="button" value="25">25</SelectItem>
                  <SelectItem as="button" value="50">50</SelectItem>
                  <SelectItem as="button" value="100">100</SelectItem>
                  <SelectItem as="button" value="250">250</SelectItem>
                </SelectGroup>
              </SelectContent>
            </Select>
          </div>
          <template v-if="hasSelection">
            <div class="h-4 w-px bg-border" />
            <span class="text-sm font-medium">
              Выбрано: {{ selectedProductIds.length }}
            </span>
            <Button variant="ghost" size="sm" class="h-8" @click="clearSelection">
              <X class="h-4 w-4" />
            </Button>
            <Button variant="outline" size="sm" class="h-8" @click="openBulkEditDialog">
              <Pencil class="h-4 w-4 mr-1" />
              Изменить
            </Button>
            <div class="flex">
              <Button
                variant="outline"
                size="sm"
                class="h-8 rounded-r-none border-r-0"
                :disabled="isExporting"
                @click="handleExportExcel"
              >
                <Download class="h-4 w-4 mr-1" />
                {{ isExporting ? "Экспорт..." : "Excel" }}
              </Button>
              <Button
                variant="outline"
                size="sm"
                class="h-8 rounded-l-none"
                @click="openImportDialog"
              >
                <Upload class="h-4 w-4 mr-1" />
                Импорт
              </Button>
            </div>
            <Button
              variant="destructive"
              size="sm"
              class="h-8"
              @click="openBulkDeleteDialog"
            >
              <Trash2 class="h-4 w-4 mr-1" />
              Удалить
            </Button>
          </template>
        </div>

        <!-- Right: Pagination -->
        <div class="flex items-center gap-2">
          <p class="text-sm text-muted-foreground mr-2">
            {{ page }} / {{ lastPossiblePage }}
          </p>
          <Button
            variant="outline"
            size="sm"
            class="h-8 w-8 p-0"
            :disabled="page - 1 <= 0"
            @click="page = 1"
          >
            <ChevronsLeft class="size-4" />
          </Button>
          <Button
            variant="outline"
            size="sm"
            class="h-8 w-8 p-0"
            :disabled="page - 1 <= 0"
            @click="page--"
          >
            <ChevronLeft class="size-4" />
          </Button>
          <Button
            variant="outline"
            size="sm"
            class="h-8 w-8 p-0"
            :disabled="lastPage"
            @click="page++"
          >
            <ChevronRight class="size-4" />
          </Button>
          <Button
            variant="outline"
            size="sm"
            class="h-8 w-8 p-0"
            :disabled="lastPage"
            @click="page = lastPossiblePage"
          >
            <ChevronsRight class="size-4" />
          </Button>
        </div>
      </div>
    </div>

    <!-- Bulk Edit Dialog -->
    <BulkEditDialog
      v-model="isBulkEditDialogOpen"
      :selected-products="selectedProducts"
      :category-attributes="categoryAttributes"
      :store-attributes="storeAttributes"
      @saved="handleBulkEditSaved"
    />

    <!-- Import Excel Dialog -->
    <ImportExcelDialog
      v-model="isImportDialogOpen"
      @saved="handleImportSaved"
    />

    <!-- Bulk Delete Confirmation Dialog -->
    <AlertDialog v-model:open="isBulkDeleteDialogOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Удалить товары?</AlertDialogTitle>
          <AlertDialogDescription>
            Вы собираетесь удалить {{ selectedProductIds.length }} товаров. Это
            действие нельзя отменить.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="isDeleting">Отмена</AlertDialogCancel>
          <AlertDialogAction
            @click.prevent="handleBulkDelete"
            :disabled="isDeleting"
            class="bg-destructive text-white hover:bg-destructive/90"
          >
            {{ isDeleting ? "Удаление..." : "Удалить" }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </Page>
</template>
