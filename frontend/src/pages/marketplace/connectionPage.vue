<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Plus,
  Store,
  Loader2,
  ChevronRight,
  ChevronLeft,
  ChevronsLeft,
  ChevronsRight,
  Package,
  Trash2,
  FolderTree,
  CheckCircle,
  XCircle,
  Save,
  TestTube,
  ArrowLeft,
  Settings,
  Link,
  Link2,
  Home,
  Search,
  Upload,
  Download,
  AlertTriangle,
  FolderOpen,
  Sparkles,
  Ban,
  DollarSign,
  Boxes,
  Activity,
  Gauge,
  EllipsisVertical,
  Pencil,
  Copy,
} from "lucide-vue-next";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
  DialogClose,
  DialogScrollContent,
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
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import Switch from "@/components/ui/switch/Switch.vue";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import Separator from "@/components/ui/separator/Separator.vue";
import { PAGE_MARKETPLACE } from "@/router";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Badge,
    Checkbox,
    Plus,
    Store,
    Loader2,
    ChevronRight,
    ChevronLeft,
    ChevronsLeft,
    ChevronsRight,
    Package,
    Trash2,
    FolderTree,
    CheckCircle,
    XCircle,
    Save,
    TestTube,
    ArrowLeft,
    Settings,
    Link,
    Link2,
    Home,
    Search,
    Upload,
    Download,
    AlertTriangle,
    FolderOpen,
    Sparkles,
    Ban,
    DollarSign,
    Boxes,
    Activity,
    Gauge,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    DialogScrollContent,
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
    CardDescription,
    CardHeader,
    CardTitle,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Switch,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    RadioGroup,
    RadioGroupItem,
    Separator,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    EllipsisVertical,
    Pencil,
    Copy,
  },
  data() {
    return {
      // Tab state
      activeTab: "settings",

      // Connection settings
      connection: null,
      capabilities: null,
      categoryMappings: [],
      localCategories: [],
      externalCategories: [],
      adapters: [],
      isLoading: false,
      isSaving: false,
      isTesting: false,
      testResult: null,
      showCreateMappingDialog: false,
      isCreatingMapping: false,
      newMapping: {
        localCategoryId: null,
        externalCategoryId: "",
        externalCategoryName: "",
        name: "",
      },
      externalCategorySearch: "",
      credentialValues: {},
      PAGE_MARKETPLACE,
      // Category navigation state
      isLoadingCategories: false,
      currentParentId: null,
      categoryPath: [],

      // === Products tab state ===
      products: [],
      selectedItems: [],
      searchQuery: "",
      isLoadingProducts: false,
      isCreatingLocal: false,
      isImportingBulk: false,
      isExportingBulk: false,
      isExportingPrices: false,
      isExportingStocks: false,
      importingLinkId: null,
      exportingLinkId: null,
      totalCount: 0,
      linkedCount: 0,
      hasMoreProducts: false,
      externalPage: 1,
      pageCursors: [null],
      productsPageCache: {}, // { pageNum: { items, hasMore, total, linkedCount } }

      // Import dialog state
      showImportDialog: false,
      isLoadingDialogData: false,
      isImportingWithCategories: false,
      existingMappings: [],
      categoryAssignments: {},

      // Single import preview dialog
      showSingleImportPreviewDialog: false,
      singleImportPreviewData: null,
      isLoadingSinglePreview: false,
      isApplyingSingleImport: false,
      previewLinkId: null,

      // Bulk import preview dialog
      showBulkImportPreviewDialog: false,
      bulkImportPreviewData: null,
      isLoadingBulkPreview: false,
      isApplyingBulkImport: false,
      bulkPreviewLinkIds: [],

      // Search debounce
      searchDebounceTimer: null,

      // === Local products for export ===
      productsViewMode: "external",
      localProducts: [],
      isLoadingLocalProducts: false,
      localTotalCount: 0,
      localPage: 1,
      localPageSize: 50,
      localSearchQuery: "",
      selectedLocalItems: [],
      isExportingNewBulk: false,
      exportNewBulkProgress: null, // { done, total, success, failed }
      localSearchDebounceTimer: null,

      // === Rename mapping dialog ===
      showRenameMappingDialog: false,
      renameMappingId: null,
      renameMappingValue: "",
      isRenamingMapping: false,

      // === Mapping selection dialog (pre-export) ===
      showMappingSelectionDialog: false,
      mappingSelectionProducts: [],
      mappingSelections: {}, // { productId: categoryMappingId }
      isResolvingMappings: false,
      isSavingMappingSelections: false,
      pendingExportType: null, // 'new' | 'existing'
      pendingExportProductIds: [],

      // === API Limits ===
      limitsData: null,
      isLoadingLimits: false,
      limitsRefreshTimer: null,
      showLimitsDialog: false,

      capabilityLabels: {
        getCategories: "Получение категорий",
        getCategoryFields: "Поля категорий",
        importProduct: "Импорт товара",
        importAllProducts: "Импорт всех товаров",
        importProductBatch: "Пакетный импорт",
        exportProduct: "Экспорт товара",
        exportProductBulk: "Массовый экспорт",
        updateSku: "Обновление артикула",
        importPrices: "Импорт цен",
        exportPrices: "Экспорт цен",
        importStocks: "Импорт остатков",
        exportStocks: "Экспорт остатков",
        getOrders: "Получение заказов",
        updateOrderStatus: "Обновление статуса заказа",
        fieldBasedGrouping: "Группировка через поле",
        endpointBasedGrouping: "Группировка через API",
        importGroups: "Импорт групп",
        deleteGroups: "Удаление групп",
        modifyGroupMembership: "Изменение состава групп",
        archiveProduct: "Архивирование товара",
      },
    };
  },
  watch: {
    activeTab(newVal) {
      this.$router.replace({ query: { ...this.$route.query, tab: newVal } });
    },
    searchQuery(newValue) {
      // Debounce search: wait 500ms after user stops typing
      if (this.searchDebounceTimer) {
        clearTimeout(this.searchDebounceTimer);
      }
      this.searchDebounceTimer = setTimeout(() => {
        // Reset products and fetch with search
        this.products = [];
        this.externalPage = 1;
        this.clearProductsCache();
        this.fetchProducts();
      }, 500);
    },
    localSearchQuery(newValue) {
      if (this.localSearchDebounceTimer) {
        clearTimeout(this.localSearchDebounceTimer);
      }
      this.localSearchDebounceTimer = setTimeout(() => {
        this.localPage = 1;
        this.fetchLocalProducts();
      }, 500);
    },
    // Обновляем данные после завершения импорта (если импорт был для этого подключения)
    "$store.state.importProgress.batchProgress": {
      handler(newVal, oldVal) {
        if (
          newVal?.isFinished &&
          !oldVal?.isFinished &&
          this.$store.state.importProgress.connectionId === this.connectionId
        ) {
          this.fetchProducts(true);
          this.fetchMappings();
          this.fetchLocalCategories();
        }
      },
      deep: true,
    },
  },
  computed: {
    connectionId() {
      return parseInt(this.$route.params.connId);
    },
    breadcrumbs() {
      return [
        { label: "Маркетплейсы", to: PAGE_MARKETPLACE },
        { label: this.connection?.name || "Подключение" },
      ];
    },
    selectedAdapterSchema() {
      if (!this.connection) return null;
      const adapter = this.adapters.find((a) => a.key === this.connection.adapterKey);
      return adapter?.credentialsSchema;
    },
    credentialFields() {
      const schema = this.selectedAdapterSchema;
      if (!schema) return [];
      return schema.fields || schema.Fields || [];
    },
    adapterDisplayName() {
      if (!this.connection) return "";
      const adapter = this.adapters.find((a) => a.key === this.connection.adapterKey);
      return adapter?.displayName || this.connection.adapterKey;
    },

    // Products computed - server handles SKU filtering, just return products
    filteredProducts() {
      return this.products;
    },
    allSelected() {
      return (
        this.filteredProducts.length > 0 &&
        this.selectedItems.length === this.filteredProducts.length
      );
    },
    someSelected() {
      return this.selectedItems.length > 0 && !this.allSelected;
    },
    selectedLinkedItems() {
      return this.filteredProducts.filter(
        (p) => p.isLinked && this.selectedItems.includes(p.externalId)
      );
    },
    selectedUnlinkedItems() {
      return this.filteredProducts.filter(
        (p) => !p.isLinked && this.selectedItems.includes(p.externalId)
      );
    },
    hasSelectedLinked() {
      return this.selectedLinkedItems.length > 0;
    },
    hasSelectedUnlinked() {
      return this.selectedUnlinkedItems.length > 0;
    },
    hasAnySelected() {
      return this.selectedItems.length > 0;
    },
    allLocalSelected() {
      return (
        this.localProducts.length > 0 &&
        this.selectedLocalItems.length === this.localProducts.length
      );
    },
    someLocalSelected() {
      return this.selectedLocalItems.length > 0 && !this.allLocalSelected;
    },
    canGoNextExternal() {
      return this.hasMoreProducts;
    },
    localLastPage() {
      return Math.max(1, Math.ceil(this.localTotalCount / this.localPageSize));
    },
    productsByExternalCategory() {
      const groups = {};
      for (const product of this.selectedUnlinkedItems) {
        const catId = product.externalCategoryId || "__none__";
        const catName = product.externalCategoryName || "Без категории";
        if (!groups[catId]) {
          groups[catId] = {
            externalCategoryId: catId === "__none__" ? null : catId,
            externalCategoryName: catName,
            products: [],
          };
        }
        groups[catId].products.push(product);
      }
      return Object.values(groups);
    },
  },
  methods: {
    ...mapActions([
      "getConnection",
      "updateConnection",
      "testConnection",
      "getCapabilities",
      "getAvailableAdapters",
      "getCategoryMappings",
      "getLocalCategories",
      "getExternalCategories",
      "createCategoryMapping",
      "deleteCategoryMapping",
      "importCategory",
      "createCategory",
      // Products actions
      "getExternalProducts",
      "importLinksBulk",
      "importLinksBulkPreview",
      "exportLinksBulk",
      "importFromLink",
      "importFromLinkPreview",
      "exportFromLink",
      "importSelectedProducts",
      "getImportBatchStatus",
      "exportPrices",
      "exportStocks",
      "getLocalProductsForExport",
      "exportProductsBulk",
      "getConnectionLimits",
      "updateCategoryMapping",
      "duplicateCategoryMapping",
      "resolveMappings",
      "saveMappingSelections",
    ]),

    async fetchData() {
      this.isLoading = true;
      await Promise.all([
        this.fetchConnection(),
        this.fetchAdapters(),
        this.fetchCapabilities(),
        this.fetchMappings(),
        this.fetchLocalCategories(),
        this.fetchLimits(),
      ]);
      this.isLoading = false;
      this.startLimitsAutoRefresh();

      // If products tab was restored from URL, fetch products now
      if (this.activeTab === "products") {
        if (this.externalPage > 1) {
          await this.restoreExternalPage(this.externalPage);
        } else {
          this.onProductsTabActivated();
        }
      }
    },

    async fetchConnection() {
      await this.getConnection({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.connection = extractResponseData(response);
          this.parseCredentials();
        },
        onError: (error) => {
          displaySonnerError(error);
          this.$router.push(PAGE_MARKETPLACE);
        },
      });
    },

    async fetchAdapters() {
      await this.getAvailableAdapters({
        onSuccess: (response) => {
          this.adapters = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchCapabilities() {
      await this.getCapabilities({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.capabilities = extractResponseData(response);
        },
        onError: () => {},
      });
    },

    async fetchLimits() {
      this.isLoadingLimits = true;
      await this.getConnectionLimits({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.limitsData = extractResponseData(response);
        },
        onError: () => {
          this.limitsData = null;
        },
      });
      this.isLoadingLimits = false;
    },

    startLimitsAutoRefresh() {
      this.stopLimitsAutoRefresh();
      this.limitsRefreshTimer = setInterval(() => {
        if (document.visibilityState === "visible") {
          this.fetchLimits();
        }
      }, 30000);
    },

    stopLimitsAutoRefresh() {
      if (this.limitsRefreshTimer) {
        clearInterval(this.limitsRefreshTimer);
        this.limitsRefreshTimer = null;
      }
    },

    getRpsColor(current, max) {
      const ratio = current / max;
      if (ratio < 0.5) return "bg-green-500";
      if (ratio < 0.8) return "bg-yellow-500";
      return "bg-red-500";
    },

    getQuotaColor(usage, limit) {
      if (limit <= 0) return "bg-green-500";
      const ratio = usage / limit;
      if (ratio < 0.5) return "bg-green-500";
      if (ratio < 0.8) return "bg-yellow-500";
      return "bg-red-500";
    },

    formatQuotaResetTime(resetsAt) {
      if (!resetsAt) return null;
      const date = new Date(resetsAt);
      return date.toLocaleString("ru-RU", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
    },

    async fetchMappings() {
      await this.getCategoryMappings({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.categoryMappings = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchLocalCategories() {
      await this.getLocalCategories({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.localCategories = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async searchExternalCategories() {
      this.isLoadingCategories = true;
      await this.getExternalCategories({
        connectionId: this.connectionId,
        search: this.externalCategorySearch || undefined,
        parentId: this.externalCategorySearch ? undefined : this.currentParentId,
        onSuccess: (response) => {
          this.externalCategories = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingCategories = false;
    },

    async loadRootCategories() {
      this.externalCategorySearch = "";
      this.currentParentId = null;
      this.categoryPath = [];
      await this.searchExternalCategories();
    },

    async navigateToCategory(category) {
      if (!category.hasChildren) return;
      this.externalCategorySearch = "";
      this.currentParentId = category.id;
      this.categoryPath.push({ id: category.id, name: category.name });
      await this.searchExternalCategories();
    },

    async navigateToPathIndex(index) {
      if (index < 0) {
        await this.loadRootCategories();
        return;
      }
      this.externalCategorySearch = "";
      const targetCategory = this.categoryPath[index];
      this.currentParentId = targetCategory.id;
      this.categoryPath = this.categoryPath.slice(0, index + 1);
      await this.searchExternalCategories();
    },

    async navigateBack() {
      if (this.categoryPath.length === 0) return;
      this.externalCategorySearch = "";
      this.categoryPath.pop();
      this.currentParentId = this.categoryPath.length > 0
        ? this.categoryPath[this.categoryPath.length - 1].id
        : null;
      await this.searchExternalCategories();
    },

    onCategoryDialogOpen() {
      this.externalCategories = [];
      this.externalCategorySearch = "";
      this.currentParentId = null;
      this.categoryPath = [];
      this.loadRootCategories();
    },

    parseCredentials() {
      if (this.connection?.credentialsJson) {
        try {
          this.credentialValues = JSON.parse(this.connection.credentialsJson);
        } catch {
          this.credentialValues = {};
        }
      }
    },

    buildCredentialsJson() {
      return JSON.stringify(this.credentialValues);
    },

    async handleSave() {
      this.isSaving = true;
      await this.updateConnection({
        connectionId: this.connectionId,
        data: {
          name: this.connection.name,
          credentialsJson: this.buildCredentialsJson(),
          settingsJson: this.connection.settingsJson,
          isActive: this.connection.isActive,
        },
        onSuccess: () => {
          displaySonnerSuccess("Подключение сохранено");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSaving = false;
    },

    async handleTest() {
      this.isTesting = true;
      this.testResult = null;
      await this.testConnection({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.testResult = extractResponseData(response, {});
          if (this.testResult.success) {
            displaySonnerSuccess(this.testResult.message || "Подключение успешно");
          } else {
            displaySonnerError(this.testResult.errorMessage || "Ошибка подключения");
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isTesting = false;
    },

    async handleCreateMapping() {
      if (!this.newMapping.localCategoryId || !this.newMapping.externalCategoryId) {
        displaySonnerError("Выберите обе категории");
        return;
      }

      this.isCreatingMapping = true;
      await this.createCategoryMapping({
        connectionId: this.connectionId,
        data: this.newMapping,
        onSuccess: () => {
          displaySonnerSuccess("Маппинг создан");
          this.showCreateMappingDialog = false;
          this.newMapping = {
            localCategoryId: null,
            externalCategoryId: "",
            externalCategoryName: "",
            name: "",
          };
          this.fetchMappings();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreatingMapping = false;
    },

    async handleDeleteMapping(mappingId) {
      await this.deleteCategoryMapping({
        connectionId: this.connectionId,
        mappingId,
        onSuccess: () => {
          displaySonnerSuccess("Маппинг удалён");
          this.fetchMappings();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async handleDuplicateMapping(mappingId) {
      try {
        await this.duplicateCategoryMapping({
          connectionId: this.connectionId,
          mappingId,
        });
        displaySonnerSuccess("Маппинг продублирован");
        this.fetchMappings();
      } catch (err) {
        displaySonnerError("Ошибка дублирования: " + (err.response?.data?.message || err.message || err));
      }
    },

    openRenameMapping(mapping) {
      this.renameMappingId = mapping.id;
      this.renameMappingValue = mapping.name || "";
      this.showRenameMappingDialog = true;
    },

    async handleRenameMapping() {
      if (this.renameMappingId == null) return;
      this.isRenamingMapping = true;
      try {
        await this.updateCategoryMapping({
          connectionId: this.connectionId,
          mappingId: this.renameMappingId,
          data: { name: this.renameMappingValue || null },
        });
        displaySonnerSuccess("Маппинг переименован");
        this.showRenameMappingDialog = false;
        this.fetchMappings();
      } catch (error) {
        displaySonnerError(error.response?.data?.message || "Ошибка переименования");
      } finally {
        this.isRenamingMapping = false;
      }
    },

    openFieldMapping(mappingId) {
      this.$router.push(`/marketplace/${this.connectionId}/categories/${mappingId}`);
    },

    selectExternalCategory(category) {
      this.newMapping.externalCategoryId = category.id;
      this.newMapping.externalCategoryName = category.name;
    },

    autoSelectExternalCategory(categoryName) {
      if (!categoryName || this.externalCategories.length === 0) return;

      const normalizedName = categoryName.toLowerCase().trim();

      // Try exact match first
      let match = this.externalCategories.find(
        (cat) => cat.name?.toLowerCase().trim() === normalizedName
      );

      // If no exact match, try partial match (category name contains search or vice versa)
      if (!match) {
        match = this.externalCategories.find(
          (cat) =>
            cat.name?.toLowerCase().includes(normalizedName) ||
            normalizedName.includes(cat.name?.toLowerCase())
        );
      }

      if (match) {
        this.selectExternalCategory(match);
      }
    },

    // === Products tab methods ===

    async fetchProducts(forceRefresh = false) {
      this.syncPageQuery();

      if (forceRefresh) {
        this.clearProductsCache();
      }

      // Use cache if available
      const cached = this.productsPageCache[this.externalPage];
      if (cached) {
        this.products = cached.items;
        this.totalCount = cached.total;
        this.linkedCount = cached.linkedCount;
        this.hasMoreProducts = cached.hasMore;
        return;
      }

      this.isLoadingProducts = true;
      const cursor = this.pageCursors[this.externalPage - 1] ?? null;

      await this.getExternalProducts({
        connectionId: this.connectionId,
        pageSize: 50,
        cursor,
        offerIdSearch: this.searchQuery || null,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.products = data.items || [];
          this.totalCount = data.total || 0;
          this.linkedCount = data.linkedCount || 0;
          this.hasMoreProducts = !!data.hasMore;
          // Store cursor for next page
          if (data.nextCursor && this.pageCursors.length <= this.externalPage) {
            this.pageCursors.push(data.nextCursor);
          }
          // Cache page results
          this.productsPageCache[this.externalPage] = {
            items: this.products,
            total: this.totalCount,
            linkedCount: this.linkedCount,
            hasMore: this.hasMoreProducts,
          };
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      this.isLoadingProducts = false;
    },

    async restoreExternalPage(targetPage) {
      this.isLoadingProducts = true;
      // Sequentially load pages to rebuild cursor chain + cache
      for (let p = 1; p <= targetPage; p++) {
        this.externalPage = p;
        await this.fetchProducts();
        if (!this.hasMoreProducts && p < targetPage) {
          break; // not enough pages exist
        }
      }
      this.isLoadingProducts = false;
    },

    clearProductsCache() {
      this.productsPageCache = {};
      this.pageCursors = [null];
      this.hasMoreProducts = false;
    },

    syncPageQuery() {
      const query = { ...this.$route.query };
      let changed = false;
      if (this.externalPage > 1 && query.ePage !== String(this.externalPage)) {
        query.ePage = String(this.externalPage);
        changed = true;
      } else if (this.externalPage <= 1 && query.ePage) {
        delete query.ePage;
        changed = true;
      }
      if (this.localPage > 1 && query.lPage !== String(this.localPage)) {
        query.lPage = String(this.localPage);
        changed = true;
      } else if (this.localPage <= 1 && query.lPage) {
        delete query.lPage;
        changed = true;
      }
      if (changed) {
        this.$router.replace({ query });
      }
    },

    onProductsTabActivated() {
      if (this.products.length === 0) {
        this.fetchProducts();
      }
    },

    toggleSelectAll() {
      if (this.allSelected) {
        this.selectedItems = [];
      } else {
        this.selectedItems = this.filteredProducts.map((p) => p.externalId);
      }
    },

    toggleSelect(externalId) {
      const index = this.selectedItems.indexOf(externalId);
      if (index === -1) {
        this.selectedItems.push(externalId);
      } else {
        this.selectedItems.splice(index, 1);
      }
    },

    isSelected(externalId) {
      return this.selectedItems.includes(externalId);
    },

    async handleImportSingle(product) {
      if (!product.isLinked || !product.linkId) return;

      this.importingLinkId = product.linkId;
      this.isLoadingSinglePreview = true;
      await this.importFromLinkPreview({
        linkId: product.linkId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (!result.hasChanges) {
            displaySonnerSuccess("Изменений не обнаружено");
          } else {
            this.singleImportPreviewData = result;
            this.previewLinkId = product.linkId;
            this.showSingleImportPreviewDialog = true;
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingSinglePreview = false;
      this.importingLinkId = null;
    },

    async confirmSingleImport() {
      if (!this.previewLinkId) return;
      this.isApplyingSingleImport = true;
      await this.importFromLink({
        linkId: this.previewLinkId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (result.updatedFields && result.updatedFields.length > 0) {
            displaySonnerSuccess(`Обновлено: ${result.updatedFields.join(", ")}`);
          } else {
            displaySonnerSuccess("Изменений не обнаружено");
          }
          this.fetchProducts(true);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isApplyingSingleImport = false;
      this.cancelSingleImport();
    },

    cancelSingleImport() {
      this.showSingleImportPreviewDialog = false;
      this.singleImportPreviewData = null;
      this.previewLinkId = null;
    },

    async handleExportSingle(product) {
      if (!product.isLinked || !product.linkId) return;

      this.exportingLinkId = product.linkId;
      await this.exportFromLink({
        linkId: product.linkId,
        onSuccess: () => {
          displaySonnerSuccess("Товар экспортирован на маркетплейс");
          this.fetchProducts(true);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.exportingLinkId = null;
    },

    // Import dialog
    async openImportDialog() {
      if (this.selectedUnlinkedItems.length === 0) {
        displaySonnerError("Выберите несвязанные товары");
        return;
      }

      this.showImportDialog = true;
      this.isLoadingDialogData = true;

      await Promise.all([
        this.loadDialogLocalCategories(),
        this.loadExistingMappings(),
      ]);

      this.initCategoryAssignments();
      this.isLoadingDialogData = false;
    },

    async loadDialogLocalCategories() {
      await this.getLocalCategories({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.localCategories = extractResponseData(response, []);
        },
        onError: () => {
          this.localCategories = [];
        },
      });
    },

    async loadExistingMappings() {
      await this.getCategoryMappings({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.existingMappings = extractResponseData(response, []);
        },
        onError: () => {
          this.existingMappings = [];
        },
      });
    },

    initCategoryAssignments() {
      this.categoryAssignments = {};

      for (const group of this.productsByExternalCategory) {
        const extCatId = group.externalCategoryId;
        if (!extCatId) {
          this.categoryAssignments["__none__"] = { mode: "none", localCategoryId: null, newCategoryName: "" };
          continue;
        }

        const existingMapping = this.existingMappings.find(
          (m) => m.externalCategoryId === extCatId
        );

        if (existingMapping) {
          this.categoryAssignments[extCatId] = {
            mode: "existing",
            localCategoryId: existingMapping.localCategoryId,
            newCategoryName: group.externalCategoryName || "",
          };
        } else {
          this.categoryAssignments[extCatId] = { mode: "new", localCategoryId: null, newCategoryName: group.externalCategoryName || "" };
        }
      }
    },

    setAssignmentMode(extCatId, mode) {
      if (!this.categoryAssignments[extCatId]) {
        this.categoryAssignments[extCatId] = { mode: "none", localCategoryId: null, newCategoryName: "" };
      }
      this.categoryAssignments[extCatId].mode = mode;
      if (mode !== "existing") {
        this.categoryAssignments[extCatId].localCategoryId = null;
      }
    },

    setAssignmentCategory(extCatId, localCategoryId) {
      if (!this.categoryAssignments[extCatId]) {
        this.categoryAssignments[extCatId] = { mode: "existing", localCategoryId: null };
      }
      this.categoryAssignments[extCatId].localCategoryId = localCategoryId;
    },

    getExistingMappingForCategory(extCatId) {
      return this.existingMappings.find((m) => m.externalCategoryId === extCatId);
    },

    async handleImportWithCategories() {
      this.isImportingWithCategories = true;

      try {
        const categoryIdMap = {};

        for (const group of this.productsByExternalCategory) {
          const extCatId = group.externalCategoryId;
          const key = extCatId || "__none__";
          const assignment = this.categoryAssignments[key];

          if (!assignment || assignment.mode === "none") {
            categoryIdMap[key] = null;
          } else if (assignment.mode === "existing") {
            categoryIdMap[key] = assignment.localCategoryId;
          } else if (assignment.mode === "new") {
            const categoryName = assignment.newCategoryName || group.externalCategoryName;
            if (!categoryName) {
              categoryIdMap[key] = null;
              continue;
            }
            let createdCategoryId = null;

            if (extCatId) {
              // Есть внешняя категория - используем importCategory для создания маппинга
              await this.importCategory({
                connectionId: this.connectionId,
                data: {
                  externalCategoryId: extCatId,
                  externalCategoryName: group.externalCategoryName || categoryName,
                  localCategoryName: categoryName,
                },
                onSuccess: (response) => {
                  const result = extractResponseData(response, {});
                  createdCategoryId = result.localCategoryId;
                },
                onError: (error) => {
                  displaySonnerError(`Ошибка создания категории: ${error}`);
                },
              });
            } else {
              // Нет внешней категории - создаём простую локальную категорию
              await this.createCategory({
                name: categoryName,
                description: "",
                onSuccess: (response) => {
                  const result = extractResponseData(response, {});
                  createdCategoryId = result.id;
                },
                onError: (error) => {
                  displaySonnerError(`Ошибка создания категории: ${error}`);
                },
              });
            }
            categoryIdMap[key] = createdCategoryId;
          }
        }

        const productsToImport = this.selectedUnlinkedItems.map((p) => {
          const key = p.externalCategoryId || "__none__";
          const targetCategoryId = categoryIdMap[key];
          const assignment = this.categoryAssignments[key];

          return {
            externalId: p.externalId,
            externalCategoryId: p.externalCategoryId || null,
            targetCategoryId: targetCategoryId || null,
            forceImportWithoutMapping: !assignment || assignment.mode === "none",
            productName: p.name || p.externalId,
          };
        });

        await this.importSelectedProducts({
          connectionId: this.connectionId,
          products: productsToImport,
          onSuccess: (response) => {
            const result = extractResponseData(response, {});
            if (result.batchId) {
              // Новая система с фоновой обработкой - используем глобальный store
              this.$store.dispatch("importProgress/startImportProgress", {
                batchId: result.batchId,
                connectionId: this.connectionId,
                initialProgress: {
                  total: result.queued + result.skipped,
                  queued: result.queued,
                  pending: result.queued,
                  completed: 0,
                  failed: 0,
                  isFinished: false,
                  products: [],
                },
              });
              displaySonnerSuccess(`Добавлено в очередь: ${result.queued} товаров`);
              this.showImportDialog = false;
              this.selectedItems = [];
            } else {
              // Старая система (если вдруг)
              displaySonnerSuccess(
                `Создано: ${result.imported || 0}, пропущено: ${result.skipped || 0}, ошибок: ${result.failed || 0}`
              );
              this.showImportDialog = false;
              this.selectedItems = [];
              this.fetchProducts(true);
              this.fetchMappings();
              this.fetchLocalCategories();
            }
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.isImportingWithCategories = false;
      }
    },

    // Bulk actions
    async handleImportBulk() {
      if (this.isImportingBulk) return;
      if (this.selectedLinkedItems.length === 0) {
        displaySonnerError("Выберите связанные товары");
        return;
      }

      this.isImportingBulk = true;
      this.isLoadingBulkPreview = true;
      const linkIds = this.selectedLinkedItems.map((p) => p.linkId);

      await this.importLinksBulkPreview({
        linkIds,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (result.totalWithChanges === 0) {
            displaySonnerSuccess("Изменений не обнаружено");
          } else {
            this.bulkImportPreviewData = result;
            this.bulkPreviewLinkIds = linkIds;
            this.showBulkImportPreviewDialog = true;
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingBulkPreview = false;
      this.isImportingBulk = false;
    },

    async confirmBulkImport() {
      if (this.isApplyingBulkImport) return;
      if (this.bulkPreviewLinkIds.length === 0) return;
      this.isApplyingBulkImport = true;
      await this.importLinksBulk({
        linkIds: this.bulkPreviewLinkIds,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          displaySonnerSuccess(
            `Импортировано: ${result.successCount || 0}, ошибок: ${result.failedCount || 0}`
          );
          this.selectedItems = [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isApplyingBulkImport = false;
      this.cancelBulkImport();
      this.fetchProducts(true);
    },

    cancelBulkImport() {
      this.showBulkImportPreviewDialog = false;
      this.bulkImportPreviewData = null;
      this.bulkPreviewLinkIds = [];
    },

    async handleExportBulk() {
      if (this.isExportingBulk) return;
      if (this.selectedLinkedItems.length === 0) {
        displaySonnerError("Выберите связанные товары");
        return;
      }

      const productIds = this.selectedLinkedItems.map((p) => p.localProductId);

      // Check if any products need mapping selection
      try {
        this.isResolvingMappings = true;
        const resolved = await this.resolveMappings({
          connectionId: this.connectionId,
          productIds,
        });
        const needsSelection = resolved.products.some((p) => p.needsSelection);

        if (needsSelection) {
          this.mappingSelectionProducts = resolved.products.filter((p) => p.needsSelection);
          this.mappingSelections = {};
          this.pendingExportType = "existing";
          this.pendingExportProductIds = productIds;
          this.showMappingSelectionDialog = true;
          return;
        }
      } catch (error) {
        displaySonnerError("Ошибка проверки маппингов: " + (error.response?.data?.message || error.message));
        return;
      } finally {
        this.isResolvingMappings = false;
      }

      await this.executeExportBulk(productIds);
    },

    async executeExportBulk(productIds) {
      this.isExportingBulk = true;
      try {
        const response = await http.post(
          `/marketplace/connections/${this.connectionId}/products/export-batch`,
          { productIds }
        );
        const result = response.data;

        this.$store.dispatch("exportProgress/startExportProgress", {
          batchId: result.batchId,
          connectionId: this.connectionId,
          initialProgress: {
            total: result.queued,
            pending: result.queued,
            inProgress: 0,
            completed: 0,
            failed: 0,
            isFinished: false,
            products: [],
          },
        });

        displaySonnerSuccess(result.message);
        this.selectedItems = [];
      } catch (error) {
        displaySonnerError(error.response?.data?.message || "Ошибка создания пакета экспорта");
      } finally {
        this.isExportingBulk = false;
      }
    },

    async handleExportPricesBulk() {
      if (this.isExportingPrices) return;
      if (this.selectedLinkedItems.length === 0) {
        displaySonnerError("Выберите связанные товары");
        return;
      }

      this.isExportingPrices = true;
      const linkIds = this.selectedLinkedItems.map((p) => p.linkId);

      await this.exportPrices({
        connectionId: this.connectionId,
        linkIds,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          displaySonnerSuccess(
            `Экспортировано цен: ${result.updatedCount || 0}, пропущено: ${result.skippedCount || 0}`
          );
          this.selectedItems = [];
          this.fetchProducts(true);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isExportingPrices = false;
    },

    async handleExportStocksBulk() {
      if (this.isExportingStocks) return;
      if (this.selectedLinkedItems.length === 0) {
        displaySonnerError("Выберите связанные товары");
        return;
      }

      this.isExportingStocks = true;
      const linkIds = this.selectedLinkedItems.map((p) => p.linkId);

      await this.exportStocks({
        connectionId: this.connectionId,
        linkIds,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          displaySonnerSuccess(
            `Экспортировано остатков: ${result.updatedCount || 0}, пропущено: ${result.skippedCount || 0}`
          );
          this.selectedItems = [];
          this.fetchProducts(true);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isExportingStocks = false;
    },

    // === Local products for export methods ===

    switchProductsView(mode) {
      if (this.productsViewMode === mode) return;
      this.productsViewMode = mode;
      if (mode === "local" && this.localProducts.length === 0) {
        this.fetchLocalProducts();
      }
    },

    async fetchLocalProducts() {
      this.syncPageQuery();
      this.isLoadingLocalProducts = true;
      this.selectedLocalItems = [];

      await this.getLocalProductsForExport({
        connectionId: this.connectionId,
        searchQuery: this.localSearchQuery || null,
        startIndex: (this.localPage - 1) * this.localPageSize,
        pageSize: this.localPageSize,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.localProducts = data.items || [];
          this.localTotalCount = data.totalCount || 0;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingLocalProducts = false;
    },

    toggleSelectAllLocal() {
      if (this.allLocalSelected) {
        this.selectedLocalItems = [];
      } else {
        this.selectedLocalItems = this.localProducts.map((p) => p.id);
      }
    },

    toggleSelectLocal(id) {
      const index = this.selectedLocalItems.indexOf(id);
      if (index === -1) {
        this.selectedLocalItems.push(id);
      } else {
        this.selectedLocalItems.splice(index, 1);
      }
    },

    isLocalSelected(id) {
      return this.selectedLocalItems.includes(id);
    },

    async handleExportNewBulk() {
      if (this.isExportingNewBulk) return;
      if (this.selectedLocalItems.length === 0) {
        displaySonnerError("Выберите товары для экспорта");
        return;
      }

      // Check if any products need mapping selection
      try {
        this.isResolvingMappings = true;
        const resolved = await this.resolveMappings({
          connectionId: this.connectionId,
          productIds: this.selectedLocalItems,
        });
        const needsSelection = resolved.products.some((p) => p.needsSelection);

        if (needsSelection) {
          this.mappingSelectionProducts = resolved.products.filter((p) => p.needsSelection);
          this.mappingSelections = {};
          this.pendingExportType = "new";
          this.pendingExportProductIds = [...this.selectedLocalItems];
          this.showMappingSelectionDialog = true;
          return;
        }
      } catch (error) {
        displaySonnerError("Ошибка проверки маппингов: " + (error.response?.data?.message || error.message));
        return;
      } finally {
        this.isResolvingMappings = false;
      }

      await this.executeExportNewBulk(this.selectedLocalItems);
    },

    async executeExportNewBulk(productIds) {
      this.isExportingNewBulk = true;
      try {
        const response = await http.post(
          `/marketplace/connections/${this.connectionId}/products/export-batch`,
          { productIds }
        );
        const result = response.data;

        this.$store.dispatch("exportProgress/startExportProgress", {
          batchId: result.batchId,
          connectionId: this.connectionId,
          initialProgress: {
            total: result.queued,
            pending: result.queued,
            inProgress: 0,
            completed: 0,
            failed: 0,
            isFinished: false,
            products: [],
          },
        });

        displaySonnerSuccess(result.message);
        this.selectedLocalItems = [];
      } catch (error) {
        displaySonnerError(error.response?.data?.message || "Ошибка создания пакета экспорта");
      } finally {
        this.isExportingNewBulk = false;
        this.exportNewBulkProgress = null;
      }
    },

    async confirmMappingSelections() {
      // Validate all products have a selection
      const unselected = this.mappingSelectionProducts.filter(
        (p) => !this.mappingSelections[p.productId]
      );
      if (unselected.length > 0) {
        displaySonnerError(`Выберите маппинг для всех товаров (осталось: ${unselected.length})`);
        return;
      }

      this.isSavingMappingSelections = true;
      try {
        const selections = Object.entries(this.mappingSelections).map(([productId, categoryMappingId]) => ({
          productId: parseInt(productId),
          categoryMappingId,
        }));

        await this.saveMappingSelections({
          connectionId: this.connectionId,
          selections,
        });

        this.showMappingSelectionDialog = false;

        // Proceed with the pending export
        if (this.pendingExportType === "new") {
          await this.executeExportNewBulk(this.pendingExportProductIds);
        } else {
          await this.executeExportBulk(this.pendingExportProductIds);
        }
      } catch (error) {
        displaySonnerError("Ошибка сохранения выбора маппингов: " + (error.response?.data?.message || error.message));
      } finally {
        this.isSavingMappingSelections = false;
      }
    },

    cancelMappingSelection() {
      this.showMappingSelectionDialog = false;
      this.mappingSelectionProducts = [];
      this.mappingSelections = {};
      this.pendingExportType = null;
      this.pendingExportProductIds = [];
    },

    getImportFieldLabel(field) {
      const labels = {
        name: "Название",
        description: "Описание",
        price: "Цена",
        storageQuantity: "Остаток",
      };
      if (field.startsWith("attr:")) return field.substring(5);
      return labels[field] || field;
    },

    formatImportFieldValue(field, value) {
      if (value === null || value === undefined) return "—";
      if (field === "price") return this.formatPrice(value);
      if (field === "description") {
        const str = String(value);
        return str.length > 80 ? str.substring(0, 80) + "..." : str;
      }
      return String(value);
    },

    getStatusInfo(product) {
      if (!product.isLinked) {
        return { variant: "secondary", icon: Package, label: "Не связан", color: "text-muted-foreground" };
      }
      switch (product.linkStatus) {
        case "Synced":
          return { variant: "default", icon: CheckCircle, label: "Синхронизирован", color: "text-green-500" };
        case "NotSynced":
          return { variant: "secondary", icon: AlertTriangle, label: "Не синхронизирован", color: "text-muted-foreground" };
        case "SyncError":
          return { variant: "destructive", icon: XCircle, label: "Ошибка", color: "text-red-500" };
        case "Modified":
          return { variant: "outline", icon: AlertTriangle, label: "Изменён", color: "text-orange-500" };
        default:
          return { variant: "secondary", icon: Package, label: product.linkStatus || "—", color: "text-muted-foreground" };
      }
    },

    formatPrice(price) {
      if (price === null || price === undefined) return "—";
      return new Intl.NumberFormat("ru-RU", {
        style: "currency",
        currency: "RUB",
        minimumFractionDigits: 0,
      }).format(price);
    },
  },
  mounted() {
    this.fetchData();

    // Handle query params
    const { tab, ePage, lPage, createMapping, localCategoryId, localCategoryName } = this.$route.query;

    if (tab) {
      this.activeTab = tab;
    }
    if (ePage) {
      this.externalPage = parseInt(ePage) || 1;
    }
    if (lPage) {
      this.localPage = parseInt(lPage) || 1;
    }

    if (createMapping === "true" && localCategoryId) {
      this.activeTab = "mappings";
      // Wait for data to load, then open dialog with pre-filled category
      const waitForData = setInterval(() => {
        if (this.localCategories.length > 0) {
          clearInterval(waitForData);
          this.newMapping.localCategoryId = parseInt(localCategoryId);
          this.showCreateMappingDialog = true;

          // Use search by name instead of loading root categories
          if (localCategoryName) {
            this.externalCategorySearch = localCategoryName;
            this.searchExternalCategories().then(() => {
              this.autoSelectExternalCategory(localCategoryName);
            });
          } else {
            this.onCategoryDialogOpen();
          }

          // Clear query params after handling
          this.$router.replace({
            query: {
              ...this.$route.query,
              createMapping: undefined,
              localCategoryId: undefined,
              localCategoryName: undefined
            }
          });
        }
      }, 100);
      // Timeout after 5 seconds
      setTimeout(() => clearInterval(waitForData), 5000);
    }
  },
  beforeUnmount() {
    this.stopLimitsAutoRefresh();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <div v-if="isLoading" class="flex justify-center py-12">
      <Loader2 class="size-8 animate-spin text-muted-foreground" />
    </div>

    <div v-else-if="connection" class="flex flex-col flex-1 mb-4">
      <div class="flex items-center justify-between mb-4">
        <div class="flex items-center gap-4">
          <Button variant="ghost" size="icon" @click="$router.push(PAGE_MARKETPLACE)">
            <ArrowLeft class="size-4" />
          </Button>
          <div>
            <h1 class="text-2xl font-bold">{{ connection.name }}</h1>
            <p class="text-muted-foreground">{{ adapterDisplayName }}</p>
          </div>
          <Badge :variant="connection.isActive ? 'default' : 'secondary'">
            {{ connection.isActive ? "Активен" : "Неактивен" }}
          </Badge>
        </div>
      </div>

      <Tabs v-model="activeTab" default-value="settings" class="w-full flex-1 flex flex-col">
        <TabsList>
          <TabsTrigger value="settings">Настройки</TabsTrigger>
          <TabsTrigger value="mappings">Маппинги</TabsTrigger>
          <TabsTrigger value="products" @click="onProductsTabActivated">Товары</TabsTrigger>
        </TabsList>

        <!-- Settings Tab -->
        <TabsContent value="settings" class="space-y-4">
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2">
                <Settings class="size-5" />
                Настройки подключения
              </CardTitle>
            </CardHeader>
            <CardContent class="space-y-4">
              <div class="space-y-2">
                <Label>Название</Label>
                <Input v-model="connection.name" />
              </div>

              <div class="flex items-center gap-2">
                <Switch :model-value="connection.isActive" @update:model-value="connection.isActive = $event" id="active" />
                <Label for="active">Активное подключение</Label>
              </div>

              <template v-if="credentialFields?.length">
                <div class="border-t pt-4">
                  <h3 class="font-medium mb-4">Данные для подключения</h3>
                  <div
                    v-for="field in credentialFields"
                    :key="field.key"
                    class="space-y-2 mb-4"
                  >
                    <Label>
                      {{ field.displayName }}
                      <span v-if="field.isRequired" class="text-destructive">*</span>
                    </Label>
                    <Input
                      v-model="credentialValues[field.key]"
                      :type="field.isSecret ? 'password' : 'text'"
                      :placeholder="field.placeholder || field.description"
                    />
                  </div>
                </div>
              </template>

              <div class="flex gap-2 pt-4">
                <Button @click="handleSave" :disabled="isSaving">
                  <Loader2 v-if="isSaving" class="size-4 mr-2 animate-spin" />
                  <Save v-else class="size-4 mr-2" />
                  Сохранить
                </Button>
                <Button variant="outline" @click="handleTest" :disabled="isTesting">
                  <Loader2 v-if="isTesting" class="size-4 mr-2 animate-spin" />
                  <TestTube v-else class="size-4 mr-2" />
                  Тест соединения
                </Button>
                <Dialog v-model:open="showLimitsDialog">
                  <DialogTrigger as-child>
                    <Button variant="outline" :disabled="!limitsData">
                      <Activity class="size-4 mr-2" />
                      API Лимиты
                    </Button>
                  </DialogTrigger>
                  <DialogContent class="sm:max-w-lg">
                    <DialogHeader>
                      <DialogTitle class="flex items-center gap-2">
                        <Activity class="size-5" />
                        API Лимиты
                      </DialogTitle>
                      <DialogDescription v-if="limitsData && !limitsData.supported">
                        Маркетплейс не поддерживает отслеживание лимитов
                      </DialogDescription>
                    </DialogHeader>
                    <div v-if="limitsData && limitsData.supported" class="space-y-6">
                      <!-- RPS Gauge -->
                      <div class="space-y-2">
                        <div class="flex items-center justify-between text-sm">
                          <div class="flex items-center gap-2">
                            <Gauge class="size-4 text-muted-foreground" />
                            <span class="font-medium">Запросов в секунду</span>
                          </div>
                          <span class="text-muted-foreground">
                            {{ limitsData.currentWindowRequests }} / {{ limitsData.maxRequestsPerSecond }} RPS
                          </span>
                        </div>
                        <div class="h-2 bg-muted rounded-full overflow-hidden">
                          <div
                            class="h-full rounded-full transition-all duration-300"
                            :class="getRpsColor(limitsData.currentWindowRequests, limitsData.maxRequestsPerSecond)"
                            :style="{ width: Math.min(100, (limitsData.currentWindowRequests / limitsData.maxRequestsPerSecond) * 100) + '%' }"
                          />
                        </div>
                        <div class="text-xs text-muted-foreground">
                          Всего запросов с момента запуска: {{ limitsData.totalRequestsSinceStart.toLocaleString('ru-RU') }}
                        </div>
                      </div>

                      <!-- Quota Bars -->
                      <div v-if="limitsData.quotas?.length > 0" class="space-y-4">
                        <Separator />
                        <div
                          v-for="quota in limitsData.quotas"
                          :key="quota.key"
                          class="space-y-2"
                        >
                          <div class="flex items-center justify-between text-sm">
                            <span class="font-medium">{{ quota.displayName }}</span>
                            <span v-if="quota.limit < 0" class="text-muted-foreground">
                              {{ quota.usage.toLocaleString('ru-RU') }} (безлимитный)
                            </span>
                            <span v-else class="text-muted-foreground">
                              {{ quota.usage.toLocaleString('ru-RU') }} / {{ quota.limit.toLocaleString('ru-RU') }}
                            </span>
                          </div>
                          <div v-if="quota.limit > 0" class="h-2 bg-muted rounded-full overflow-hidden">
                            <div
                              class="h-full rounded-full transition-all duration-300"
                              :class="getQuotaColor(quota.usage, quota.limit)"
                              :style="{ width: Math.min(100, (quota.usage / quota.limit) * 100) + '%' }"
                            />
                          </div>
                          <div v-if="quota.resetsAt" class="text-xs text-muted-foreground">
                            Сброс: {{ formatQuotaResetTime(quota.resetsAt) }}
                          </div>
                        </div>
                      </div>
                    </div>
                  </DialogContent>
                </Dialog>
              </div>

              <div v-if="testResult" class="mt-4 p-4 rounded-lg" :class="testResult.success ? 'bg-green-50 border-green-200' : 'bg-red-50 border-red-200'">
                <div class="flex items-center gap-2">
                  <CheckCircle v-if="testResult.success" class="size-5 text-green-600" />
                  <XCircle v-else class="size-5 text-red-600" />
                  <span :class="testResult.success ? 'text-green-700' : 'text-red-700'">
                    {{ testResult.success ? testResult.message || 'Подключение успешно' : testResult.errorMessage || 'Ошибка подключения' }}
                  </span>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card v-if="capabilities">
            <CardHeader>
              <CardTitle>Возможности адаптера</CardTitle>
            </CardHeader>
            <CardContent>
              <div class="grid grid-cols-2 md:grid-cols-3 gap-2">
                <div v-for="(value, key) in capabilities.capabilities" :key="key" class="flex items-center gap-2">
                  <CheckCircle v-if="value" class="size-4 text-green-600" />
                  <XCircle v-else class="size-4 text-gray-300" />
                  <span class="text-sm">{{ capabilityLabels[key] || key }}</span>
                </div>
              </div>
            </CardContent>
          </Card>
        </TabsContent>

        <!-- Mappings Tab -->
        <TabsContent value="mappings" class="space-y-4">
          <div class="flex justify-end gap-2">
            <Dialog v-model:open="showCreateMappingDialog" @update:open="$event && onCategoryDialogOpen()">
              <DialogTrigger as-child>
                <Button variant="outline">
                  <Link class="size-4 mr-2" />
                  Создать маппинг
                </Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-4xl md:max-w-6xl">
                <DialogHeader>
                  <DialogTitle>Создать маппинг</DialogTitle>
                  <DialogDescription>
                    Выберите локальную категорию и категорию маркетплейса для связывания
                  </DialogDescription>
                </DialogHeader>
                <div class="py-4 space-y-4">
                  <div class="space-y-2">
                    <Label>Локальная категория</Label>
                    <Select v-model="newMapping.localCategoryId">
                      <SelectTrigger>
                        <SelectValue placeholder="Выберите категорию" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem
                          v-for="cat in localCategories"
                          :key="cat.id"
                          :value="cat.id"
                        >
                          {{ cat.name }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  <div class="space-y-2">
                    <Label>Категория маркетплейса</Label>
                    <div class="flex gap-2">
                      <Input
                        v-model="externalCategorySearch"
                        placeholder="Поиск по названию..."
                        @keyup.enter="searchExternalCategories"
                      />
                      <Button variant="outline" size="icon" @click="searchExternalCategories">
                        <Search class="size-4" />
                      </Button>
                    </div>
                  </div>

                  <div v-if="categoryPath.length > 0 && !externalCategorySearch" class="flex items-center gap-1 text-sm flex-wrap">
                    <Button variant="ghost" size="sm" class="h-7 px-2" @click="navigateToPathIndex(-1)">
                      <Home class="size-3" />
                    </Button>
                    <ChevronRight class="size-3 text-muted-foreground" />
                    <template v-for="(pathItem, index) in categoryPath" :key="pathItem.id">
                      <Button
                        variant="ghost"
                        size="sm"
                        class="h-7 px-2 text-xs"
                        :disabled="index === categoryPath.length - 1"
                        @click="navigateToPathIndex(index)"
                      >
                        {{ pathItem.name }}
                      </Button>
                      <ChevronRight v-if="index < categoryPath.length - 1" class="size-3 text-muted-foreground" />
                    </template>
                  </div>

                  <Button
                    v-if="categoryPath.length > 0 && !externalCategorySearch"
                    variant="outline"
                    size="sm"
                    @click="navigateBack"
                    class="w-full justify-start"
                  >
                    <ChevronLeft class="size-4 mr-2" />
                    Назад
                  </Button>

                  <div class="border rounded-lg max-h-48 overflow-y-auto">
                    <div v-if="isLoadingCategories" class="flex justify-center py-8">
                      <Loader2 class="size-6 animate-spin text-muted-foreground" />
                    </div>
                    <div v-else-if="externalCategories.length === 0" class="text-center py-8 text-muted-foreground text-sm">
                      Категории не найдены
                    </div>
                    <div
                      v-else
                      v-for="cat in externalCategories"
                      :key="cat.id"
                      class="p-2 hover:bg-accent cursor-pointer border-b last:border-b-0 flex items-center justify-between"
                      :class="{ 'bg-accent': newMapping.externalCategoryId === cat.id }"
                      @click="cat.hasChildren && !externalCategorySearch ? navigateToCategory(cat) : selectExternalCategory(cat)"
                    >
                      <div class="flex-1 min-w-0">
                        <div class="font-medium truncate">{{ cat.name }}</div>
                        <div v-if="cat.fullPath" class="text-xs text-muted-foreground truncate">{{ cat.fullPath }}</div>
                      </div>
                      <ChevronRight v-if="cat.hasChildren && !externalCategorySearch" class="size-4 text-muted-foreground flex-shrink-0 ml-2" />
                    </div>
                  </div>

                  <div class="space-y-2">
                    <Label>Или введите ID категории вручную</Label>
                    <Input
                      v-model="newMapping.externalCategoryId"
                      placeholder="ID категории маркетплейса"
                    />
                  </div>

                  <div class="space-y-2">
                    <Label>Название маппинга <span class="text-muted-foreground text-xs">(необязательно)</span></Label>
                    <Input
                      v-model="newMapping.name"
                      placeholder="Например: Стандарт, Премиум..."
                    />
                  </div>
                </div>
                <DialogFooter>
                  <DialogClose as-child>
                    <Button variant="outline">Отмена</Button>
                  </DialogClose>
                  <Button @click="handleCreateMapping" :disabled="isCreatingMapping">
                    <Loader2 v-if="isCreatingMapping" class="size-4 mr-2 animate-spin" />
                    {{ isCreatingMapping ? 'Связывание...' : 'Связать' }}
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <Button @click="$router.push(`/marketplace/${connectionId}/categories/import`)">
              <Plus class="size-4 mr-2" />
              Импортировать категорию
            </Button>
          </div>

          <div v-if="categoryMappings.length === 0" class="text-center py-12 text-muted-foreground border rounded-lg">
            <FolderTree class="size-12 mx-auto mb-4 opacity-50" />
            <p>Нет маппингов</p>
            <p class="text-sm">Импортируйте категорию или создайте маппинг вручную</p>
          </div>

          <div v-else class="space-y-2">
            <Card
              v-for="mapping in categoryMappings"
              :key="mapping.id"
              class="cursor-pointer hover:bg-accent/50 transition-colors"
            >
              <CardContent>
                <div class="flex items-center justify-between">
                  <div class="flex items-center gap-4" @click="openFieldMapping(mapping.id)">
                    <FolderTree class="size-5 text-muted-foreground" />
                    <div>
                      <div class="font-medium">
                        {{ mapping.localCategoryName }}
                        <span v-if="mapping.name" class="text-muted-foreground font-normal"> — {{ mapping.name }}</span>
                      </div>
                      <div class="text-sm text-muted-foreground">
                        {{ mapping.externalCategoryName || mapping.externalCategoryId }}
                      </div>
                    </div>
                    <Badge variant="outline">
                      {{ mapping.fieldMappingsCount }} полей
                    </Badge>
                  </div>
                  <div class="flex items-center gap-2">
                    <Button variant="outline" size="sm" @click="openFieldMapping(mapping.id)">
                      Настроить поля
                      <ChevronRight class="size-4 ml-2" />
                    </Button>
                    <DropdownMenu>
                      <DropdownMenuTrigger as-child>
                        <Button variant="ghost" size="icon" @click.stop>
                          <EllipsisVertical class="size-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem @click.stop="openRenameMapping(mapping)">
                          <Pencil class="size-4 mr-2" />
                          Переименовать
                        </DropdownMenuItem>
                        <DropdownMenuItem @click.stop="handleDuplicateMapping(mapping.id)">
                          <Copy class="size-4 mr-2" />
                          Дублировать
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem class="text-destructive" @click.stop="handleDeleteMapping(mapping.id)">
                          <Trash2 class="size-4 mr-2" />
                          Удалить
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>
        </TabsContent>

        <!-- Products Tab -->
        <TabsContent value="products" class="flex-1 flex flex-col space-y-4">
          <!-- View toggle -->
          <div class="flex items-center gap-2">
            <Button
              :variant="productsViewMode === 'external' ? 'default' : 'outline'"
              size="sm"
              @click="switchProductsView('external')"
            >
              МП товары
            </Button>
            <Button
              :variant="productsViewMode === 'local' ? 'default' : 'outline'"
              size="sm"
              @click="switchProductsView('local')"
            >
              Локальные для экспорта
            </Button>
          </div>

          <!-- ==================== EXTERNAL VIEW ==================== -->
          <template v-if="productsViewMode === 'external'">
            <!-- Search -->
            <div class="flex items-center gap-4">
              <div class="relative flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
                <Input
                  v-model="searchQuery"
                  placeholder="Поиск по SKU (точное совпадение)"
                  class="pl-10"
                />
              </div>
            </div>

            <!-- Loading state -->
            <div v-if="isLoadingProducts" class="flex justify-center py-12">
              <Loader2 class="size-8 animate-spin text-muted-foreground" />
            </div>

            <!-- Products table -->
            <div v-else class="border rounded-lg flex-1 overflow-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead class="w-[50px]">
                      <Checkbox
                        :model-value="allSelected"
                        :indeterminate="someSelected"
                        @update:model-value="toggleSelectAll"
                      />
                    </TableHead>
                    <TableHead>Название</TableHead>
                    <TableHead>Внешний ID</TableHead>
                    <TableHead>Локальный SKU</TableHead>
                    <TableHead class="text-right">Цена</TableHead>
                    <TableHead class="text-right">Остаток</TableHead>
                    <TableHead>Статус</TableHead>
                    <TableHead class="w-[100px]">Действия</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow
                    v-for="product in filteredProducts"
                    :key="product.externalId"
                    :class="{ 'bg-muted/30': !product.isLinked }"
                  >
                    <TableCell>
                      <Checkbox
                        :model-value="isSelected(product.externalId)"
                        @update:model-value="toggleSelect(product.externalId)"
                      />
                    </TableCell>
                    <TableCell>
                      <div class="font-medium">{{ product.name }}</div>
                      <div v-if="product.sku" class="text-xs text-muted-foreground">
                        SKU: {{ product.sku }}
                      </div>
                    </TableCell>
                    <TableCell>
                      <code class="text-sm bg-muted px-1.5 py-0.5 rounded">{{ product.externalId }}</code>
                    </TableCell>
                    <TableCell>
                      <div v-if="product.isLinked" class="flex items-center gap-1.5">
                        <Link2 class="size-3 text-green-500" />
                        <code class="text-sm">{{ product.localProductSku }}</code>
                      </div>
                      <span v-else class="text-muted-foreground">—</span>
                    </TableCell>
                    <TableCell class="text-right">
                      <div v-if="product.isLinked" class="space-y-0.5">
                        <div class="font-medium">{{ formatPrice(product.localPrice) }}</div>
                        <div v-if="product.externalPrice !== null && product.externalPrice !== product.localPrice" class="text-xs text-muted-foreground">
                          МП: {{ formatPrice(product.externalPrice) }}
                        </div>
                      </div>
                      <span v-else class="text-muted-foreground">—</span>
                    </TableCell>
                    <TableCell class="text-right">
                      <div v-if="product.isLinked" class="space-y-0.5">
                        <div class="font-medium">{{ product.localStock ?? '—' }}</div>
                        <div v-if="product.externalStock !== null && product.externalStock !== product.localStock" class="text-xs text-muted-foreground">
                          МП: {{ product.externalStock }}
                        </div>
                      </div>
                      <span v-else class="text-muted-foreground">—</span>
                    </TableCell>
                    <TableCell>
                      <Badge :variant="getStatusInfo(product).variant">
                        <component :is="getStatusInfo(product).icon" class="size-3 mr-1" />
                        {{ getStatusInfo(product).label }}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div v-if="product.isLinked" class="flex items-center gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          @click="handleImportSingle(product)"
                          :disabled="importingLinkId === product.linkId"
                          title="Импорт с маркетплейса"
                        >
                          <Loader2 v-if="importingLinkId === product.linkId" class="size-4 animate-spin" />
                          <Download v-else class="size-4" />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          @click="handleExportSingle(product)"
                          :disabled="exportingLinkId === product.linkId"
                          title="Экспорт на маркетплейс"
                        >
                          <Loader2 v-if="exportingLinkId === product.linkId" class="size-4 animate-spin" />
                          <Upload v-else class="size-4" />
                        </Button>
                      </div>
                      <span v-else class="text-muted-foreground">—</span>
                    </TableCell>
                  </TableRow>

                  <TableRow v-if="filteredProducts.length === 0">
                    <TableCell colspan="8" class="text-center py-8 text-muted-foreground">
                      <Package class="size-12 mx-auto mb-4 opacity-50" />
                      <p>Нет товаров для отображения</p>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>

            <!-- Pagination -->
            <div v-if="products.length > 0" class="flex items-center justify-between py-3">
              <p class="text-sm text-muted-foreground">
                Страница {{ externalPage }}, показано: {{ products.length }}
              </p>
              <div class="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="externalPage <= 1"
                  @click="externalPage = 1; clearProductsCache(); fetchProducts()"
                >
                  <ChevronsLeft class="size-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="externalPage <= 1"
                  @click="externalPage--; fetchProducts()"
                >
                  <ChevronLeft class="size-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="!canGoNextExternal"
                  @click="externalPage++; fetchProducts()"
                >
                  <ChevronRight class="size-4" />
                </Button>
              </div>
            </div>

            <!-- Bulk actions bar -->
            <div
              v-if="hasAnySelected"
              class="sticky bottom-0 left-0 right-0 bg-background border-t p-4 flex items-center justify-between -mx-4 -mb-4"
            >
              <div class="text-sm text-muted-foreground">
                Выбрано: {{ selectedItems.length }}
                <span v-if="hasSelectedLinked && hasSelectedUnlinked">
                  ({{ selectedLinkedItems.length }} связанных, {{ selectedUnlinkedItems.length }} несвязанных)
                </span>
                <span v-else-if="hasSelectedLinked">
                  ({{ selectedLinkedItems.length }} связанных)
                </span>
                <span v-else-if="hasSelectedUnlinked">
                  ({{ selectedUnlinkedItems.length }} несвязанных)
                </span>
              </div>
              <div class="flex items-center gap-2">
                <!-- Create local products from unlinked -->
                <Button
                  v-if="hasSelectedUnlinked"
                  variant="outline"
                  @click="openImportDialog"
                  :disabled="isCreatingLocal"
                >
                  <Loader2 v-if="isCreatingLocal" class="size-4 mr-2 animate-spin" />
                  <Plus v-else class="size-4 mr-2" />
                  Создать локальные ({{ selectedUnlinkedItems.length }})
                </Button>

                <!-- Import from marketplace for linked -->
                <Button
                  v-if="hasSelectedLinked"
                  variant="outline"
                  @click="handleImportBulk"
                  :disabled="isImportingBulk"
                >
                  <Loader2 v-if="isImportingBulk" class="size-4 mr-2 animate-spin" />
                  <Download v-else class="size-4 mr-2" />
                  Импорт ({{ selectedLinkedItems.length }})
                </Button>

                <!-- Export to marketplace for linked -->
                <Button
                  v-if="hasSelectedLinked"
                  variant="outline"
                  @click="handleExportBulk"
                  :disabled="isExportingBulk"
                >
                  <Loader2 v-if="isExportingBulk" class="size-4 mr-2 animate-spin" />
                  <Upload v-else class="size-4 mr-2" />
                  Экспорт ({{ selectedLinkedItems.length }})
                </Button>

                <!-- Export prices -->
                <Button
                  v-if="hasSelectedLinked"
                  variant="outline"
                  @click="handleExportPricesBulk"
                  :disabled="isExportingPrices"
                >
                  <Loader2 v-if="isExportingPrices" class="size-4 mr-2 animate-spin" />
                  <DollarSign v-else class="size-4 mr-2" />
                  Экспорт цен
                </Button>

                <!-- Export stocks -->
                <Button
                  v-if="hasSelectedLinked"
                  @click="handleExportStocksBulk"
                  :disabled="isExportingStocks"
                >
                  <Loader2 v-if="isExportingStocks" class="size-4 mr-2 animate-spin" />
                  <Boxes v-else class="size-4 mr-2" />
                  Экспорт остатков
                </Button>
              </div>
            </div>
          </template>

          <!-- ==================== LOCAL VIEW ==================== -->
          <template v-if="productsViewMode === 'local'">
            <!-- Search -->
            <div class="flex items-center gap-4">
              <div class="relative flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
                <Input
                  v-model="localSearchQuery"
                  placeholder="Поиск по названию или SKU..."
                  class="pl-10"
                />
              </div>
              <div class="text-sm text-muted-foreground whitespace-nowrap">
                Найдено: {{ localTotalCount }}
              </div>
            </div>

            <!-- Loading state -->
            <div v-if="isLoadingLocalProducts && localProducts.length === 0" class="flex justify-center py-12">
              <Loader2 class="size-8 animate-spin text-muted-foreground" />
            </div>

            <!-- Local products table -->
            <div v-else class="border rounded-lg flex-1 overflow-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead class="w-[50px]">
                      <Checkbox
                        :model-value="allLocalSelected"
                        :indeterminate="someLocalSelected"
                        @update:model-value="toggleSelectAllLocal"
                      />
                    </TableHead>
                    <TableHead>Название</TableHead>
                    <TableHead>SKU</TableHead>
                    <TableHead class="text-right">Цена</TableHead>
                    <TableHead class="text-right">Остаток</TableHead>
                    <TableHead>Категория</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow
                    v-for="product in localProducts"
                    :key="product.id"
                  >
                    <TableCell>
                      <Checkbox
                        :model-value="isLocalSelected(product.id)"
                        @update:model-value="toggleSelectLocal(product.id)"
                      />
                    </TableCell>
                    <TableCell>
                      <div class="font-medium">{{ product.name }}</div>
                    </TableCell>
                    <TableCell>
                      <code class="text-sm bg-muted px-1.5 py-0.5 rounded">{{ product.sku }}</code>
                    </TableCell>
                    <TableCell class="text-right">
                      {{ formatPrice(product.price) }}
                    </TableCell>
                    <TableCell class="text-right">
                      {{ product.storageQuantity ?? '—' }}
                    </TableCell>
                    <TableCell>
                      <span class="text-sm text-muted-foreground">{{ product.categoryName || '—' }}</span>
                    </TableCell>
                  </TableRow>

                  <TableRow v-if="localProducts.length === 0">
                    <TableCell colspan="6" class="text-center py-8 text-muted-foreground">
                      <Package class="size-12 mx-auto mb-4 opacity-50" />
                      <p>Нет товаров для экспорта</p>
                      <p class="text-sm">Все товары уже экспортированы, или нет маппингов</p>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>

            <!-- Pagination -->
            <div v-if="localTotalCount > 0" class="flex items-center justify-between py-3">
              <p class="text-sm text-muted-foreground">
                Всего: {{ localTotalCount }}
              </p>
              <div class="flex items-center gap-2">
                <p class="text-sm text-muted-foreground mr-2">
                  {{ localPage }} / {{ localLastPage }}
                </p>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="localPage <= 1"
                  @click="localPage = 1; fetchLocalProducts()"
                >
                  <ChevronsLeft class="size-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="localPage <= 1"
                  @click="localPage--; fetchLocalProducts()"
                >
                  <ChevronLeft class="size-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="localPage >= localLastPage"
                  @click="localPage++; fetchLocalProducts()"
                >
                  <ChevronRight class="size-4" />
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8 w-8 p-0"
                  :disabled="localPage >= localLastPage"
                  @click="localPage = localLastPage; fetchLocalProducts()"
                >
                  <ChevronsRight class="size-4" />
                </Button>
              </div>
            </div>

            <!-- Bulk export bar -->
            <div
              v-if="selectedLocalItems.length > 0 || isExportingNewBulk"
              class="sticky bottom-0 left-0 right-0 bg-background border-t p-4 flex items-center justify-between -mx-4 -mb-4"
            >
              <div class="text-sm text-muted-foreground">
                Выбрано: {{ selectedLocalItems.length }}
              </div>
              <Button
                @click="handleExportNewBulk"
                :disabled="isExportingNewBulk"
              >
                <Loader2 v-if="isExportingNewBulk" class="size-4 mr-2 animate-spin" />
                <Upload v-else class="size-4 mr-2" />
                Экспортировать на МП ({{ selectedLocalItems.length }})
              </Button>
            </div>
          </template>
        </TabsContent>
      </Tabs>
    </div>

    <!-- Single Import Preview Dialog -->
    <Dialog v-model:open="showSingleImportPreviewDialog">
      <DialogContent class="sm:max-w-3xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Подтверждение импорта</DialogTitle>
          <DialogDescription>
            Следующие поля будут обновлены из маркетплейса
          </DialogDescription>
        </DialogHeader>
        <div v-if="singleImportPreviewData" class="py-4 max-h-[60vh] overflow-y-auto">
          <table class="w-full table-fixed text-sm">
            <thead>
              <tr class="border-b">
                <th class="w-[100px] text-left py-2 px-2 font-medium text-muted-foreground">Поле</th>
                <th class="text-left py-2 px-2 font-medium text-muted-foreground">Текущее</th>
                <th class="text-left py-2 px-2 font-medium text-muted-foreground">Новое</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="field in singleImportPreviewData.updatedFields"
                :key="field"
                class="border-b"
              >
                <td class="py-2 px-2 font-medium align-top">{{ getImportFieldLabel(field) }}</td>
                <td class="py-2 px-2 text-muted-foreground break-words whitespace-pre-wrap align-top">
                  {{ formatImportFieldValue(field, singleImportPreviewData.oldValues[field]) }}
                </td>
                <td class="py-2 px-2 text-green-600 font-medium break-words whitespace-pre-wrap align-top">
                  {{ formatImportFieldValue(field, singleImportPreviewData.newValues[field]) }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="cancelSingleImport">Отменить</Button>
          <Button @click="confirmSingleImport" :disabled="isApplyingSingleImport">
            <Loader2 v-if="isApplyingSingleImport" class="size-4 mr-2 animate-spin" />
            <Download v-else class="size-4 mr-2" />
            Подтвердить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Bulk Import Preview Dialog -->
    <Dialog v-model:open="showBulkImportPreviewDialog">
      <DialogScrollContent class="sm:max-w-4xl max-h-[85vh]">
        <DialogHeader>
          <DialogTitle>Подтверждение массового импорта</DialogTitle>
          <DialogDescription v-if="bulkImportPreviewData">
            Будет обновлено {{ bulkImportPreviewData.totalWithChanges }} из
            {{ bulkImportPreviewData.totalWithChanges + bulkImportPreviewData.totalWithoutChanges }} товаров
          </DialogDescription>
        </DialogHeader>
        <div v-if="bulkImportPreviewData" class="py-4 space-y-3 max-h-[60vh] overflow-y-auto">
          <template v-for="item in bulkImportPreviewData.items" :key="item.linkId">
            <div v-if="item.hasChanges" class="border rounded-lg p-3">
              <div class="font-medium mb-2">{{ item.productName || `Связь #${item.linkId}` }}</div>
              <table class="w-full table-fixed text-sm">
                <thead>
                  <tr class="border-b">
                    <th class="w-[100px] text-left py-2 px-2 font-medium text-muted-foreground">Поле</th>
                    <th class="text-left py-2 px-2 font-medium text-muted-foreground">Текущее</th>
                    <th class="text-left py-2 px-2 font-medium text-muted-foreground">Новое</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="field in item.updatedFields"
                    :key="field"
                    class="border-b last:border-b-0"
                  >
                    <td class="py-2 px-2 font-medium align-top">{{ getImportFieldLabel(field) }}</td>
                    <td class="py-2 px-2 text-muted-foreground break-words whitespace-pre-wrap align-top">
                      {{ formatImportFieldValue(field, item.oldValues[field]) }}
                    </td>
                    <td class="py-2 px-2 text-green-600 font-medium break-words whitespace-pre-wrap align-top">
                      {{ formatImportFieldValue(field, item.newValues[field]) }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </template>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="cancelBulkImport">Отменить</Button>
          <Button @click="confirmBulkImport" :disabled="isApplyingBulkImport">
            <Loader2 v-if="isApplyingBulkImport" class="size-4 mr-2 animate-spin" />
            <Download v-else class="size-4 mr-2" />
            Подтвердить
          </Button>
        </DialogFooter>
      </DialogScrollContent>
    </Dialog>

    <!-- Import Dialog -->
    <Dialog v-model:open="showImportDialog">
      <DialogContent class="max-w-2xl max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Импорт товаров</DialogTitle>
          <DialogDescription>
            Выберите локальную категорию для каждой категории маркетплейса
          </DialogDescription>
        </DialogHeader>

        <div v-if="isLoadingDialogData" class="flex justify-center py-8">
          <Loader2 class="size-8 animate-spin text-muted-foreground" />
        </div>

        <div v-else class="flex-1 overflow-auto space-y-4 py-4">
          <div class="text-sm text-muted-foreground mb-4">
            Выбрано товаров: {{ selectedUnlinkedItems.length }}
          </div>

          <div
            v-for="group in productsByExternalCategory"
            :key="group.externalCategoryId || '__none__'"
            class="border rounded-lg p-4"
          >
            <div class="flex items-center gap-2 mb-3">
              <FolderOpen class="size-4 text-muted-foreground" />
              <span class="font-medium">{{ group.externalCategoryName }}</span>
              <Badge variant="secondary" class="ml-auto">
                {{ group.products.length }} товаров
              </Badge>
            </div>

            <Separator class="mb-3" />

            <div class="space-y-3">
              <Label class="text-sm text-muted-foreground">Локальная категория:</Label>

              <RadioGroup
                :model-value="categoryAssignments[group.externalCategoryId || '__none__']?.mode || 'none'"
                @update:model-value="setAssignmentMode(group.externalCategoryId || '__none__', $event)"
                class="space-y-3"
              >
                <div class="flex items-center gap-3">
                  <RadioGroupItem value="existing" :id="`existing-${group.externalCategoryId}`" class="mt-0.5 shrink-0" />
                  <Label :for="`existing-${group.externalCategoryId}`" class="flex items-center gap-2 cursor-pointer">
                    <Link2 class="size-4 text-blue-500 shrink-0" />
                    <span>Выбрать существующую:</span>
                  </Label>
                  <Select
                    :model-value="categoryAssignments[group.externalCategoryId || '__none__']?.localCategoryId?.toString() || ''"
                    @update:model-value="setAssignmentCategory(group.externalCategoryId || '__none__', parseInt($event))"
                    :disabled="categoryAssignments[group.externalCategoryId || '__none__']?.mode !== 'existing'"
                  >
                    <SelectTrigger class="w-[180px]">
                      <SelectValue placeholder="Выберите категорию" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem
                        v-for="cat in localCategories"
                        :key="cat.id"
                        :value="cat.id.toString()"
                      >
                        {{ cat.name }}
                        <span v-if="getExistingMappingForCategory(group.externalCategoryId)?.localCategoryId === cat.id" class="text-green-500 ml-1">
                          (маппинг)
                        </span>
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>

                <div class="flex items-start gap-3">
                  <RadioGroupItem value="new" :id="`new-${group.externalCategoryId || '__none__'}`" class="mt-0.5 shrink-0" />
                  <Label :for="`new-${group.externalCategoryId || '__none__'}`" class="flex items-start gap-2 cursor-pointer">
                    <Sparkles class="size-4 text-amber-500 mt-0.5 shrink-0" />
                    <span v-if="group.externalCategoryId">Создать новую из МП: "{{ group.externalCategoryName }}"</span>
                    <span v-else>Создать новую категорию:</span>
                  </Label>
                  <Input
                    v-if="!group.externalCategoryId && categoryAssignments[group.externalCategoryId || '__none__']?.mode === 'new'"
                    v-model="categoryAssignments[group.externalCategoryId || '__none__'].newCategoryName"
                    placeholder="Название категории"
                    class="w-[180px]"
                  />
                </div>

                <div class="flex items-center gap-3">
                  <RadioGroupItem value="none" :id="`none-${group.externalCategoryId || '__none__'}`" class="shrink-0" />
                  <Label :for="`none-${group.externalCategoryId || '__none__'}`" class="flex items-center gap-2 cursor-pointer">
                    <Ban class="size-4 text-muted-foreground shrink-0" />
                    <span>Без категории</span>
                  </Label>
                </div>
              </RadioGroup>
            </div>
          </div>
        </div>

        <DialogFooter class="mt-4">
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button
            @click="handleImportWithCategories"
            :disabled="isImportingWithCategories"
          >
            <Loader2 v-if="isImportingWithCategories" class="size-4 mr-2 animate-spin" />
            <Download v-else class="size-4 mr-2" />
            Импортировать ({{ selectedUnlinkedItems.length }})
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <!-- Rename Mapping Dialog -->
    <Dialog v-model:open="showRenameMappingDialog">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Переименовать маппинг</DialogTitle>
        </DialogHeader>
        <div class="py-4">
          <Input
            v-model="renameMappingValue"
            placeholder="Название маппинга"
            @keyup.enter="handleRenameMapping"
          />
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button @click="handleRenameMapping" :disabled="isRenamingMapping">
            <Loader2 v-if="isRenamingMapping" class="size-4 mr-2 animate-spin" />
            Сохранить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Mapping Selection Dialog (pre-export) -->
    <Dialog v-model:open="showMappingSelectionDialog" @update:open="!$event && cancelMappingSelection()">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Выберите маппинг для товаров</DialogTitle>
          <DialogDescription>
            Для некоторых товаров доступно несколько маппингов категории. Выберите нужный маппинг для каждого товара.
          </DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-3 max-h-96 overflow-y-auto">
          <div
            v-for="product in mappingSelectionProducts"
            :key="product.productId"
            class="border rounded-lg p-3 space-y-2"
          >
            <div class="flex items-center justify-between">
              <div>
                <div class="font-medium text-sm">{{ product.productName }}</div>
                <div class="text-xs text-muted-foreground">{{ product.categoryName }}</div>
              </div>
            </div>
            <Select
              :model-value="mappingSelections[product.productId]?.toString()"
              @update:model-value="(val) => mappingSelections[product.productId] = parseInt(val)"
            >
              <SelectTrigger class="w-full">
                <SelectValue placeholder="Выберите маппинг..." />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="mapping in product.availableMappings"
                  :key="mapping.id"
                  :value="mapping.id.toString()"
                >
                  {{ mapping.name || `Маппинг #${mapping.id}` }}
                  <span class="text-muted-foreground ml-1">
                    ({{ mapping.externalCategoryName || mapping.externalCategoryId }})
                  </span>
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" @click="cancelMappingSelection">Отмена</Button>
          <Button
            @click="confirmMappingSelections"
            :disabled="isSavingMappingSelections"
          >
            <Loader2 v-if="isSavingMappingSelections" class="size-4 mr-2 animate-spin" />
            <Upload v-else class="size-4 mr-2" />
            Продолжить экспорт
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
