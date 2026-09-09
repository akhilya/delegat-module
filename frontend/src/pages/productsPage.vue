<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageDockFooter from "@/components/PageDockFooter.vue";
import ProductsTable from "@/components/products/productsTable.vue";
import ProductRowActionButtons from "@/components/products/productRowActionButtons.vue";
import TrashRowActionButtons from "@/components/products/trashRowActionButtons.vue";
import ColumnSelector from "@/components/products/columnSelector.vue";
import BulkEditDialog from "@/components/products/bulkEditDialog.vue";
import BulkAdapterDialog from "@/components/products/BulkAdapterDialog.vue";
import ImportExcelDialog from "@/components/products/importExcelDialog.vue";
import ProductTransferDialog from "@/components/products/ProductTransferDialog.vue";
import {
  columns as baseProductColumns,
  defaultVisibleColumns,
  buildColumnsWithStoreAttributes,
  buildColumnsWithPreset,
  getDefaultVisibilityWithStoreAttributes,
  combinedColumnPresets,
} from "@/components/products/columns";
import { getBaseCurrency } from "@/lib/currency";
import {
  parseSortFromQuery,
  serializeSortKey,
  serializeSortToQuery,
  toggleSortColumn,
} from "@/lib/productSort";
import { mapActions } from "vuex";
import { displaySonnerError, extractResponseData } from "@/store/sonnerHelper";
import { toast } from "vue-sonner";
import { Button } from "@/components/ui/button";
import NavSearchField from "@/components/NavSearchField.vue";
import {
  Search,
  ChevronLeft,
  ChevronsLeft,
  ChevronRight,
  ChevronsRight,
  ChevronDown,
  Pencil,
  Trash2,
  X,
  Download,
  Upload,
  ArrowUpDown,
  ArrowUp,
  ArrowDown,
  Check,
  FileSpreadsheet,
  Undo2,
  RefreshCw,
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
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
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
} from "@/components/ui/alert-dialog";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { PAGE_PRODUCT_EDIT, setPageTitle } from "@/router";
import { MENU_ITEM_CHECK, MENU_ITEM_INDICATOR, MENU_SEPARATOR } from "@/lib/menuSurface";
import { getCachedMe } from "@/lib/meCache";
import {
  PRODUCTS_LIST_NS,
  PRODUCTS_TRASH_NS,
  buildProductsListCacheKey,
  buildProductsTrashCacheKey,
  getProductsListCache,
  setProductsListCache,
  invalidateProductsListCache,
} from "@/lib/productsListCache";

