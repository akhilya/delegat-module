<script>
import AiErrorExplainer from "@/components/ai/AiErrorExplainer.vue";
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { EmptyState } from "@/components/ui/empty-state";
import { mapActions } from "vuex";
import { http } from "@/main";
import { confirm } from "@/composables/useConfirm";
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
  ArrowRightLeft,
  Unlink,
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
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import Progress from "@/components/ui/progress/Progress.vue";
import { formatMoney } from "@/lib/currency";
import { useSelectedStore } from "@/composables/useSelectedStore";

export default {
  components: {
    AiErrorExplainer,
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Badge,
    Checkbox,
    EmptyState,
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
    ArrowRightLeft,
    Unlink,
    FieldChangesTable,
    PasswordInput,
    SettingsFormField,
    SettingsFormSection,
    Progress,
  },
  setup() {
    const { storeBaseCurrency, loadMemberships } = useSelectedStore();
    loadMemberships();
    return { storeBaseCurrency, formatMoney };
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
      deleteMappingMode: "reassign", // "reassign" | "force"
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
      const tabLabels = { settings: "Настройки", mappings: "Маппинги", products: "Товары" };
      return [
        { label: "Адаптеры", to: PAGE_MARKETPLACE },
        { label: this.connection?.name || "Подключение", to: this.$route.fullPath.split("?")[0] },
        { label: tabLabels[this.activeTab] || "Настройки" },
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
    rpsUsagePercent() {
      const limits = this.limitsData;
      if (!limits?.supported || !limits.maxRequestsPerSecond) return 0;
      return Math.min(
        100,
        (limits.currentWindowRequests / limits.maxRequestsPerSecond) * 100,
      );
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
          this.$store.commit("aiChat/setPageContext", {
            page: "connection",
            connectionId: parseInt(this.connectionId),
            connectionName: this.connection.name || "",
          });
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

    getUsagePercent(usage, limit) {
      if (limit <= 0) return 0;
      return Math.min(100, (usage / limit) * 100);
    },

    getUsageLoadLabel(usage, limit) {
      if (limit < 0) return "Безлимит";
      if (limit <= 0) return "—";
      const ratio = usage / limit;
      if (ratio < 0.5) return "Норма";
      if (ratio < 0.8) return "Высокая";
      return "Критическая";
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
          this.showCreateMappingDialog = false;
          this.resetNewMapping();
          this.fetchMappings();
          displaySonnerSuccess("Маппинг создан");
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

      this.deleteMappingTarget = target;
      const replacements = this.categoryMappings.filter(
        (m) =>
          m.id !== target.id &&
          m.localCategoryId === target.localCategoryId,
      );
      this.deleteMappingImpact = {
        linkedProductsCount: target.linkedProductsCount || 0,
        replacementMappings: replacements,
      };
      this.deleteMappingMode = replacements.length > 0 ? "reassign" : "force";
      this.deleteMappingReplacementId = replacements[0]?.id?.toString() ?? null;
      this.showDeleteMappingDialog = true;
    },

    async confirmDeleteMapping(mappingId = null, reassignToMappingId = null, force = false) {
      const targetId = mappingId || this.deleteMappingTarget?.id;
      if (!targetId) return;

      this.isDeletingMapping = true;
      await this.deleteCategoryMapping({
        connectionId: this.connectionId,
        mappingId: targetId,
        reassignToMappingId,
        force,
        onSuccess: (response) => {
          const reassigned = response?.data?.reassignedProducts || 0;
          const detached = response?.data?.detachedProducts || 0;
          displaySonnerSuccess(
            reassigned > 0
              ? `Маппинг удалён, товары перенесены: ${reassigned}`
              : detached > 0
                ? `Маппинг удалён, товаров без маппинга: ${detached}`
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
            const impact = error.response.data.data;
            this.deleteMappingImpact = impact;
            const replacements = impact.replacementMappings || [];
            this.deleteMappingMode = replacements.length > 0 ? "reassign" : "force";
            this.deleteMappingReplacementId = replacements[0]?.id?.toString() ?? null;
            this.showDeleteMappingDialog = true;
          } else {
            displaySonnerError(error);
          }
        },
      });
      this.isDeletingMapping = false;
    },

    confirmDeleteMappingChoice() {
      if (this.deleteMappingMode === "force") {
        this.confirmDeleteMapping(this.deleteMappingTarget?.id, null, true);
        return;
      }
      if (!this.deleteMappingReplacementId) {
        displaySonnerError({
          response: { data: { title: "Выберите маппинг, на который перенести товары" } },
        });
        return;
      }
      this.confirmDeleteMapping(
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

      const ok = await confirm({
        title: "Отвязать товар?",
        description: "Товар будет отвязан от адаптера. Связь с маркетплейсом исчезнет.",
        confirmLabel: "Отвязать",
      });
      if (!ok) return;

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
      return formatMoney(price, this.storeBaseCurrency);
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
    this.$store.commit("aiChat/clearPageContext");
  },
};
</script>

<template>
  <Page class="!min-h-0 flex flex-col">
    <NavigationBar>
      <template v-if="connection" #center>
        <Tabs v-model="activeTab">
          <TabsList>
            <TabsTrigger value="settings">Настройки</TabsTrigger>
            <TabsTrigger value="mappings">Маппинги</TabsTrigger>
            <TabsTrigger value="products" @click="onProductsTabActivated">
              Товары
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </template>
    </NavigationBar>
    <Breadcrumbs :items="breadcrumbs" />

    <PageSection :loading="isLoading" class="!min-h-0 flex flex-col">
      <div v-if="connection" class="flex flex-col flex-1 min-h-0 gap-4">
        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div class="min-w-0 space-y-2">
            <div class="flex flex-wrap items-center gap-2">
              <h1 class="text-lg font-semibold break-words">{{ connection.name }}</h1>
              <Badge variant="outline">{{ adapterDisplayName }}</Badge>
              <Badge :variant="connection.isActive ? 'secondary' : 'outline'">
                {{ connection.isActive ? "Активно" : "Неактивно" }}
              </Badge>
            </div>
            <p class="text-sm text-muted-foreground">
              Подключение к адаптеру: настройки, маппинги категорий и синхронизация товаров.
            </p>
          </div>
          <div v-if="activeTab === 'settings'" class="flex flex-wrap gap-2 shrink-0">
            <Button @click="handleSave" :disabled="isSaving">
              <Loader2 v-if="isSaving" class="size-4 mr-2 animate-spin" />
              <Save v-else class="size-4 mr-2" />
              Сохранить
            </Button>
            <Button variant="outline" @click="handleTest" :disabled="isTesting">
              <Loader2 v-if="isTesting" class="size-4 mr-2 animate-spin" />
              <TestTube v-else class="size-4 mr-2" />
              Тест
            </Button>
            <Dialog v-model:open="showLimitsDialog">
              <DialogTrigger as-child>
                <Button variant="outline" :disabled="!limitsData">
                  <Activity class="size-4 mr-2" />
                  API лимиты
                </Button>
              </DialogTrigger>
                    <DialogContent size="lg" class="max-h-[85vh] overflow-y-auto">
                      <DialogHeader>
                        <DialogTitle class="flex items-center gap-2">
                          <Activity class="size-5" />
                          API лимиты
                        </DialogTitle>
                        <DialogDescription>
                          Текущая нагрузка и квоты запросов к API адаптера
                        </DialogDescription>
                      </DialogHeader>

                      <div
                        v-if="isLoadingLimits"
                        class="flex justify-center py-10"
                      >
                        <Loader2 class="size-8 animate-spin text-muted-foreground" />
                      </div>

                      <EmptyState
                        v-else-if="limitsData && !limitsData.supported"
                        size="sm"
                        class="py-6"
                        title="Лимиты недоступны"
                        description="Этот адаптер не поддерживает отслеживание API-лимитов."
                      >
                        <template #icon>
                          <Activity />
                        </template>
                      </EmptyState>

                      <div
                        v-else-if="limitsData?.supported"
                        class="space-y-4"
                      >
                        <Card>
                          <CardHeader class="pb-2">
                            <CardTitle class="flex items-center justify-between gap-3 text-sm font-medium">
                              <span class="flex items-center gap-2">
                                <Gauge class="size-4 text-muted-foreground" />
                                Запросов в секунду
                              </span>
                              <Badge variant="secondary" class="font-normal tabular-nums">
                                {{ limitsData.currentWindowRequests }} /
                                {{ limitsData.maxRequestsPerSecond }} RPS
                              </Badge>
                            </CardTitle>
                          </CardHeader>
                          <CardContent class="space-y-3">
                            <div class="flex items-center justify-between gap-2 text-xs text-muted-foreground">
                              <span>Текущее окно</span>
                              <span>{{ getUsageLoadLabel(limitsData.currentWindowRequests, limitsData.maxRequestsPerSecond) }}</span>
                            </div>
                            <Progress :model-value="rpsUsagePercent" :max="100" />
                            <p class="text-xs text-muted-foreground">
                              Всего запросов с момента запуска:
                              {{ limitsData.totalRequestsSinceStart.toLocaleString("ru-RU") }}
                            </p>
                          </CardContent>
                        </Card>

                        <div
                          v-if="limitsData.quotas?.length > 0"
                          class="grid gap-3"
                        >
                          <Card
                            v-for="quota in limitsData.quotas"
                            :key="quota.key"
                          >
                            <CardHeader class="pb-2">
                              <CardTitle class="flex items-center justify-between gap-3 text-sm font-medium">
                                <span class="truncate">{{ quota.displayName }}</span>
                                <Badge variant="secondary" class="shrink-0 font-normal tabular-nums">
                                  <template v-if="quota.limit < 0">
                                    {{ quota.usage.toLocaleString("ru-RU") }} · безлимит
                                  </template>
                                  <template v-else>
                                    {{ quota.usage.toLocaleString("ru-RU") }} /
                                    {{ quota.limit.toLocaleString("ru-RU") }}
                                  </template>
                                </Badge>
                              </CardTitle>
                            </CardHeader>
                            <CardContent class="space-y-3">
                              <div
                                v-if="quota.limit > 0"
                                class="space-y-2"
                              >
                                <div class="flex items-center justify-between gap-2 text-xs text-muted-foreground">
                                  <span>Использование</span>
                                  <span>{{ getUsageLoadLabel(quota.usage, quota.limit) }}</span>
                                </div>
                                <Progress
                                  :model-value="getUsagePercent(quota.usage, quota.limit)"
                                  :max="100"
                                />
                              </div>
                              <p
                                v-if="quota.resetsAt"
                                class="text-xs text-muted-foreground"
                              >
                                Сброс: {{ formatQuotaResetTime(quota.resetsAt) }}
                              </p>
                            </CardContent>
                          </Card>
                        </div>
                      </div>
                    </DialogContent>
            </Dialog>
          </div>
        </div>

        <Tabs v-model="activeTab" class="w-full flex-1 flex flex-col min-h-0">
          <TabsContent value="settings" class="mt-0">
            <div class="max-w-2xl">
              <SettingsFormSection title="Подключение">
                <SettingsFormField label="Название" label-for="connection-name">
                  <Input id="connection-name" v-model="connection.name" />
                </SettingsFormField>

                <SettingsFormField label="Активное подключение" label-for="active" inline>
                  <Switch
                    id="active"
                    :model-value="connection.isActive"
                    @update:model-value="connection.isActive = $event"
                  />
                </SettingsFormField>
              </SettingsFormSection>

              <SettingsFormSection
                v-if="credentialFields?.length"
                title="Данные для подключения"
              >
                <SettingsFormField
                  v-for="field in credentialFields"
                  :key="field.key"
                  :label="field.displayName"
                  :label-for="`cred-${field.key}`"
                  :required="field.isRequired"
                >
                  <PasswordInput
                    v-if="field.isSecret"
                    :id="`cred-${field.key}`"
                    v-model="credentialValues[field.key]"
                    :placeholder="field.placeholder || field.description"
                  />
                  <Input
                    v-else
                    :id="`cred-${field.key}`"
                    v-model="credentialValues[field.key]"
                    type="text"
                    :placeholder="field.placeholder || field.description"
                  />
                  <template v-if="field.description" #description>
                    {{ field.description }}
                  </template>
                </SettingsFormField>
              </SettingsFormSection>

              <SettingsFormSection
                v-if="capabilities"
                title="Возможности адаптера"
              >
                <div class="grid grid-cols-2 gap-3 md:grid-cols-3">
                  <div
                    v-for="(value, key) in capabilities.capabilities"
                    :key="key"
                    class="flex items-center gap-2"
                  >
                    <CheckCircle
                      v-if="value"
                      class="size-4 shrink-0 text-success-foreground"
                    />
                    <XCircle v-else class="size-4 shrink-0 text-muted-foreground" />
                    <span class="text-sm">{{ capabilityLabels[key] || key }}</span>
                  </div>
                </div>
              </SettingsFormSection>

              <div
                v-if="testResult"
                class="mt-8 p-4 rounded-lg"
                :class="
                  testResult.success ? 'bg-success-muted' : 'bg-error-muted'
                "
              >
                <div class="flex items-center gap-2 flex-wrap">
                  <CheckCircle
                    v-if="testResult.success"
                    class="size-5 text-success-foreground shrink-0"
                  />
                  <XCircle v-else class="size-5 text-error-foreground shrink-0" />
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
                  <AiErrorExplainer
                    v-if="!testResult.success && (testResult.errorMessage || 'Ошибка подключения')"
                    :error-message="testResult.errorMessage || 'Ошибка подключения'"
                    :context="`test_connection`"
                    :connection-id="connection?.id"
                  />
                </div>
              </div>
            </div>
          </TabsContent>

          <!-- Mappings Tab -->
          <TabsContent value="mappings" class="mt-0 flex flex-col min-h-0 gap-3">
            <div class="flex flex-wrap items-center justify-between gap-2 shrink-0">
                <p class="text-sm font-medium flex items-center gap-2">
                  <FolderTree class="size-4" />
                  Маппинги категорий
                  <span class="text-muted-foreground font-normal tabular-nums">
                    ({{ categoryMappings.length }})
                  </span>
                </p>
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
                  <DialogContent
                    size="4xl"
                    class="flex min-h-[36rem] max-h-[85vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-5xl"
                  >
                    <DialogHeader class="space-y-1.5 px-6 pt-6 pb-4">
                      <DialogTitle class="flex items-center gap-2">
                        <Link class="size-5" />
                        Создать маппинг
                      </DialogTitle>
                      <DialogDescription>
                        Свяжите локальную категорию с категорией {{ adapterDisplayName }} для экспорта и импорта товаров.
                      </DialogDescription>
                    </DialogHeader>

                    <div class="px-6 pb-4">
                      <SettingsFormField label="Название маппинга">
                        <Input
                          v-model="newMapping.name"
                          placeholder="Например: Стандарт, Премиум..."
                        />
                        <template #description>
                          Необязательно. Помогает различать несколько маппингов одной категории.
                        </template>
                      </SettingsFormField>
                    </div>

                    <div class="grid min-h-0 flex-1 grid-cols-2 gap-4 border-y px-6">
                      <div class="flex min-h-0 flex-col gap-2 py-4">
                        <p class="text-sm font-medium">Локальная категория</p>
                        <div class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border">
                          <div
                            v-if="!localCategories.length"
                            class="flex flex-1 items-center justify-center p-6 text-sm text-muted-foreground"
                          >
                            Нет категорий
                          </div>
                          <div
                            v-else
                            class="min-h-0 flex-1 overflow-y-auto p-1.5"
                          >
                            <button
                              v-for="cat in localCategories"
                              :key="cat.id"
                              type="button"
                              class="flex w-full items-center justify-between rounded-md px-2.5 py-2 text-left text-sm transition-colors hover:bg-accent"
                              :class="{ 'bg-accent': newMapping.localCategoryId === cat.id }"
                              @click="newMapping.localCategoryId = cat.id"
                            >
                              <span class="truncate font-medium">{{ cat.name }}</span>
                              <Badge
                                v-if="cat.attributesCount"
                                variant="secondary"
                                class="ml-2 shrink-0 font-normal tabular-nums"
                              >
                                {{ cat.attributesCount }} атр.
                              </Badge>
                            </button>
                          </div>
                        </div>
                        <p
                          v-if="selectedLocalCategory"
                          class="text-xs text-muted-foreground"
                        >
                          Выбрано:
                          <span class="font-medium text-foreground">{{ selectedLocalCategory.name }}</span>
                          <span v-if="selectedLocalCategory.attributesCount">
                            · {{ selectedLocalCategory.attributesCount }} атр.
                          </span>
                        </p>
                      </div>

                      <div class="flex min-h-0 flex-col gap-2 py-4">
                        <p class="text-sm font-medium">Категория {{ adapterDisplayName }}</p>

                        <div class="flex gap-2">
                          <div class="relative min-w-0 flex-1">
                            <Search
                              class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                            />
                            <Input
                              v-model="externalCategorySearch"
                              placeholder="Поиск по названию..."
                              class="pl-9"
                              @keyup.enter="searchExternalCategories"
                            />
                          </div>
                          <Button
                            variant="outline"
                            size="icon"
                            aria-label="Найти категории"
                            @click="searchExternalCategories"
                          >
                            <Search class="size-4" />
                          </Button>
                        </div>

                        <div
                          v-if="categoryPath.length > 0 && !externalCategorySearch"
                          class="flex flex-wrap items-center gap-1 text-sm"
                        >
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
                            <ChevronRight
                              v-if="index < categoryPath.length - 1"
                              class="size-3 text-muted-foreground"
                            />
                          </template>
                        </div>

                        <Button
                          v-if="categoryPath.length > 0 && !externalCategorySearch"
                          variant="outline"
                          size="sm"
                          class="justify-start"
                          @click="navigateBack"
                        >
                          <ChevronLeft class="size-4 mr-2" />
                          Назад
                        </Button>

                        <div class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border">
                          <div
                            v-if="isLoadingCategories"
                            class="flex flex-1 items-center justify-center py-10"
                          >
                            <Loader2 class="size-6 animate-spin text-muted-foreground" />
                          </div>
                          <div
                            v-else-if="externalCategories.length === 0"
                            class="flex flex-1 items-center justify-center p-4"
                          >
                            <EmptyState
                              size="sm"
                              title="Категории не найдены"
                              :description="
                                capabilities?.capabilities?.createCategory && selectedLocalCategory
                                  ? `Измените запрос или создайте категорию в ${adapterDisplayName}.`
                                  : 'Измените поисковый запрос.'
                              "
                            >
                              <template #icon>
                                <Search />
                              </template>
                              <Button
                                v-if="capabilities?.capabilities?.createCategory"
                                size="sm"
                                variant="outline"
                                :disabled="!newMapping.localCategoryId || isCreatingExternalCategory"
                                @click="handleCreateExternalCategoryMapping"
                              >
                                <Loader2 v-if="isCreatingExternalCategory" class="size-4 mr-2 animate-spin" />
                                <FolderTree v-else class="size-4 mr-2" />
                                {{ isCreatingExternalCategory ? "Создание..." : `Создать в ${adapterDisplayName}` }}
                              </Button>
                            </EmptyState>
                          </div>
                          <div
                            v-else
                            class="min-h-0 flex-1 overflow-y-auto p-1.5"
                          >
                            <button
                              v-for="cat in externalCategories"
                              :key="cat.id"
                              type="button"
                              class="flex w-full items-center justify-between rounded-md px-2.5 py-2 text-left transition-colors hover:bg-accent"
                              :class="{ 'bg-accent': newMapping.externalCategoryId === cat.id }"
                              @click="cat.hasChildren && !externalCategorySearch ? navigateToCategory(cat) : selectExternalCategory(cat)"
                            >
                              <div class="min-w-0 flex-1">
                                <div class="truncate text-sm font-medium">{{ cat.name }}</div>
                                <div
                                  v-if="cat.fullPath"
                                  class="truncate text-xs text-muted-foreground"
                                >
                                  {{ cat.fullPath }}
                                </div>
                              </div>
                              <ChevronRight
                                v-if="cat.hasChildren && !externalCategorySearch"
                                class="ml-2 size-4 shrink-0 text-muted-foreground"
                              />
                            </button>
                          </div>
                        </div>

                        <p
                          v-if="newMapping.externalCategoryId"
                          class="text-xs text-muted-foreground"
                        >
                          Выбрано:
                          <span class="font-medium text-foreground">
                            {{ newMapping.externalCategoryName || newMapping.externalCategoryId }}
                          </span>
                        </p>
                      </div>
                    </div>

                    <DialogFooter class="gap-2 px-6 py-4">
                      <DialogClose as-child>
                        <Button variant="outline">Отмена</Button>
                      </DialogClose>
                      <Button
                        v-if="capabilities?.capabilities?.createCategory"
                        variant="outline"
                        @click="handleCreateExternalCategoryMapping"
                        :disabled="!newMapping.localCategoryId || isCreatingExternalCategory"
                      >
                        <Loader2 v-if="isCreatingExternalCategory" class="size-4 mr-2 animate-spin" />
                        <FolderTree v-else class="size-4 mr-2" />
                        {{ isCreatingExternalCategory ? "Создание..." : `Создать в ${adapterDisplayName}` }}
                      </Button>
                      <Button
                        @click="handleCreateMapping"
                        :disabled="isCreatingMapping || isCreatingExternalCategory"
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

            <EmptyState
              v-if="categoryMappings.length === 0"
              class="py-10"
                title="Пока нет маппингов"
              :description="`Маппинг связывает локальную категорию с категорией ${adapterDisplayName} и управляет полями при экспорте и импорте.`"
            >
              <template #icon>
                <FolderTree />
              </template>
              <Button
                @click="$router.push(`/adapter/${connectionId}/categories/import`)"
              >
                <Download class="size-4 mr-2" />
                Импортировать категорию
              </Button>
              <Button variant="outline" @click="showCreateMappingDialog = true">
                <Plus class="size-4 mr-2" />
                Создать маппинг
              </Button>
            </EmptyState>

            <div v-else class="border rounded-xl overflow-hidden">
              <div class="overflow-x-auto">
                <table class="w-full caption-bottom text-sm">
                  <TableHeader>
                    <TableRow class="hover:bg-transparent">
                      <TableHead class="px-3">Локальная категория</TableHead>
                      <TableHead class="hidden sm:table-cell px-3">
                        Категория {{ adapterDisplayName }}
                      </TableHead>
                      <TableHead class="hidden md:table-cell px-3">Название</TableHead>
                      <TableHead class="w-24 text-right px-3">Товары</TableHead>
                      <TableHead class="w-12 px-2" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow
                      v-for="mapping in categoryMappings"
                      :key="mapping.id"
                      class="group cursor-pointer"
                      @click="openFieldMapping(mapping.id)"
                    >
                      <TableCell class="font-medium max-w-0">
                        <span class="truncate block">{{ mapping.localCategoryName }}</span>
                        <span class="text-xs text-muted-foreground truncate block sm:hidden">
                          {{ mapping.externalCategoryName || mapping.externalCategoryId }}
                        </span>
                        <span
                          v-if="mapping.name"
                          class="text-xs text-muted-foreground truncate block md:hidden"
                        >
                          {{ mapping.name }}
                        </span>
                      </TableCell>
                      <TableCell class="hidden sm:table-cell text-muted-foreground max-w-xs truncate">
                        {{ mapping.externalCategoryName || mapping.externalCategoryId }}
                      </TableCell>
                      <TableCell class="hidden md:table-cell text-muted-foreground max-w-xs truncate">
                        {{ mapping.name || "—" }}
                      </TableCell>
                      <TableCell class="text-right tabular-nums text-muted-foreground">
                        {{ mapping.linkedProductsCount || 0 }}
                      </TableCell>
                      <TableCell class="px-2">
                        <DropdownMenu>
                          <DropdownMenuTrigger as-child>
                            <Button
                              variant="ghost"
                              size="icon"
                              class="size-8 opacity-0 group-hover:opacity-100 transition-opacity"
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
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </table>
              </div>
            </div>
          </TabsContent>

          <!-- Products Tab -->
          <TabsContent value="products" class="flex-1 flex flex-col mt-0 min-h-0 gap-3">
            <div class="flex flex-wrap items-center gap-2 shrink-0">
                <Button
                  size="sm"
                  :variant="productsViewMode === 'external' ? 'default' : 'outline'"
                  @click="switchProductsView('external')"
                >
                  Товары адаптера
                </Button>
                <Button
                  size="sm"
                  :variant="productsViewMode === 'local' ? 'default' : 'outline'"
                  @click="switchProductsView('local')"
                >
                  Локальные для экспорта
                </Button>
                <div class="relative flex-1 min-w-[12rem]">
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

            <div
              class="flex flex-col border rounded-xl overflow-hidden flex-1 min-h-0"
              style="height: calc(100vh - 14rem)"
            >
              <div class="flex-1 min-h-0 flex flex-col">
            <!-- ==================== EXTERNAL VIEW ==================== -->
            <template v-if="productsViewMode === 'external'">
              <!-- Loading state -->
              <div v-if="isLoadingProducts" class="flex justify-center py-12">
                <Loader2 class="size-8 animate-spin text-muted-foreground" />
              </div>

              <!-- Products table -->
              <div v-else class="flex-1 min-h-0 overflow-auto">
                <table class="w-full caption-bottom text-sm">
                  <TableHeader class="sticky top-0 z-10 bg-background [&_tr]:border-b [&_th]:bg-background">
                    <TableRow class="hover:bg-transparent">
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
                      <TableCell colspan="8">
                        <EmptyState
                          size="sm"
                          title="Нет товаров"
                          description="Товары появятся здесь после экспорта или привязки к адаптеру."
                        >
                          <template #icon>
                            <Package />
                          </template>
                        </EmptyState>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </table>

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
              </div>

              <!-- Bulk actions bar -->
              <div
                v-if="hasAnySelected"
                class="shrink-0 p-4 flex items-center justify-between bg-muted/40"
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
              <div v-else class="flex-1 min-h-0 overflow-auto">
                <table class="w-full caption-bottom text-sm">
                  <TableHeader class="sticky top-0 z-10 bg-background [&_tr]:border-b [&_th]:bg-background">
                    <TableRow class="hover:bg-transparent">
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
                      <TableCell colspan="6">
                        <EmptyState
                          size="sm"
                          title="Нет товаров для экспорта"
                          description="Все товары уже экспортированы, или для их категорий нет маппингов."
                        >
                          <template #icon>
                            <Package />
                          </template>
                        </EmptyState>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </table>
              </div>

              <!-- Pagination -->
              <div
                v-if="localTotalCount > 0"
                class="shrink-0 flex items-center justify-between px-4 py-3"
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
                class="shrink-0 p-4 flex items-center justify-between bg-muted/40"
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
              </div>
            </div>
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
          <DialogTitle>
            Удалить маппинг
            «{{ deleteMappingTarget?.name || deleteMappingTarget?.localCategoryName }}»?
          </DialogTitle>
          <DialogDescription>
            С ним связано товаров:
            {{
              deleteMappingImpact?.linkedProductsCount ||
              deleteMappingTarget?.linkedProductsCount ||
              0
            }}. Выберите, что с ними сделать.
          </DialogDescription>
        </DialogHeader>

        <div
          class="space-y-2 py-2"
          role="radiogroup"
          aria-label="Что сделать с товарами"
        >
          <!-- Safe path: only rendered when an alternative mapping exists -->
          <Button
            v-if="deleteMappingReplacementOptions.length > 0"
            type="button"
            variant="outline"
            role="radio"
            :aria-checked="deleteMappingMode === 'reassign'"
            :class="[
              'w-full h-auto rounded-lg p-3 text-left font-normal justify-start whitespace-normal',
              deleteMappingMode === 'reassign'
                ? 'border-primary bg-primary/5'
                : 'border-input hover:bg-accent/50',
            ]"
            @click="deleteMappingMode = 'reassign'"
          >
            <div class="flex items-start gap-3">
              <ArrowRightLeft
                :class="[
                  'mt-0.5 size-4 shrink-0',
                  deleteMappingMode === 'reassign'
                    ? 'text-primary'
                    : 'text-muted-foreground',
                ]"
              />
              <div class="min-w-0 flex-1 space-y-1.5">
                <div class="text-sm font-medium">
                  Перенести товары на другой маппинг
                </div>
                <p class="text-xs text-muted-foreground">
                  Связи переключатся на выбранный маппинг, экспорт продолжит
                  работать.
                </p>
                <Select
                  v-if="deleteMappingMode === 'reassign'"
                  :model-value="deleteMappingReplacementId ?? undefined"
                  @update:model-value="
                    (value) => (deleteMappingReplacementId = value)
                  "
                >
                  <SelectTrigger class="h-9 w-full" @click.stop>
                    <SelectValue placeholder="Маппинг для переноса" />
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
              </div>
            </div>
          </Button>

          <Button
            type="button"
            variant="outline"
            role="radio"
            :aria-checked="deleteMappingMode === 'force'"
            :class="[
              'w-full h-auto rounded-lg p-3 text-left font-normal justify-start whitespace-normal',
              deleteMappingMode === 'force'
                ? 'border-destructive/60 bg-destructive/5'
                : 'border-input hover:bg-accent/50',
            ]"
            @click="deleteMappingMode = 'force'"
          >
            <div class="flex items-start gap-3">
              <Unlink
                :class="[
                  'mt-0.5 size-4 shrink-0',
                  deleteMappingMode === 'force'
                    ? 'text-destructive'
                    : 'text-muted-foreground',
                ]"
              />
              <div class="min-w-0 flex-1 space-y-1.5">
                <div class="text-sm font-medium">Удалить без переноса</div>
                <p class="text-xs text-muted-foreground">
                  Товары останутся связанными с маркетплейсом, но потеряют
                  маппинг — экспорт будет недоступен, пока не назначите новый.
                </p>
                <p
                  v-if="deleteMappingReplacementOptions.length === 0"
                  class="text-xs text-muted-foreground"
                >
                  Перенести некуда: другого маппинга для этой категории нет.
                  Чтобы перенести, сначала создайте или продублируйте маппинг.
                </p>
              </div>
            </div>
          </Button>

          <div v-if="deleteMappingImpact?.products?.length" class="pt-1">
            <div class="mb-1.5 text-xs font-medium text-muted-foreground">
              Связанные товары
            </div>
            <div class="max-h-28 divide-y overflow-y-auto rounded-md border">
              <div
                v-for="product in deleteMappingImpact.products"
                :key="product.id"
                class="flex items-baseline justify-between gap-2 px-3 py-1.5 text-xs"
              >
                <span class="truncate">
                  {{ product.name || `Товар #${product.id}` }}
                </span>
                <span class="shrink-0 font-mono text-muted-foreground">
                  {{ product.sku || `ID ${product.id}` }}
                </span>
              </div>
              <div
                v-if="
                  (deleteMappingImpact.linkedProductsCount || 0) >
                  deleteMappingImpact.products.length
                "
                class="px-3 py-1.5 text-xs text-muted-foreground"
              >
                и ещё
                {{
                  deleteMappingImpact.linkedProductsCount -
                  deleteMappingImpact.products.length
                }}
              </div>
            </div>
          </div>
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">Отмена</Button>
          </DialogClose>
          <Button
            variant="destructive"
            @click="confirmDeleteMappingChoice"
            :disabled="
              isDeletingMapping ||
              (deleteMappingMode === 'reassign' && !deleteMappingReplacementId)
            "
          >
            <Loader2
              v-if="isDeletingMapping"
              class="size-4 mr-2 animate-spin"
            />
            {{
              deleteMappingMode === "reassign"
                ? "Перенести и удалить"
                : "Удалить без переноса"
            }}
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
