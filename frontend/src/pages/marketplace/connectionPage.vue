<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { http } from "@/main";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
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
  ArrowRight,
  ListChecks,
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
  FileText,
  Settings2,
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
  SelectGroup,
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import Separator from "@/components/ui/separator/Separator.vue";
import { PAGE_MARKETPLACE, setPageTitle } from "@/router";
import FieldChangesTable from "@/components/products/FieldChangesTable.vue";

export default {
  components: {
    Page,
    PageSection,
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
    ArrowRight,
    ListChecks,
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
    SelectGroup,
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
    FileText,
    Settings2,
    FieldChangesTable,
    PasswordInput,
  },
  data() {
    const validTabs = ["settings", "mappings", "products"];
    const validViews = ["external", "local"];
    const queryTab = this.$route.query.tab;
    const queryView = this.$route.query.view;
    return {
      // Tab state
      activeTab: validTabs.includes(queryTab) ? queryTab : "settings",

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
      isCreatingExternalCategory: false,
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
      lastSelectedExternalId: null,
      isShiftSelecting: false,
      searchQuery: "",
      isLoadingProducts: false,
      isCreatingLocal: false,
      isImportingBulk: false,
      isExportingBulk: false,
      isExportingPrices: false,
      isExportingStocks: false,
      importingLinkId: null,
      exportingLinkId: null,
      unlinkingLinkId: null,
      totalCount: 0,
      linkedCount: 0,
      hasMoreProducts: false,
      externalPageSize: 50,
      nextCursor: null,
      isLoadingMore: false,

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
      singleImportExcludedFields: new Set(),

      // Bulk import preview dialog
      showBulkImportPreviewDialog: false,
      bulkImportPreviewData: null,
      isLoadingBulkPreview: false,
      isApplyingBulkImport: false,
      bulkPreviewLinkIds: [],

      // Search debounce
      searchDebounceTimer: null,

      // === Local products for export ===
      productsViewMode: validViews.includes(queryView) ? queryView : "external",
      localProducts: [],
      isLoadingLocalProducts: false,
      localTotalCount: 0,
      localPage: 1,
      localPageSize: 50,
      localSearchQuery: "",
      selectedLocalItemsMap: {},
      lastSelectedLocalId: null,
      isExportingNewBulk: false,
      exportNewBulkProgress: null, // { done, total, success, failed }
      localSearchDebounceTimer: null,

      // === Rename mapping dialog ===
      showRenameMappingDialog: false,
      renameMappingId: null,
      renameMappingValue: "",
      isRenamingMapping: false,

      // === Duplicate mapping confirmation ===
      showDuplicateMappingDialog: false,
      duplicateMappingId: null,

      // === Delete mapping dialog ===
      showDeleteMappingDialog: false,
      deleteMappingTarget: null,
      deleteMappingImpact: null,
      deleteMappingReplacementId: null,
      isDeletingMapping: false,

      // === Mapping settings dialog ===
      showMappingSettingsDialog: false,
      mappingSettingsId: null,
      mappingSettingsName: "",
      isSavingMappingSettings: false,

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
        createCategory: "Создание категорий",
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
    productsViewMode(newVal) {
      const query = { ...this.$route.query };
      if (newVal === "external") {
        delete query.view;
      } else {
        query.view = newVal;
      }
      this.$router.replace({ query });
    },
    searchQuery(newValue) {
      // Debounce search: wait 500ms after user stops typing
      if (this.searchDebounceTimer) {
        clearTimeout(this.searchDebounceTimer);
      }
      this.searchDebounceTimer = setTimeout(() => {
        this.fetchProducts(true);
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
    // Обновляем данные после завершения операции (если она была для этого подключения)
    "$store.state.operations.operations": {
      handler(newOps, oldOps) {
        if (!newOps || !oldOps) return;
        for (const op of newOps) {
          if (op.connectionId !== this.connectionId) continue;
          const old = oldOps.find((o) => o.id === op.id);
          if (old && old.status === "running" && op.status !== "running") {
            this.fetchProducts(true);
            this.fetchMappings();
            this.fetchLocalCategories();
            break;
          }
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
        { label: "Адаптеры", to: PAGE_MARKETPLACE },
        { label: this.connection?.name || "Подключение" },
      ];
    },
    selectedAdapterSchema() {
      if (!this.connection) return null;
      const adapter = this.adapters.find(
        (a) => a.key === this.connection.adapterKey,
      );
      return adapter?.credentialsSchema;
    },
    credentialFields() {
      const schema = this.selectedAdapterSchema;
      if (!schema) return [];
      // Plugin-sourced schemas arrive PascalCase; normalize each field so the
      // template's camelCase access (key/displayName/isSecret/…) always resolves.
      const raw = schema.fields || schema.Fields || [];
      return raw.map((f) => {
        const type = f.type ?? f.Type ?? "text";
        return {
          key: f.key ?? f.Key,
          displayName: f.displayName ?? f.DisplayName ?? f.key ?? f.Key,
          description: f.description ?? f.Description,
          placeholder: f.placeholder ?? f.Placeholder,
          type,
          isRequired: f.isRequired ?? f.IsRequired ?? true,
          isSecret: (f.isSecret ?? f.IsSecret ?? false) || type === "password",
        };
      });
    },
    adapterDisplayName() {
      if (!this.connection) return "";
      const adapter = this.adapters.find(
        (a) => a.key === this.connection.adapterKey,
      );
      return adapter?.displayName || this.connection.adapterKey;
    },
    selectedLocalCategory() {
      if (!this.newMapping.localCategoryId) return null;
      return (
        this.localCategories.find(
          (cat) => cat.id === this.newMapping.localCategoryId,
        ) || null
      );
    },

    // Products computed - server handles SKU filtering, just return products
    filteredProducts() {
      return this.products;
    },
    // Resolve each row's status once per render instead of 3× inline in the table cell.
    productStatusMap() {
      const map = {};
      for (const p of this.filteredProducts) {
        map[p.externalId] = this.getStatusInfo(p);
      }
      return map;
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
        (p) => p.isLinked && this.selectedItems.includes(p.externalId),
      );
    },
    selectedUnlinkedItems() {
      return this.filteredProducts.filter(
        (p) => !p.isLinked && this.selectedItems.includes(p.externalId),
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
    selectedLocalIds() {
      return Object.keys(this.selectedLocalItemsMap).map(Number);
    },
    allLocalSelected() {
      return (
        this.localProducts.length > 0 &&
        this.localProducts.every((p) => this.selectedLocalItemsMap[p.id])
      );
    },
    someLocalSelected() {
      return this.selectedLocalIds.length > 0 && !this.allLocalSelected;
    },
    canLoadMore() {
      return this.hasMoreProducts || this.isLoadingMore;
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

    mappingCategoryGroups() {
      if (!this.mappingSelectionProducts.length) return [];
      const groups = {};
      for (const p of this.mappingSelectionProducts) {
        const key = p.categoryName || "Без категории";
        if (!groups[key]) {
          groups[key] = {
            categoryName: key,
            products: [],
            availableMappings: p.availableMappings,
          };
        }
        groups[key].products.push(p);
      }
      return Object.values(groups);
    },
    deleteMappingReplacementOptions() {
      if (!this.deleteMappingTarget) return [];
      const replacements = this.deleteMappingImpact?.replacementMappings;
      if (Array.isArray(replacements) && replacements.length > 0) {
        return replacements;
      }

      return this.categoryMappings.filter(
        (mapping) =>
          mapping.id !== this.deleteMappingTarget.id &&
          mapping.localCategoryId === this.deleteMappingTarget.localCategoryId,
      );
    },
    singleImportPreviewItems() {
      if (!this.singleImportPreviewData?.updatedFields) return [];
      return [
        {
          fields: this.singleImportPreviewData.updatedFields.map((field) => ({
            key: field,
            label: this.getImportFieldLabel(field),
            oldValue: this.formatImportFieldValue(
              field,
              this.singleImportPreviewData.oldValues[field],
            ),
            newValue: this.formatImportFieldValue(
              field,
              this.singleImportPreviewData.newValues[field],
            ),
          })),
        },
      ];
    },
    bulkImportPreviewItems() {
      if (!this.bulkImportPreviewData?.items) return [];
      return this.bulkImportPreviewData.items
        .filter((item) => item.hasChanges)
        .map((item) => ({
          label: item.productName || `Связь #${item.linkId}`,
          fields: item.updatedFields.map((field) => ({
            key: field,
            label: this.getImportFieldLabel(field),
            oldValue: this.formatImportFieldValue(field, item.oldValues[field]),
            newValue: this.formatImportFieldValue(field, item.newValues[field]),
          })),
        }));
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
      "createExternalCategoryMapping",
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
      "unlinkProduct",
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
      ]);
      this.isLoading = false;
      // fetchLimits makes an external API call (GetQuotasAsync) that can be slow;
      // run it after the page is already visible so it doesn't block rendering.
      this.fetchLimits();
      this.startLimitsAutoRefresh();

      // If products tab was restored from URL, fetch products now
      if (this.activeTab === "products") {
        this.onProductsTabActivated();
        if (this.productsViewMode === "local") {
          this.fetchLocalProducts();
        }
      }
    },

    async fetchConnection() {
      await this.getConnection({
        connectionId: this.connectionId,
        onSuccess: (response) => {
          this.connection = extractResponseData(response);
          setPageTitle(this.connection.name);
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
      if (ratio < 0.5) return "bg-success";
      if (ratio < 0.8) return "bg-warning";
      return "bg-error";
    },

    getQuotaColor(usage, limit) {
      if (limit <= 0) return "bg-success";
      const ratio = usage / limit;
      if (ratio < 0.5) return "bg-success";
      if (ratio < 0.8) return "bg-warning";
      return "bg-error";
    },

    formatQuotaResetTime(resetsAt) {
      if (!resetsAt) return null;
      const date = new Date(resetsAt);
      return date.toLocaleString("ru-RU", {
        day: "numeric",
        month: "short",
        hour: "2-digit",
        minute: "2-digit",
      });
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
        parentId: this.externalCategorySearch
          ? undefined
          : this.currentParentId,
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
      this.currentParentId =
        this.categoryPath.length > 0
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

    resetNewMapping() {
      this.newMapping = {
        localCategoryId: null,
        externalCategoryId: "",
        externalCategoryName: "",
        name: "",
      };
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
            displaySonnerSuccess(
              this.testResult.message || "Подключение успешно",
            );
          } else {
            displaySonnerError(
              this.testResult.errorMessage || "Ошибка подключения",
            );
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isTesting = false;
    },

    async handleCreateMapping() {
      if (
        !this.newMapping.localCategoryId ||
        !this.newMapping.externalCategoryId
      ) {
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
          this.resetNewMapping();
          this.fetchMappings();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreatingMapping = false;
    },

    async handleCreateExternalCategoryMapping() {
      if (!this.newMapping.localCategoryId) {
        displaySonnerError("Выберите локальную категорию");
        return;
      }

      if (!this.capabilities?.capabilities?.createCategory) {
        displaySonnerError("Адаптер не поддерживает создание категорий");
        return;
      }

      this.isCreatingExternalCategory = true;
      await this.createExternalCategoryMapping({
        connectionId: this.connectionId,
        data: {
          localCategoryId: this.newMapping.localCategoryId,
          externalCategoryName:
            this.newMapping.externalCategoryName ||
            this.selectedLocalCategory?.name,
          parentExternalCategoryId: this.currentParentId,
          mappingName: this.newMapping.name,
        },
        onSuccess: () => {
          displaySonnerSuccess("Категория создана и связана");
          this.showCreateMappingDialog = false;
          this.resetNewMapping();
          this.fetchMappings();
          this.loadRootCategories();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreatingExternalCategory = false;
    },

    handleDeleteMapping(mapping) {
      const target =
        typeof mapping === "object"
          ? mapping
          : this.categoryMappings.find((m) => m.id === mapping);

      if (!target) return;

      if ((target.linkedProductsCount || 0) > 0) {
        this.deleteMappingTarget = target;
        this.deleteMappingImpact = {
          linkedProductsCount: target.linkedProductsCount,
          replacementMappings: this.categoryMappings.filter(
            (m) =>
              m.id !== target.id &&
              m.localCategoryId === target.localCategoryId,
          ),
        };
        this.deleteMappingReplacementId = null;
        this.showDeleteMappingDialog = true;
        return;
      }

      this.confirmDeleteMapping(target.id, null);
    },

    async confirmDeleteMapping(mappingId = null, reassignToMappingId = null) {
      const targetId = mappingId || this.deleteMappingTarget?.id;
      if (!targetId) return;

      this.isDeletingMapping = true;
      await this.deleteCategoryMapping({
        connectionId: this.connectionId,
        mappingId: targetId,
        reassignToMappingId,
        onSuccess: (response) => {
          const reassigned = response?.data?.reassignedProducts || 0;
          displaySonnerSuccess(
            reassigned > 0
              ? `Маппинг удалён, товары переназначены: ${reassigned}`
              : "Маппинг удалён",
          );
          this.showDeleteMappingDialog = false;
          this.deleteMappingTarget = null;
          this.deleteMappingImpact = null;
          this.deleteMappingReplacementId = null;
          this.fetchMappings();
        },
        onError: (error) => {
          if (error?.response?.status === 409 && error.response.data?.data) {
            this.deleteMappingImpact = error.response.data.data;
            this.showDeleteMappingDialog = true;
          } else {
            displaySonnerError(error);
          }
        },
      });
      this.isDeletingMapping = false;
    },

    async confirmDeleteMappingWithReassign() {
      if (!this.deleteMappingReplacementId) {
        displaySonnerError({
          response: { data: { title: "Выберите маппинг для переназначения" } },
        });
        return;
      }

      await this.confirmDeleteMapping(
        this.deleteMappingTarget?.id,
        parseInt(this.deleteMappingReplacementId),
      );
    },

    requestDuplicateMapping(mappingId) {
      this.duplicateMappingId = mappingId;
      this.showDuplicateMappingDialog = true;
    },

    confirmDuplicateMapping() {
      if (this.duplicateMappingId != null) {
        this.handleDuplicateMapping(this.duplicateMappingId);
      }
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
        displaySonnerError(
          "Ошибка дублирования: " +
            (err.response?.data?.message || err.message || err),
        );
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
        displaySonnerError(
          error.response?.data?.message || "Ошибка переименования",
        );
      } finally {
        this.isRenamingMapping = false;
      }
    },

    openMappingSettingsDialog(mapping) {
      this.mappingSettingsId = mapping.id;
      this.mappingSettingsName = mapping.name || "";
      this.showMappingSettingsDialog = true;
    },

    async saveMappingSettingsDialog() {
      if (this.mappingSettingsId == null) return;
      this.isSavingMappingSettings = true;
      try {
        await this.updateCategoryMapping({
          connectionId: this.connectionId,
          mappingId: this.mappingSettingsId,
          data: { name: this.mappingSettingsName || null },
        });
        displaySonnerSuccess("Настройки сохранены");
        this.showMappingSettingsDialog = false;
        this.fetchMappings();
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message || "Ошибка сохранения",
        );
      } finally {
        this.isSavingMappingSettings = false;
      }
    },

    openFieldMapping(mappingId) {
      this.$router.push(
        `/adapter/${this.connectionId}/categories/${mappingId}`,
      );
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
        (cat) => cat.name?.toLowerCase().trim() === normalizedName,
      );

      // If no exact match, try partial match (category name contains search or vice versa)
      if (!match) {
        match = this.externalCategories.find(
          (cat) =>
            cat.name?.toLowerCase().includes(normalizedName) ||
            normalizedName.includes(cat.name?.toLowerCase()),
        );
      }

      if (match) {
        this.selectExternalCategory(match);
      }
    },

    // === Products tab methods ===

    async fetchProducts(forceRefresh = false) {
      if (forceRefresh) {
        this.nextCursor = null;
        this.hasMoreProducts = false;
      }

      this.isLoadingProducts = true;

      await this.getExternalProducts({
        connectionId: this.connectionId,
        pageSize: this.externalPageSize,
        cursor: null,
        offerIdSearch: this.searchQuery || null,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.products = data.items || [];
          this.lastSelectedExternalId = null;
          this.totalCount = data.total || 0;
          this.linkedCount = data.linkedCount || 0;
          this.hasMoreProducts = !!data.hasMore;
          this.nextCursor = data.nextCursor || null;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      this.isLoadingProducts = false;
    },

    async loadMoreProducts() {
      if (!this.hasMoreProducts || !this.nextCursor || this.isLoadingMore)
        return;

      this.isLoadingMore = true;

      await this.getExternalProducts({
        connectionId: this.connectionId,
        pageSize: this.externalPageSize,
        cursor: this.nextCursor,
        offerIdSearch: this.searchQuery || null,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          const newItems = data.items || [];
          // Append without duplicates
          const existingIds = new Set(this.products.map((p) => p.externalId));
          for (const item of newItems) {
            if (!existingIds.has(item.externalId)) {
              this.products.push(item);
            }
          }
          this.totalCount = data.total || this.totalCount;
          this.linkedCount = data.linkedCount || this.linkedCount;
          this.hasMoreProducts = !!data.hasMore;
          this.nextCursor = data.nextCursor || null;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      this.isLoadingMore = false;
    },

    syncPageQuery() {
      const query = { ...this.$route.query };
      let changed = false;
      if (query.ePage) {
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
      this.lastSelectedExternalId = null;
    },

    captureShiftSelection(event) {
      this.isShiftSelecting = !!event?.shiftKey;
    },

    toggleSelect(externalId, checked) {
      const shouldSelect = checked === true;

      if (this.isShiftSelecting && this.lastSelectedExternalId) {
        this.selectExternalRange(
          this.lastSelectedExternalId,
          externalId,
          shouldSelect,
        );
        this.lastSelectedExternalId = externalId;
        this.isShiftSelecting = false;
        return;
      }

      const index = this.selectedItems.indexOf(externalId);
      if (shouldSelect && index === -1) {
        this.selectedItems.push(externalId);
      } else if (!shouldSelect && index !== -1) {
        this.selectedItems.splice(index, 1);
      }
      this.lastSelectedExternalId = externalId;
      this.isShiftSelecting = false;
    },

    handleExternalRowClick(product, event) {
      if (this.isSelectionIgnoredClick(event)) return;

      const shouldSelect = !this.isSelected(product.externalId);
      if (event?.shiftKey && this.lastSelectedExternalId) {
        this.selectExternalRange(
          this.lastSelectedExternalId,
          product.externalId,
          shouldSelect,
        );
      } else {
        this.toggleSelect(product.externalId, shouldSelect);
      }
      this.lastSelectedExternalId = product.externalId;
    },

    selectExternalRange(anchorId, targetId, shouldSelect) {
      const ids = this.filteredProducts.map((p) => p.externalId);
      const anchorIndex = ids.indexOf(anchorId);
      const targetIndex = ids.indexOf(targetId);

      if (anchorIndex === -1 || targetIndex === -1) {
        const index = this.selectedItems.indexOf(targetId);
        if (shouldSelect && index === -1) {
          this.selectedItems.push(targetId);
        } else if (!shouldSelect && index !== -1) {
          this.selectedItems.splice(index, 1);
        }
        return;
      }

      const [start, end] =
        anchorIndex < targetIndex
          ? [anchorIndex, targetIndex]
          : [targetIndex, anchorIndex];
      const rangeIds = ids.slice(start, end + 1);

      if (shouldSelect) {
        this.selectedItems = Array.from(
          new Set([...this.selectedItems, ...rangeIds]),
        );
      } else {
        const rangeSet = new Set(rangeIds);
        this.selectedItems = this.selectedItems.filter(
          (id) => !rangeSet.has(id),
        );
      }
    },

    isSelectionIgnoredClick(event) {
      const target = event?.target;
      return !!target?.closest?.(
        'a, button, input, textarea, select, [role="button"], [role="checkbox"], [data-ignore-row-select]',
      );
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
      const excludeFields = [...this.singleImportExcludedFields];

      await this.importFromLink({
        linkId: this.previewLinkId,
        excludeFields: excludeFields.length > 0 ? excludeFields : undefined,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (result.updatedFields && result.updatedFields.length > 0) {
            displaySonnerSuccess(
              `Обновлено: ${result.updatedFields.join(", ")}`,
            );
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
      this.singleImportExcludedFields = new Set();
    },

    isSingleImportFieldIncluded(field) {
      return !this.singleImportExcludedFields.has(field);
    },
    toggleSingleImportField(field) {
      const next = new Set(this.singleImportExcludedFields);
      if (next.has(field)) {
        next.delete(field);
      } else {
        next.add(field);
      }
      this.singleImportExcludedFields = next;
    },
    allSingleImportFieldsIncluded() {
      if (!this.singleImportPreviewData?.updatedFields) return false;
      return this.singleImportPreviewData.updatedFields.every(
        (f) => !this.singleImportExcludedFields.has(f),
      );
    },
    allSingleImportFieldsExcluded() {
      if (!this.singleImportPreviewData?.updatedFields) return true;
      return this.singleImportPreviewData.updatedFields.every((f) =>
        this.singleImportExcludedFields.has(f),
      );
    },
    toggleAllSingleImportFields() {
      if (!this.singleImportPreviewData?.updatedFields) return;
      if (this.allSingleImportFieldsIncluded()) {
        this.singleImportExcludedFields = new Set(
          this.singleImportPreviewData.updatedFields,
        );
      } else {
        this.singleImportExcludedFields = new Set();
      }
    },

    async handleExportSingle(product) {
      if (!product.isLinked || !product.linkId) return;

      this.exportingLinkId = product.linkId;

      await this.exportFromLink({
        linkId: product.linkId,
        onSuccess: () => {
          displaySonnerSuccess("Товар экспортирован на адаптер");
          this.fetchProducts(true);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.exportingLinkId = null;
    },

    async handleUnlinkProduct(product) {
      if (!product.isLinked || !product.linkId || this.unlinkingLinkId) return;

      this.unlinkingLinkId = product.linkId;
      await this.unlinkProduct({
        linkId: product.linkId,
        onSuccess: () => {
          displaySonnerSuccess("Товар отвязан от адаптера");
          this.selectedItems = this.selectedItems.filter(
            (id) => id !== product.externalId,
          );
          this.fetchProducts(true);
          this.fetchLocalProducts();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.unlinkingLinkId = null;
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
          this.categoryAssignments["__none__"] = {
            mode: "none",
            localCategoryId: null,
            newCategoryName: "",
          };
          continue;
        }

        const existingMapping = this.existingMappings.find(
          (m) => m.externalCategoryId === extCatId,
        );

        if (existingMapping) {
          this.categoryAssignments[extCatId] = {
            mode: "existing",
            localCategoryId: existingMapping.localCategoryId,
            newCategoryName: group.externalCategoryName || "",
          };
        } else {
          this.categoryAssignments[extCatId] = {
            mode: "new",
            localCategoryId: null,
            newCategoryName: group.externalCategoryName || "",
          };
        }
      }
    },

    setAssignmentMode(extCatId, mode) {
      if (!this.categoryAssignments[extCatId]) {
        this.categoryAssignments[extCatId] = {
          mode: "none",
          localCategoryId: null,
          newCategoryName: "",
        };
      }
      this.categoryAssignments[extCatId].mode = mode;
      if (mode !== "existing") {
        this.categoryAssignments[extCatId].localCategoryId = null;
      }
    },

    setAssignmentCategory(extCatId, localCategoryId) {
      if (!this.categoryAssignments[extCatId]) {
        this.categoryAssignments[extCatId] = {
          mode: "existing",
          localCategoryId: null,
        };
      }
      this.categoryAssignments[extCatId].localCategoryId = localCategoryId;
    },

    getExistingMappingForCategory(extCatId) {
      return this.existingMappings.find(
        (m) => m.externalCategoryId === extCatId,
      );
    },

    async handleImportWithCategories() {
      this.isImportingWithCategories = true;

      // Create operation upfront so user sees it immediately
      const operationId = crypto.randomUUID().replace(/-/g, "");
      const totalProducts = this.selectedUnlinkedItems.length;
      const connectionName = this.connection?.name || "";

      try {
        await http.post("/operations/start", {
          operationId,
          type: "batch-import",
          title: `Импорт ${totalProducts} товаров`,
          batchType: "import",
          connectionId: this.connectionId,
          connectionName,
          totalItems: totalProducts,
        });
      } catch {
        /* operation panel will still work via SignalR */
      }

      try {
        const categoryIdMap = {};
        let cancelled = false;

        for (const group of this.productsByExternalCategory) {
          // Check if operation was paused or stopped
          try {
            const { data } = await http.get(
              `/operations/${operationId}/status`,
            );
            if (data.status === "paused" || data.status === "cancelled") {
              cancelled = true;
              break;
            }
          } catch {
            /* ignore */
          }

          const extCatId = group.externalCategoryId;
          const key = extCatId || "__none__";
          const assignment = this.categoryAssignments[key];

          if (!assignment || assignment.mode === "none") {
            categoryIdMap[key] = null;
          } else if (assignment.mode === "existing") {
            categoryIdMap[key] = assignment.localCategoryId;
          } else if (assignment.mode === "new") {
            const categoryName =
              assignment.newCategoryName || group.externalCategoryName;
            if (!categoryName) {
              categoryIdMap[key] = null;
              continue;
            }
            let createdCategoryId = null;

            if (extCatId) {
              await this.importCategory({
                connectionId: this.connectionId,
                data: {
                  externalCategoryId: extCatId,
                  externalCategoryName:
                    group.externalCategoryName || categoryName,
                  localCategoryName: categoryName,
                  operationId,
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

        if (cancelled) {
          this.showImportDialog = false;
          this.selectedItems = [];
          return;
        }

        const productsToImport = this.selectedUnlinkedItems.map((p) => {
          const key = p.externalCategoryId || "__none__";
          const targetCategoryId = categoryIdMap[key];
          const assignment = this.categoryAssignments[key];

          return {
            externalId: p.externalId,
            externalCategoryId: p.externalCategoryId || null,
            targetCategoryId: targetCategoryId || null,
            forceImportWithoutMapping:
              !assignment || assignment.mode === "none",
            productName: p.name || p.externalId,
          };
        });

        await this.importSelectedProducts({
          connectionId: this.connectionId,
          products: productsToImport,
          operationId,
          onSuccess: (response) => {
            const result = extractResponseData(response, {});
            if (result.batchId) {
              displaySonnerSuccess(
                `Добавлено в очередь: ${result.queued} товаров`,
              );
              this.showImportDialog = false;
              this.selectedItems = [];
            } else {
              displaySonnerSuccess(
                `Создано: ${result.imported || 0}, пропущено: ${result.skipped || 0}, ошибок: ${result.failed || 0}`,
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
            `Импортировано: ${result.successCount || 0}, ошибок: ${result.failedCount || 0}`,
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
          this.mappingSelectionProducts = resolved.products.filter(
            (p) => p.needsSelection,
          );
          this.mappingSelections = {};
          this.pendingExportType = "existing";
          this.pendingExportProductIds = productIds;
          this.showMappingSelectionDialog = true;
          return;
        }
      } catch (error) {
        displaySonnerError(
          "Ошибка проверки маппингов: " +
            (error.response?.data?.message || error.message),
        );
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
          { productIds },
        );
        const result = response.data;

        displaySonnerSuccess(result.message);
        this.selectedItems = [];
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message || "Ошибка создания пакета экспорта",
        );
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
            `Экспортировано цен: ${result.updatedCount || 0}, пропущено: ${result.skippedCount || 0}`,
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
            `Экспортировано остатков: ${result.updatedCount || 0}, пропущено: ${result.skippedCount || 0}`,
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
      if (mode === "local") {
        this.fetchLocalProducts();
      }
    },

    async fetchLocalProducts() {
      this.syncPageQuery();
      this.isLoadingLocalProducts = true;

      await this.getLocalProductsForExport({
        connectionId: this.connectionId,
        searchQuery: this.localSearchQuery || null,
        startIndex: (this.localPage - 1) * this.localPageSize,
        pageSize: this.localPageSize,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.localProducts = data.items || [];
          this.lastSelectedLocalId = null;
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
        // Deselect only current page items
        const next = { ...this.selectedLocalItemsMap };
        for (const p of this.localProducts) {
          delete next[p.id];
        }
        this.selectedLocalItemsMap = next;
      } else {
        const next = { ...this.selectedLocalItemsMap };
        for (const p of this.localProducts) {
          next[p.id] = true;
        }
        this.selectedLocalItemsMap = next;
      }
      this.lastSelectedLocalId = null;
    },

    toggleSelectLocal(id, checked) {
      const shouldSelect = checked === true;

      if (this.isShiftSelecting && this.lastSelectedLocalId) {
        this.selectLocalRange(this.lastSelectedLocalId, id, shouldSelect);
        this.lastSelectedLocalId = id;
        this.isShiftSelecting = false;
        return;
      }

      const next = { ...this.selectedLocalItemsMap };
      if (shouldSelect) {
        next[id] = true;
      } else {
        delete next[id];
      }
      this.selectedLocalItemsMap = next;
      this.lastSelectedLocalId = id;
      this.isShiftSelecting = false;
    },

    handleLocalRowClick(product, event) {
      if (this.isSelectionIgnoredClick(event)) return;

      const shouldSelect = !this.isLocalSelected(product.id);
      if (event?.shiftKey && this.lastSelectedLocalId) {
        this.selectLocalRange(
          this.lastSelectedLocalId,
          product.id,
          shouldSelect,
        );
      } else {
        this.toggleSelectLocal(product.id, shouldSelect);
      }
      this.lastSelectedLocalId = product.id;
    },

    selectLocalRange(anchorId, targetId, shouldSelect) {
      const ids = this.localProducts.map((p) => p.id);
      const anchorIndex = ids.indexOf(anchorId);
      const targetIndex = ids.indexOf(targetId);

      if (anchorIndex === -1 || targetIndex === -1) {
        const next = { ...this.selectedLocalItemsMap };
        if (shouldSelect) {
          next[targetId] = true;
        } else {
          delete next[targetId];
        }
        this.selectedLocalItemsMap = next;
        return;
      }

      const [start, end] =
        anchorIndex < targetIndex
          ? [anchorIndex, targetIndex]
          : [targetIndex, anchorIndex];
      const next = { ...this.selectedLocalItemsMap };

      for (const id of ids.slice(start, end + 1)) {
        if (shouldSelect) {
          next[id] = true;
        } else {
          delete next[id];
        }
      }

      this.selectedLocalItemsMap = next;
    },

    isLocalSelected(id) {
      return !!this.selectedLocalItemsMap[id];
    },

    async handleExportNewBulk() {
      if (this.isExportingNewBulk) return;
      const productIds = this.selectedLocalIds;
      if (productIds.length === 0) {
        displaySonnerError("Выберите товары для экспорта");
        return;
      }

      // Check if any products need mapping selection
      try {
        this.isResolvingMappings = true;
        const resolved = await this.resolveMappings({
          connectionId: this.connectionId,
          productIds,
        });
        const needsSelection = resolved.products.some((p) => p.needsSelection);

        if (needsSelection) {
          this.mappingSelectionProducts = resolved.products.filter(
            (p) => p.needsSelection,
          );
          this.mappingSelections = {};
          this.pendingExportType = "new";
          this.pendingExportProductIds = [...productIds];
          this.showMappingSelectionDialog = true;
          return;
        }
      } catch (error) {
        displaySonnerError(
          "Ошибка проверки маппингов: " +
            (error.response?.data?.message || error.message),
        );
        return;
      } finally {
        this.isResolvingMappings = false;
      }

      await this.executeExportNewBulk(productIds);
    },

    async executeExportNewBulk(productIds) {
      this.isExportingNewBulk = true;
      try {
        const response = await http.post(
          `/marketplace/connections/${this.connectionId}/products/export-batch`,
          { productIds },
        );
        const result = response.data;

        displaySonnerSuccess(result.message);
        this.selectedLocalItemsMap = {};
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message || "Ошибка создания пакета экспорта",
        );
      } finally {
        this.isExportingNewBulk = false;
        this.exportNewBulkProgress = null;
      }
    },

    async confirmMappingSelections() {
      // Validate all products have a selection
      const unselected = this.mappingSelectionProducts.filter(
        (p) => !this.mappingSelections[p.productId],
      );
      if (unselected.length > 0) {
        displaySonnerError(
          `Выберите маппинг для всех товаров (осталось: ${unselected.length})`,
        );
        return;
      }

      this.isSavingMappingSelections = true;
      try {
        const selections = Object.entries(this.mappingSelections).map(
          ([productId, categoryMappingId]) => ({
            productId: parseInt(productId),
            categoryMappingId,
          }),
        );

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
        displaySonnerError(
          "Ошибка сохранения выбора маппингов: " +
            (error.response?.data?.message || error.message),
        );
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

    applyMappingToGroup(group, mappingId) {
      const id = parseInt(mappingId);
      for (const p of group.products) {
        this.mappingSelections[p.productId] = id;
      }
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
        return {
          variant: "secondary",
          icon: Package,
          label: "Не связан",
          color: "text-muted-foreground",
        };
      }
      if (product.mappingMissing) {
        return {
          variant: "destructive",
          icon: AlertTriangle,
          label: "Нет маппинга",
          color: "text-error",
        };
      }
      switch (product.linkStatus) {
        case "Synced":
          return {
            variant: "default",
            icon: CheckCircle,
            label: "Синхронизирован",
            color: "text-success",
          };
        case "NotSynced":
          return {
            variant: "secondary",
            icon: AlertTriangle,
            label: "Не синхронизирован",
            color: "text-muted-foreground",
          };
        case "SyncError":
          return {
            variant: "destructive",
            icon: XCircle,
            label: "Ошибка",
            color: "text-error",
          };
        case "Modified":
          return {
            variant: "outline",
            icon: AlertTriangle,
            label: "Изменён",
            color: "text-warning",
          };
        default:
          return {
            variant: "secondary",
            icon: Package,
            label: product.linkStatus || "—",
            color: "text-muted-foreground",
          };
      }
    },

    getMarketplaceProductUrl(externalId) {
      const template = this.capabilities?.productEditUrlTemplate;
      if (!template) return null;
      return template.replace("{externalId}", externalId);
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
    const {
      tab,
      ePage,
      lPage,
      createMapping,
      localCategoryId,
      localCategoryName,
    } = this.$route.query;

    if (tab) {
      this.activeTab = tab;
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
              localCategoryName: undefined,
            },
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
    <NavigationBar>
      <template v-if="connection" #center>
        <Tabs v-model="activeTab">
          <TabsList>
            <TabsTrigger value="settings">Настройки</TabsTrigger>
            <TabsTrigger value="mappings">Маппинги</TabsTrigger>
            <TabsTrigger value="products" @click="onProductsTabActivated"
              >Товары</TabsTrigger
            >
          </TabsList>
        </Tabs>
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />

    <PageSection :loading="isLoading" class="flex flex-col">
      <div v-if="connection" class="flex flex-col flex-1">
        <Tabs v-model="activeTab" class="w-full flex-1 flex flex-col">
          <!-- Settings Tab -->
          <TabsContent value="settings" class="space-y-4">
            <h2 class="sr-only">Настройки подключения</h2>
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
                  <Switch
                    :model-value="connection.isActive"
                    @update:model-value="connection.isActive = $event"
                    id="active"
                  />
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
                        <span v-if="field.isRequired" class="text-destructive"
                          >*</span
                        >
                      </Label>
                      <PasswordInput
                        v-if="field.isSecret"
                        v-model="credentialValues[field.key]"
                        :placeholder="field.placeholder || field.description"
                      />
                      <Input
                        v-else
                        v-model="credentialValues[field.key]"
                        type="text"
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
                  <Button
                    variant="outline"
                    @click="handleTest"
                    :disabled="isTesting"
                  >
                    <Loader2
                      v-if="isTesting"
                      class="size-4 mr-2 animate-spin"
                    />
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
                    <DialogContent size="lg">
                      <DialogHeader>
                        <DialogTitle>API Лимиты</DialogTitle>
                        <DialogDescription>
                          {{
                            limitsData && !limitsData.supported
                              ? "Адаптер не поддерживает отслеживание лимитов"
                              : "Текущие лимиты API адаптера"
                          }}
                        </DialogDescription>
                      </DialogHeader>
                      <div
                        v-if="limitsData && limitsData.supported"
                        class="space-y-6"
                      >
                        <!-- RPS Gauge -->
                        <div class="space-y-2">
                          <div
                            class="flex items-center justify-between text-sm"
                          >
                            <div class="flex items-center gap-2">
                              <Gauge class="size-4 text-muted-foreground" />
                              <span class="font-medium"
                                >Запросов в секунду</span
                              >
                            </div>
                            <span class="text-muted-foreground">
                              {{ limitsData.currentWindowRequests }} /
                              {{ limitsData.maxRequestsPerSecond }} RPS
                            </span>
                          </div>
                          <div
                            class="h-2 bg-muted rounded-full overflow-hidden"
                          >
                            <div
                              class="h-full rounded-full transition-all duration-300"
                              :class="
                                getRpsColor(
                                  limitsData.currentWindowRequests,
                                  limitsData.maxRequestsPerSecond,
                                )
                              "
                              :style="{
                                width:
                                  Math.min(
                                    100,
                                    (limitsData.currentWindowRequests /
                                      limitsData.maxRequestsPerSecond) *
                                      100,
                                  ) + '%',
                              }"
                            />
                          </div>
                          <div class="text-xs text-muted-foreground">
                            Всего запросов с момента запуска:
                            {{
                              limitsData.totalRequestsSinceStart.toLocaleString(
                                "ru-RU",
                              )
                            }}
                          </div>
                        </div>

                        <!-- Quota Bars -->
                        <div
                          v-if="limitsData.quotas?.length > 0"
                          class="space-y-4"
                        >
                          <Separator />
                          <div
                            v-for="quota in limitsData.quotas"
                            :key="quota.key"
                            class="space-y-2"
                          >
                            <div
                              class="flex items-center justify-between text-sm"
                            >
                              <span class="font-medium">{{
                                quota.displayName
                              }}</span>
                              <span
                                v-if="quota.limit < 0"
                                class="text-muted-foreground"
                              >
                                {{ quota.usage.toLocaleString("ru-RU") }}
                                (безлимитный)
                              </span>
                              <span v-else class="text-muted-foreground">
                                {{ quota.usage.toLocaleString("ru-RU") }} /
                                {{ quota.limit.toLocaleString("ru-RU") }}
                              </span>
                            </div>
                            <div
                              v-if="quota.limit > 0"
                              class="h-2 bg-muted rounded-full overflow-hidden"
                            >
                              <div
                                class="h-full rounded-full transition-all duration-300"
                                :class="getQuotaColor(quota.usage, quota.limit)"
                                :style="{
                                  width:
                                    Math.min(
                                      100,
                                      (quota.usage / quota.limit) * 100,
                                    ) + '%',
                                }"
                              />
                            </div>
                            <div
                              v-if="quota.resetsAt"
                              class="text-xs text-muted-foreground"
                            >
                              Сброс: {{ formatQuotaResetTime(quota.resetsAt) }}
                            </div>
                          </div>
                        </div>
                      </div>
                    </DialogContent>
                  </Dialog>
                </div>

                <div
                  v-if="testResult"
                  class="mt-4 p-4 rounded-lg border"
                  :class="
                    testResult.success
                      ? 'bg-success-muted border-success/30'
                      : 'bg-error-muted border-error/30'
                  "
                >
                  <div class="flex items-center gap-2">
                    <CheckCircle
                      v-if="testResult.success"
                      class="size-5 text-success-foreground"
                    />
                    <XCircle v-else class="size-5 text-error-foreground" />
                    <span
                      :class="
                        testResult.success
                          ? 'text-success-foreground'
                          : 'text-error-foreground'
                      "
                    >
                      {{
                        testResult.success
                          ? testResult.message || "Подключение успешно"
                          : testResult.errorMessage || "Ошибка подключения"
                      }}
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
                  <div
                    v-for="(value, key) in capabilities.capabilities"
                    :key="key"
                    class="flex items-center gap-2"
                  >
                    <CheckCircle
                      v-if="value"
                      class="size-4 text-success-foreground"
                    />
                    <XCircle v-else class="size-4 text-muted-foreground" />
                    <span class="text-sm">{{
                      capabilityLabels[key] || key
                    }}</span>
                  </div>
                </div>
              </CardContent>
            </Card>
          </TabsContent>

          <!-- Mappings Tab -->
          <TabsContent value="mappings" class="space-y-4">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div class="flex items-center gap-2">
                <h2 class="text-base font-semibold">Маппинги категорий</h2>
                <Badge v-if="categoryMappings.length" variant="secondary">
                  {{ categoryMappings.length }}
                </Badge>
              </div>
              <div class="flex shrink-0 flex-wrap gap-2">
                <Dialog
                  v-model:open="showCreateMappingDialog"
                  @update:open="$event && onCategoryDialogOpen()"
                >
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
                        Выберите локальную категорию и категорию адаптера для
                        связывания
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
                        <p
                          v-if="selectedLocalCategory"
                          class="text-xs text-muted-foreground"
                        >
                          Атрибутов:
                          {{ selectedLocalCategory.attributesCount || 0 }}
                        </p>
                      </div>

                      <div class="space-y-2">
                        <Label>Категория адаптера</Label>
                        <div class="flex gap-2">
                          <Input
                            v-model="externalCategorySearch"
                            placeholder="Поиск по названию..."
                            @keyup.enter="searchExternalCategories"
                          />
                          <Button
                            variant="outline"
                            size="icon"
                            aria-label="Найти категории"
                            @click="searchExternalCategories"
                          >
                            <Search class="size-4" />
                          </Button>
                        </div>
                      </div>

                      <div
                        v-if="
                          categoryPath.length > 0 && !externalCategorySearch
                        "
                        class="flex items-center gap-1 text-sm flex-wrap"
                      >
                        <Button
                          variant="ghost"
                          size="sm"
                          class="h-7 px-2"
                          @click="navigateToPathIndex(-1)"
                        >
                          <Home class="size-3" />
                        </Button>
                        <ChevronRight class="size-3 text-muted-foreground" />
                        <template
                          v-for="(pathItem, index) in categoryPath"
                          :key="pathItem.id"
                        >
                          <Button
                            variant="ghost"
                            size="sm"
                            class="h-7 px-2 text-xs"
                            :disabled="index === categoryPath.length - 1"
                            @click="navigateToPathIndex(index)"
                          >
                            {{ pathItem.name }}
                          </Button>
                          <ChevronRight
                            v-if="index < categoryPath.length - 1"
                            class="size-3 text-muted-foreground"
                          />
                        </template>
                      </div>

                      <Button
                        v-if="
                          categoryPath.length > 0 && !externalCategorySearch
                        "
                        variant="outline"
                        size="sm"
                        @click="navigateBack"
                        class="w-full justify-start"
                      >
                        <ChevronLeft class="size-4 mr-2" />
                        Назад
                      </Button>

                      <div class="border rounded-lg max-h-48 overflow-y-auto">
                        <div
                          v-if="isLoadingCategories"
                          class="flex justify-center py-8"
                        >
                          <Loader2
                            class="size-6 animate-spin text-muted-foreground"
                          />
                        </div>
                        <div
                          v-else-if="externalCategories.length === 0"
                          class="text-center py-8 text-muted-foreground text-sm space-y-3"
                        >
                          <div>
                            <p>Категории не найдены</p>
                            <p
                              v-if="
                                capabilities?.capabilities?.createCategory &&
                                selectedLocalCategory
                              "
                              class="text-xs"
                            >
                              Можно создать категорию в
                              {{ adapterDisplayName }} из выбранной локальной
                              категории
                            </p>
                          </div>
                          <Button
                            v-if="capabilities?.capabilities?.createCategory"
                            size="sm"
                            variant="outline"
                            :disabled="
                              !newMapping.localCategoryId ||
                              isCreatingExternalCategory
                            "
                            @click="handleCreateExternalCategoryMapping"
                          >
                            <Loader2
                              v-if="isCreatingExternalCategory"
                              class="size-4 mr-2 animate-spin"
                            />
                            <FolderTree v-else class="size-4 mr-2" />
                            {{
                              isCreatingExternalCategory
                                ? "Создание..."
                                : `Создать в ${adapterDisplayName}`
                            }}
                          </Button>
                        </div>
                        <div
                          v-else
                          v-for="cat in externalCategories"
                          :key="cat.id"
                          class="p-2 hover:bg-accent cursor-pointer border-b last:border-b-0 flex items-center justify-between"
                          :class="{
                            'bg-accent':
                              newMapping.externalCategoryId === cat.id,
                          }"
                          @click="
                            cat.hasChildren && !externalCategorySearch
                              ? navigateToCategory(cat)
                              : selectExternalCategory(cat)
                          "
                        >
                          <div class="flex-1 min-w-0">
                            <div class="font-medium truncate">
                              {{ cat.name }}
                            </div>
                            <div
                              v-if="cat.fullPath"
                              class="text-xs text-muted-foreground truncate"
                            >
                              {{ cat.fullPath }}
                            </div>
                          </div>
                          <ChevronRight
                            v-if="cat.hasChildren && !externalCategorySearch"
                            class="size-4 text-muted-foreground flex-shrink-0 ml-2"
                          />
                        </div>
                      </div>

                      <div class="space-y-2">
                        <Label>Или введите ID категории вручную</Label>
                        <Input
                          v-model="newMapping.externalCategoryId"
                          placeholder="ID категории адаптера"
                        />
                      </div>

                      <div class="space-y-2">
                        <Label
                          >Название маппинга
                          <span class="text-muted-foreground text-xs"
                            >(необязательно)</span
                          ></Label
                        >
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
                      <Button
                        v-if="capabilities?.capabilities?.createCategory"
                        variant="outline"
                        @click="handleCreateExternalCategoryMapping"
                        :disabled="
                          !newMapping.localCategoryId ||
                          isCreatingExternalCategory
                        "
                      >
                        <Loader2
                          v-if="isCreatingExternalCategory"
                          class="size-4 mr-2 animate-spin"
                        />
                        <FolderTree v-else class="size-4 mr-2" />
                        {{
                          isCreatingExternalCategory
                            ? "Создание..."
                            : `Создать в ${adapterDisplayName}`
                        }}
                      </Button>
                      <Button
                        @click="handleCreateMapping"
                        :disabled="
                          isCreatingMapping || isCreatingExternalCategory
                        "
                      >
                        <Loader2
                          v-if="isCreatingMapping"
                          class="size-4 mr-2 animate-spin"
                        />
                        {{ isCreatingMapping ? "Связывание..." : "Связать" }}
                      </Button>
                    </DialogFooter>
                  </DialogContent>
                </Dialog>

                <Button
                  @click="
                    $router.push(`/adapter/${connectionId}/categories/import`)
                  "
                >
                  <Plus class="size-4 mr-2" />
                  Импортировать категорию
                </Button>
              </div>
            </div>

            <div
              v-if="categoryMappings.length === 0"
              class="flex flex-col items-center rounded-lg border border-dashed py-16 text-center"
            >
              <div
                class="grid size-12 place-items-center rounded-full border bg-muted/40"
              >
                <FolderTree class="size-6 text-muted-foreground" />
              </div>
              <p class="mt-4 font-medium">Пока нет маппингов</p>
              <p class="mt-1 max-w-sm text-sm text-muted-foreground">
                Импортируйте категорию из {{ adapterDisplayName }} или создайте
                маппинг вручную, чтобы связать локальные категории с адаптером.
              </p>
            </div>

            <div v-else class="rounded-lg border overflow-hidden">
              <ul class="divide-y divide-border">
                <li
                  v-for="mapping in categoryMappings"
                  :key="mapping.id"
                  role="button"
                  tabindex="0"
                  :aria-label="`Открыть маппинг: ${mapping.localCategoryName}`"
                  class="group flex items-center gap-4 px-5 py-4 hover:bg-accent/40 cursor-pointer transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
                  @click="openFieldMapping(mapping.id)"
                  @keydown.enter.prevent="openFieldMapping(mapping.id)"
                  @keydown.space.prevent="openFieldMapping(mapping.id)"
                >
                  <!-- Main content -->
                  <div class="flex-1 min-w-0">
                    <!-- Categories row -->
                    <div class="flex items-baseline gap-2 min-w-0">
                      <span class="font-semibold truncate">{{ mapping.localCategoryName }}</span>
                      <span class="text-muted-foreground shrink-0 text-sm">→</span>
                      <span class="font-semibold truncate text-muted-foreground">{{ mapping.externalCategoryName || mapping.externalCategoryId }}</span>
                    </div>
                    <!-- Mapping name -->
                    <div v-if="mapping.name" class="mt-0.5 text-sm text-muted-foreground truncate">
                      {{ mapping.name }}
                    </div>
                  </div>

                  <!-- Right side: products count + open hint + menu -->
                  <div class="flex items-center gap-4 shrink-0">
                    <span v-if="mapping.linkedProductsCount" class="text-sm text-muted-foreground">
                      {{ mapping.linkedProductsCount }} товаров
                    </span>
                    <span class="text-sm text-muted-foreground/60 group-hover:text-foreground transition-colors">
                      Открыть →
                    </span>
                    <DropdownMenu>
                      <DropdownMenuTrigger as-child>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="shrink-0 text-muted-foreground"
                          aria-label="Действия с маппингом"
                          @click.stop
                        >
                          <EllipsisVertical class="size-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem @click.stop="openRenameMapping(mapping)">
                          Переименовать
                        </DropdownMenuItem>
                        <DropdownMenuItem @click.stop="requestDuplicateMapping(mapping.id)">
                          Дублировать
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          class="text-destructive"
                          @click.stop="handleDeleteMapping(mapping)"
                        >
                          Удалить
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </li>
              </ul>
            </div>
          </TabsContent>

          <!-- Products Tab -->
          <TabsContent value="products" class="flex-1 flex flex-col space-y-4">
            <h2 class="sr-only">Товары адаптера</h2>
            <!-- View toggle + Search -->
            <div class="flex items-center gap-2">
              <Button
                :variant="
                  productsViewMode === 'external' ? 'default' : 'outline'
                "
                @click="switchProductsView('external')"
              >
                Товары адаптера
              </Button>
              <Button
                :variant="productsViewMode === 'local' ? 'default' : 'outline'"
                @click="switchProductsView('local')"
              >
                Локальные для экспорта
              </Button>
              <div class="relative flex-1">
                <Search
                  class="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground"
                />
                <Input
                  v-if="productsViewMode === 'external'"
                  v-model="searchQuery"
                  placeholder="Поиск по SKU (точное совпадение)"
                  class="pl-10"
                />
                <Input
                  v-else
                  v-model="localSearchQuery"
                  placeholder="Поиск по названию или SKU..."
                  class="pl-10"
                />
              </div>
              <div
                v-if="productsViewMode === 'local'"
                class="text-sm text-muted-foreground whitespace-nowrap"
              >
                Найдено: {{ localTotalCount }}
              </div>
            </div>

            <!-- ==================== EXTERNAL VIEW ==================== -->
            <template v-if="productsViewMode === 'external'">
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
                      :class="[
                        'cursor-pointer select-none',
                        { 'bg-muted/30': !product.isLinked },
                      ]"
                      @click="handleExternalRowClick(product, $event)"
                    >
                      <TableCell>
                        <Checkbox
                          :model-value="isSelected(product.externalId)"
                          @click.capture="captureShiftSelection"
                          @update:model-value="
                            (checked) =>
                              toggleSelect(product.externalId, checked)
                          "
                        />
                      </TableCell>
                      <TableCell>
                        <RouterLink
                          v-if="product.localProductId"
                          :to="`/products/${product.localProductId}/edit`"
                          class="font-medium hover:underline underline-offset-2"
                          @click.stop
                          >{{ product.name }}</RouterLink
                        >
                        <div v-else class="font-medium">{{ product.name }}</div>
                        <div
                          v-if="product.sku"
                          class="text-xs text-muted-foreground"
                        >
                          SKU: {{ product.sku }}
                        </div>
                      </TableCell>
                      <TableCell>
                        <a
                          v-if="getMarketplaceProductUrl(product.externalId)"
                          :href="getMarketplaceProductUrl(product.externalId)"
                          target="_blank"
                          rel="noopener"
                          class="text-sm bg-muted px-1.5 py-0.5 rounded font-mono hover:underline underline-offset-2"
                          >{{ product.externalId }}</a
                        >
                        <code
                          v-else
                          class="text-sm bg-muted px-1.5 py-0.5 rounded"
                          >{{ product.externalId }}</code
                        >
                      </TableCell>
                      <TableCell>
                        <div
                          v-if="product.isLinked"
                          class="flex items-center gap-1.5"
                        >
                          <Link2 class="size-3 text-success-foreground" />
                          <code class="text-sm">{{
                            product.localProductSku
                          }}</code>
                        </div>
                        <span v-else class="text-muted-foreground">—</span>
                      </TableCell>
                      <TableCell class="text-right">
                        <div v-if="product.isLinked" class="space-y-0.5">
                          <div class="font-medium">
                            {{ formatPrice(product.localPrice) }}
                          </div>
                          <div
                            v-if="
                              product.externalPrice !== null &&
                              product.externalPrice !== product.localPrice
                            "
                            class="text-xs text-muted-foreground"
                          >
                            Адаптер: {{ formatPrice(product.externalPrice) }}
                          </div>
                        </div>
                        <span v-else class="text-muted-foreground">—</span>
                      </TableCell>
                      <TableCell class="text-right">
                        <div v-if="product.isLinked" class="space-y-0.5">
                          <div class="font-medium">
                            {{ product.localStock ?? "—" }}
                          </div>
                          <div
                            v-if="
                              product.externalStock !== null &&
                              product.externalStock !== product.localStock
                            "
                            class="text-xs text-muted-foreground"
                          >
                            Адаптер: {{ product.externalStock }}
                          </div>
                        </div>
                        <span v-else class="text-muted-foreground">—</span>
                      </TableCell>
                      <TableCell>
                        <Badge
                          :variant="
                            productStatusMap[product.externalId].variant
                          "
                        >
                          <component
                            :is="productStatusMap[product.externalId].icon"
                            class="size-3 mr-1"
                          />
                          {{ productStatusMap[product.externalId].label }}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <div
                          v-if="product.isLinked"
                          class="flex items-center gap-1"
                        >
                          <Button
                            variant="ghost"
                            size="sm"
                            @click="handleImportSingle(product)"
                            :disabled="importingLinkId === product.linkId"
                            title="Импорт с адаптера"
                            aria-label="Импорт с адаптера"
                          >
                            <Loader2
                              v-if="importingLinkId === product.linkId"
                              class="size-4 animate-spin"
                            />
                            <Download v-else class="size-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            @click="handleExportSingle(product)"
                            :disabled="
                              exportingLinkId === product.linkId ||
                              product.mappingMissing
                            "
                            title="Экспорт на адаптер"
                            aria-label="Экспорт на адаптер"
                          >
                            <Loader2
                              v-if="exportingLinkId === product.linkId"
                              class="size-4 animate-spin"
                            />
                            <Upload v-else class="size-4" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            class="text-destructive hover:text-destructive"
                            @click="handleUnlinkProduct(product)"
                            :disabled="unlinkingLinkId === product.linkId"
                            title="Отвязать товар"
                            aria-label="Отвязать товар"
                          >
                            <Loader2
                              v-if="unlinkingLinkId === product.linkId"
                              class="size-4 animate-spin"
                            />
                            <Ban v-else class="size-4" />
                          </Button>
                        </div>
                        <span v-else class="text-muted-foreground">—</span>
                      </TableCell>
                    </TableRow>

                    <TableRow v-if="filteredProducts.length === 0">
                      <TableCell
                        colspan="8"
                        class="text-center py-8 text-muted-foreground"
                      >
                        <Package class="size-12 mx-auto mb-4 opacity-50" />
                        <p>Нет товаров для отображения</p>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </div>

              <!-- Load more -->
              <div v-if="canLoadMore" class="flex justify-center py-4">
                <Button
                  variant="outline"
                  size="sm"
                  :disabled="isLoadingMore"
                  @click="loadMoreProducts"
                >
                  <Loader2
                    v-if="isLoadingMore"
                    class="size-4 mr-2 animate-spin"
                  />
                  {{ isLoadingMore ? "Загрузка..." : "Загрузить ещё" }}
                </Button>
              </div>

              <!-- Bulk actions bar -->
              <div
                v-if="hasAnySelected"
                class="sticky bottom-0 left-0 right-0 bg-background border-t p-4 flex items-center justify-between -mx-4 -mb-4"
              >
                <div class="text-sm text-muted-foreground">
                  Выбрано: {{ selectedItems.length }}
                  <span v-if="hasSelectedLinked && hasSelectedUnlinked">
                    ({{ selectedLinkedItems.length }} связанных,
                    {{ selectedUnlinkedItems.length }} несвязанных)
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
                    <Loader2
                      v-if="isCreatingLocal"
                      class="size-4 mr-2 animate-spin"
                    />
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
                    <Loader2
                      v-if="isImportingBulk"
                      class="size-4 mr-2 animate-spin"
                    />
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
                    <Loader2
                      v-if="isExportingBulk"
                      class="size-4 mr-2 animate-spin"
                    />
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
                    <Loader2
                      v-if="isExportingPrices"
                      class="size-4 mr-2 animate-spin"
                    />
                    <DollarSign v-else class="size-4 mr-2" />
                    Экспорт цен
                  </Button>

                  <!-- Export stocks -->
                  <Button
                    v-if="hasSelectedLinked"
                    @click="handleExportStocksBulk"
                    :disabled="isExportingStocks"
                  >
                    <Loader2
                      v-if="isExportingStocks"
                      class="size-4 mr-2 animate-spin"
                    />
                    <Boxes v-else class="size-4 mr-2" />
                    Экспорт остатков
                  </Button>
                </div>
              </div>
            </template>

            <!-- ==================== LOCAL VIEW ==================== -->
            <template v-if="productsViewMode === 'local'">
              <!-- Loading state -->
              <div
                v-if="isLoadingLocalProducts && localProducts.length === 0"
                class="flex justify-center py-12"
              >
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
                      class="cursor-pointer select-none"
                      @click="handleLocalRowClick(product, $event)"
                    >
                      <TableCell>
                        <Checkbox
                          :model-value="isLocalSelected(product.id)"
                          @click.capture="captureShiftSelection"
                          @update:model-value="
                            (checked) => toggleSelectLocal(product.id, checked)
                          "
                        />
                      </TableCell>
                      <TableCell>
                        <RouterLink
                          :to="`/products/${product.id}/edit`"
                          class="font-medium hover:underline underline-offset-2"
                          >{{ product.name }}</RouterLink
                        >
                      </TableCell>
                      <TableCell>
                        <code class="text-sm bg-muted px-1.5 py-0.5 rounded">{{
                          product.sku
                        }}</code>
                      </TableCell>
                      <TableCell class="text-right">
                        {{ formatPrice(product.price) }}
                      </TableCell>
                      <TableCell class="text-right">
                        {{ product.storageQuantity ?? "—" }}
                      </TableCell>
                      <TableCell>
                        <span class="text-sm text-muted-foreground">{{
                          product.categoryName || "—"
                        }}</span>
                      </TableCell>
                    </TableRow>

                    <TableRow v-if="localProducts.length === 0">
                      <TableCell
                        colspan="6"
                        class="text-center py-8 text-muted-foreground"
                      >
                        <Package class="size-12 mx-auto mb-4 opacity-50" />
                        <p>Нет товаров для экспорта</p>
                        <p class="text-sm">
                          Все товары уже экспортированы, или нет маппингов
                        </p>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </div>

              <!-- Pagination -->
              <div
                v-if="localTotalCount > 0"
                class="flex items-center justify-between py-3"
              >
                <div class="flex items-center gap-2">
                  <p class="text-sm text-muted-foreground">по</p>
                  <Select
                    :model-value="String(localPageSize)"
                    @update:model-value="
                      (val) => {
                        localPageSize = Number(val);
                        localPage = 1;
                        fetchLocalProducts();
                      }
                    "
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
                  <p class="text-sm text-muted-foreground">
                    Всего: {{ localTotalCount }}
                  </p>
                </div>
                <div class="flex items-center gap-2">
                  <p class="text-sm text-muted-foreground mr-2">
                    {{ localPage }} / {{ localLastPage }}
                  </p>
                  <Button
                    variant="outline"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :disabled="localPage <= 1"
                    @click="
                      localPage = 1;
                      fetchLocalProducts();
                    "
                  >
                    <ChevronsLeft class="size-4" />
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :disabled="localPage <= 1"
                    @click="
                      localPage--;
                      fetchLocalProducts();
                    "
                  >
                    <ChevronLeft class="size-4" />
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :disabled="localPage >= localLastPage"
                    @click="
                      localPage++;
                      fetchLocalProducts();
                    "
                  >
                    <ChevronRight class="size-4" />
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    class="h-8 w-8 p-0"
                    :disabled="localPage >= localLastPage"
                    @click="
                      localPage = localLastPage;
                      fetchLocalProducts();
                    "
                  >
                    <ChevronsRight class="size-4" />
                  </Button>
                </div>
              </div>

              <!-- Bulk export bar -->
              <div
                v-if="selectedLocalIds.length > 0 || isExportingNewBulk"
                class="sticky bottom-0 left-0 right-0 bg-background border-t p-4 flex items-center justify-between -mx-4 -mb-4"
              >
                <div class="text-sm text-muted-foreground">
                  Выбрано: {{ selectedLocalIds.length }}
                </div>
                <Button
                  @click="handleExportNewBulk"
                  :disabled="isExportingNewBulk"
                >
                  <Loader2
                    v-if="isExportingNewBulk"
                    class="size-4 mr-2 animate-spin"
                  />
                  <Upload v-else class="size-4 mr-2" />
                  Экспортировать на адаптер ({{ selectedLocalIds.length }})
                </Button>
              </div>
            </template>
          </TabsContent>
        </Tabs>
      </div>
    </PageSection>

    <!-- Single Import Preview Dialog -->
    <Dialog v-model:open="showSingleImportPreviewDialog">
      <DialogContent size="3xl" class="max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Подтверждение импорта</DialogTitle>
          <DialogDescription>
            Следующие поля будут обновлены из адаптера
          </DialogDescription>
        </DialogHeader>
        <div
          v-if="singleImportPreviewData"
          class="py-4 max-h-[60vh] overflow-y-auto"
        >
          <FieldChangesTable
            :items="singleImportPreviewItems"
            selectable
            :excluded-fields="singleImportExcludedFields"
            @update:excluded-fields="singleImportExcludedFields = $event"
          />
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline" @click="cancelSingleImport"
              >Отмена</Button
            >
          </DialogClose>
          <Button
            @click="confirmSingleImport"
            :disabled="
              isApplyingSingleImport || allSingleImportFieldsExcluded()
            "
          >
            <Loader2
              v-if="isApplyingSingleImport"
              class="size-4 mr-2 animate-spin"
            />
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
            {{
              bulkImportPreviewData.totalWithChanges +
              bulkImportPreviewData.totalWithoutChanges
            }}
            товаров
          </DialogDescription>
        </DialogHeader>
        <div
          v-if="bulkImportPreviewData"
          class="py-4 max-h-[60vh] overflow-y-auto"
        >
          <FieldChangesTable :items="bulkImportPreviewItems" />
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline" @click="cancelBulkImport">Отмена</Button>
          </DialogClose>
          <Button @click="confirmBulkImport" :disabled="isApplyingBulkImport">
            <Loader2
              v-if="isApplyingBulkImport"
              class="size-4 mr-2 animate-spin"
            />
            <Download v-else class="size-4 mr-2" />
            Подтвердить
          </Button>
        </DialogFooter>
      </DialogScrollContent>
    </Dialog>

    <!-- Import Dialog -->
    <Dialog v-model:open="showImportDialog">
      <DialogContent size="2xl" class="max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Импорт товаров</DialogTitle>
          <DialogDescription>
            Выберите локальную категорию для каждой категории адаптера
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
              <Label class="text-sm text-muted-foreground"
                >Локальная категория:</Label
              >

              <RadioGroup
                :model-value="
                  categoryAssignments[group.externalCategoryId || '__none__']
                    ?.mode || 'none'
                "
                @update:model-value="
                  setAssignmentMode(
                    group.externalCategoryId || '__none__',
                    $event,
                  )
                "
                class="space-y-3"
              >
                <div class="flex items-center gap-3">
                  <RadioGroupItem
                    value="existing"
                    :id="`existing-${group.externalCategoryId}`"
                    class="mt-0.5 shrink-0"
                  />
                  <Label
                    :for="`existing-${group.externalCategoryId}`"
                    class="flex items-center gap-2 cursor-pointer"
                  >
                    <Link2 class="size-4 text-muted-foreground shrink-0" />
                    <span>Выбрать существующую:</span>
                  </Label>
                  <Select
                    :model-value="
                      categoryAssignments[
                        group.externalCategoryId || '__none__'
                      ]?.localCategoryId?.toString() || ''
                    "
                    @update:model-value="
                      setAssignmentCategory(
                        group.externalCategoryId || '__none__',
                        parseInt($event),
                      )
                    "
                    :disabled="
                      categoryAssignments[
                        group.externalCategoryId || '__none__'
                      ]?.mode !== 'existing'
                    "
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
                        <span
                          v-if="
                            getExistingMappingForCategory(
                              group.externalCategoryId,
                            )?.localCategoryId === cat.id
                          "
                          class="text-success ml-1"
                        >
                          (маппинг)
                        </span>
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>

                <div class="flex items-start gap-3">
                  <RadioGroupItem
                    value="new"
                    :id="`new-${group.externalCategoryId || '__none__'}`"
                    class="mt-0.5 shrink-0"
                  />
                  <Label
                    :for="`new-${group.externalCategoryId || '__none__'}`"
                    class="flex items-start gap-2 cursor-pointer"
                  >
                    <Sparkles class="size-4 text-warning mt-0.5 shrink-0" />
                    <span v-if="group.externalCategoryId"
                      >Создать новую из адаптера: "{{
                        group.externalCategoryName
                      }}"</span
                    >
                    <span v-else>Создать новую категорию:</span>
                  </Label>
                  <Input
                    v-if="
                      !group.externalCategoryId &&
                      categoryAssignments[
                        group.externalCategoryId || '__none__'
                      ]?.mode === 'new'
                    "
                    v-model="
                      categoryAssignments[
                        group.externalCategoryId || '__none__'
                      ].newCategoryName
                    "
                    placeholder="Название категории"
                    class="w-[180px]"
                  />
                </div>

                <div class="flex items-center gap-3">
                  <RadioGroupItem
                    value="none"
                    :id="`none-${group.externalCategoryId || '__none__'}`"
                    class="shrink-0"
                  />
                  <Label
                    :for="`none-${group.externalCategoryId || '__none__'}`"
                    class="flex items-center gap-2 cursor-pointer"
                  >
                    <Ban class="size-4 text-muted-foreground shrink-0" />
                    <span>Без категории</span>
                  </Label>
                </div>
              </RadioGroup>
            </div>
          </div>
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button
            @click="handleImportWithCategories"
            :disabled="isImportingWithCategories"
          >
            <Loader2
              v-if="isImportingWithCategories"
              class="size-4 mr-2 animate-spin"
            />
            <Download v-else class="size-4 mr-2" />
            Импортировать ({{ selectedUnlinkedItems.length }})
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <!-- Duplicate Mapping confirmation -->
    <AlertDialog v-model:open="showDuplicateMappingDialog">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Дублировать маппинг?</AlertDialogTitle>
          <AlertDialogDescription>
            Будет создана полная копия этого маппинга со всеми настройками
            графа.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Отмена</AlertDialogCancel>
          <AlertDialogAction @click="confirmDuplicateMapping">
            Дублировать
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <!-- Rename Mapping Dialog -->
    <Dialog v-model:open="showRenameMappingDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Переименовать маппинг</DialogTitle>
          <DialogDescription
            >Укажите новое название для маппинга</DialogDescription
          >
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
            <Loader2
              v-if="isRenamingMapping"
              class="size-4 mr-2 animate-spin"
            />
            Сохранить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Delete Mapping Dialog -->
    <Dialog v-model:open="showDeleteMappingDialog">
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>Удалить маппинг</DialogTitle>
          <DialogDescription>
            Этот маппинг используется товарами. Чтобы экспорт не сломался,
            переназначьте товары на другой маппинг той же категории.
          </DialogDescription>
        </DialogHeader>

        <div class="space-y-4 py-4">
          <div class="rounded-md border p-3 text-sm">
            <div class="font-medium">
              {{ deleteMappingTarget?.localCategoryName }}
              <span
                v-if="deleteMappingTarget?.name"
                class="text-muted-foreground font-normal"
              >
                — {{ deleteMappingTarget.name }}
              </span>
            </div>
            <div class="text-muted-foreground">
              Зависимых товаров:
              {{
                deleteMappingImpact?.linkedProductsCount ||
                deleteMappingTarget?.linkedProductsCount ||
                0
              }}
            </div>
          </div>

          <div v-if="deleteMappingImpact?.products?.length" class="space-y-2">
            <Label>Примеры товаров</Label>
            <div class="max-h-32 overflow-y-auto rounded-md border divide-y">
              <div
                v-for="product in deleteMappingImpact.products"
                :key="product.id"
                class="px-3 py-2 text-sm"
              >
                <div class="font-medium truncate">
                  {{ product.name || `Товар #${product.id}` }}
                </div>
                <div class="text-xs text-muted-foreground">
                  {{ product.sku || `ID ${product.id}` }}
                </div>
              </div>
            </div>
          </div>

          <div class="space-y-2">
            <Label>Переназначить на</Label>
            <Select
              :model-value="deleteMappingReplacementId?.toString()"
              @update:model-value="
                (value) => (deleteMappingReplacementId = value)
              "
            >
              <SelectTrigger>
                <SelectValue placeholder="Выберите маппинг..." />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="mapping in deleteMappingReplacementOptions"
                  :key="mapping.id"
                  :value="mapping.id.toString()"
                >
                  {{ mapping.name || `Маппинг #${mapping.id}` }}
                  <span class="text-muted-foreground ml-1">
                    ({{
                      mapping.externalCategoryName ||
                      mapping.externalCategoryId
                    }})
                  </span>
                </SelectItem>
              </SelectContent>
            </Select>
            <p
              v-if="deleteMappingReplacementOptions.length === 0"
              class="text-sm text-destructive"
            >
              Нет другого маппинга для этой локальной категории. Сначала
              создайте или продублируйте маппинг.
            </p>
          </div>
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button
            variant="destructive"
            @click="confirmDeleteMappingWithReassign"
            :disabled="isDeletingMapping || !deleteMappingReplacementId"
          >
            <Loader2
              v-if="isDeletingMapping"
              class="size-4 mr-2 animate-spin"
            />
            Переназначить и удалить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Mapping Settings Dialog -->
    <Dialog v-model:open="showMappingSettingsDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Настройки маппинга</DialogTitle>
          <DialogDescription
            >Настройте параметры маппинга категорий</DialogDescription
          >
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div class="space-y-2">
            <Label>Название</Label>
            <Input
              v-model="mappingSettingsName"
              placeholder="Название маппинга"
            />
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button
            @click="saveMappingSettingsDialog"
            :disabled="isSavingMappingSettings"
          >
            <Loader2
              v-if="isSavingMappingSettings"
              class="size-4 mr-2 animate-spin"
            />
            Сохранить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Mapping Selection Dialog (pre-export) -->
    <Dialog
      v-model:open="showMappingSelectionDialog"
      @update:open="!$event && cancelMappingSelection()"
    >
      <DialogContent size="2xl">
        <DialogHeader>
          <DialogTitle>Выберите маппинг для товаров</DialogTitle>
          <DialogDescription>
            Для некоторых товаров доступно несколько маппингов категории.
            Выберите нужный маппинг для каждого товара.
          </DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4 max-h-96 overflow-y-auto">
          <!-- Быстрый выбор по категории -->
          <div
            v-if="mappingCategoryGroups.length > 0"
            class="space-y-2 pb-3 border-b"
          >
            <div class="text-sm font-medium text-muted-foreground">
              Применить ко всем
            </div>
            <div
              v-for="group in mappingCategoryGroups"
              :key="group.categoryName"
              class="flex items-center gap-2"
            >
              <span
                class="text-sm shrink-0 w-[140px] truncate"
                :title="group.categoryName"
              >
                {{ group.categoryName }}
                <span class="text-muted-foreground"
                  >({{ group.products.length }})</span
                >
              </span>
              <Select
                @update:model-value="(val) => applyMappingToGroup(group, val)"
              >
                <SelectTrigger class="h-8 flex-1">
                  <SelectValue placeholder="Выберите маппинг..." />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="m in group.availableMappings"
                    :key="m.id"
                    :value="m.id.toString()"
                  >
                    {{ m.name || `Маппинг #${m.id}` }}
                    <span class="text-muted-foreground ml-1">
                      ({{ m.externalCategoryName || m.externalCategoryId }})
                    </span>
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div
            v-for="product in mappingSelectionProducts"
            :key="product.productId"
            class="border rounded-lg p-3 space-y-2"
          >
            <div class="flex items-center justify-between">
              <div>
                <div class="font-medium text-sm">{{ product.productName }}</div>
                <div class="text-xs text-muted-foreground">
                  {{ product.categoryName }}
                </div>
              </div>
            </div>
            <Select
              :model-value="mappingSelections[product.productId]?.toString()"
              @update:model-value="
                (val) => (mappingSelections[product.productId] = parseInt(val))
              "
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
                    ({{
                      mapping.externalCategoryName ||
                      mapping.externalCategoryId
                    }})
                  </span>
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline" @click="cancelMappingSelection"
              >Отмена</Button
            >
          </DialogClose>
          <Button
            @click="confirmMappingSelections"
            :disabled="isSavingMappingSelections"
          >
            <Loader2
              v-if="isSavingMappingSelections"
              class="size-4 mr-2 animate-spin"
            />
            <Upload v-else class="size-4 mr-2" />
            Продолжить экспорт
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </Page>
</template>
