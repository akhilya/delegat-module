<script>
import { Spinner } from "@/components/ui/spinner";
import AiErrorExplainer from "@/components/ai/AiErrorExplainer.vue";
import DeleteCategoryMappingDialog from "@/components/marketplace/DeleteCategoryMappingDialog.vue";
import PublishProductsWizard from "@/components/marketplace/PublishProductsWizard.vue";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageListToolbar from "@/components/PageListToolbar.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import { ButtonGroup } from "@/components/ui/button-group";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { mapActions } from "vuex";
import { http } from "@/main";
import { confirm } from "@/composables/useConfirm";
import { fillProductEditUrl } from "@/lib/productEditUrl";
import { getCachedMe } from "@/lib/meCache";
import { canAct, PERMS } from "@/lib/permissions";
import { randomUuidCompact } from "@/utils/uuid";
import { FIELD_LIMITS, clipToLimit, fieldLimitIssue } from "@/lib/fieldLimits";
import { normalizeCredentialFields } from "@/lib/credentialFields";
import { displayFieldValue } from "@/lib/fieldDiff";
import {
  displaySonnerError,
  displaySonnerSuccess,
  displayImportFieldsUpdated,
  extractResponseData,
} from "@/store/sonnerHelper";
import {
  Plus,
  Store,
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
  Settings,
  Link,
  Link2,
  Home,
  Upload,
  Download,
  AlertTriangle,
  Ban,
  DollarSign,
  Boxes,
  Activity,
  EllipsisVertical,
  Pencil,
  Copy,
  Check,
  FileText,
  Settings2,
  Unlink,
  SearchCheck,
  X,
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
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { PAGE_MARKETPLACE, setPageTitle } from "@/router";
import FieldChangesPreviewDialog from "@/components/products/FieldChangesPreviewDialog.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@/components/ui/field";
import ExternalCategoryBrowser from "@/components/marketplace/ExternalCategoryBrowser.vue";
import Progress from "@/components/ui/progress/Progress.vue";
import { formatMoney } from "@/lib/currency";
import { useSelectedStore } from "@/composables/useSelectedStore";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { getAdapterCredentialsDocsSlug } from "@/utils/docsUrl";
import DocsHelpRow from "@/components/docs/DocsHelpRow.vue";
import ExperimentalBadge from "@/components/marketplace/ExperimentalBadge.vue";
import { mappingDisplayName } from "@/lib/mappingDisplayName";
import { isExperimentalAdapter } from "@/lib/adapterStage";

export default {
  components: {
    Spinner,
    AiErrorExplainer,
    DeleteCategoryMappingDialog,
    PublishProductsWizard,
    AppPageChrome,
    PageListToolbar,
    PageSection,
    Button,
    Input,
    Badge,
    ButtonGroup,
    Checkbox,
    EmptyState,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    Plus,
    Store,
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
    Settings,
    Link,
    Link2,
    Home,
    Upload,
    Download,
    X,
    AlertTriangle,
    Ban,
    DollarSign,
    Boxes,
    Activity,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
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
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    EllipsisVertical,
    Pencil,
    Copy,
    Check,
    FileText,
    Settings2,
    Unlink,
    SearchCheck,
    DocsHelpRow,
    ExperimentalBadge,
    FieldChangesPreviewDialog,
    PasswordInput,
    SettingsFormField,
    SettingsFormSection,
    SearchableSelect,
    Field,
    FieldContent,
    FieldDescription,
    FieldGroup,
    FieldLabel,
    FieldLegend,
    FieldSet,
    ExternalCategoryBrowser,
    Progress,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
  },
  setup() {
    const { storeBaseCurrency, loadMemberships } = useSelectedStore();
    const { formatDateTime: formatStoreDateTime } = useStoreDateTime();
    loadMemberships();
    return { storeBaseCurrency, formatMoney, formatStoreDateTime };
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
      FIELD_LIMITS,
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
      showBulkCreateCategoriesDialog: false,
      bulkCreateSelectedIds: {},
      isBulkCreatingCategories: false,
      bulkCreateProgress: null, // { done, total, success, failed, errors: [{ name, error }] }
      isCreatingMapping: false,
      isCreatingExternalCategory: false,
      newMapping: {
        localCategoryId: null,
        externalCategoryId: "",
        externalCategoryName: "",
        name: "",
      },
      /** True when a folder was somehow selected; leaf-only linking is required. */
      selectedExternalIsFolder: false,
      externalCategorySearch: "",
      credentialValues: {},
      /** Secret field keys currently unlocked for re-entry (mask hidden). */
      editingSecrets: {},
      PAGE_MARKETPLACE,
      // Category navigation state
      isLoadingCategories: false,
      currentParentId: null,
      categoryPath: [],

      // === Products tab state ===
      products: [],
      productsListUnsupported: false,
      localExportReason: null, // "no_mappings" | null
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
      showExportReadinessDialog: false,
      isCheckingExportReadiness: false,
      exportReadiness: null,
      exportReadinessProductIds: [],
      publishWizardOpen: false,
      publishWizardStep: "categories",
      publishWizardChecking: false,
      publishWizardError: null,
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
      showCapabilitiesDialog: false,
      isClearingCache: false,
      isVerifyingLinks: false,
      aiEnabled: false,
    };
  },
  watch: {
    activeTab(newVal) {
      this.$router.replace({ query: { ...this.$route.query, tab: newVal } });
    },
    showLimitsDialog(open) {
      if (open) {
        this.fetchLimits();
        this.startLimitsAutoRefresh();
      } else {
        this.stopLimitsAutoRefresh();
      }
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
    searchQuery() {
      // Debounce search: wait 500ms after user stops typing
      if (this.searchDebounceTimer) {
        clearTimeout(this.searchDebounceTimer);
      }
      this.searchDebounceTimer = setTimeout(() => {
        this.fetchProducts(true);
      }, 500);
    },
    localSearchQuery() {
      if (this.localSearchDebounceTimer) {
        clearTimeout(this.localSearchDebounceTimer);
      }
      this.localSearchDebounceTimer = setTimeout(() => {
        this.localPage = 1;
        this.fetchLocalProducts();
      }, 500);
    },
    // Refresh data after an operation completes (if it belonged to this connection)
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
    linkCheckRunning() {
      const id = this.connectionId;
      return (this.$store.state.operations.operations || []).some(
        (op) =>
          op.type === "link-verify" &&
          Number(op.connectionId) === id &&
          (op.status === "running" || op.status === "paused"),
      );
    },
    canManageAdapters() {
      return canAct(getCachedMe(), PERMS.MarketplaceManage);
    },
    publishWizardUnmappedCategories() {
      const mappedIds = new Set(
        this.categoryMappings.map((mapping) => Number(mapping.localCategoryId)),
      );
      return this.localCategories.filter(
        (category) => !mappedIds.has(Number(category.id)),
      );
    },
    capabilityLabels() {
      return {
        getCategories: this.$t(
          "marketplace.connectionPage.capabilities.getCategories",
        ),
        getCategoryFields: this.$t(
          "marketplace.connectionPage.capabilities.getCategoryFields",
        ),
        createCategory: this.$t(
          "marketplace.connectionPage.capabilities.createCategory",
        ),
        importProduct: this.$t(
          "marketplace.connectionPage.capabilities.importProduct",
        ),
        importAllProducts: this.$t(
          "marketplace.connectionPage.capabilities.importAllProducts",
        ),
        importProductBatch: this.$t(
          "marketplace.connectionPage.capabilities.importProductBatch",
        ),
        exportProduct: this.$t(
          "marketplace.connectionPage.capabilities.exportProduct",
        ),
        exportProductBulk: this.$t(
          "marketplace.connectionPage.capabilities.exportProductBulk",
        ),
        updateSku: this.$t("marketplace.connectionPage.capabilities.updateSku"),
        importPrices: this.$t(
          "marketplace.connectionPage.capabilities.importPrices",
        ),
        exportPrices: this.$t(
          "marketplace.connectionPage.capabilities.exportPrices",
        ),
        importStocks: this.$t(
          "marketplace.connectionPage.capabilities.importStocks",
        ),
        exportStocks: this.$t(
          "marketplace.connectionPage.capabilities.exportStocks",
        ),
        getOrders: this.$t("marketplace.connectionPage.capabilities.getOrders"),
        updateOrderStatus: this.$t(
          "marketplace.connectionPage.capabilities.updateOrderStatus",
        ),
        getOrderLabels: this.$t(
          "marketplace.connectionPage.capabilities.getOrderLabels",
        ),
        getFulfillmentLocations: this.$t(
          "marketplace.connectionPage.capabilities.getFulfillmentLocations",
        ),
        manageShipments: this.$t(
          "marketplace.connectionPage.capabilities.manageShipments",
        ),
        getPromotions: this.$t(
          "marketplace.connectionPage.capabilities.getPromotions",
        ),
        updatePromotions: this.$t(
          "marketplace.connectionPage.capabilities.updatePromotions",
        ),
        fieldBasedGrouping: this.$t(
          "marketplace.connectionPage.capabilities.fieldBasedGrouping",
        ),
        endpointBasedGrouping: this.$t(
          "marketplace.connectionPage.capabilities.endpointBasedGrouping",
        ),
        importGroups: this.$t(
          "marketplace.connectionPage.capabilities.importGroups",
        ),
        deleteGroups: this.$t(
          "marketplace.connectionPage.capabilities.deleteGroups",
        ),
        modifyGroupMembership: this.$t(
          "marketplace.connectionPage.capabilities.modifyGroupMembership",
        ),
        archiveProduct: this.$t(
          "marketplace.connectionPage.capabilities.archiveProduct",
        ),
        searchPublicCatalog: this.$t(
          "marketplace.connectionPage.capabilities.searchPublicCatalog",
        ),
      };
    },
    selectedAdapterSchema() {
      if (!this.connection) return null;
      const adapter = this.adapters.find(
        (a) => a.key === this.connection.adapterKey,
      );
      return adapter?.credentialsSchema;
    },
    credentialFields() {
      return normalizeCredentialFields(this.selectedAdapterSchema);
    },
    adapterDisplayName() {
      if (!this.connection) return "";
      const adapter = this.adapters.find(
        (a) => a.key === this.connection.adapterKey,
      );
      return adapter?.displayName || this.connection.adapterKey;
    },
    isExperimentalAdapter() {
      if (!this.connection) return false;
      const adapter = this.adapters.find(
        (a) => a.key === this.connection.adapterKey,
      );
      return isExperimentalAdapter(adapter);
    },
    credentialsDocsSlug() {
      const key = this.connection?.adapterKey;
      if (!key) return null;
      return getAdapterCredentialsDocsSlug(key);
    },
    canCreateCategory() {
      return !!this.capabilities?.capabilities?.createCategory;
    },
    unmappedLocalCategories() {
      const mappedIds = new Set(
        this.categoryMappings.map((m) => m.localCategoryId),
      );
      return this.localCategories.filter((c) => !mappedIds.has(c.id));
    },
    bulkCreateSelectedCount() {
      return Object.keys(this.bulkCreateSelectedIds).filter(
        (id) => this.bulkCreateSelectedIds[id],
      ).length;
    },
    allBulkCreateSelected() {
      const cats = this.unmappedLocalCategories;
      return (
        cats.length > 0 && cats.every((c) => this.bulkCreateSelectedIds[c.id])
      );
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
        // Keep marketplace name only when we have a real external category id.
        // Uncategorized products must not inherit the i18n label as a create name.
        const marketplaceName = product.externalCategoryName || null;
        const displayName =
          marketplaceName ||
          this.$t("marketplace.connectionPage.categories.uncategorized");
        if (!groups[catId]) {
          groups[catId] = {
            externalCategoryId: catId === "__none__" ? null : catId,
            externalCategoryName: marketplaceName,
            displayName,
            products: [],
          };
        }
        groups[catId].products.push(product);
      }
      return Object.values(groups);
    },
    canConfirmImportWithCategories() {
      if (this.isImportingWithCategories) return false;
      if (!this.selectedUnlinkedItems.length) return false;
      for (const group of this.productsByExternalCategory) {
        const key = group.externalCategoryId || "__none__";
        const assignment = this.categoryAssignments[key];
        if (!assignment || assignment.mode !== "new") continue;
        const typedName = (assignment.newCategoryName || "").trim();
        if (group.externalCategoryId) {
          const marketplaceName = (group.externalCategoryName || "").trim();
          if (!typedName && !marketplaceName) return false;
        } else if (!typedName) {
          return false;
        }
      }
      return true;
    },

    mappingCategoryGroups() {
      if (!this.mappingSelectionProducts.length) return [];
      const groups = {};
      for (const p of this.mappingSelectionProducts) {
        const key =
          p.categoryName ||
          this.$t("marketplace.connectionPage.categories.uncategorized");
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
          label:
            item.productName ||
            this.$t(
              "marketplace.connectionPage.mappingSelection.linkFallback",
              {
                id: item.linkId,
              },
            ),
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
    mappingDisplayName,
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
      "verifyConnectionLinks",
      "importSelectedProducts",
      "getImportBatchStatus",
      "exportPrices",
      "exportStocks",
      "getLocalProductsForExport",
      "exportProductsBulk",
      "getConnectionLimits",
      "clearConnectionMetadataCache",
      "updateCategoryMapping",
      "duplicateCategoryMapping",
      "resolveMappings",
      "saveMappingSelections",
    ]),

    async fetchData() {
      this.isLoading = true;
      try {
        const { loadMe } = await import("@/main");
        const me = await loadMe();
        this.aiEnabled = me?.aiEnabled === true;
      } catch {
        this.aiEnabled = false;
      }
      await Promise.all([
        this.fetchConnection(),
        this.fetchAdapters(),
        this.fetchCapabilities(),
        this.fetchMappings(),
        this.fetchLocalCategories(),
      ]);
      // Credentials schema comes from adapters; connection carries publicCredentials.
      // Both must be ready — parse here so we don't race Promise.all completion order.
      this.parseCredentials();
      this.isLoading = false;
      // fetchLimits is lazy: only when the limits dialog opens (see showLimitsDialog watcher).
      // Avoid competing with mapping/category WB Content API calls on every page load.

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
          if (this.$store.state.aiChat) {
            this.$store.commit("aiChat/setPageContext", {
              page: "connection",
              connectionId: parseInt(this.connectionId),
              connectionName: this.connection.name || "",
            });
          }
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
          // Keep last good payload if any; otherwise show local RPS shell so the dialog is usable.
          if (!this.limitsData) {
            this.limitsData = {
              supported: true,
              maxRequestsPerSecond: 1,
              currentWindowRequests: 0,
              totalRequestsSinceStart: 0,
              quotasAvailable: false,
              quotas: [],
            };
          } else {
            this.limitsData = {
              ...this.limitsData,
              quotasAvailable: false,
            };
          }
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
      if (limit < 0)
        return this.$t("marketplace.connectionPage.limits.unlimited");
      if (limit <= 0) return this.$t("common.emDash");
      const ratio = usage / limit;
      if (ratio < 0.5)
        return this.$t("marketplace.connectionPage.limits.loadNormal");
      if (ratio < 0.8)
        return this.$t("marketplace.connectionPage.limits.loadHigh");
      return this.$t("marketplace.connectionPage.limits.loadCritical");
    },

    formatQuotaResetTime(resetsAt) {
      if (!resetsAt) return null;
      const formatted = this.formatStoreDateTime(resetsAt);
      return formatted === "-" ? null : formatted;
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
      this.selectedExternalIsFolder = false;
    },

    parseCredentials() {
      // Secrets are never returned by the API - leave those empty so saving keeps them.
      // Non-secret fields come back as publicCredentials.
      this.credentialValues = {};
      this.editingSecrets = {};
      const publicCreds =
        this.connection?.publicCredentials ||
        this.connection?.PublicCredentials ||
        {};
      if (!this.credentialFields?.length) return;

      for (const field of this.credentialFields) {
        if (field.isSecret) {
          this.credentialValues[field.key] = "";
        } else {
          const fromPublic =
            publicCreds[field.key] != null
              ? String(publicCreds[field.key])
              : "";
          this.credentialValues[field.key] =
            fromPublic || field.defaultValue || "";
        }
      }
    },

    isSecretMasked(fieldKey) {
      return Boolean(
        this.connection?.hasCredentials && !this.editingSecrets[fieldKey],
      );
    },

    startEditSecret(fieldKey) {
      this.editingSecrets = { ...this.editingSecrets, [fieldKey]: true };
      this.credentialValues = { ...this.credentialValues, [fieldKey]: "" };
    },

    cancelEditSecret(fieldKey) {
      const next = { ...this.editingSecrets };
      delete next[fieldKey];
      this.editingSecrets = next;
      this.credentialValues = { ...this.credentialValues, [fieldKey]: "" };
    },

    buildCredentialsJson() {
      if (!this.credentialFields?.length) return null;

      const payload = {};
      let hasMeaningfulValue = false;
      for (const field of this.credentialFields) {
        const raw = this.credentialValues[field.key];
        const value = raw == null ? "" : String(raw).trim();
        payload[field.key] = value;
        if (value) hasMeaningfulValue = true;
      }

      // No credentials configured and nothing entered — skip patch field.
      if (!this.connection?.hasCredentials && !hasMeaningfulValue) return null;
      return JSON.stringify(payload);
    },

    async handleSave() {
      const nameIssue = fieldLimitIssue(
        this.connection?.name,
        FIELD_LIMITS.connectionName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(
          this.$t("marketplace.marketplacePage.enterConnectionName"),
        );
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.connectionName }),
        );
        return;
      }
      this.isSaving = true;
      const credentialsJson = this.buildCredentialsJson();
      const data = {
        name: this.connection.name,
        settingsJson: this.connection.settingsJson,
        isActive: this.connection.isActive,
      };
      if (credentialsJson != null) {
        data.credentialsJson = credentialsJson;
      }
      await this.updateConnection({
        connectionId: this.connectionId,
        data,
        onSuccess: () => {
          displaySonnerSuccess(
            this.$t("marketplace.connectionPage.notifications.connectionSaved"),
          );
          this.parseCredentials();
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
            const pub =
              this.testResult.publicCredentials ||
              this.testResult.PublicCredentials;
            if (pub && this.connection) {
              this.connection = {
                ...this.connection,
                publicCredentials: pub,
                hasCredentials: true,
              };
              this.parseCredentials();
            }
            displaySonnerSuccess(
              this.testResult.message ||
                this.$t("marketplace.connectionPage.notifications.testSuccess"),
            );
          } else {
            displaySonnerError(
              this.testResult.errorMessage ||
                this.$t("marketplace.connectionPage.notifications.testError"),
            );
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isTesting = false;
    },

    async handleClearCache() {
      this.isClearingCache = true;
      await this.clearConnectionMetadataCache({
        connectionId: this.connectionId,
        onSuccess: () => {
          this.limitsData = null;
          displaySonnerSuccess(
            this.$t("marketplace.connectionPage.notifications.cacheCleared"),
          );
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isClearingCache = false;
    },

    async handleVerifyLinks() {
      if (this.isVerifyingLinks || this.linkCheckRunning || !this.connectionId)
        return;
      this.isVerifyingLinks = true;
      await this.verifyConnectionLinks({
        connectionId: this.connectionId,
        onSuccess: () => {
          displaySonnerSuccess(
            this.$t(
              "marketplace.connectionPage.notifications.linksVerifyStarted",
            ),
          );
        },
        onError: (error) => {
          displaySonnerError(
            error ||
              this.$t(
                "marketplace.connectionPage.notifications.linksVerifyError",
              ),
          );
        },
      });
      this.isVerifyingLinks = false;
    },

    async handleCreateMapping() {
      if (
        fieldLimitIssue(this.newMapping.name, FIELD_LIMITS.mappingName, {
          required: false,
        }) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.mappingName }),
        );
        return;
      }
      if (
        !this.newMapping.localCategoryId ||
        !this.newMapping.externalCategoryId
      ) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectBothCategories",
          ),
        );
        return;
      }

      if (this.selectedExternalIsFolder) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectLeafCategory",
          ),
        );
        return;
      }

      this.isCreatingMapping = true;
      await this.createCategoryMapping({
        connectionId: this.connectionId,
        data: this.newMapping,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          this.showCreateMappingDialog = false;
          this.resetNewMapping();
          this.fetchMappings();
          if (this.publishWizardOpen) {
            this.fetchLocalCategories();
          }
          if (data.fieldsCreated === 0) {
            displaySonnerError(
              this.$t(
                "marketplace.connectionPage.notifications.mappingCreatedWithoutFields",
              ),
            );
          } else {
            displaySonnerSuccess(
              this.$t(
                "marketplace.connectionPage.notifications.mappingCreated",
              ),
            );
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreatingMapping = false;
    },

    goToCreateExternalCategory() {
      if (!this.newMapping.localCategoryId) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectLocalCategory",
          ),
        );
        return;
      }

      if (!this.canCreateCategory) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.createCategoryUnsupported",
          ),
        );
        return;
      }

      const parent = this.categoryPath[this.categoryPath.length - 1];
      this.showCreateMappingDialog = false;
      this.$router.push({
        path: `/adapter/${this.connectionId}/categories/create`,
        query: {
          localCategoryId: String(this.newMapping.localCategoryId),
          parentId: this.currentParentId || undefined,
          parentName: parent?.name || undefined,
        },
      });
    },

    async handleDeleteMapping(mapping) {
      const target =
        typeof mapping === "object"
          ? mapping
          : this.categoryMappings.find((m) => m.id === mapping);

      if (!target) return;

      const linkedCount = target.linkedProductsCount || 0;

      this.deleteMappingTarget = target;
      const replacements = this.categoryMappings.filter(
        (m) =>
          m.id !== target.id && m.localCategoryId === target.localCategoryId,
      );
      this.deleteMappingMode =
        linkedCount > 0 && replacements.length > 0 ? "reassign" : "force";
      this.deleteMappingReplacementId = replacements[0]?.id?.toString() ?? null;
      this.deleteMappingImpact = {
        linkedProductsCount: linkedCount,
        replacementMappings: replacements,
        products: [],
      };
      this.showDeleteMappingDialog = true;
    },

    async confirmDeleteMapping(
      mappingId = null,
      reassignToMappingId = null,
      force = false,
    ) {
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
              ? this.$t(
                  "marketplace.connectionPage.notifications.mappingDeletedReassigned",
                  { count: reassigned },
                )
              : detached > 0
                ? this.$t(
                    "marketplace.connectionPage.notifications.mappingDeletedDetached",
                    { count: detached },
                  )
                : this.$t(
                    "marketplace.connectionPage.notifications.mappingDeleted",
                  ),
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
            this.deleteMappingMode =
              replacements.length > 0 ? "reassign" : "force";
            this.deleteMappingReplacementId =
              replacements[0]?.id?.toString() ?? null;
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
        const force = (this.deleteMappingImpact?.linkedProductsCount ?? 0) > 0;
        this.confirmDeleteMapping(this.deleteMappingTarget?.id, null, force);
        return;
      }
      if (!this.deleteMappingReplacementId) {
        displaySonnerError({
          response: {
            data: {
              title: this.$t(
                "marketplace.connectionPage.notifications.selectReplacementMapping",
              ),
            },
          },
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
        displaySonnerSuccess(
          this.$t("marketplace.connectionPage.notifications.mappingDuplicated"),
        );
        this.fetchMappings();
      } catch (err) {
        displaySonnerError(
          this.$t("marketplace.connectionPage.notifications.duplicateError") +
            ": " +
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
      if (
        fieldLimitIssue(this.renameMappingValue, FIELD_LIMITS.mappingName, {
          required: false,
        }) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.mappingName }),
        );
        return;
      }
      this.isRenamingMapping = true;
      try {
        await this.updateCategoryMapping({
          connectionId: this.connectionId,
          mappingId: this.renameMappingId,
          data: { name: this.renameMappingValue || null },
        });
        displaySonnerSuccess(
          this.$t("marketplace.connectionPage.notifications.mappingRenamed"),
        );
        this.showRenameMappingDialog = false;
        this.fetchMappings();
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message ||
            this.$t("marketplace.connectionPage.notifications.renameError"),
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
      if (
        fieldLimitIssue(this.mappingSettingsName, FIELD_LIMITS.mappingName, {
          required: false,
        }) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.mappingName }),
        );
        return;
      }
      this.isSavingMappingSettings = true;
      try {
        await this.updateCategoryMapping({
          connectionId: this.connectionId,
          mappingId: this.mappingSettingsId,
          data: { name: this.mappingSettingsName || null },
        });
        displaySonnerSuccess(
          this.$t("marketplace.connectionPage.notifications.settingsSaved"),
        );
        this.showMappingSettingsDialog = false;
        this.fetchMappings();
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message ||
            this.$t("marketplace.connectionPage.notifications.saveError"),
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

    mappingRowSubtitle(mapping) {
      const count = mapping.productsCount ?? mapping.linkedProductsCount ?? 0;
      const remote = mappingDisplayName(mapping);
      if (remote && remote !== mapping.localCategoryName) {
        return this.$t("marketplace.connectionPage.mappings.rowSubtitle", {
          remote,
          count,
        });
      }
      return this.$t(
        "marketplace.connectionPage.mappings.rowSubtitleProducts",
        {
          count,
        },
      );
    },

    selectExternalCategory(category) {
      if (category.hasChildren) {
        this.navigateToCategory(category);
        return;
      }
      this.newMapping.externalCategoryId = category.id;
      this.newMapping.externalCategoryName = category.name;
      this.selectedExternalIsFolder = false;
    },

    autoSelectExternalCategory(categoryName) {
      if (!categoryName || this.externalCategories.length === 0) return;

      const normalizedName = categoryName.toLowerCase().trim();
      const leaves = this.externalCategories.filter((cat) => !cat.hasChildren);

      // Try exact match first
      let match = leaves.find(
        (cat) => cat.name?.toLowerCase().trim() === normalizedName,
      );

      // If no exact match, try partial match (category name contains search or vice versa)
      if (!match) {
        match = leaves.find(
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
      this.productsListUnsupported = false;

      // Skip the request when the adapter cannot list remote products.
      if (this.capabilities?.capabilities?.importAllProducts === false) {
        this.products = [];
        this.totalCount = 0;
        this.linkedCount = 0;
        this.hasMoreProducts = false;
        this.nextCursor = null;
        this.productsListUnsupported = true;
        this.isLoadingProducts = false;
        return;
      }

      await this.getExternalProducts({
        connectionId: this.connectionId,
        pageSize: this.externalPageSize,
        cursor: null,
        offerIdSearch: this.searchQuery || null,
        onSuccess: (response) => {
          const data = extractResponseData(response, {});
          if (data.supported === false) {
            this.products = [];
            this.totalCount = 0;
            this.linkedCount = 0;
            this.hasMoreProducts = false;
            this.nextCursor = null;
            this.productsListUnsupported = true;
            return;
          }
          this.products = data.items || [];
          this.lastSelectedExternalId = null;
          this.totalCount = data.total || 0;
          this.linkedCount = data.linkedCount || 0;
          this.hasMoreProducts = !!data.hasMore;
          this.nextCursor = data.nextCursor || null;
        },
        onError: (error) => {
          const message =
            error?.response?.data?.title ||
            error?.response?.data?.message ||
            "";
          if (
            error?.response?.status === 400 &&
            typeof message === "string" &&
            message.includes("does not support fetching the product list")
          ) {
            this.products = [];
            this.totalCount = 0;
            this.linkedCount = 0;
            this.hasMoreProducts = false;
            this.nextCursor = null;
            this.productsListUnsupported = true;
            return;
          }
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

    clearExternalSelection() {
      this.selectedItems = [];
      this.lastSelectedExternalId = null;
    },

    clearLocalSelection() {
      this.selectedLocalItemsMap = {};
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
            displaySonnerSuccess(
              this.$t(
                "marketplace.connectionPage.notifications.noChangesDetected",
              ),
            );
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
            displayImportFieldsUpdated(result.updatedFields);
          } else {
            displayImportFieldsUpdated([]);
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

    resetSingleImportPreview() {
      this.singleImportPreviewData = null;
      this.previewLinkId = null;
      this.singleImportExcludedFields = new Set();
    },

    async handleExportSingle(product) {
      if (!product.isLinked || !product.linkId) return;

      this.exportingLinkId = product.linkId;

      await this.exportFromLink({
        linkId: product.linkId,
        onSuccess: () => {
          displaySonnerSuccess(
            this.$t("marketplace.connectionPage.notifications.productExported"),
          );
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
        title: this.$t("marketplace.connectionPage.confirm.unlinkTitle"),
        description: this.$t(
          "marketplace.connectionPage.confirm.unlinkDescription",
        ),
        confirmLabel: this.$t(
          "marketplace.connectionPage.confirm.unlinkConfirm",
        ),
      });
      if (!ok) return;

      this.unlinkingLinkId = product.linkId;
      await this.unlinkProduct({
        linkId: product.linkId,
        onSuccess: () => {
          displaySonnerSuccess(
            this.$t("marketplace.connectionPage.notifications.productUnlinked"),
          );
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
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectUnlinkedProducts",
          ),
        );
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

    importAssignmentKey(group) {
      return group.externalCategoryId || "__none__";
    },

    importLocalCategoryValue(group) {
      const id =
        this.categoryAssignments[this.importAssignmentKey(group)]
          ?.localCategoryId;
      return id != null ? String(id) : "";
    },

    importLocalCategoryOptions(group) {
      const mappedId = this.getExistingMappingForCategory(
        group.externalCategoryId,
      )?.localCategoryId;
      const mappedHint = this.$t(
        "marketplace.connectionPage.dialogs.mappingParenthetical",
      );
      return this.localCategories.map((cat) => ({
        value: String(cat.id),
        label: mappedId === cat.id ? `${cat.name} ${mappedHint}` : cat.name,
      }));
    },

    onImportLocalCategory(group, value) {
      this.setAssignmentCategory(
        this.importAssignmentKey(group),
        value ? parseInt(value, 10) : null,
      );
    },

    setAssignmentNewName(extCatId, name) {
      if (!this.categoryAssignments[extCatId]) {
        this.categoryAssignments[extCatId] = {
          mode: "new",
          localCategoryId: null,
          newCategoryName: "",
        };
      }
      this.categoryAssignments[extCatId].newCategoryName = name;
    },

    async handleImportWithCategories() {
      if (!this.canConfirmImportWithCategories) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.categoryNameRequired",
          ),
        );
        return;
      }

      this.isImportingWithCategories = true;

      // Create operation upfront so user sees it immediately
      const operationId = randomUuidCompact();
      const totalProducts = this.selectedUnlinkedItems.length;
      const connectionName = this.connection?.name || "";

      try {
        await http.post("/operations/start", {
          operationId,
          type: "batch-import",
          title: this.$t("marketplace.connectionPage.operations.importTitle", {
            count: totalProducts,
          }),
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
        const categoryNameMap = {};
        const externalNameMap = {};

        for (const group of this.productsByExternalCategory) {
          const extCatId = group.externalCategoryId;
          const key = extCatId || "__none__";
          const assignment = this.categoryAssignments[key];
          const marketplaceName = (group.externalCategoryName || "").trim();
          externalNameMap[key] = marketplaceName || null;

          if (!assignment || assignment.mode === "none") {
            categoryIdMap[key] = null;
          } else if (assignment.mode === "existing") {
            categoryIdMap[key] = assignment.localCategoryId;
          } else if (assignment.mode === "new") {
            const typedName = (assignment.newCategoryName || "").trim();
            const categoryName = clipToLimit(
              typedName || (extCatId ? marketplaceName : ""),
              FIELD_LIMITS.categoryName,
            );
            if (!categoryName) {
              displaySonnerError(
                this.$t(
                  "marketplace.connectionPage.notifications.categoryNameRequired",
                ),
              );
              categoryIdMap[key] = null;
              continue;
            }

            if (extCatId) {
              categoryIdMap[key] = null;
              categoryNameMap[key] = categoryName;
            } else {
              let createdCategoryId = null;
              await this.createCategory({
                name: categoryName,
                description: "",
                onSuccess: (response) => {
                  const result = extractResponseData(response, {});
                  createdCategoryId = result.id;
                },
                onError: (error) => {
                  displaySonnerError(
                    this.$t(
                      "marketplace.connectionPage.notifications.categoryCreateError",
                      {
                        error,
                      },
                    ),
                  );
                },
              });
              categoryIdMap[key] = createdCategoryId;
            }
          }
        }

        const productsToImport = this.selectedUnlinkedItems.map((p) => {
          const key = p.externalCategoryId || "__none__";
          const targetCategoryId = categoryIdMap[key];
          const assignment = this.categoryAssignments[key];

          return {
            externalId: p.externalId,
            externalCategoryId: p.externalCategoryId || null,
            externalCategoryName:
              externalNameMap[key] || p.externalCategoryName || null,
            targetCategoryId: targetCategoryId || null,
            targetCategoryName: categoryNameMap[key] || null,
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
                this.$t(
                  "marketplace.connectionPage.notifications.queuedForImport",
                  {
                    count: result.queued,
                  },
                ),
              );
              this.showImportDialog = false;
              this.selectedItems = [];
            } else {
              displaySonnerSuccess(
                this.$t(
                  "marketplace.connectionPage.notifications.importSummary",
                  {
                    created: result.imported || 0,
                    skipped: result.skipped || 0,
                    failed: result.failed || 0,
                  },
                ),
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
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectLinkedProducts",
          ),
        );
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
            displaySonnerSuccess(
              this.$t(
                "marketplace.connectionPage.notifications.noChangesDetected",
              ),
            );
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
            this.$t(
              "marketplace.connectionPage.notifications.bulkImportSummary",
              {
                success: result.successCount || 0,
                failed: result.failedCount || 0,
              },
            ),
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

    resetBulkImportPreview() {
      this.bulkImportPreviewData = null;
      this.bulkPreviewLinkIds = [];
    },

    async handleExportBulk() {
      if (this.isExportingBulk) return;
      if (this.selectedLinkedItems.length === 0) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectLinkedProducts",
          ),
        );
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
          this.$t(
            "marketplace.connectionPage.notifications.mappingCheckError",
          ) +
            ": " +
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
          error.response?.data?.message ||
            this.$t(
              "marketplace.connectionPage.notifications.exportBatchError",
            ),
        );
      } finally {
        this.isExportingBulk = false;
      }
    },

    async handleExportPricesBulk() {
      if (this.isExportingPrices) return;
      if (this.selectedLinkedItems.length === 0) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectLinkedProducts",
          ),
        );
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
            this.$t(
              "marketplace.connectionPage.notifications.pricesExportSummary",
              {
                updated: result.updatedCount || 0,
                skipped: result.skippedCount || 0,
              },
            ),
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
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.selectLinkedProducts",
          ),
        );
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
            this.$t(
              "marketplace.connectionPage.notifications.stocksExportSummary",
              {
                updated: result.updatedCount || 0,
                skipped: result.skippedCount || 0,
              },
            ),
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
          this.localExportReason = data.reason || null;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingLocalProducts = false;
    },

    setPublishWizardPage(page) {
      const nextPage = Math.max(
        1,
        Math.min(Number(page) || 1, this.localLastPage),
      );
      if (nextPage === this.localPage) return;
      this.localPage = nextPage;
      this.fetchLocalProducts();
    },

    openBulkCreateCategoriesDialog() {
      if (!this.canCreateCategory) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.createCategoryUnsupported",
          ),
        );
        return;
      }
      const selected = {};
      for (const cat of this.unmappedLocalCategories) {
        selected[cat.id] = true;
      }
      this.bulkCreateSelectedIds = selected;
      this.bulkCreateProgress = null;
      this.showBulkCreateCategoriesDialog = true;
    },

    toggleBulkCreateSelectAll() {
      if (this.allBulkCreateSelected) {
        this.bulkCreateSelectedIds = {};
        return;
      }
      const selected = {};
      for (const cat of this.unmappedLocalCategories) {
        selected[cat.id] = true;
      }
      this.bulkCreateSelectedIds = selected;
    },

    toggleBulkCreateCategory(id, checked) {
      const next = { ...this.bulkCreateSelectedIds };
      if (checked === true) {
        next[id] = true;
      } else {
        delete next[id];
      }
      this.bulkCreateSelectedIds = next;
    },

    goToMappingsForCreate() {
      if (this.canCreateCategory) {
        this.$router.push(`/adapter/${this.connectionId}/categories/create`);
        return;
      }
      this.resetNewMapping();
      this.activeTab = "mappings";
      this.$nextTick(() => {
        this.showCreateMappingDialog = true;
      });
    },

    async handleBulkCreateCategories() {
      const selected = this.unmappedLocalCategories.filter(
        (c) => this.bulkCreateSelectedIds[c.id],
      );
      if (selected.length === 0) return;

      this.isBulkCreatingCategories = true;
      const progress = {
        done: 0,
        total: selected.length,
        success: 0,
        failed: 0,
        errors: [],
      };
      this.bulkCreateProgress = progress;

      for (const cat of selected) {
        try {
          await http.post(
            `/marketplace/connections/${this.connectionId}/create-external-category`,
            {
              localCategoryId: cat.id,
              externalCategoryName: cat.name,
            },
            { timeout: 5 * 60 * 1000 },
          );
          progress.success += 1;
        } catch (error) {
          progress.failed += 1;
          const payload = error?.response?.data;
          progress.errors.push({
            name: cat.name,
            error:
              (typeof payload === "string" ? payload : null) ||
              payload?.message ||
              payload?.title ||
              error?.message ||
              String(error),
          });
        }
        progress.done += 1;
        this.bulkCreateProgress = { ...progress, errors: [...progress.errors] };
      }

      this.isBulkCreatingCategories = false;
      await this.fetchMappings();
      await this.fetchLocalCategories();

      displaySonnerSuccess(
        this.$t(
          "marketplace.connectionPage.mappings.categoriesCreatedSuccess",
          {
            success: progress.success,
            failed: progress.failed,
          },
        ),
      );

      if (progress.failed === 0) {
        this.showBulkCreateCategoriesDialog = false;
        this.bulkCreateSelectedIds = {};
        this.bulkCreateProgress = null;
      }
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
      this.startPublishWizard(this.selectedLocalIds);
    },

    async startPublishWizard(preselectedIds = []) {
      this.publishWizardOpen = true;
      this.publishWizardStep = "categories";
      this.publishWizardError = null;
      this.exportReadiness = null;
      this.pendingExportType = "wizard";
      this.pendingExportProductIds = [...preselectedIds];
      this.selectedLocalItemsMap = Object.fromEntries(
        preselectedIds.map((id) => [id, true]),
      );

      await Promise.all([this.fetchMappings(), this.fetchLocalCategories()]);
      if (this.publishWizardUnmappedCategories.length === 0) {
        this.publishWizardStep = "products";
      }
      await this.fetchLocalProducts();
    },

    openWizardMapping(category) {
      this.resetNewMapping();
      this.newMapping.localCategoryId = category.id;
      this.showCreateMappingDialog = true;
    },

    continuePublishWizardToProducts() {
      this.publishWizardStep = "products";
      this.fetchLocalProducts();
    },

    async checkWizardReadiness() {
      const productIds = this.selectedLocalIds;
      if (productIds.length === 0) {
        this.publishWizardError = this.$t(
          "marketplace.connectionPage.notifications.selectProductsToExport",
        );
        return;
      }

      this.publishWizardChecking = true;
      this.publishWizardError = null;
      this.isResolvingMappings = true;
      try {
        const resolved = await this.resolveMappings({
          connectionId: this.connectionId,
          productIds,
        });
        const needsSelection = resolved.products.some(
          (item) => item.needsSelection,
        );
        if (needsSelection) {
          this.mappingSelectionProducts = resolved.products.filter(
            (item) => item.needsSelection,
          );
          this.mappingSelections = {};
          this.pendingExportType = "wizard";
          this.pendingExportProductIds = [...productIds];
          this.showMappingSelectionDialog = true;
          return;
        }
        await this.loadWizardReadiness(productIds);
      } catch (error) {
        this.publishWizardError =
          error.response?.data?.message ||
          error.message ||
          this.$t("marketplace.connectionPage.notifications.mappingCheckError");
      } finally {
        this.publishWizardChecking = false;
        this.isResolvingMappings = false;
      }
    },

    async loadWizardReadiness(productIds) {
      this.publishWizardChecking = true;
      this.publishWizardError = null;
      try {
        const response = await http.post(
          `/marketplace/connections/${this.connectionId}/products/export-readiness`,
          { productIds },
        );
        this.exportReadiness = response.data;
        this.exportReadinessProductIds = [...productIds];
        this.publishWizardStep = "review";
      } catch (error) {
        this.publishWizardError =
          error.response?.data?.message ||
          this.$t("marketplace.connectionPage.products.readinessCheckFailed");
      } finally {
        this.publishWizardChecking = false;
      }
    },

    async publishFromWizard() {
      const readyIds = (this.exportReadiness?.products || [])
        .filter((product) => product.isReady)
        .map((product) => product.productId);
      if (readyIds.length === 0) return;
      this.publishWizardOpen = false;
      await this.executeExportNewBulk(readyIds);
      this.publishWizardStep = "categories";
      this.publishWizardError = null;
    },

    closePublishWizard() {
      this.publishWizardOpen = false;
      this.publishWizardError = null;
      this.exportReadiness = null;
      this.pendingExportType = null;
      this.pendingExportProductIds = [];
    },

    async checkNewExportReadiness(productIds) {
      this.isCheckingExportReadiness = true;
      try {
        const response = await http.post(
          `/marketplace/connections/${this.connectionId}/products/export-readiness`,
          { productIds },
        );
        this.exportReadiness = response.data;
        this.exportReadinessProductIds = [...productIds];
        this.showExportReadinessDialog = true;
      } catch (error) {
        displaySonnerError(
          error.response?.data?.message ||
            this.$t("marketplace.connectionPage.products.readinessCheckFailed"),
        );
      } finally {
        this.isCheckingExportReadiness = false;
      }
    },

    async confirmNewExportReadiness() {
      const readyIds = (this.exportReadiness?.products || [])
        .filter((product) => product.isReady)
        .map((product) => product.productId);
      if (readyIds.length === 0) return;

      this.showExportReadinessDialog = false;
      await this.executeExportNewBulk(readyIds);
      this.exportReadiness = null;
      this.exportReadinessProductIds = [];
    },

    closeExportReadiness() {
      this.showExportReadinessDialog = false;
      this.exportReadiness = null;
      this.exportReadinessProductIds = [];
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
          error.response?.data?.message ||
            this.$t(
              "marketplace.connectionPage.notifications.exportBatchError",
            ),
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
          this.$t(
            "marketplace.connectionPage.notifications.selectMappingForAll",
            {
              count: unselected.length,
            },
          ),
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
        if (this.pendingExportType === "wizard") {
          await this.loadWizardReadiness(this.pendingExportProductIds);
        } else if (this.pendingExportType === "new") {
          await this.checkNewExportReadiness(this.pendingExportProductIds);
        } else {
          await this.executeExportBulk(this.pendingExportProductIds);
        }
      } catch (error) {
        displaySonnerError(
          this.$t(
            "marketplace.connectionPage.notifications.saveMappingSelectionsError",
          ) +
            ": " +
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
        name: this.$t("common.name"),
        description: this.$t("common.description"),
        price: this.$t("marketplace.connectionPage.fields.price"),
        storageQuantity: this.$t(
          "marketplace.connectionPage.fields.storageQuantity",
        ),
        barcode: this.$t("marketplace.connectionPage.fields.barcode"),
      };
      if (field.startsWith("attr:")) return field.substring(5);
      return labels[field] || field;
    },

    formatImportFieldValue(field, value) {
      if (value === null || value === undefined) return null;
      if (field === "price") return this.formatPrice(value);
      return displayFieldValue(value, "");
    },

    getStatusInfo(product) {
      if (!product.isLinked) {
        return {
          variant: "outline",
          icon: Package,
          label: this.$t("marketplace.connectionPage.status.unlinked"),
          color: "text-muted-foreground",
          class: "text-muted-foreground",
        };
      }
      if (product.mappingUnselected) {
        return {
          variant: "destructive",
          icon: AlertTriangle,
          label: this.$t(
            "marketplace.connectionPage.status.mappingNotSelected",
          ),
          color: "text-error",
        };
      }
      if (product.mappingMissing) {
        return {
          variant: "destructive",
          icon: AlertTriangle,
          label: this.$t("marketplace.connectionPage.status.noMapping"),
          color: "text-error",
        };
      }
      switch (product.linkStatus) {
        case "Synced":
          return {
            variant: "outline",
            icon: CheckCircle,
            label: this.$t("marketplace.connectionPage.status.synced"),
            color: "text-success",
            class: "border-success/40 bg-success-muted/50 text-success",
          };
        case "NotSynced":
          return {
            variant: "outline",
            icon: AlertTriangle,
            label: this.$t("marketplace.connectionPage.status.notSynced"),
            color: "text-muted-foreground",
            class: "text-muted-foreground",
          };
        case "SyncError":
          return {
            variant: "destructive",
            icon: XCircle,
            label: this.$t("marketplace.connectionPage.status.syncError"),
            color: "text-error",
          };
        case "Modified":
          return {
            variant: "outline",
            icon: AlertTriangle,
            label: this.$t("marketplace.connectionPage.status.modified"),
            color: "text-warning",
            class: "border-warning/40 bg-warning-muted/50 text-warning",
          };
        case "RemoteMissing":
          return {
            variant: "destructive",
            icon: Unlink,
            label: this.$t("marketplace.connectionPage.status.remoteMissing"),
            color: "text-error",
          };
        default:
          return {
            variant: "outline",
            icon: Package,
            label: product.linkStatus || this.$t("common.emDash"),
            color: "text-muted-foreground",
            class: "text-muted-foreground",
          };
      }
    },

    getMarketplaceProductUrl(externalId) {
      const template = this.capabilities?.productEditUrlTemplate;
      if (!template || !externalId) return null;
      const publicCreds =
        this.connection?.publicCredentials ||
        this.connection?.PublicCredentials ||
        {};
      return fillProductEditUrl(template, {
        ...publicCreds,
        externalId,
      });
    },

    formatPrice(price) {
      if (price === null || price === undefined)
        return this.$t("common.emDash");
      return formatMoney(price, this.storeBaseCurrency);
    },

    onSettingsHotkey(e) {
      if (this.activeTab !== "settings") return;
      const t = e.target;
      if (
        t &&
        (t.tagName === "INPUT" ||
          t.tagName === "TEXTAREA" ||
          t.tagName === "SELECT" ||
          t.isContentEditable)
      ) {
        return;
      }
      if ((e.ctrlKey || e.metaKey) && e.code === "KeyS") {
        e.preventDefault();
        if (!this.isSaving) this.handleSave();
      }
    },
  },
  async mounted() {
    window.addEventListener("keydown", this.onSettingsHotkey);
    await this.fetchData();

    // Handle query params
    const {
      tab,
      lPage,
      startPublish,
      createMapping,
      createFromLocal,
      localCategoryId,
      localCategoryName,
    } = this.$route.query;

    if (tab) {
      this.activeTab = tab;
    }
    if (lPage) {
      this.localPage = parseInt(lPage) || 1;
    }

    if (startPublish === "1" || startPublish === "true") {
      const query = { ...this.$route.query };
      delete query.startPublish;
      this.$router.replace({ query });
      await this.startPublishWizard();
      return;
    }

    if (createFromLocal === "1" || createFromLocal === "true") {
      this.$router.replace(`/adapter/${this.connectionId}/categories/create`);
      return;
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
    window.removeEventListener("keydown", this.onSettingsHotkey);
    this.stopLimitsAutoRefresh();
    this.$store.commit("aiChat/clearPageContext");
  },
};
</script>

<template>
  <AppPageChrome
    :fill-viewport="activeTab === 'products'"
    :has-nav-center="true"
  >
    <template #nav-center>
      <Tabs v-model="activeTab">
        <TabsList>
          <TabsTrigger value="settings">{{
            $t("marketplace.connectionPage.tabs.settings")
          }}</TabsTrigger>
          <TabsTrigger value="mappings">{{
            $t("marketplace.connectionPage.tabs.mappings")
          }}</TabsTrigger>
          <TabsTrigger value="products" @click="onProductsTabActivated">
            {{ $t("marketplace.connectionPage.tabs.products") }}
          </TabsTrigger>
        </TabsList>
      </Tabs>
    </template>

    <PageSection
      :loading="isLoading"
      :class="
        activeTab === 'products'
          ? 'mb-0 min-h-0 flex-1 overflow-hidden'
          : 'flex flex-col'
      "
    >
      <div
        v-if="connection"
        :class="
          activeTab === 'products'
            ? 'flex min-h-0 flex-1 flex-col'
            : 'flex flex-col gap-4'
        "
      >
        <Tabs
          v-model="activeTab"
          :class="
            activeTab === 'products'
              ? 'flex min-h-0 w-full flex-1 flex-col overflow-hidden'
              : 'flex w-full flex-col'
          "
        >
          <TabsContent
            value="settings"
            class="mt-0 data-[state=inactive]:hidden"
          >
            <div class="max-w-2xl">
              <SettingsFormSection
                :title="
                  $t('marketplace.connectionPage.settings.connectionTitle')
                "
              >
                <template #actions>
                  <ExperimentalBadge v-if="isExperimentalAdapter" />
                </template>
                <SettingsFormField
                  :label="$t('common.name')"
                  label-for="connection-name"
                >
                  <Input
                    id="connection-name"
                    v-model="connection.name"
                    :maxlength="FIELD_LIMITS.connectionName"
                  />
                </SettingsFormField>

                <SettingsFormField
                  :label="
                    $t('marketplace.connectionPage.settings.activeConnection')
                  "
                  label-for="active"
                  inline
                >
                  <Switch
                    id="active"
                    :model-value="connection.isActive"
                    @update:model-value="connection.isActive = $event"
                  />
                </SettingsFormField>
              </SettingsFormSection>

              <SettingsFormSection
                v-if="credentialFields?.length"
                :title="
                  $t('marketplace.connectionPage.settings.credentialsTitle')
                "
              >
                <DocsHelpRow
                  v-if="credentialsDocsSlug"
                  :slug="credentialsDocsSlug"
                  :title="
                    $t(
                      'marketplace.connectionPage.settings.credentialsDocsLink',
                    )
                  "
                  :description="
                    $t(
                      'marketplace.connectionPage.settings.credentialsDocsHint',
                      { adapter: adapterDisplayName },
                    )
                  "
                />
                <SettingsFormField
                  v-for="field in credentialFields"
                  :key="field.key"
                  :label="field.displayName"
                  :label-for="`cred-${field.key}`"
                  :required="field.isRequired && !connection?.hasCredentials"
                >
                  <div
                    v-if="field.isSecret && isSecretMasked(field.key)"
                    class="w-full space-y-1.5"
                  >
                    <Input
                      :id="`cred-${field.key}`"
                      model-value="••••••••••••"
                      disabled
                      type="password"
                      class="w-full"
                      autocomplete="off"
                    />
                    <Button
                      type="button"
                      variant="link"
                      class="h-auto px-0"
                      @click="startEditSecret(field.key)"
                    >
                      {{
                        $t("marketplace.connectionPage.settings.changeSecret")
                      }}
                    </Button>
                  </div>
                  <div v-else-if="field.isSecret" class="w-full space-y-1.5">
                    <PasswordInput
                      :id="`cred-${field.key}`"
                      v-model="credentialValues[field.key]"
                      class="w-full"
                      :placeholder="field.placeholder || field.description"
                      :maxlength="FIELD_LIMITS.credentialValue"
                    />
                    <Button
                      v-if="connection?.hasCredentials"
                      type="button"
                      variant="link"
                      class="h-auto px-0 text-muted-foreground"
                      @click="cancelEditSecret(field.key)"
                    >
                      {{
                        $t(
                          "marketplace.connectionPage.settings.cancelSecretEdit",
                        )
                      }}
                    </Button>
                  </div>
                  <Select
                    v-else-if="field.type === 'select'"
                    :model-value="credentialValues[field.key]"
                    @update:model-value="credentialValues[field.key] = $event"
                  >
                    <SelectTrigger :id="`cred-${field.key}`" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem
                        v-for="option in field.options"
                        :key="option.value"
                        :value="option.value"
                      >
                        {{ option.label }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                  <Input
                    v-else
                    :id="`cred-${field.key}`"
                    v-model="credentialValues[field.key]"
                    type="text"
                    :placeholder="field.placeholder || field.description"
                    :maxlength="FIELD_LIMITS.credentialValue"
                  />
                  <template v-if="field.description" #description>
                    {{ field.description }}
                  </template>
                </SettingsFormField>
              </SettingsFormSection>

              <div class="mt-6 flex flex-wrap items-center gap-x-4 gap-y-2">
                <ButtonGroup class="items-stretch [&_button]:h-9">
                  <Button @click="handleSave" :disabled="isSaving">
                    <Spinner v-if="isSaving" class="mr-2" />
                    <Save v-else class="size-4 mr-2" />
                    {{ $t("common.save") }}
                  </Button>
                  <DropdownMenu>
                    <DropdownMenuTrigger as-child>
                      <Button
                        size="icon"
                        class="h-9 w-9 shrink-0"
                        :aria-label="
                          $t('marketplace.connectionPage.header.moreActions')
                        "
                      >
                        <EllipsisVertical class="size-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="start" class="w-56">
                      <DropdownMenuItem
                        :disabled="isTesting"
                        @click="handleTest"
                      >
                        <Spinner v-if="isTesting" class="mr-2" />
                        <TestTube v-else class="size-4 mr-2" />
                        {{ $t("marketplace.connectionPage.header.test") }}
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        :disabled="isVerifyingLinks || linkCheckRunning"
                        @click="handleVerifyLinks"
                      >
                        <Spinner
                          v-if="isVerifyingLinks || linkCheckRunning"
                          class="mr-2"
                        />
                        <SearchCheck v-else class="size-4 mr-2" />
                        {{
                          $t("marketplace.connectionPage.header.verifyLinks")
                        }}
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        :disabled="
                          limitsData != null && limitsData.supported === false
                        "
                        @click="showLimitsDialog = true"
                      >
                        <Activity class="size-4 mr-2" />
                        {{ $t("marketplace.connectionPage.limits.title") }}
                      </DropdownMenuItem>
                      <DropdownMenuSeparator />
                      <DropdownMenuItem
                        variant="destructive"
                        :disabled="isClearingCache"
                        @click="handleClearCache"
                      >
                        <Spinner v-if="isClearingCache" class="mr-2" />
                        <Trash2 v-else class="size-4 mr-2" />
                        {{ $t("marketplace.connectionPage.header.clearCache") }}
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </ButtonGroup>

                <Button
                  v-if="capabilities"
                  type="button"
                  variant="link"
                  class="h-auto px-0"
                  @click="showCapabilitiesDialog = true"
                >
                  {{
                    $t("marketplace.connectionPage.settings.capabilitiesLink")
                  }}
                </Button>

                <Dialog v-model:open="showCapabilitiesDialog">
                  <DialogContent
                    size="lg"
                    class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
                  >
                    <DialogHeader variant="bar">
                      <DialogTitle>
                        {{
                          $t(
                            "marketplace.connectionPage.settings.capabilitiesTitle",
                          )
                        }}
                      </DialogTitle>
                      <DialogDescription>
                        {{
                          $t(
                            "marketplace.connectionPage.settings.capabilitiesDescription",
                            {
                              adapter: adapterDisplayName,
                            },
                          )
                        }}
                      </DialogDescription>
                    </DialogHeader>

                    <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
                      <InsetGroup v-if="capabilities?.capabilities">
                        <InsetGroupItem
                          v-for="(value, key) in capabilities.capabilities"
                          :key="key"
                          as="div"
                        >
                          <p>{{ capabilityLabels[key] || key }}</p>
                          <template #trailing>
                            <Check
                              v-if="value"
                              class="size-4 text-muted-foreground"
                            />
                          </template>
                        </InsetGroupItem>
                      </InsetGroup>
                    </div>
                  </DialogContent>
                </Dialog>

                <Dialog v-model:open="showLimitsDialog">
                  <DialogContent
                    size="lg"
                    class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
                  >
                    <DialogHeader variant="bar">
                      <DialogTitle>
                        {{ $t("marketplace.connectionPage.limits.title") }}
                      </DialogTitle>
                      <DialogDescription>
                        {{ $t("marketplace.connectionPage.limits.subtitle") }}
                      </DialogDescription>
                    </DialogHeader>

                    <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
                      <div
                        v-if="isLoadingLimits"
                        class="flex justify-center py-10"
                      >
                        <Spinner size="2xl" class="text-muted-foreground" />
                      </div>

                      <EmptyState
                        v-else-if="limitsData && !limitsData.supported"
                        class="flex-1 py-12"
                        :title="
                          $t(
                            'marketplace.connectionPage.limits.unavailableTitle',
                          )
                        "
                        :description="
                          $t(
                            'marketplace.connectionPage.limits.unavailableDescription',
                          )
                        "
                      >
                        <template #icon>
                          <Activity />
                        </template>
                      </EmptyState>

                      <div v-else-if="limitsData?.supported" class="space-y-6">
                        <InsetGroup>
                          <InsetGroupItem as="div">
                            <p>
                              {{
                                $t(
                                  "marketplace.connectionPage.limits.requestsPerSecond",
                                )
                              }}
                            </p>
                            <p
                              class="text-muted-foreground mt-0.5 text-xs font-normal"
                            >
                              {{
                                $t(
                                  "marketplace.connectionPage.limits.currentWindow",
                                )
                              }}
                              ·
                              {{
                                getUsageLoadLabel(
                                  limitsData.currentWindowRequests,
                                  limitsData.maxRequestsPerSecond,
                                )
                              }}
                            </p>
                            <div
                              class="mt-1.5 h-1 overflow-hidden rounded-md bg-muted"
                              aria-hidden="true"
                            >
                              <div
                                class="bg-primary h-full rounded-md transition-[width] duration-300 ease-out"
                                :style="{ width: `${rpsUsagePercent}%` }"
                              />
                            </div>
                            <p
                              class="text-muted-foreground mt-1.5 text-xs font-normal"
                            >
                              {{
                                $t(
                                  "marketplace.connectionPage.limits.totalRequestsSinceStart",
                                )
                              }}
                              {{
                                limitsData.totalRequestsSinceStart.toLocaleString(
                                  "ru-RU",
                                )
                              }}
                            </p>
                            <template #trailing>
                              <span
                                class="text-muted-foreground text-xs font-normal tabular-nums"
                              >
                                {{ limitsData.currentWindowRequests }} /
                                {{ limitsData.maxRequestsPerSecond }} RPS
                              </span>
                            </template>
                          </InsetGroupItem>
                        </InsetGroup>

                        <p
                          v-if="limitsData.quotasAvailable === false"
                          class="px-1 text-xs text-muted-foreground"
                        >
                          {{
                            $t(
                              "marketplace.connectionPage.limits.quotasUnavailable",
                            )
                          }}
                        </p>

                        <InsetGroup v-if="limitsData.quotas?.length > 0">
                          <InsetGroupItem
                            v-for="quota in limitsData.quotas"
                            :key="quota.key"
                            as="div"
                          >
                            <p class="truncate">{{ quota.displayName }}</p>
                            <p
                              v-if="quota.resetsAt"
                              class="text-muted-foreground mt-0.5 text-xs font-normal"
                            >
                              {{
                                $t("marketplace.connectionPage.limits.reset")
                              }}
                              {{ formatQuotaResetTime(quota.resetsAt) }}
                            </p>
                            <div
                              v-if="quota.limit > 0"
                              class="mt-1.5 h-1 overflow-hidden rounded-md bg-muted"
                              aria-hidden="true"
                            >
                              <div
                                class="bg-primary h-full rounded-md transition-[width] duration-300 ease-out"
                                :style="{
                                  width: `${getUsagePercent(quota.usage, quota.limit)}%`,
                                }"
                              />
                            </div>
                            <template #trailing>
                              <span
                                class="text-muted-foreground shrink-0 text-xs font-normal tabular-nums"
                              >
                                <template v-if="quota.limit < 0">
                                  {{ quota.usage.toLocaleString("ru-RU") }} ·
                                  {{
                                    $t(
                                      "marketplace.connectionPage.limits.unlimited",
                                    )
                                  }}
                                </template>
                                <template v-else>
                                  {{ quota.usage.toLocaleString("ru-RU") }} /
                                  {{ quota.limit.toLocaleString("ru-RU") }}
                                </template>
                              </span>
                            </template>
                          </InsetGroupItem>
                        </InsetGroup>
                      </div>
                    </div>
                  </DialogContent>
                </Dialog>
              </div>

              <InsetGroup v-if="testResult" class="mt-8">
                <InsetGroupItem as="div">
                  <p>
                    {{
                      testResult.success
                        ? testResult.message ||
                          $t(
                            "marketplace.connectionPage.notifications.testSuccess",
                          )
                        : testResult.errorMessage ||
                          $t(
                            "marketplace.connectionPage.notifications.testError",
                          )
                    }}
                  </p>
                  <AiErrorExplainer
                    v-if="
                      aiEnabled &&
                      !testResult.success &&
                      (testResult.errorMessage ||
                        $t(
                          'marketplace.connectionPage.notifications.testError',
                        ))
                    "
                    class="mt-2"
                    :error-message="
                      testResult.errorMessage ||
                      $t('marketplace.connectionPage.notifications.testError')
                    "
                    :context="`test_connection`"
                    :connection-id="connection?.id"
                  />
                  <template #trailing>
                    <Check
                      v-if="testResult.success"
                      class="size-4 text-muted-foreground"
                    />
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </div>
          </TabsContent>

          <!-- Mappings Tab -->
          <TabsContent
            value="mappings"
            class="mt-0 flex min-h-0 flex-col gap-3 data-[state=inactive]:hidden"
          >
            <div
              class="flex flex-wrap items-center justify-between gap-2 shrink-0"
            >
              <p class="text-sm text-muted-foreground">
                {{ $t("marketplace.connectionPage.mappings.title") }}
                <span class="tabular-nums">
                  · {{ categoryMappings.length }}
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
                      {{
                        $t("marketplace.connectionPage.mappings.createMapping")
                      }}
                    </Button>
                  </DialogTrigger>
                  <DialogContent
                    size="6xl"
                    class="flex h-[min(92vh,60rem)] flex-col gap-0 overflow-hidden p-0 sm:max-w-7xl"
                  >
                    <DialogHeader variant="bar">
                      <DialogTitle>
                        {{
                          $t(
                            "marketplace.connectionPage.mappings.createMapping",
                          )
                        }}
                      </DialogTitle>
                      <DialogDescription>
                        {{
                          $t(
                            "marketplace.connectionPage.mappings.createMappingDescription",
                            { adapter: adapterDisplayName },
                          )
                        }}
                      </DialogDescription>
                    </DialogHeader>

                    <div class="px-6 pt-5 pb-4">
                      <SettingsFormField
                        :label="
                          $t(
                            'marketplace.connectionPage.mappings.mappingNameLabel',
                          )
                        "
                      >
                        <Input
                          v-model="newMapping.name"
                          :placeholder="
                            $t(
                              'marketplace.connectionPage.mappings.mappingNamePlaceholder',
                            )
                          "
                          :maxlength="FIELD_LIMITS.mappingName"
                        />
                        <template #description>
                          {{
                            $t(
                              "marketplace.connectionPage.mappings.mappingNameHint",
                            )
                          }}
                        </template>
                      </SettingsFormField>
                    </div>

                    <div
                      class="grid min-h-0 flex-1 grid-cols-2 gap-4 border-t px-6"
                    >
                      <div class="flex min-h-0 flex-col gap-2 py-4">
                        <InsetGroupLabel class="mb-0">
                          {{
                            $t(
                              "marketplace.connectionPage.mappings.localCategory",
                            )
                          }}
                        </InsetGroupLabel>
                        <InsetGroup scrollable class="flex-1">
                          <div
                            v-if="!localCategories.length"
                            class="flex flex-1 items-center justify-center p-6 text-sm text-muted-foreground"
                          >
                            {{
                              $t(
                                "marketplace.connectionPage.mappings.noCategories",
                              )
                            }}
                          </div>
                          <template v-else>
                            <InsetGroupItem
                              v-for="cat in localCategories"
                              :key="cat.id"
                              :selected="newMapping.localCategoryId === cat.id"
                              @click="newMapping.localCategoryId = cat.id"
                            >
                              <p class="truncate">{{ cat.name }}</p>
                              <template v-if="cat.attributesCount" #trailing>
                                <span
                                  class="text-muted-foreground text-xs font-normal tabular-nums"
                                >
                                  {{
                                    $t(
                                      "marketplace.connectionPage.mappings.attributesCountAbbr",
                                      { count: cat.attributesCount },
                                    )
                                  }}
                                </span>
                              </template>
                            </InsetGroupItem>
                          </template>
                        </InsetGroup>
                        <p
                          v-if="selectedLocalCategory"
                          class="text-xs text-muted-foreground"
                        >
                          {{
                            $t("marketplace.connectionPage.mappings.selected")
                          }}
                          <span class="font-medium text-foreground">{{
                            selectedLocalCategory.name
                          }}</span>
                          <span v-if="selectedLocalCategory.attributesCount">
                            ·
                            {{
                              $t(
                                "marketplace.connectionPage.mappings.attributesCountAbbr",
                                {
                                  count: selectedLocalCategory.attributesCount,
                                },
                              )
                            }}
                          </span>
                        </p>
                      </div>

                      <div class="flex min-h-0 flex-1 flex-col gap-2 py-4">
                        <InsetGroupLabel class="mb-0">
                          {{
                            $t(
                              "marketplace.connectionPage.mappings.categoryOf",
                              { adapter: adapterDisplayName },
                            )
                          }}
                        </InsetGroupLabel>

                        <ExternalCategoryBrowser
                          v-model:search="externalCategorySearch"
                          class="min-h-0 flex-1"
                          :categories="externalCategories"
                          :path="categoryPath"
                          :loading="isLoadingCategories"
                          :selected-id="newMapping.externalCategoryId"
                          :search-placeholder="
                            $t(
                              'marketplace.connectionPage.mappings.searchByNamePlaceholder',
                            )
                          "
                          :search-aria-label="
                            $t(
                              'marketplace.connectionPage.mappings.findCategoriesAria',
                            )
                          "
                          :empty-title="
                            $t(
                              'marketplace.connectionPage.mappings.categoriesNotFoundTitle',
                            )
                          "
                          :empty-description="
                            capabilities?.capabilities?.createCategory &&
                            selectedLocalCategory
                              ? $t(
                                  'marketplace.connectionPage.mappings.categoriesNotFoundHintWithCreate',
                                  { adapter: adapterDisplayName },
                                )
                              : $t(
                                  'marketplace.connectionPage.mappings.categoriesNotFoundHint',
                                )
                          "
                          @search="searchExternalCategories"
                          @select="selectExternalCategory"
                          @navigate="navigateToCategory"
                          @path-index="navigateToPathIndex"
                        >
                          <template #empty-action>
                            <Button
                              v-if="capabilities?.capabilities?.createCategory"
                              size="sm"
                              variant="outline"
                              :disabled="!newMapping.localCategoryId"
                              @click="goToCreateExternalCategory"
                            >
                              <Upload class="size-4 mr-2" />
                              {{
                                $t(
                                  "marketplace.connectionPage.mappings.exportCategory",
                                )
                              }}
                            </Button>
                          </template>
                        </ExternalCategoryBrowser>

                        <p
                          v-if="newMapping.externalCategoryId"
                          class="text-xs text-muted-foreground"
                        >
                          {{
                            $t("marketplace.connectionPage.mappings.selected")
                          }}
                          <span class="font-medium text-foreground">
                            {{
                              newMapping.externalCategoryName ||
                              newMapping.externalCategoryId
                            }}
                          </span>
                        </p>
                      </div>
                    </div>

                    <DialogFooter variant="bar">
                      <DialogClose as-child>
                        <Button variant="outline">{{
                          $t("common.cancel")
                        }}</Button>
                      </DialogClose>
                      <Button
                        v-if="capabilities?.capabilities?.createCategory"
                        variant="outline"
                        @click="goToCreateExternalCategory"
                        :disabled="!newMapping.localCategoryId"
                      >
                        <Upload class="size-4 mr-2" />
                        {{
                          $t(
                            "marketplace.connectionPage.mappings.exportCategory",
                          )
                        }}
                      </Button>
                      <Button
                        @click="handleCreateMapping"
                        :disabled="
                          isCreatingMapping ||
                          !newMapping.localCategoryId ||
                          !newMapping.externalCategoryId ||
                          selectedExternalIsFolder
                        "
                      >
                        <Spinner v-if="isCreatingMapping" class="mr-2" />
                        {{
                          isCreatingMapping
                            ? $t("marketplace.connectionPage.mappings.linking")
                            : $t("marketplace.connectionPage.mappings.link")
                        }}
                      </Button>
                    </DialogFooter>
                  </DialogContent>
                </Dialog>

                <ButtonGroup>
                  <Button
                    v-if="canCreateCategory"
                    variant="outline"
                    @click="
                      $router.push(`/adapter/${connectionId}/categories/create`)
                    "
                  >
                    <Upload class="size-4 mr-2" />
                    {{
                      $t("marketplace.connectionPage.mappings.exportCategory")
                    }}
                  </Button>
                  <Button
                    variant="outline"
                    @click="
                      $router.push(`/adapter/${connectionId}/categories/import`)
                    "
                  >
                    <Download class="size-4 mr-2" />
                    {{
                      $t("marketplace.connectionPage.mappings.importCategory")
                    }}
                  </Button>
                </ButtonGroup>
              </div>
            </div>

            <EmptyState
              v-if="categoryMappings.length === 0"
              class="flex-1 py-12"
              :title="
                canCreateCategory
                  ? $t(
                      'marketplace.connectionPage.mappings.noMappingsYetCreateTitle',
                      { adapter: adapterDisplayName },
                    )
                  : $t('marketplace.connectionPage.mappings.noMappingsYetTitle')
              "
              :description="
                canCreateCategory
                  ? $t(
                      'marketplace.connectionPage.mappings.noMappingsYetCreateDescription',
                      { adapter: adapterDisplayName },
                    )
                  : $t(
                      'marketplace.connectionPage.mappings.noMappingsYetDescription',
                      { adapter: adapterDisplayName },
                    )
              "
            >
              <template #icon>
                <FolderTree />
              </template>
              <div class="flex flex-wrap items-center justify-center gap-2">
                <Button
                  variant="outline"
                  @click="showCreateMappingDialog = true"
                >
                  <Link class="size-4 mr-2" />
                  {{ $t("marketplace.connectionPage.mappings.createMapping") }}
                </Button>
                <ButtonGroup>
                  <Button
                    v-if="canCreateCategory"
                    variant="outline"
                    @click="
                      $router.push(`/adapter/${connectionId}/categories/create`)
                    "
                  >
                    <Upload class="size-4 mr-2" />
                    {{
                      $t("marketplace.connectionPage.mappings.exportCategory")
                    }}
                  </Button>
                  <Button
                    variant="outline"
                    @click="
                      $router.push(`/adapter/${connectionId}/categories/import`)
                    "
                  >
                    <Download class="size-4 mr-2" />
                    {{
                      $t("marketplace.connectionPage.mappings.importCategory")
                    }}
                  </Button>
                </ButtonGroup>
              </div>
            </EmptyState>

            <Dialog v-model:open="showBulkCreateCategoriesDialog">
              <DialogContent
                class="sm:max-w-lg flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0"
              >
                <DialogHeader variant="bar">
                  <DialogTitle>
                    {{
                      $t(
                        "marketplace.connectionPage.mappings.createCategoriesFromLocal",
                        { adapter: adapterDisplayName },
                      )
                    }}
                  </DialogTitle>
                  <DialogDescription>
                    {{
                      $t(
                        "marketplace.connectionPage.mappings.createCategoriesFromLocalDescription",
                        { adapter: adapterDisplayName },
                      )
                    }}
                  </DialogDescription>
                </DialogHeader>

                <div
                  class="min-h-0 flex-1 space-y-3 overflow-y-auto border-y px-6 py-4"
                >
                  <div
                    v-if="unmappedLocalCategories.length === 0"
                    class="py-8 text-center text-sm text-muted-foreground"
                  >
                    {{
                      $t(
                        "marketplace.connectionPage.mappings.noUnmappedCategories",
                      )
                    }}
                  </div>
                  <template v-else>
                    <div class="flex items-center justify-between gap-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        class="h-8 px-2"
                        :disabled="isBulkCreatingCategories"
                        @click="toggleBulkCreateSelectAll"
                      >
                        {{
                          allBulkCreateSelected
                            ? $t(
                                "marketplace.connectionPage.mappings.deselectAllCategories",
                              )
                            : $t(
                                "marketplace.connectionPage.mappings.selectAllCategories",
                              )
                        }}
                      </Button>
                      <span class="text-xs text-muted-foreground tabular-nums">
                        {{ bulkCreateSelectedCount }} /
                        {{ unmappedLocalCategories.length }}
                      </span>
                    </div>
                    <InsetGroup>
                      <InsetGroupItem
                        v-for="cat in unmappedLocalCategories"
                        :key="cat.id"
                        as="div"
                        class="cursor-pointer"
                        @click="
                          toggleBulkCreateCategory(
                            cat.id,
                            !bulkCreateSelectedIds[cat.id],
                          )
                        "
                      >
                        <template #leading>
                          <Checkbox
                            :model-value="!!bulkCreateSelectedIds[cat.id]"
                            :disabled="isBulkCreatingCategories"
                            @update:model-value="
                              (checked) =>
                                toggleBulkCreateCategory(cat.id, checked)
                            "
                            @click.stop
                          />
                        </template>
                        <p class="truncate">{{ cat.name }}</p>
                        <template v-if="cat.attributesCount" #trailing>
                          <span
                            class="text-muted-foreground text-xs font-normal tabular-nums"
                          >
                            {{
                              $t(
                                "marketplace.connectionPage.mappings.attributesCountAbbr",
                                { count: cat.attributesCount },
                              )
                            }}
                          </span>
                        </template>
                      </InsetGroupItem>
                    </InsetGroup>
                  </template>

                  <div v-if="bulkCreateProgress" class="space-y-2 pt-2">
                    <p class="text-sm text-muted-foreground">
                      {{
                        $t(
                          "marketplace.connectionPage.mappings.creatingCategoriesProgress",
                          {
                            done: bulkCreateProgress.done,
                            total: bulkCreateProgress.total,
                          },
                        )
                      }}
                    </p>
                    <Progress
                      :model-value="
                        bulkCreateProgress.total
                          ? (bulkCreateProgress.done /
                              bulkCreateProgress.total) *
                            100
                          : 0
                      "
                    />
                    <ul
                      v-if="bulkCreateProgress.errors.length"
                      class="max-h-28 space-y-1 overflow-y-auto text-xs text-destructive"
                    >
                      <li
                        v-for="(err, idx) in bulkCreateProgress.errors"
                        :key="idx"
                      >
                        {{ err.name }}: {{ err.error }}
                      </li>
                    </ul>
                  </div>
                </div>

                <DialogFooter variant="bar">
                  <DialogClose as-child>
                    <Button
                      variant="outline"
                      :disabled="isBulkCreatingCategories"
                    >
                      {{ $t("common.cancel") }}
                    </Button>
                  </DialogClose>
                  <Button
                    :disabled="
                      isBulkCreatingCategories ||
                      bulkCreateSelectedCount === 0 ||
                      unmappedLocalCategories.length === 0
                    "
                    @click="handleBulkCreateCategories"
                  >
                    <Spinner v-if="isBulkCreatingCategories" class="mr-2" />
                    <Upload v-else class="size-4 mr-2" />
                    {{
                      isBulkCreatingCategories
                        ? $t("marketplace.connectionPage.mappings.creating")
                        : $t(
                            "marketplace.connectionPage.mappings.createSelectedCategories",
                            {
                              count: bulkCreateSelectedCount,
                            },
                          )
                    }}
                  </Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>

            <div v-if="categoryMappings.length > 0" class="max-w-2xl">
              <InsetGroup>
                <InsetGroupItem
                  v-for="mapping in categoryMappings"
                  :key="mapping.id"
                  show-chevron
                  @click="openFieldMapping(mapping.id)"
                >
                  <p class="truncate">{{ mapping.localCategoryName }}</p>
                  <p
                    class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
                  >
                    {{ mappingRowSubtitle(mapping) }}
                  </p>
                  <template #trailing>
                    <DropdownMenu>
                      <DropdownMenuTrigger as-child>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8"
                          :aria-label="
                            $t(
                              'marketplace.connectionPage.mappings.mappingActionsAria',
                            )
                          "
                          @click.stop
                        >
                          <EllipsisVertical class="size-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem
                          @click.stop="openRenameMapping(mapping)"
                        >
                          <Pencil />
                          {{ $t("marketplace.connectionPage.mappings.rename") }}
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          @click.stop="requestDuplicateMapping(mapping.id)"
                        >
                          <Copy />
                          {{
                            $t("marketplace.connectionPage.mappings.duplicate")
                          }}
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem
                          variant="destructive"
                          @click.stop="handleDeleteMapping(mapping)"
                        >
                          <Trash2 />
                          {{ $t("common.delete") }}
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </div>
          </TabsContent>

          <!-- Products Tab -->
          <TabsContent
            value="products"
            class="mt-0 flex min-h-0 flex-1 flex-col gap-3 overflow-hidden data-[state=inactive]:hidden"
          >
            <div class="flex shrink-0 flex-wrap items-center gap-3">
              <Tabs
                :model-value="productsViewMode"
                class="min-w-0 max-w-full overflow-x-auto gap-0"
                @update:model-value="switchProductsView"
              >
                <TabsList>
                  <TabsTrigger value="external">{{
                    $t("marketplace.connectionPage.products.adapterProducts")
                  }}</TabsTrigger>
                  <TabsTrigger value="local">{{
                    $t("marketplace.connectionPage.products.localForExport")
                  }}</TabsTrigger>
                </TabsList>
              </Tabs>
              <PageListToolbar
                v-if="productsViewMode === 'external'"
                v-model="searchQuery"
                :placeholder="
                  $t(
                    'marketplace.connectionPage.products.searchBySkuPlaceholder',
                  )
                "
              />
              <PageListToolbar
                v-else
                v-model="localSearchQuery"
                :placeholder="
                  $t(
                    'marketplace.connectionPage.products.searchByNameOrSkuPlaceholder',
                  )
                "
              />
            </div>
            <div
              v-if="productsViewMode === 'local'"
              class="shrink-0 text-sm text-muted-foreground"
            >
              {{
                $t("marketplace.connectionPage.products.foundCount", {
                  count: localTotalCount,
                })
              }}
            </div>

            <div class="flex min-h-0 flex-1 flex-col">
              <!-- ==================== EXTERNAL VIEW ==================== -->
              <template v-if="productsViewMode === 'external'">
                <!-- Loading state -->
                <div
                  v-if="isLoadingProducts"
                  class="flex min-h-0 flex-1 items-center justify-center py-12"
                >
                  <Spinner size="2xl" class="text-muted-foreground" />
                </div>

                <EmptyState
                  v-else-if="productsListUnsupported"
                  class="flex-1 py-12"
                  :title="
                    $t(
                      'marketplace.connectionPage.products.fetchUnsupportedTitle',
                    )
                  "
                  :description="
                    $t(
                      'marketplace.connectionPage.products.fetchUnsupportedDescription',
                    )
                  "
                >
                  <template #icon>
                    <AlertTriangle />
                  </template>
                </EmptyState>

                <EmptyState
                  v-else-if="filteredProducts.length === 0"
                  class="flex-1 py-12"
                  :title="
                    $t('marketplace.connectionPage.products.noProductsTitle')
                  "
                  :description="
                    $t(
                      'marketplace.connectionPage.products.noProductsDescription',
                    )
                  "
                >
                  <template #icon>
                    <Package />
                  </template>
                </EmptyState>

                <!-- Products table (scroll pane, not EmptyState) -->
                <div v-else class="flex min-h-0 flex-1 flex-col">
                  <div
                    data-slot="table-scroll"
                    class="min-h-0 min-w-0 flex-1 overflow-auto"
                  >
                    <Table
                      :contain="false"
                      :aria-label="
                        $t(
                          'marketplace.connectionPage.products.adapterProducts',
                        )
                      "
                      class="w-full min-w-[78rem] table-fixed border-separate border-spacing-0"
                    >
                      <colgroup>
                        <col class="w-12" />
                        <col />
                        <col class="w-36" />
                        <col class="w-40" />
                        <col class="w-36" />
                        <col class="w-28" />
                        <col class="w-48" />
                        <col class="w-36" />
                      </colgroup>
                      <TableHeader
                        class="bg-background [&_th]:h-12 [&_th]:px-4 [&_th]:sticky [&_th]:top-0 [&_th]:z-[var(--z-sticky)] [&_th]:bg-background [&_th]:border-b"
                      >
                        <TableRow class="bg-background hover:bg-transparent">
                          <TableHead>
                            <Checkbox
                              :model-value="allSelected"
                              :indeterminate="someSelected"
                              @update:model-value="toggleSelectAll"
                            />
                          </TableHead>
                          <TableHead>{{ $t("common.name") }}</TableHead>
                          <TableHead>{{
                            $t(
                              "marketplace.connectionPage.products.externalIdHeader",
                            )
                          }}</TableHead>
                          <TableHead>{{
                            $t(
                              "marketplace.connectionPage.products.localSkuHeader",
                            )
                          }}</TableHead>
                          <TableHead class="text-right">{{
                            $t("marketplace.connectionPage.fields.price")
                          }}</TableHead>
                          <TableHead class="text-right">{{
                            $t(
                              "marketplace.connectionPage.fields.storageQuantity",
                            )
                          }}</TableHead>
                          <TableHead>{{ $t("common.status") }}</TableHead>
                          <TableHead>{{ $t("common.actions") }}</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody
                        class="[&_tr]:h-16 [&_td]:border-b [&_td]:px-4 [&_td]:py-3.5 [&_tr:last-child_td]:border-b-0"
                      >
                        <TableRow
                          v-for="product in filteredProducts"
                          :key="product.externalId"
                          class="cursor-pointer select-none"
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
                          <TableCell class="whitespace-normal">
                            <RouterLink
                              v-if="product.localProductId"
                              :to="`/products/${product.localProductId}/edit`"
                              :title="product.name"
                              class="line-clamp-2 break-words font-medium leading-5 text-foreground underline-offset-2 hover:underline"
                              @click.stop
                              >{{ product.name }}</RouterLink
                            >
                            <div
                              v-else
                              :title="product.name"
                              class="line-clamp-2 break-words font-medium leading-5"
                            >
                              {{ product.name }}
                            </div>
                            <div
                              v-if="product.sku"
                              :title="product.sku"
                              class="mt-1 truncate text-xs text-muted-foreground"
                            >
                              SKU: {{ product.sku }}
                            </div>
                          </TableCell>
                          <TableCell>
                            <a
                              v-if="
                                getMarketplaceProductUrl(product.externalId)
                              "
                              :href="
                                getMarketplaceProductUrl(product.externalId)
                              "
                              target="_blank"
                              rel="noopener"
                              :title="product.externalId"
                              class="block truncate font-mono text-sm text-foreground hover:underline underline-offset-2"
                              >{{ product.externalId }}</a
                            >
                            <code
                              v-else
                              :title="product.externalId"
                              class="block truncate font-mono text-sm text-muted-foreground"
                              >{{ product.externalId }}</code
                            >
                          </TableCell>
                          <TableCell>
                            <div
                              v-if="product.isLinked"
                              class="flex min-w-0 items-center gap-1.5"
                            >
                              <Link2 class="size-3 shrink-0 text-success" />
                              <code
                                :title="product.localProductSku"
                                class="truncate text-sm"
                                >{{ product.localProductSku }}</code
                              >
                            </div>
                            <span v-else class="text-muted-foreground">-</span>
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
                                {{
                                  $t(
                                    "marketplace.connectionPage.products.adapterValuePrefix",
                                  )
                                }}
                                {{ formatPrice(product.externalPrice) }}
                              </div>
                            </div>
                            <span v-else class="text-muted-foreground">-</span>
                          </TableCell>
                          <TableCell class="text-right">
                            <div v-if="product.isLinked" class="space-y-0.5">
                              <div class="font-medium">
                                {{ product.localStock ?? "-" }}
                              </div>
                              <div
                                v-if="
                                  product.externalStock !== null &&
                                  product.externalStock !== product.localStock
                                "
                                class="text-xs text-muted-foreground"
                              >
                                {{
                                  $t(
                                    "marketplace.connectionPage.products.adapterValuePrefix",
                                  )
                                }}
                                {{ product.externalStock }}
                              </div>
                            </div>
                            <span v-else class="text-muted-foreground">-</span>
                          </TableCell>
                          <TableCell>
                            <Badge
                              :variant="
                                productStatusMap[product.externalId]?.variant
                              "
                              :class="[
                                'max-w-full whitespace-normal justify-start text-left',
                                productStatusMap[product.externalId]?.class,
                              ]"
                            >
                              <component
                                :is="productStatusMap[product.externalId]?.icon"
                                class="size-3 shrink-0"
                              />
                              <span class="min-w-0 break-words">{{
                                productStatusMap[product.externalId]?.label
                              }}</span>
                            </Badge>
                          </TableCell>
                          <TableCell>
                            <div
                              v-if="product.isLinked"
                              class="flex items-center gap-1"
                            >
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                @click="handleImportSingle(product)"
                                :disabled="importingLinkId === product.linkId"
                                :title="
                                  $t(
                                    'marketplace.connectionPage.products.importFromAdapter',
                                  )
                                "
                                :aria-label="
                                  $t(
                                    'marketplace.connectionPage.products.importFromAdapter',
                                  )
                                "
                              >
                                <Spinner
                                  v-if="importingLinkId === product.linkId"
                                />
                                <Download v-else class="size-4" />
                              </Button>
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                @click="handleExportSingle(product)"
                                :disabled="
                                  exportingLinkId === product.linkId ||
                                  product.mappingMissing ||
                                  product.mappingUnselected
                                "
                                :title="
                                  $t(
                                    'marketplace.connectionPage.products.exportToAdapter',
                                  )
                                "
                                :aria-label="
                                  $t(
                                    'marketplace.connectionPage.products.exportToAdapter',
                                  )
                                "
                              >
                                <Spinner
                                  v-if="exportingLinkId === product.linkId"
                                />
                                <Upload v-else class="size-4" />
                              </Button>
                              <Button
                                variant="ghost"
                                size="icon-sm"
                                class="text-destructive hover:bg-destructive/10 hover:text-destructive"
                                @click="handleUnlinkProduct(product)"
                                :disabled="unlinkingLinkId === product.linkId"
                                :title="
                                  $t(
                                    'marketplace.connectionPage.products.unlinkProduct',
                                  )
                                "
                                :aria-label="
                                  $t(
                                    'marketplace.connectionPage.products.unlinkProduct',
                                  )
                                "
                              >
                                <Spinner
                                  v-if="unlinkingLinkId === product.linkId"
                                />
                                <Ban v-else class="size-4" />
                              </Button>
                            </div>
                            <span v-else class="text-muted-foreground">-</span>
                          </TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>

                    <div v-if="canLoadMore" class="flex justify-center py-4">
                      <Button
                        variant="outline"
                        size="sm"
                        :disabled="isLoadingMore"
                        @click="loadMoreProducts"
                      >
                        <Spinner v-if="isLoadingMore" class="mr-2" />
                        {{
                          isLoadingMore
                            ? $t("common.loading")
                            : $t("marketplace.connectionPage.products.loadMore")
                        }}
                      </Button>
                    </div>
                  </div>

                  <!-- Bulk actions bar -->
                  <div
                    v-if="hasAnySelected"
                    data-slot="table-actions-footer"
                    class="bg-background flex shrink-0 flex-wrap items-center gap-2 px-3 py-2"
                  >
                    <span class="text-sm font-medium">
                      {{
                        $t("marketplace.connectionPage.products.selectedLabel")
                      }}
                      {{ selectedItems.length }}
                      <span v-if="hasSelectedLinked && hasSelectedUnlinked">
                        ({{
                          $t(
                            "marketplace.connectionPage.products.linkedCount",
                            { count: selectedLinkedItems.length },
                          )
                        }},
                        {{
                          $t(
                            "marketplace.connectionPage.products.unlinkedCount",
                            { count: selectedUnlinkedItems.length },
                          )
                        }})
                      </span>
                      <span v-else-if="hasSelectedLinked">
                        ({{
                          $t(
                            "marketplace.connectionPage.products.linkedCount",
                            { count: selectedLinkedItems.length },
                          )
                        }})
                      </span>
                      <span v-else-if="hasSelectedUnlinked">
                        ({{
                          $t(
                            "marketplace.connectionPage.products.unlinkedCount",
                            { count: selectedUnlinkedItems.length },
                          )
                        }})
                      </span>
                    </span>
                    <Button
                      variant="ghost"
                      size="sm"
                      class="h-8"
                      @click="clearExternalSelection"
                    >
                      <X class="h-4 w-4" />
                    </Button>
                    <div class="flex flex-wrap items-center gap-2">
                      <!-- Create local products from unlinked -->
                      <Button
                        v-if="canManageAdapters && hasSelectedUnlinked"
                        variant="outline"
                        size="sm"
                        class="h-8"
                        @click="openImportDialog"
                        :disabled="isCreatingLocal"
                      >
                        <Spinner v-if="isCreatingLocal" class="mr-2" />
                        <Plus v-else class="mr-1 size-4" />
                        {{
                          $t(
                            "marketplace.connectionPage.products.createLocal",
                            { count: selectedUnlinkedItems.length },
                          )
                        }}
                      </Button>

                      <!-- Import from marketplace for linked -->
                      <Button
                        v-if="canManageAdapters && hasSelectedLinked"
                        variant="outline"
                        size="sm"
                        class="h-8"
                        @click="handleImportBulk"
                        :disabled="isImportingBulk"
                      >
                        <Spinner v-if="isImportingBulk" class="mr-2" />
                        <Download v-else class="size-4 mr-2" />
                        {{
                          $t(
                            "marketplace.connectionPage.products.importCount",
                            { count: selectedLinkedItems.length },
                          )
                        }}
                      </Button>

                      <!-- Export to marketplace for linked -->
                      <Button
                        v-if="canManageAdapters && hasSelectedLinked"
                        variant="outline"
                        size="sm"
                        class="h-8"
                        @click="handleExportBulk"
                        :disabled="isExportingBulk"
                      >
                        <Spinner v-if="isExportingBulk" class="mr-2" />
                        <Upload v-else class="mr-1 size-4" />
                        {{
                          $t(
                            "marketplace.connectionPage.products.exportCount",
                            { count: selectedLinkedItems.length },
                          )
                        }}
                      </Button>

                      <!-- Export prices -->
                      <Button
                        v-if="canManageAdapters && hasSelectedLinked"
                        variant="outline"
                        size="sm"
                        class="h-8"
                        @click="handleExportPricesBulk"
                        :disabled="isExportingPrices"
                      >
                        <Spinner v-if="isExportingPrices" class="mr-2" />
                        <DollarSign v-else class="mr-1 size-4" />
                        {{
                          $t("marketplace.connectionPage.products.exportPrices")
                        }}
                      </Button>

                      <!-- Export stocks -->
                      <Button
                        v-if="canManageAdapters && hasSelectedLinked"
                        variant="outline"
                        size="sm"
                        class="h-8"
                        @click="handleExportStocksBulk"
                        :disabled="isExportingStocks"
                      >
                        <Spinner v-if="isExportingStocks" class="mr-2" />
                        <Boxes v-else class="mr-1 size-4" />
                        {{
                          $t("marketplace.connectionPage.products.exportStocks")
                        }}
                      </Button>
                    </div>
                  </div>
                </div>
              </template>

              <!-- ==================== LOCAL VIEW ==================== -->
              <template v-if="productsViewMode === 'local'">
                <!-- Loading state -->
                <div
                  v-if="isLoadingLocalProducts && localProducts.length === 0"
                  class="flex min-h-0 flex-1 items-center justify-center py-12"
                >
                  <Spinner size="2xl" class="text-muted-foreground" />
                </div>

                <EmptyState
                  v-else-if="
                    localProducts.length === 0 &&
                    (localExportReason === 'no_mappings' ||
                      categoryMappings.length === 0)
                  "
                  class="flex-1 py-12"
                  :title="
                    $t('marketplace.connectionPage.products.needMappingsTitle')
                  "
                  :description="
                    $t(
                      'marketplace.connectionPage.products.needMappingsDescription',
                      {
                        adapter: adapterDisplayName,
                      },
                    )
                  "
                >
                  <template #icon>
                    <FolderTree />
                  </template>
                  <Button @click="goToMappingsForCreate">
                    <FolderTree class="size-4 mr-2" />
                    {{
                      canCreateCategory
                        ? $t(
                            "marketplace.connectionPage.mappings.exportCategory",
                          )
                        : $t("marketplace.connectionPage.products.goToMappings")
                    }}
                  </Button>
                </EmptyState>

                <EmptyState
                  v-else-if="localProducts.length === 0"
                  class="flex-1 py-12"
                  :title="
                    $t(
                      'marketplace.connectionPage.products.noProductsToExportTitle',
                    )
                  "
                  :description="
                    $t(
                      'marketplace.connectionPage.products.noProductsToExportDescription',
                    )
                  "
                >
                  <template #icon>
                    <Package />
                  </template>
                </EmptyState>

                <!-- Local products table (scroll pane, not EmptyState) -->
                <div v-else class="flex min-h-0 flex-1 flex-col">
                  <div
                    data-slot="table-scroll"
                    class="min-h-0 min-w-0 flex-1 overflow-auto"
                  >
                    <Table
                      :contain="false"
                      :aria-label="
                        $t('marketplace.connectionPage.products.localForExport')
                      "
                      class="w-full min-w-[64rem] table-fixed border-separate border-spacing-0"
                    >
                      <colgroup>
                        <col class="w-12" />
                        <col />
                        <col class="w-44" />
                        <col class="w-36" />
                        <col class="w-28" />
                        <col class="w-56" />
                      </colgroup>
                      <TableHeader
                        class="bg-background [&_th]:h-12 [&_th]:px-4 [&_th]:sticky [&_th]:top-0 [&_th]:z-[var(--z-sticky)] [&_th]:bg-background [&_th]:border-b"
                      >
                        <TableRow class="bg-background hover:bg-transparent">
                          <TableHead>
                            <Checkbox
                              :model-value="allLocalSelected"
                              :indeterminate="someLocalSelected"
                              @update:model-value="toggleSelectAllLocal"
                            />
                          </TableHead>
                          <TableHead>{{ $t("common.name") }}</TableHead>
                          <TableHead>SKU</TableHead>
                          <TableHead class="text-right">{{
                            $t("marketplace.connectionPage.fields.price")
                          }}</TableHead>
                          <TableHead class="text-right">{{
                            $t(
                              "marketplace.connectionPage.fields.storageQuantity",
                            )
                          }}</TableHead>
                          <TableHead>{{
                            $t(
                              "marketplace.connectionPage.products.categoryHeader",
                            )
                          }}</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody
                        class="[&_tr]:h-16 [&_td]:border-b [&_td]:px-4 [&_td]:py-3.5 [&_tr:last-child_td]:border-b-0"
                      >
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
                                (checked) =>
                                  toggleSelectLocal(product.id, checked)
                              "
                            />
                          </TableCell>
                          <TableCell class="whitespace-normal">
                            <RouterLink
                              :to="`/products/${product.id}/edit`"
                              :title="product.name"
                              class="line-clamp-2 break-words font-medium leading-5 text-foreground underline-offset-2 hover:underline"
                              >{{ product.name }}</RouterLink
                            >
                          </TableCell>
                          <TableCell>
                            <span
                              :title="product.sku"
                              class="block truncate font-mono text-sm text-muted-foreground"
                              >{{ product.sku }}</span
                            >
                          </TableCell>
                          <TableCell class="text-right">
                            {{ formatPrice(product.price) }}
                          </TableCell>
                          <TableCell class="text-right">
                            {{ product.storageQuantity ?? "-" }}
                          </TableCell>
                          <TableCell>
                            <span
                              :title="product.categoryName"
                              class="line-clamp-2 whitespace-normal break-words text-sm text-muted-foreground"
                              >{{ product.categoryName || "-" }}</span
                            >
                          </TableCell>
                        </TableRow>
                      </TableBody>
                    </Table>
                  </div>

                  <div
                    data-slot="table-actions-footer"
                    class="flex shrink-0 flex-wrap items-center justify-between gap-2 bg-background px-3 py-2"
                  >
                    <div class="flex min-w-0 flex-wrap items-center gap-4">
                      <div class="flex items-center gap-2">
                        <p class="text-sm text-muted-foreground">
                          {{
                            $t(
                              "marketplace.connectionPage.products.pageSizeLabel",
                            )
                          }}
                        </p>
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
                          <SelectTrigger class="h-8 w-[100px]">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectGroup>
                              <SelectItem as="button" value="10">10</SelectItem>
                              <SelectItem as="button" value="25">25</SelectItem>
                              <SelectItem as="button" value="50">50</SelectItem>
                              <SelectItem as="button" value="100"
                                >100</SelectItem
                              >
                              <SelectItem as="button" value="250"
                                >250</SelectItem
                              >
                            </SelectGroup>
                          </SelectContent>
                        </Select>
                      </div>
                      <template
                        v-if="selectedLocalIds.length > 0 || isExportingNewBulk"
                      >
                        <div class="h-4 w-px bg-border" />
                        <span class="text-sm font-medium">
                          {{
                            $t(
                              "marketplace.connectionPage.products.selectedLabel",
                            )
                          }}
                          {{ selectedLocalIds.length }}
                        </span>
                        <Button
                          variant="ghost"
                          size="sm"
                          class="h-8"
                          @click="clearLocalSelection"
                        >
                          <X class="h-4 w-4" />
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          class="h-8"
                          @click="handleExportNewBulk"
                          :disabled="
                            isExportingNewBulk || isCheckingExportReadiness
                          "
                        >
                          <Spinner
                            v-if="
                              isExportingNewBulk || isCheckingExportReadiness
                            "
                            class="mr-2"
                          />
                          <Upload v-else class="mr-1 size-4" />
                          {{
                            $t(
                              "marketplace.connectionPage.products.publishToAdapterCount",
                              {
                                count: selectedLocalIds.length,
                                adapter: adapterDisplayName,
                              },
                            )
                          }}
                        </Button>
                      </template>
                    </div>
                    <div class="flex items-center gap-2">
                      <p class="mr-2 text-sm text-muted-foreground">
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
                </div>
              </template>
            </div>
          </TabsContent>
        </Tabs>
      </div>
    </PageSection>

    <!-- Single Import Preview Dialog -->
    <FieldChangesPreviewDialog
      v-if="showSingleImportPreviewDialog"
      :open="showSingleImportPreviewDialog"
      :title="$t('productMarketplaceIntegrations.importDialogTitle')"
      :description="
        $t('productMarketplaceIntegrations.importDialogDescription')
      "
      :items="singleImportPreviewItems"
      selectable
      :excluded-fields="singleImportExcludedFields"
      :confirm-loading="isApplyingSingleImport"
      @update:open="(v) => (showSingleImportPreviewDialog = v)"
      @update:excluded-fields="singleImportExcludedFields = $event"
      @confirm="confirmSingleImport"
      @cancel="resetSingleImportPreview"
    />

    <!-- Bulk Import Preview Dialog -->
    <FieldChangesPreviewDialog
      v-if="showBulkImportPreviewDialog"
      :open="showBulkImportPreviewDialog"
      :title="$t('marketplace.connectionPage.dialogs.confirmBulkImportTitle')"
      :description="
        bulkImportPreviewData
          ? $t('marketplace.connectionPage.dialogs.bulkImportWillUpdate', {
              count: bulkImportPreviewData.totalWithChanges,
              total:
                bulkImportPreviewData.totalWithChanges +
                bulkImportPreviewData.totalWithoutChanges,
            })
          : ''
      "
      :items="bulkImportPreviewItems"
      :confirm-loading="isApplyingBulkImport"
      @update:open="(v) => (showBulkImportPreviewDialog = v)"
      @confirm="confirmBulkImport"
      @cancel="resetBulkImportPreview"
    />

    <!-- Import Dialog (create local products from unlinked) -->
    <Dialog v-model:open="showImportDialog">
      <DialogContent
        size="2xl"
        class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <DialogTitle>
            {{ $t("marketplace.connectionPage.dialogs.importProductsTitle") }}
          </DialogTitle>
          <DialogDescription>
            {{
              $t("marketplace.connectionPage.dialogs.importProductsDescription")
            }}
          </DialogDescription>
        </DialogHeader>

        <div
          v-if="isLoadingDialogData"
          class="flex flex-1 items-center justify-center py-12"
        >
          <Spinner size="2xl" class="text-muted-foreground" />
        </div>

        <div v-else class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
          <FieldGroup class="gap-5">
            <FieldDescription>
              {{
                $t("marketplace.connectionPage.dialogs.selectedProductsCount", {
                  count: selectedUnlinkedItems.length,
                })
              }}
            </FieldDescription>

            <FieldSet
              v-for="group in productsByExternalCategory"
              :key="group.externalCategoryId || '__none__'"
              class="gap-3"
            >
              <FieldLegend variant="label" class="mb-0">
                {{ group.displayName }}
              </FieldLegend>
              <FieldDescription>
                {{
                  $t("marketplace.connectionPage.dialogs.productsCount", {
                    count: group.products.length,
                  })
                }}
              </FieldDescription>

              <RadioGroup
                :model-value="
                  categoryAssignments[importAssignmentKey(group)]?.mode ||
                  'none'
                "
                class="gap-3"
                @update:model-value="
                  setAssignmentMode(importAssignmentKey(group), $event)
                "
              >
                <Field orientation="horizontal">
                  <RadioGroupItem
                    :id="`import-existing-${group.externalCategoryId || 'none'}`"
                    value="existing"
                  />
                  <FieldContent>
                    <FieldLabel
                      :for="`import-existing-${group.externalCategoryId || 'none'}`"
                      class="cursor-pointer text-sm font-medium"
                    >
                      {{
                        $t(
                          "marketplace.connectionPage.dialogs.useExistingCategory",
                        )
                      }}
                    </FieldLabel>
                    <SearchableSelect
                      v-if="
                        categoryAssignments[importAssignmentKey(group)]
                          ?.mode === 'existing'
                      "
                      :model-value="importLocalCategoryValue(group)"
                      :options="importLocalCategoryOptions(group)"
                      :placeholder="
                        $t(
                          'marketplace.connectionPage.dialogs.choosePlaceholder',
                        )
                      "
                      @update:model-value="onImportLocalCategory(group, $event)"
                    />
                  </FieldContent>
                </Field>

                <Field orientation="horizontal">
                  <RadioGroupItem
                    :id="`import-new-${group.externalCategoryId || 'none'}`"
                    value="new"
                  />
                  <FieldContent>
                    <FieldLabel
                      :for="`import-new-${group.externalCategoryId || 'none'}`"
                      class="cursor-pointer text-sm font-medium"
                    >
                      <template v-if="group.externalCategoryId">
                        {{
                          $t(
                            "marketplace.connectionPage.import.createNewFromAdapter",
                            { name: group.displayName },
                          )
                        }}
                      </template>
                      <template v-else>
                        {{
                          $t(
                            "marketplace.connectionPage.import.createNewCategory",
                          )
                        }}
                      </template>
                    </FieldLabel>
                    <SettingsFormField
                      v-if="
                        !group.externalCategoryId &&
                        categoryAssignments[importAssignmentKey(group)]
                          ?.mode === 'new'
                      "
                      :label="
                        $t(
                          'marketplace.connectionPage.import.categoryNameLabel',
                        )
                      "
                      :label-for="`import-new-cat-${group.externalCategoryId || 'none'}`"
                    >
                      <Input
                        :id="`import-new-cat-${group.externalCategoryId || 'none'}`"
                        :model-value="
                          categoryAssignments[importAssignmentKey(group)]
                            ?.newCategoryName || ''
                        "
                        :placeholder="
                          $t(
                            'marketplace.connectionPage.import.categoryNamePlaceholder',
                          )
                        "
                        :maxlength="FIELD_LIMITS.categoryName"
                        @update:model-value="
                          setAssignmentNewName(
                            importAssignmentKey(group),
                            $event,
                          )
                        "
                      />
                    </SettingsFormField>
                  </FieldContent>
                </Field>

                <Field orientation="horizontal">
                  <RadioGroupItem
                    :id="`import-none-${group.externalCategoryId || 'none'}`"
                    value="none"
                  />
                  <FieldLabel
                    :for="`import-none-${group.externalCategoryId || 'none'}`"
                    class="cursor-pointer text-sm font-medium"
                  >
                    {{ $t("marketplace.connectionPage.import.noCategory") }}
                  </FieldLabel>
                </Field>
              </RadioGroup>
            </FieldSet>
          </FieldGroup>
        </div>

        <DialogFooter variant="bar">
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button
            :disabled="!canConfirmImportWithCategories"
            @click="handleImportWithCategories"
          >
            <Spinner v-if="isImportingWithCategories" class="mr-2" />
            <Download v-else class="mr-2 size-4" />
            {{
              $t("marketplace.connectionPage.import.importCount", {
                count: selectedUnlinkedItems.length,
              })
            }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <!-- Duplicate Mapping confirmation -->
    <AlertDialog v-model:open="showDuplicateMappingDialog">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{
            $t("marketplace.connectionPage.dialogs.duplicateMappingTitle")
          }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{
              $t(
                "marketplace.connectionPage.dialogs.duplicateMappingDescription",
              )
            }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction @click="confirmDuplicateMapping">
            {{ $t("marketplace.connectionPage.dialogs.duplicateAction") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <!-- Rename Mapping Dialog -->
    <Dialog v-model:open="showRenameMappingDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{
            $t("marketplace.connectionPage.dialogs.renameMappingTitle")
          }}</DialogTitle>
          <DialogDescription>{{
            $t("marketplace.connectionPage.dialogs.renameMappingDescription")
          }}</DialogDescription>
        </DialogHeader>
        <FieldGroup class="gap-4">
          <SettingsFormField
            :label="$t('common.name')"
            label-for="rename-mapping-name"
          >
            <Input
              id="rename-mapping-name"
              v-model="renameMappingValue"
              :placeholder="
                $t('marketplace.connectionPage.dialogs.mappingNamePlaceholder')
              "
              :maxlength="FIELD_LIMITS.mappingName"
              @keyup.enter="handleRenameMapping"
            />
          </SettingsFormField>
        </FieldGroup>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button @click="handleRenameMapping" :disabled="isRenamingMapping">
            <Spinner v-if="isRenamingMapping" class="mr-2" />
            {{ $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <DeleteCategoryMappingDialog
      v-model:open="showDeleteMappingDialog"
      :mapping="deleteMappingTarget"
      :impact="deleteMappingImpact"
      :replacement-options="deleteMappingReplacementOptions"
      v-model:mode="deleteMappingMode"
      v-model:replacement-id="deleteMappingReplacementId"
      :loading="isDeletingMapping"
      @confirm="confirmDeleteMappingChoice"
    />

    <!-- Mapping Settings Dialog -->
    <Dialog v-model:open="showMappingSettingsDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{
            $t("marketplace.connectionPage.dialogs.mappingSettingsTitle")
          }}</DialogTitle>
          <DialogDescription>{{
            $t("marketplace.connectionPage.dialogs.mappingSettingsDescription")
          }}</DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <FieldGroup class="gap-4">
            <SettingsFormField
              :label="$t('common.name')"
              label-for="mapping-settings-name"
            >
              <Input
                id="mapping-settings-name"
                v-model="mappingSettingsName"
                :placeholder="
                  $t(
                    'marketplace.connectionPage.mappings.mappingNamePlaceholder',
                  )
                "
                :maxlength="FIELD_LIMITS.mappingName"
              />
            </SettingsFormField>
          </FieldGroup>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline">{{ $t("common.cancel") }}</Button>
          </DialogClose>
          <Button
            @click="saveMappingSettingsDialog"
            :disabled="isSavingMappingSettings"
          >
            <Spinner v-if="isSavingMappingSettings" class="mr-2" />
            {{ $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <PublishProductsWizard
      v-model:open="publishWizardOpen"
      :adapter-name="adapterDisplayName"
      :step="publishWizardStep"
      :unmapped-categories="publishWizardUnmappedCategories"
      :selected-count="selectedLocalIds.length"
      :search="localSearchQuery"
      :products="localProducts"
      :selected-ids="selectedLocalIds"
      :is-loading-products="isLoadingLocalProducts"
      :page="localPage"
      :last-page="localLastPage"
      :readiness="exportReadiness"
      :is-checking="publishWizardChecking"
      :is-publishing="isExportingNewBulk"
      :error="publishWizardError"
      @update:open="$event || closePublishWizard()"
      @update:search="localSearchQuery = $event"
      @create-mapping="openWizardMapping"
      @toggle-product="toggleSelectLocal"
      @change-page="setPublishWizardPage"
      @change-step="publishWizardStep = $event"
      @continue="continuePublishWizardToProducts"
      @check="checkWizardReadiness"
      @publish="publishFromWizard"
      @cancel="closePublishWizard"
    />

    <!-- Mapping Selection Dialog (pre-export) -->
    <Dialog
      v-model:open="showMappingSelectionDialog"
      @update:open="!$event && cancelMappingSelection()"
    >
      <DialogContent size="2xl">
        <DialogHeader>
          <DialogTitle>{{
            $t(
              "marketplace.connectionPage.dialogs.selectMappingForProductsTitle",
            )
          }}</DialogTitle>
          <DialogDescription>
            {{
              $t(
                "marketplace.connectionPage.dialogs.selectMappingForProductsDescription",
              )
            }}
          </DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4 max-h-96 overflow-y-auto">
          <!-- Quick selection by category -->
          <div
            v-if="mappingCategoryGroups.length > 0"
            class="space-y-2 pb-3 border-b"
          >
            <InsetGroupLabel class="mb-0">
              {{ $t("marketplace.connectionPage.dialogs.applyToAll") }}
            </InsetGroupLabel>
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
                  <SelectValue
                    :placeholder="
                      $t(
                        'marketplace.connectionPage.dialogs.chooseMappingPlaceholder',
                      )
                    "
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="m in group.availableMappings"
                    :key="m.id"
                    :value="m.id.toString()"
                  >
                    {{
                      mappingDisplayName(
                        m,
                        $t(
                          "marketplace.connectionPage.dialogs.mappingFallback",
                          { id: m.id },
                        ),
                      )
                    }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <FieldGroup class="gap-5">
            <SettingsFormField
              v-for="product in mappingSelectionProducts"
              :key="product.productId"
              :label="product.productName"
              :description="product.categoryName"
            >
              <Select
                :model-value="mappingSelections[product.productId]?.toString()"
                @update:model-value="
                  (val) =>
                    (mappingSelections[product.productId] = parseInt(val))
                "
              >
                <SelectTrigger class="w-full">
                  <SelectValue
                    :placeholder="
                      $t(
                        'marketplace.connectionPage.dialogs.chooseMappingPlaceholder',
                      )
                    "
                  />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="mapping in product.availableMappings"
                    :key="mapping.id"
                    :value="mapping.id.toString()"
                  >
                    {{
                      mappingDisplayName(
                        mapping,
                        $t(
                          "marketplace.connectionPage.dialogs.mappingFallback",
                          { id: mapping.id },
                        ),
                      )
                    }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </SettingsFormField>
          </FieldGroup>
        </div>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline" @click="cancelMappingSelection">{{
              $t("common.cancel")
            }}</Button>
          </DialogClose>
          <Button
            @click="confirmMappingSelections"
            :disabled="isSavingMappingSelections"
          >
            <Spinner v-if="isSavingMappingSelections" class="mr-2" />
            <Upload v-else class="size-4 mr-2" />
            {{ $t("marketplace.connectionPage.dialogs.continueExport") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Review the selected products before publishing them to the marketplace. -->
    <Dialog
      v-model:open="showExportReadinessDialog"
      @update:open="!$event && closeExportReadiness()"
    >
      <DialogContent size="2xl">
        <DialogHeader>
          <DialogTitle>{{
            $t("marketplace.connectionPage.products.publishReadyTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{
              $t("marketplace.connectionPage.products.publishReadyDescription")
            }}
          </DialogDescription>
        </DialogHeader>

        <div
          v-if="isCheckingExportReadiness"
          class="flex items-center justify-center gap-2 py-10 text-sm text-muted-foreground"
        >
          <Spinner />
          {{
            $t("marketplace.connectionPage.products.publishReadinessLoading")
          }}
        </div>

        <div v-else-if="exportReadiness" class="space-y-4 py-3">
          <div class="flex flex-wrap gap-x-5 gap-y-2 text-sm">
            <span class="font-medium text-success">
              {{
                $t("marketplace.connectionPage.products.publishReadyCount", {
                  count: exportReadiness.readyCount,
                })
              }}
            </span>
            <span
              v-if="exportReadiness.blockedCount > 0"
              class="font-medium text-warning-foreground"
            >
              {{
                $t("marketplace.connectionPage.products.publishBlockedCount", {
                  count: exportReadiness.blockedCount,
                })
              }}
            </span>
          </div>

          <p
            v-if="exportReadiness.readyCount === 0"
            class="rounded-md border border-warning/40 bg-warning-muted px-3 py-2 text-sm"
          >
            {{ $t("marketplace.connectionPage.products.publishNoReady") }}
          </p>

          <div v-if="exportReadiness.blockedCount > 0" class="space-y-2">
            <p class="text-sm font-medium">
              {{
                $t("marketplace.connectionPage.products.publishBlockedTitle")
              }}
            </p>
            <div
              class="max-h-72 space-y-2 overflow-y-auto rounded-md border p-2"
            >
              <div
                v-for="product in exportReadiness.products.filter(
                  (item) => !item.isReady,
                )"
                :key="product.productId"
                class="rounded-sm px-2 py-1.5"
              >
                <p class="text-sm font-medium">
                  {{ product.productName || `#${product.productId}` }}
                </p>
                <ul
                  class="mt-1 list-disc space-y-0.5 pl-5 text-sm text-muted-foreground"
                >
                  <li v-for="(issue, index) in product.issues" :key="index">
                    {{ issue }}
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" @click="closeExportReadiness">
            {{ $t("marketplace.connectionPage.products.publishCancel") }}
          </Button>
          <Button
            :disabled="
              !exportReadiness ||
              exportReadiness.readyCount === 0 ||
              isExportingNewBulk
            "
            @click="confirmNewExportReadiness"
          >
            <Spinner v-if="isExportingNewBulk" class="mr-2" />
            <Upload v-else class="mr-2 size-4" />
            {{
              $t("marketplace.connectionPage.products.publishConfirm", {
                count: exportReadiness?.readyCount || 0,
              })
            }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </AppPageChrome>
</template>