const COLUMN_VISIBILITY_KEY = "products-column-visibility";
const COLUMN_PRESET_KEY = "products-column-preset";
const COLUMN_ORDER_KEY = "products-column-order";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageDockFooter,
    ProductsTable,
    ProductRowActionButtons,
    TrashRowActionButtons,
    ColumnSelector,
    BulkEditDialog,
    BulkAdapterDialog,
    ImportExcelDialog,
    ProductTransferDialog,
    NavSearchField,
    Button,
    Search,
    ChevronLeft,
    ChevronsLeft,
    ChevronRight,
    ChevronsRight,
    ChevronDown,
    Pencil,
    Trash2,
    X,
    Download,
    Upload,
    ArrowUpDown,
    ArrowUp,
    ArrowDown,
    Check,
    FileSpreadsheet,
    Undo2,
    RefreshCw,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
    Input,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
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
    AlertDialogTitle
  },
  setup() {
    return { toast };
  },
  methods: {
    ...mapActions([
      "getAllProducts",
      "bulkDeleteProducts",
      "deleteProduct",
      "getAllStoreAttributes",
      "getCategoryAttributes",
      "getAllCategories",
      "exportProductsTemplate",
      "importProductsPreview",
      "importProductsApply",
      "getTrashProducts",
      "getTrashCount",
      "getMyMemberships",
      "getMe",
      "bulkRestoreProducts",
      "bulkPermanentDeleteProducts",
      "restoreProduct",
      "permanentDeleteProduct",
      "getConnections",
    ]),

    onRowClick(product) {
      if (this.isTrashMode) return;
      this.$router.push({
        name: PAGE_PRODUCT_EDIT,
        params: { id: product.id },
      });
    },

    onTransferRequest(payload) {
      this.transferTarget = {
        id: payload.id,
        name: payload.name || "",
        sku: payload.sku || "",
      };
      this.transferMode = payload.mode === "move" ? "move" : "clone";
      this.isTransferDialogOpen = true;
    },

    onTransferred(payload) {
      // Transfer is queued, not done - actual completion happens in background.
      // Side panel shows progress; row will only disappear (for move) once the
      // background worker soft-deletes the original. We don't refresh now -
      // SignalR-driven panel updates will reflect state. User can refresh manually.
      // (No-op kept as a hook for any future "show toast with link to operation".)
    },

    async fetchProducts() {
      const storeId = getCachedMe()?.selectedStoreId ?? null;
      const sort = serializeSortToQuery(this.sortColumns);
      const cacheKey = buildProductsListCacheKey({
        storeId,
        startIndex: this.startIndex,
        pageSize: this.pageSize,
        searchQuery: this.searchQuery,
        categoryId: this.filterCategoryId,
        sort,
      });
      const cached = getProductsListCache(PRODUCTS_LIST_NS, cacheKey);
      if (cached) {
        this.products = cached.items;
        this.lastPage = cached.lastPage;
        this.totalCount = cached.totalCount;
        for (const p of this.products) {
          if (this.rowSelection[String(p.id)]) {
            this.selectedProductsMap[p.id] = p;
          }
        }
        if (this.page > this.lastPossiblePage) {
          this.page = this.lastPossiblePage;
        }
        this.isProductsLoading = false;
        return;
      }

      const generation = ++this.fetchGeneration;
      this.isProductsLoading = true;
      this.products = [];

      await this.getAllProducts({
        startIndex: this.startIndex,
        pageSize: this.pageSize,
        searchQuery: this.searchQuery,
        categoryId: this.filterCategoryId,
        sort,
        onSuccess: (response) => {
          if (generation !== this.fetchGeneration) return;
          this.products = response.data.value.items.map((f) => f);
          this.lastPage = response.data.value.lastPage;
          this.totalCount = response.data.value.totalCount;
          setProductsListCache(PRODUCTS_LIST_NS, cacheKey, {
            items: this.products,
            totalCount: this.totalCount,
            lastPage: this.lastPage,
          });
          // Sync cross-page selection cache with fresh product data
          for (const p of this.products) {
            if (this.rowSelection[String(p.id)]) {
              this.selectedProductsMap[p.id] = p;
            }
          }
          if (this.page > this.lastPossiblePage) {
            this.page = this.lastPossiblePage;
          }
        },
        onError: (error) => {
          if (generation !== this.fetchGeneration) return;
          displaySonnerError(error);
        },
      });
      if (generation === this.fetchGeneration) {
        this.isProductsLoading = false;
      }
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

    toggleSort(columnId, event) {
      const shiftKey = Boolean(event?.shiftKey);
      this.sortColumns = toggleSortColumn(this.sortColumns, columnId, shiftKey);
      const sort = serializeSortToQuery(this.sortColumns);
      this.$router.replace({
        query: {
          ...this.$route.query,
          sort: sort ?? undefined,
          sortBy: undefined,
          sortDesc: undefined,
        },
      });
      if (this.page !== 1) this.page = 1;
      else this.fetchProducts();
    },

    async fetchConnections() {
      await this.getConnections({
        onSuccess: (response) => {
          this.connections = extractResponseData(response, []);
        },
        onError: () => {
          this.connections = [];
        },
      });
    },

    onAdaptersChanged() {
      if (this.isTrashMode) return;
      invalidateProductsListCache();
      this.fetchProducts();
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

    async fetchStoreBaseCurrency() {
      let selectedStoreId = null;
      await new Promise((resolve) => {
        this.getMe({
          onSuccess: (response) => {
            selectedStoreId = response.data.value?.selectedStoreId ?? null;
            resolve();
          },
          onError: () => resolve(),
        });
      });
      await new Promise((resolve) => {
        this.getMyMemberships({
          onSuccess: (response) => {
            const stores = response.data.value || [];
            const store = stores.find((s) => s.id === selectedStoreId) || stores[0];
            this.storeBaseCurrency = getBaseCurrency(store);
            resolve();
          },
          onError: () => resolve(),
        });
      });
    },

    clearSelection() {
      this.rowSelection = {};
      this.selectedProductsMap = {};
    },

    openBulkEditDialog() {
      // Rebuild cache from the current page so IDs always resolve to product objects.
      for (const p of this.products) {
        if (this.rowSelection[String(p.id)]) {
          this.selectedProductsMap[p.id] = p;
        }
      }
      if (this.selectedProductIds.length === 0) return;
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
          toast.success(this.$t("pages.productsPage.toast.bulkDeleted", { count: deletedCount }));
          this.isBulkDeleteDialogOpen = false;
          this.rowSelection = {};
          this.selectedProductsMap = {};
          invalidateProductsListCache();
          this.fetchProducts();
          this.fetchTrashCount();
        },
        onError: (error) => {
          toast.error(
            error.response?.data?.message || this.$t("pages.productsPage.toast.bulkDeleteFailed"),
          );
        },
      });

      this.isDeleting = false;
    },

    async fetchTrashProducts() {
      const storeId = getCachedMe()?.selectedStoreId ?? null;
      const cacheKey = buildProductsTrashCacheKey({
        storeId,
        startIndex: this.trashStartIndex,
        pageSize: this.pageSize,
        searchQuery: this.trashSearchQuery,
      });
      const cached = getProductsListCache(PRODUCTS_TRASH_NS, cacheKey);
      if (cached) {
        this.trashProducts = cached.items;
        this.trashLastPage = cached.lastPage;
        this.trashTotalCount = cached.totalCount;
        if (this.trashPage > this.trashLastPossiblePage) {
          this.trashPage = this.trashLastPossiblePage;
        }
        this.isTrashLoading = false;
        return;
      }

      this.isTrashLoading = true;
      this.trashProducts = [];
      await this.getTrashProducts({
        startIndex: this.trashStartIndex,
        pageSize: this.pageSize,
        searchQuery: this.trashSearchQuery,
        onSuccess: (response) => {
          this.trashProducts = response.data.value.items;
          this.trashLastPage = response.data.value.lastPage;
          this.trashTotalCount = response.data.value.totalCount;
          setProductsListCache(PRODUCTS_TRASH_NS, cacheKey, {
            items: this.trashProducts,
            totalCount: this.trashTotalCount,
            lastPage: this.trashLastPage,
          });
          if (this.trashPage > this.trashLastPossiblePage) {
            this.trashPage = this.trashLastPossiblePage;
          }
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isTrashLoading = false;
    },

    async fetchTrashCount() {
      await this.getTrashCount({
        onSuccess: (response) => {
          this.trashCount = response.data.value.count;
        },
        onError: () => { this.trashCount = 0; },
      });
    },

    async handleRestoreOne(productId) {
      await this.restoreProduct({
        productId,
        onSuccess: () => {
          toast.success(this.$t("pages.productsPage.toast.restored"));
          invalidateProductsListCache();
          this.fetchTrashProducts();
          this.fetchTrashCount();
        },
        onError: (error) => displaySonnerError(error),
      });
    },

    async handlePermanentDeleteOne(productId) {
      await this.permanentDeleteProduct({
        productId,
        onSuccess: () => {
          toast.success(this.$t("pages.productsPage.toast.permanentlyDeleted"));
          invalidateProductsListCache();
          this.fetchTrashProducts();
          this.fetchTrashCount();
        },
        onError: (error) => displaySonnerError(error),
      });
    },

    async handleDeleteOne(productId) {
      await this.deleteProduct({
        productId,
        onSuccess: () => {
          toast.success(this.$t("pages.productsPage.toast.movedToTrash"));
          invalidateProductsListCache();
          this.fetchProducts();
          this.fetchTrashCount();
        },
        onError: (error) => {
          toast.error(
            error.response?.data?.message || this.$t("pages.productsPage.toast.moveToTrashFailed"),
          );
        },
      });
    },

    syncTrashTitle() {
      if (!this.isTrashMode) {
        setPageTitle(null);
        return;
      }
      setPageTitle(
        this.trashCount > 0
          ? this.$t("pages.productsPage.breadcrumbTrashWithCount", {
              count: this.trashCount,
            })
          : this.$t("pages.productsPage.breadcrumbTrash"),
      );
    },

    selectCategoryFilter(id) {
      this.filterCategoryId = id;
      this.categoryFilterOpen = false;
    },

    toggleTrashMode() {
      this.isTrashMode = !this.isTrashMode;
      this.clearSelection();
      // Sync query param
      const query = { ...this.$route.query };
      if (this.isTrashMode) {
        query.trash = '1';
        this.trashStartIndex = 0;
        this.fetchTrashProducts();
      } else {
        delete query.trash;
        this.fetchProducts();
      }
      this.$router.replace({ query });
    },

    async handleBulkRestore() {
      this.isRestoring = true;
      await this.bulkRestoreProducts({
        productIds: this.selectedProductIds,
        onSuccess: ({ data }) => {
          toast.success(this.$t("pages.productsPage.toast.bulkRestored", { count: data.value.restoredCount }));
          this.clearSelection();
          invalidateProductsListCache();
          this.fetchTrashProducts();
          this.fetchTrashCount();
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isRestoring = false;
    },

    async handleBulkPermanentDelete() {
      this.isPermanentDeleting = true;
      await this.bulkPermanentDeleteProducts({
        productIds: this.selectedProductIds,
        onSuccess: ({ data }) => {
          toast.success(this.$t("pages.productsPage.toast.bulkPermanentlyDeleted", { count: data.value.deletedCount }));
          this.isBulkPermanentDeleteDialogOpen = false;
          this.clearSelection();
          invalidateProductsListCache();
          this.fetchTrashProducts();
          this.fetchTrashCount();
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isPermanentDeleting = false;
    },

    handleBulkEditSaved() {
      this.rowSelection = {};
      this.selectedProductsMap = {};
      invalidateProductsListCache();
      this.fetchProducts();
    },

    async handleExportExcel() {
      this.isExporting = true;
      await this.exportProductsTemplate({
        productIds: this.selectedProductIds,
        onSuccess: () => {
          toast.success(this.$t("pages.productsPage.toast.fileExported"));
        },
        onError: (error) => {
          toast.error(
            error.response?.data?.message || this.$t("pages.productsPage.toast.exportFailed"),
          );
        },
      });
      this.isExporting = false;
    },

    openImportFromExcelDialog() {
      this.isExcelDialogOpen = false;
      this.isImportDialogOpen = true;
    },

    handleImportSaved() {
      this.clearSelection();
      invalidateProductsListCache();
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
      connections: [],
      fetchGeneration: 0,
      // True until first fetch finishes — otherwise the table flashes empty while
      // categories / store attributes load in mounted() before fetchProducts().
      isProductsLoading: true,
      lastPage: false,
      startIndex,
      pageSize,
      totalCount: 0,
      searchQuery,
      prevSearchQuery: searchQuery,
      // breadcrumbs is a computed property now

      // Categories for filter (restore from query)
      categories: [],
      filterCategoryId: this.$route.query.categoryId != null
        ? Number(this.$route.query.categoryId)
        : null,
      categoryFilterOpen: false,
      categoryMenuQuery: "",
      MENU_ITEM_CHECK,
      MENU_ITEM_INDICATOR,
      MENU_SEPARATOR,

      // Multi-column sorting (restore from query)
      sortColumns: parseSortFromQuery(this.$route.query),

      // Row selection
      rowSelection: {},

      // Column visibility
      columnVisibility: this.loadColumnVisibility
        ? this.loadColumnVisibility()
        : { ...defaultVisibleColumns },

      // Store attributes for bulk edit
      storeAttributes: [],
      storeBaseCurrency: null,

      // Category attributes for bulk edit (loaded dynamically)
      loadedCategoryAttributes: [],
      loadedCategoryId: null,

      // Dialogs
      isBulkEditDialogOpen: false,
      isBulkAdapterDialogOpen: false,
      isBulkDeleteDialogOpen: false,
      isImportDialogOpen: false,
      isExcelDialogOpen: false,
      isDeleting: false,
      isExporting: false,

      // Transfer dialog
      isTransferDialogOpen: false,
      transferMode: "clone",
      transferTarget: { id: null, name: "", sku: "" },

      // Trash mode
      isTrashMode: false,
      trashProducts: [],
      isTrashLoading: true,
      trashLastPage: false,
      trashStartIndex: 0,
      trashTotalCount: 0,
      trashSearchQuery: "",
      trashCount: 0,

      // Trash dialogs
      isBulkPermanentDeleteDialogOpen: false,
      isPermanentDeleting: false,
      isRestoring: false,

      // Cross-page selection cache: { productId: productObject }
      selectedProductsMap: {},

      // Column preset and order
      selectedPreset: localStorage.getItem(COLUMN_PRESET_KEY) || "product",
      columnOrder: this.loadColumnOrder ? this.loadColumnOrder() : [],
    };
  },
  watch: {
    // React to ?trash=1 query param changes (e.g. from navbar dropdown)
    '$route.query.trash'(newVal) {
      const wantTrash = newVal === '1';
      if (wantTrash !== this.isTrashMode) {
        this.toggleTrashMode();
      }
    },
    '$route.query.sort'() {
      const next = parseSortFromQuery(this.$route.query);
      if (serializeSortKey(this.sortColumns) === serializeSortKey(next)) return;
      this.sortColumns = next;
      if (!this.isTrashMode) this.fetchProducts();
    },
    startIndex() {
      this.fetchProducts();
    },
    trashStartIndex() {
      if (this.isTrashMode) this.fetchTrashProducts();
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
    categoryFilterOpen(open) {
      if (!open) this.categoryMenuQuery = "";
    },
    isTrashMode: {
      handler() {
        this.syncTrashTitle();
      },
    },
    trashCount() {
      this.syncTrashTitle();
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
    categoryFilterLabel() {
      if (this.filterCategoryId == null) {
        return this.$t("pages.productsPage.filters.allCategories");
      }
      if (this.filterCategoryId === -1) {
        return this.$t("pages.productsPage.filters.noCategory");
      }
      const cat = this.categories.find((c) => c.id === this.filterCategoryId);
      return cat?.name || this.$t("pages.productsPage.filters.allCategories");
    },
    filteredMenuCategories() {
      const q = (this.categoryMenuQuery || "").trim().toLowerCase();
      if (!q) return this.categories;
      return this.categories.filter((cat) =>
        String(cat.name || "").toLowerCase().includes(q),
      );
    },
    showCategoryMenuSearch() {
      return this.categories.length > 6;
    },
    // Dynamic columns including store attributes and preset
    productColumns() {
      return buildColumnsWithPreset(this.storeAttributes, this.selectedPreset, this.storeBaseCurrency);
    },
    sortableColumnIds() {
      const base = ['name', 'sku', 'price', 'storageQuantity', 'category', 'createdAt', 'updatedAt'];
      const storeAttrIds = (this.storeAttributes || []).map((a) => `storeAttr_${a.id}`);
      return [...base, ...storeAttrIds];
    },
    lastPossiblePage() {
      return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
    },
    trashPage: {
      get() {
        return this.trashStartIndex / this.pageSize + 1;
      },
      set(value) {
        const max = this.trashLastPossiblePage;
        const next = Math.min(Math.max(1, Number(value) || 1), max);
        this.trashStartIndex = (next - 1) * this.pageSize;
      },
    },
    trashLastPossiblePage() {
      return Math.max(1, Math.ceil(this.trashTotalCount / this.pageSize));
    },
    page: {
      get() {
        return this.startIndex / this.pageSize + 1;
      },
      set(value) {
        const max = this.lastPossiblePage;
        const next = Math.min(Math.max(1, Number(value) || 1), max);
        this.$router.replace({
          query: { ...this.$route.query, page: next },
        });
        this.startIndex = (next - 1) * this.pageSize;
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
        .map((id) => {
          const cached = this.selectedProductsMap[id];
          if (cached) return cached;
          return this.products.find((p) => p.id === id) || null;
        })
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
    if (this.$route.query.trash === "1") {
      this.isProductsLoading = false;
      this.isTrashMode = true;
    }

    this.syncTrashTitle();

    // Meta (categories / attributes / …) must not block the list: otherwise a
    // cache hit on remount still waits on those APIs and feels like a full reload.
    const metaPromise = Promise.all([
      this.fetchStoreAttributes(),
      this.fetchStoreBaseCurrency(),
      this.fetchCategories(),
      this.fetchTrashCount(),
      this.fetchConnections(),
    ]);

    if (this.isTrashMode) {
      await this.fetchTrashProducts();
    } else {
      await this.fetchProducts();
      if (this.page > this.lastPossiblePage) this.page = this.lastPossiblePage;
    }

    await metaPromise;
    // After store attributes exist, re-merge saved column visibility presets.
    this.columnVisibility = this.loadColumnVisibility();
  },
};
</script>

<template>
  <AppPageChrome fill-viewport>
    <template #nav-center>
      <TooltipProvider :delay-duration="400">
        <Tooltip>
          <TooltipTrigger as-child>
            <NavSearchField
              v-model="searchQuery"
              :placeholder="$t('common.search')"
              @submit="fetchProducts"
            >
              <template #trailing>
                <Popover v-model:open="categoryFilterOpen">
                  <PopoverTrigger as-child>
                    <button
                      type="button"
                      class="inline-flex h-7 max-w-[8rem] cursor-pointer items-center gap-0.5 rounded-md px-2 text-xs font-medium text-muted-foreground transition-[color,background-color,transform] hover:bg-muted/80 hover:text-foreground active:scale-[0.97]"
                      :class="
                        filterCategoryId != null
                          ? 'bg-muted text-foreground shadow-none'
                          : ''
                      "
                      :aria-label="categoryFilterLabel"
                    >
                      <span class="min-w-0 truncate">{{
                        categoryFilterLabel
                      }}</span>
                      <ChevronDown class="size-3.5 shrink-0 opacity-60" />
                    </button>
                  </PopoverTrigger>
                  <PopoverContent
                    align="end"
                    class="flex w-56 max-w-[min(14rem,calc(100vw-2rem))] flex-col overflow-hidden p-0"
                  >
                    <div v-if="showCategoryMenuSearch" class="border-b p-1.5">
                      <div class="relative">
                        <Search
                          class="pointer-events-none absolute left-2 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground"
                        />
                        <Input
                          v-model="categoryMenuQuery"
                          type="search"
                          class="h-7 pl-7 text-sm"
                          :placeholder="$t('common.search')"
                          @click.stop
                        />
                      </div>
                    </div>
                    <div class="max-h-64 overflow-y-auto p-1">
                      <button
                        type="button"
                        :class="MENU_ITEM_INDICATOR + ' h-8 w-full py-0'"
                        @click="selectCategoryFilter(null)"
                      >
                        <span class="min-w-0 flex-1 truncate text-left">{{
                          $t("pages.productsPage.filters.allCategories")
                        }}</span>
                        <Check
                          v-if="filterCategoryId == null"
                          :class="MENU_ITEM_CHECK + ' text-foreground'"
                        />
                      </button>
                      <button
                        type="button"
                        :class="MENU_ITEM_INDICATOR + ' h-8 w-full py-0'"
                        @click="selectCategoryFilter(-1)"
                      >
                        <span class="min-w-0 flex-1 truncate text-left">{{
                          $t("pages.productsPage.filters.noCategory")
                        }}</span>
                        <Check
                          v-if="filterCategoryId === -1"
                          :class="MENU_ITEM_CHECK + ' text-foreground'"
                        />
                      </button>
                      <div :class="MENU_SEPARATOR" />
                      <button
                        v-for="cat in filteredMenuCategories"
                        :key="cat.id"
                        type="button"
                        :class="MENU_ITEM_INDICATOR + ' h-8 w-full py-0'"
                        :title="cat.name"
                        @click="selectCategoryFilter(cat.id)"
                      >
                        <span class="min-w-0 flex-1 truncate text-left">{{
                          cat.name
                        }}</span>
                        <Check
                          v-if="filterCategoryId === cat.id"
                          :class="MENU_ITEM_CHECK + ' text-foreground'"
                        />
                      </button>
                    </div>
                  </PopoverContent>
                </Popover>
              </template>
            </NavSearchField>
          </TooltipTrigger>
          <TooltipContent side="bottom" class="max-w-xs text-left">
            {{ $t("pages.productsPage.searchHint") }}
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
    </template>

    <div class="flex min-h-0 w-full flex-1 flex-col">
      <ProductsTable
        :data="isTrashMode ? trashProducts : products"
        :columns="productColumns"
        :is-loading="isTrashMode ? isTrashLoading : isProductsLoading"
        :column-visibility="columnVisibility"
        :column-order="columnOrder"
        :row-selection="rowSelection"
        :start-index="startIndex"
        :sort-columns="sortColumns"
        :sortable-columns="sortableColumnIds"
        :connections="connections"
        :adapters-disabled="isTrashMode"
        @update:row-selection="rowSelection = $event"
        @update:column-visibility="onColumnVisibilityChange"
        @update:column-order="onColumnOrderChange"
        @sort="toggleSort"
        @row-click="onRowClick"
        @adapters-changed="onAdaptersChanged"
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
        <template #rowActions="{ row }">
          <ProductRowActionButtons
            v-if="!isTrashMode"
            :id="row.original.id"
            :name="row.original.name"
            :sku="row.original.sku"
            @transfer="onTransferRequest"
            @delete="handleDeleteOne"
          />
          <TrashRowActionButtons
            v-else
            :id="row.original.id"
            :name="row.original.name"
            :sku="row.original.sku"
            @restore="handleRestoreOne"
            @permanent-delete="handlePermanentDeleteOne"
          />
        </template>
        <template #rowContextMenu="{ row }">
          <ProductRowActionButtons
            v-if="!isTrashMode"
            mode="context"
            :id="row.original.id"
            :name="row.original.name"
            :sku="row.original.sku"
            @transfer="onTransferRequest"
            @delete="handleDeleteOne"
          />
          <TrashRowActionButtons
            v-else
            mode="context"
            :id="row.original.id"
            :name="row.original.name"
            :sku="row.original.sku"
            @restore="handleRestoreOne"
            @permanent-delete="handlePermanentDeleteOne"
          />
        </template>
      </ProductsTable>
      <!-- Combined Footer -->
      <PageDockFooter v-if="!isProductsLoading">
        <div class="flex min-w-0 flex-1 items-center justify-between gap-3">
        <!-- Left: Page size + Selection info -->
        <div class="flex items-center gap-4">
          <div class="flex items-center gap-2">
            <p class="text-sm text-muted-foreground">{{ $t("pages.productsPage.perPageLabel") }}</p>
            <Select
              :model-value="String(pageSize)"
              @update:model-value="(val) => pageSize = Number(val)"
            >
              <SelectTrigger class="h-8 w-[100px]">
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
              {{ $t("pages.productsPage.selectedCount", { count: selectedProductIds.length }) }}
            </span>
            <Button variant="ghost" size="sm" class="h-8" @click="clearSelection">
              <X class="h-4 w-4" />
            </Button>

            <!-- Normal mode actions -->
            <template v-if="!isTrashMode">
              <div class="flex">
                <Button variant="outline" size="sm" class="h-8 rounded-r-none border-r-0" @click="openBulkEditDialog">
                  <Pencil class="h-4 w-4 mr-1" />
                  {{ $t("common.edit") }}
                </Button>
                <Button variant="outline" size="sm" class="h-8 rounded-l-none" @click="isExcelDialogOpen = true">
                  <FileSpreadsheet class="h-4 w-4 mr-1" />
                  {{ $t("pages.productsPage.bulkEditFromTable") }}
                </Button>
              </div>
              <Button variant="outline" size="sm" class="h-8" @click="isBulkAdapterDialogOpen = true">
                <RefreshCw class="h-4 w-4 mr-1" />
                {{ $t("nav.adapters") }}
              </Button>
              <Button variant="destructive" size="sm" class="h-8" @click="openBulkDeleteDialog">
                <Trash2 class="h-4 w-4 mr-1" />
                {{ $t("pages.productsPage.moveToTrash") }}
              </Button>
            </template>

            <!-- Trash mode actions -->
            <template v-else>
              <Button variant="outline" size="sm" class="h-8" :disabled="isRestoring" @click="handleBulkRestore">
                <Undo2 class="h-4 w-4 mr-1" />
                {{ isRestoring ? $t("pages.productsPage.restoring") : $t("trashRowActions.restore") }}
              </Button>
              <Button variant="destructive" size="sm" class="h-8" @click="isBulkPermanentDeleteDialogOpen = true">
                <Trash2 class="h-4 w-4 mr-1" />
                {{ $t("trashRowActions.deleteForever") }}
              </Button>
            </template>
          </template>
        </div>

        <!-- Right: Pagination -->
        <div class="flex items-center gap-2">
          <p class="text-sm text-muted-foreground mr-2">
            {{ isTrashMode ? trashPage : page }} / {{ isTrashMode ? trashLastPossiblePage : lastPossiblePage }}
          </p>
          <Button variant="outline" size="sm" class="h-8 w-8 p-0"
            :disabled="(isTrashMode ? trashPage : page) <= 1"
            @click="isTrashMode ? trashPage = 1 : page = 1">
            <ChevronsLeft class="size-4" />
          </Button>
          <Button variant="outline" size="sm" class="h-8 w-8 p-0"
            :disabled="(isTrashMode ? trashPage : page) <= 1"
            @click="isTrashMode ? trashPage-- : page--">
            <ChevronLeft class="size-4" />
          </Button>
          <Button variant="outline" size="sm" class="h-8 w-8 p-0"
            :disabled="(isTrashMode ? trashPage : page) >= (isTrashMode ? trashLastPossiblePage : lastPossiblePage)"
            @click="isTrashMode ? trashPage++ : page++">
            <ChevronRight class="size-4" />
          </Button>
          <Button variant="outline" size="sm" class="h-8 w-8 p-0"
            :disabled="(isTrashMode ? trashPage : page) >= (isTrashMode ? trashLastPossiblePage : lastPossiblePage)"
            @click="isTrashMode ? (trashPage = trashLastPossiblePage) : (page = lastPossiblePage)">
            <ChevronsRight class="size-4" />
          </Button>
        </div>
        </div>
      </PageDockFooter>
    </div>

    <!-- Bulk Edit Dialog -->
    <BulkEditDialog
      v-model="isBulkEditDialogOpen"
      :selected-products="selectedProducts"
      :product-ids="selectedProductIds"
      :category-attributes="categoryAttributes"
      :store-attributes="storeAttributes"
      @saved="handleBulkEditSaved"
    />

    <!-- Bulk Adapter Dialog -->
    <BulkAdapterDialog
      v-model="isBulkAdapterDialogOpen"
      :selected-products="selectedProducts"
    />

    <!-- Excel Actions Dialog -->
    <Dialog v-model:open="isExcelDialogOpen">
      <DialogContent size="md" class="gap-0 overflow-hidden p-0">
        <DialogHeader variant="bar">
          <DialogTitle>
            {{ $t("pages.productsPage.excelDialog.title") }}
          </DialogTitle>
          <DialogDescription>
            {{ $t("pages.productsPage.excelDialog.description", { count: selectedProductIds.length }) }}
          </DialogDescription>
        </DialogHeader>

        <div class="space-y-2 px-6 py-5">
          <button
            type="button"
            class="flex w-full items-start gap-3 rounded-lg border px-3.5 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:ring-[3px] focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-60"
            :disabled="isExporting"
            @click="handleExportExcel"
          >
            <div class="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-md border bg-background">
              <Spinner v-if="isExporting" class="text-muted-foreground" />
              <Download v-else class="size-4 text-muted-foreground" />
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-sm font-medium">
                {{
                  isExporting
                    ? $t("pages.productsPage.excelDialog.exporting")
                    : $t("pages.productsPage.excelDialog.downloadTemplate")
                }}
              </div>
              <p class="mt-0.5 text-xs text-muted-foreground">
                {{ $t("pages.productsPage.excelDialog.exportHint") }}
              </p>
            </div>
          </button>

          <button
            type="button"
            class="flex w-full items-start gap-3 rounded-lg border px-3.5 py-3 text-left transition-colors outline-none hover:bg-muted/40 focus-visible:ring-[3px] focus-visible:ring-ring/50"
            @click="openImportFromExcelDialog"
          >
            <div class="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-md border bg-background">
              <Upload class="size-4 text-muted-foreground" />
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-sm font-medium">
                {{ $t("pages.productsPage.excelDialog.uploadChanges") }}
              </div>
              <p class="mt-0.5 text-xs text-muted-foreground">
                {{ $t("pages.productsPage.excelDialog.importHint") }}
              </p>
            </div>
          </button>
        </div>

        <DialogFooter variant="bar" class="sm:justify-start">
          <Button variant="outline" @click="isExcelDialogOpen = false">
            {{ $t("common.cancel") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Import Excel Dialog -->
    <ImportExcelDialog
      v-model="isImportDialogOpen"
      @saved="handleImportSaved"
    />

    <!-- Transfer (Clone/Move to another store) Dialog -->
    <ProductTransferDialog
      v-model:open="isTransferDialogOpen"
      :mode="transferMode"
      :product-id="transferTarget.id"
      :product-name="transferTarget.name"
      :product-sku="transferTarget.sku"
      @transferred="onTransferred"
    />

    <!-- Bulk Delete Confirmation Dialog -->
    <AlertDialog v-model:open="isBulkDeleteDialogOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t("productRowActions.confirmTrashTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t("pages.productsPage.bulkTrashConfirm.description") }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="isDeleting">{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            @click.prevent="handleBulkDelete"
            :disabled="isDeleting"
            variant="destructive"
          >
            {{ isDeleting ? $t("pages.productsPage.bulkTrashConfirm.moving") : $t("productRowActions.toTrash") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <!-- Permanent Delete Confirmation Dialog -->
    <AlertDialog v-model:open="isBulkPermanentDeleteDialogOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t("trashRowActions.confirmDeleteTitle") }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t("pages.productsPage.bulkPermanentDeleteConfirm.description", { count: selectedProductIds.length }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="isPermanentDeleting">{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            @click.prevent="handleBulkPermanentDelete"
            :disabled="isPermanentDeleting"
            variant="destructive"
          >
            {{ isPermanentDeleting ? $t("pages.productsPage.bulkPermanentDeleteConfirm.deleting") : $t("trashRowActions.deleteForever") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </AppPageChrome>
</template>
