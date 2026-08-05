<script>
import { Spinner } from "@/components/ui/spinner";
// ProductMarketplaceIntegrations - Manages marketplace connections for a product: export, import, and sync status.
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { fillProductEditUrl } from "@/lib/productEditUrl";
import { randomUuidCompact } from "@/utils/uuid";
import {
  Upload,
  Download,
  CheckCircle,
  XCircle,
  AlertTriangle,
  Link2,
  Settings,
  ExternalLink,
  Plus,
  RefreshCw,
  EllipsisVertical,
  Ruler,
  Layers,
  Unlink,
  SearchCheck,
  ChevronRight,
  Package,
  DollarSign,
  Boxes,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import Input from "@/components/ui/input/Input.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import Separator from "@/components/ui/separator/Separator.vue";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuSubContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from "@/components/ui/dropdown-menu";
import { ButtonGroup } from "@/components/ui/button-group";
import { PAGE_MARKETPLACE, PAGE_MARKETPLACE_CONNECTION, PAGE_MARKETPLACE_FIELD_MAPPING } from "@/router";
import FieldChangesPreviewDialog from "@/components/products/FieldChangesPreviewDialog.vue";
import { formatMoney } from "@/lib/currency";
import { useSelectedStore } from "@/composables/useSelectedStore";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { t } from "@/i18n";

const EXPORT_MODE_FULL = "full";
const EXPORT_MODE_PRICE = "price";
const EXPORT_MODE_STOCK = "stock";

export default {
  components: {
    Spinner,
    Button,
    Badge,
    EmptyState,
    AdapterIcon,
    Upload,
    Download,
    CheckCircle,
    XCircle,
    AlertTriangle,
    Link2,
    Settings,
    ExternalLink,
    Plus,
    RefreshCw,
    Input,
    SettingsFormField,
    FieldGroup,
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
    Dialog,
    DialogClose,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Separator,
    Checkbox,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuGroup,
    DropdownMenuItem,
    DropdownMenuLabel,
    DropdownMenuSeparator,
    DropdownMenuTrigger,
    DropdownMenuSub,
    DropdownMenuSubTrigger,
    DropdownMenuSubContent,
    DropdownMenuRadioGroup,
    DropdownMenuRadioItem,
    EllipsisVertical,
    Ruler,
    Layers,
    Unlink,
    SearchCheck,
    ChevronRight,
    Package,
    DollarSign,
    Boxes,
    ButtonGroup,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
    FieldChangesPreviewDialog
  },
  props: {
    productId: {
      type: Number,
      required: true,
    },
  },
  setup() {
    const { storeBaseCurrency, loadMemberships } = useSelectedStore();
    const { formatDate: formatStoreDate } = useStoreDateTime();
    loadMemberships();
    return { storeBaseCurrency, formatMoney, formatStoreDate };
  },
  data() {
    return {
      connections: null, // All connections with status info
      isLoading: false,
      isExportingAll: false,
      isExportingPricesAll: false,
      isExportingStocksAll: false,
      exportingConnectionId: null,
      exportingPricesConnectionId: null,
      exportingStocksConnectionId: null,
      importingLinkId: null,
      unlinkingLinkId: null,
      verifyingLinkId: null,
      PAGE_MARKETPLACE,
      PAGE_MARKETPLACE_CONNECTION,
      PAGE_MARKETPLACE_FIELD_MAPPING,
      // Recreate dialog
      showRecreateDialog: false,
      recreateConn: null,
      recreateNewSku: "",
      isRecreating: false,
      // Import preview dialog
      showImportPreviewDialog: false,
      importPreviewData: null,
      isLoadingPreview: false,
      isApplyingImport: false,
      previewLinkId: null,
      previewConnection: null,
      importExcludedFields: new Set(),
      // Mapping selection
      mappingSelections: {}, // { connectionId: categoryMappingId }
      // Variants per connection: { connectionId: { loading, variants: [] } }
      variantsData: {},
      // Bulk mapping dialog
      showBulkMappingDialog: false,
      // Manual link dialog
      showLinkDialog: false,
      linkConn: null,
      linkExternalId: "",
      isLinking: false,
      isRelinking: false,
      bulkMappingConflicts: [],
      bulkMappingSelections: {}, // temp selections in dialog
    };
  },
  computed: {
    exportAllMode() {
      return this.resolveExportMode("all");
    },
    exportAllModeLabel() {
      return this.getExportModeLabel(this.exportAllMode);
    },
    isExportingAnyAll() {
      return this.isExportingAll || this.isExportingPricesAll || this.isExportingStocksAll;
    },
    // Connections with mapping (can be exported)
    connectionsWithMapping() {
      if (!this.connections?.allConnections) return [];
      return this.connections.allConnections.filter(
        (c) => c.hasMapping && c.canExport && !c.mappingMissing,
      );
    },
    // Connections without mapping (excluding already linked ones)
    connectionsWithoutMapping() {
      if (!this.connections?.allConnections) return [];
      return this.connections.allConnections.filter(
        (c) => c.needsMapping && !c.isLinked,
      );
    },
    // Linked connections (already exported)
    linkedConnections() {
      if (!this.connections?.allConnections) return [];
      return this.connections.allConnections.filter((c) => c.isLinked);
    },
    // Connections ready to export (has mapping, not yet linked)
    readyToExportConnections() {
      if (!this.connections?.allConnections) return [];
      return this.connections.allConnections.filter(
        (c) => c.hasMapping && !c.isLinked && c.canExport,
      );
    },
    // Whether there are any connections available for export
    canExportToAll() {
      return this.connectionsWithMapping.length > 0;
    },
    linkedForPriceExport() {
      return this.linkedConnections.filter((c) => c.canExportPrices && c.linkId);
    },
    linkedForStockExport() {
      return this.linkedConnections.filter((c) => c.canExportStocks && c.linkId);
    },
    importPreviewItems() {
      if (!this.importPreviewData?.updatedFields) return [];
      return [{
        fields: this.importPreviewData.updatedFields.map((field) => ({
          key: field,
          label: this.getFieldLabel(field),
          oldValue: this.formatFieldValue(field, this.importPreviewData.oldValues[field]),
          newValue: this.formatFieldValue(field, this.importPreviewData.newValues[field]),
        })),
      }];
    },
    importWarning() {
      return this.importIrreversibleFields.length > 0
        ? this.$t("productMarketplaceIntegrations.irreversibleFieldsWarning")
        : null;
    },
    importIrreversibleFields() {
      return this.importPreviewData?.irreversibleFields || [];
    },
  },
  methods: {
    ...mapActions([
      "getAvailableConnectionsForProduct",
      "exportProduct",
      "importFromLink",
      "importFromLinkPreview",
      "exportFromLink",
      "exportPrices",
      "exportStocks",
      "setLinkPreferredExportMode",
      "setProductPreferredExportAllMode",
      "recreateProductOnMarketplace",
      "saveMappingSelections",
      "getProductVariants",
      "updateProductVariant",
      "linkProduct",
      "relinkProduct",
      "unlinkProduct",
      "verifyLink",
    ]),

    async fetchData() {
      this.isLoading = true;
      await this.getAvailableConnectionsForProduct({
        productId: this.productId,
        onSuccess: (response) => {
          this.connections = extractResponseData(response, {});
          // Initialize mapping selections from saved values or auto-select single mappings
          const selections = {};
          for (const conn of this.connections.allConnections || []) {
            if (conn.selectedMappingId) {
              selections[conn.connectionId] = conn.selectedMappingId;
            } else if (conn.availableMappings?.length === 1) {
              selections[conn.connectionId] = conn.availableMappings[0].id;
            }
          }
          this.mappingSelections = selections;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleMappingChange(conn, mappingId) {
      this.mappingSelections = { ...this.mappingSelections, [conn.connectionId]: mappingId };
      try {
        await this.saveMappingSelections({
          connectionId: conn.connectionId,
          selections: [{ productId: this.productId, categoryMappingId: mappingId }],
        });
        await this.fetchData();
      } catch {
        displaySonnerError(t("productMarketplaceIntegrations.errorSavingMapping"));
      }
    },

    async loadVariants(connectionId) {
      this.variantsData = {
        ...this.variantsData,
        [connectionId]: { loading: true, variants: this.variantsData[connectionId]?.variants || [] },
      };
      try {
        const data = await this.getProductVariants({ productId: this.productId });
        this.variantsData = {
          ...this.variantsData,
          [connectionId]: { loading: false, variants: data || [] },
        };
      } catch {
        this.variantsData = {
          ...this.variantsData,
          [connectionId]: { loading: false, variants: [] },
        };
      }
    },

    toggleVariants(conn) {
      const current = this.variantsData[conn.connectionId];
      if (current && current.variants.length > 0) {
        // Hide variants
        const copy = { ...this.variantsData };
        delete copy[conn.connectionId];
        this.variantsData = copy;
      } else {
        this.loadVariants(conn.connectionId);
      }
    },

    // Export to all marketplaces that have category mapping
    async handleExportToAll() {
      const allExportable = this.connectionsWithMapping;
      if (allExportable.length === 0) {
        displaySonnerSuccess(t("productMarketplaceIntegrations.noExportableConnections"));
        return;
      }

      const toLink = allExportable.filter((c) => !c.isLinked);

      // Check for unlinked connections with multiple mappings but no selection
      const conflicts = toLink.filter(
        (c) => c.availableMappings?.length > 1 && !this.mappingSelections[c.connectionId],
      );
      if (conflicts.length > 0) {
        this.bulkMappingConflicts = conflicts;
        this.bulkMappingSelections = {};
        this.showBulkMappingDialog = true;
        return;
      }

      this.isExportingAll = true;

      let successCount = 0;
      let errorCount = 0;

      for (const conn of allExportable) {
        const operationId = randomUuidCompact();
        const title = conn.isLinked
          ? t("productMarketplaceIntegrations.exportTitleLinked", { id: this.productId })
          : t("productMarketplaceIntegrations.exportTitleUnlinked", { id: this.productId });
        await this.$store.dispatch("operations/startOperation", {
          id: operationId,
          type: "single-export",
          title,
          connectionId: conn.connectionId,
          connectionName: conn.connectionName,
        });

        try {
          if (conn.isLinked) {
            await this.exportFromLink({
              linkId: conn.linkId,
              operationId,
              onSuccess: () => {
                successCount++;
              },
              onError: () => {
                errorCount++;
              },
            });
          } else {
            await this.exportProduct({
              connectionId: conn.connectionId,
              productId: this.productId,
              operationId,
              onSuccess: () => {
                successCount++;
              },
              onError: () => {
                errorCount++;
              },
            });
          }
        } catch {
          errorCount++;
        }
      }

      if (successCount > 0) {
        displaySonnerSuccess(t("productMarketplaceIntegrations.exportedToAdapters", { count: successCount }));
      }
      if (errorCount > 0) {
        displaySonnerError(t("productMarketplaceIntegrations.exportErrors", { count: errorCount }));
      }

      this.isExportingAll = false;
      await this.fetchData();
    },

    async handleExportPricesToAll() {
      const targets = this.linkedForPriceExport;
      if (targets.length === 0) {
        displaySonnerError(t("productMarketplaceIntegrations.noLinkedForPriceExport"));
        return;
      }

      this.isExportingPricesAll = true;
      let successCount = 0;
      let errorCount = 0;

      for (const conn of targets) {
        try {
          await this.exportPrices({
            connectionId: conn.connectionId,
            linkIds: [conn.linkId],
            onSuccess: () => {
              successCount++;
            },
            onError: () => {
              errorCount++;
            },
          });
        } catch {
          errorCount++;
        }
      }

      if (successCount > 0) {
        displaySonnerSuccess(
          t("productMarketplaceIntegrations.exportedPricesToAdapters", { count: successCount }),
        );
      }
      if (errorCount > 0) {
        displaySonnerError(t("productMarketplaceIntegrations.exportErrors", { count: errorCount }));
      }

      this.isExportingPricesAll = false;
      await this.fetchData();
    },

    async handleExportStocksToAll() {
      const targets = this.linkedForStockExport;
      if (targets.length === 0) {
        displaySonnerError(t("productMarketplaceIntegrations.noLinkedForStockExport"));
        return;
      }

      this.isExportingStocksAll = true;
      let successCount = 0;
      let errorCount = 0;

      for (const conn of targets) {
        try {
          await this.exportStocks({
            connectionId: conn.connectionId,
            linkIds: [conn.linkId],
            onSuccess: () => {
              successCount++;
            },
            onError: () => {
              errorCount++;
            },
          });
        } catch {
          errorCount++;
        }
      }

      if (successCount > 0) {
        displaySonnerSuccess(
          t("productMarketplaceIntegrations.exportedStocksToAdapters", { count: successCount }),
        );
      }
      if (errorCount > 0) {
        displaySonnerError(t("productMarketplaceIntegrations.exportErrors", { count: errorCount }));
      }

      this.isExportingStocksAll = false;
      await this.fetchData();
    },

    allBulkMappingsSelected() {
      return this.bulkMappingConflicts.every(
        (c) => this.bulkMappingSelections[c.connectionId],
      );
    },

    async confirmBulkMappingAndExport() {
      // Save all bulk mapping selections
      for (const conn of this.bulkMappingConflicts) {
        const mappingId = this.bulkMappingSelections[conn.connectionId];
        if (mappingId) {
          this.mappingSelections = { ...this.mappingSelections, [conn.connectionId]: mappingId };
          try {
            await this.saveMappingSelections({
              connectionId: conn.connectionId,
              selections: [{ productId: this.productId, categoryMappingId: mappingId }],
            });
          } catch {
            displaySonnerError(t("productMarketplaceIntegrations.errorSavingMappingFor", { adapter: conn.adapterName }));
            return;
          }
        }
      }
      this.showBulkMappingDialog = false;
      this.bulkMappingConflicts = [];
      this.bulkMappingSelections = {};
      // Now proceed with export
      await this.handleExportToAll();
    },

    // Import from a specific marketplace (pull changes) - preview first
    async handleImportFromLink(conn) {
      this.importingLinkId = conn.linkId;
      this.isLoadingPreview = true;
      await this.importFromLinkPreview({
        linkId: conn.linkId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (!result.hasChanges) {
            displaySonnerSuccess(t("productMarketplaceIntegrations.noChangesFound"));
          } else {
            this.importPreviewData = result;
            this.importExcludedFields = new Set(result.irreversibleFields || []);
            this.previewLinkId = conn.linkId;
            this.previewConnection = conn;
            this.showImportPreviewDialog = true;
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingPreview = false;
      this.importingLinkId = null;
    },

    async confirmImport() {
      if (!this.previewLinkId) return;
      this.isApplyingImport = true;
      const excludeFields = [...this.importExcludedFields];

      const operationId = randomUuidCompact();
      const conn = this.previewConnection;
      await this.$store.dispatch("operations/startOperation", {
        id: operationId,
        type: "single-import",
        title: t("productMarketplaceIntegrations.importTitle", { id: this.productId }),
        connectionId: conn?.connectionId || null,
        connectionName: conn?.connectionName || null,
      });

      await this.importFromLink({
        linkId: this.previewLinkId,
        excludeFields: excludeFields.length > 0 ? excludeFields : undefined,
        operationId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (result.updatedFields && result.updatedFields.length > 0) {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.updatedFieldsMessage", { fields: result.updatedFields.join(", ") }),
            );
            this.$emit("product-updated");
          } else {
            displaySonnerSuccess(t("productMarketplaceIntegrations.noChangesFound"));
          }
          this.fetchData();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isApplyingImport = false;
      this.cancelImport();
    },

    cancelImport() {
      this.showImportPreviewDialog = false;
      this.importPreviewData = null;
      this.previewLinkId = null;
      this.previewConnection = null;
      this.importExcludedFields = new Set();
    },

    resetImportPreview() {
      this.importPreviewData = null;
      this.previewLinkId = null;
      this.previewConnection = null;
      this.importExcludedFields = new Set();
    },

    getFieldLabel(field) {
      if (field.startsWith("attr:")) return field.substring(5);
      const known = ["name", "description", "price", "storageQuantity"];
      return known.includes(field)
        ? t(`productMarketplaceIntegrations.fieldLabels.${field}`)
        : field;
    },

    formatFieldValue(field, value) {
      if (value === null || value === undefined) return null;
      if (field === "price") {
        return formatMoney(value, this.storeBaseCurrency);
      }
      return String(value);
    },

    resolveExportMode(target) {
      let mode;
      if (target === "all") {
        mode = this.connections?.preferredExportAllMode;
      } else {
        mode = target.preferredExportMode;
      }
      if (mode !== EXPORT_MODE_PRICE && mode !== EXPORT_MODE_STOCK && mode !== EXPORT_MODE_FULL) {
        mode = EXPORT_MODE_FULL;
      }

      if (target === "all") {
        if (mode === EXPORT_MODE_PRICE && this.linkedForPriceExport.length === 0) {
          return EXPORT_MODE_FULL;
        }
        if (mode === EXPORT_MODE_STOCK && this.linkedForStockExport.length === 0) {
          return EXPORT_MODE_FULL;
        }
        return mode;
      }

      if (mode === EXPORT_MODE_PRICE && !target.canExportPrices) return EXPORT_MODE_FULL;
      if (mode === EXPORT_MODE_STOCK && !target.canExportStocks) return EXPORT_MODE_FULL;
      return mode;
    },

    getExportModeLabel(mode) {
      if (mode === EXPORT_MODE_PRICE) {
        return t("productMarketplaceIntegrations.menuExportPrice");
      }
      if (mode === EXPORT_MODE_STOCK) {
        return t("productMarketplaceIntegrations.menuExportStock");
      }
      return t("productMarketplaceIntegrations.menuExportFull");
    },

    async setExportMode(target, mode) {
      if (target === "all") {
        const prev = this.connections?.preferredExportAllMode;
        if (this.connections) {
          this.connections = { ...this.connections, preferredExportAllMode: mode };
        }
        await this.setProductPreferredExportAllMode({
          productId: this.productId,
          mode,
          onSuccess: () => {},
          onError: (error) => {
            if (this.connections) {
              this.connections = { ...this.connections, preferredExportAllMode: prev };
            }
            displaySonnerError(error);
          },
        });
        return;
      }

      const prev = target.preferredExportMode;
      const updated = (this.connections?.allConnections || []).map((c) =>
        c.connectionId === target.connectionId ? { ...c, preferredExportMode: mode } : c,
      );
      this.connections = { ...this.connections, allConnections: updated };

      await this.setLinkPreferredExportMode({
        linkId: target.linkId,
        mode,
        onSuccess: () => {},
        onError: (error) => {
          const reverted = (this.connections?.allConnections || []).map((c) =>
            c.connectionId === target.connectionId ? { ...c, preferredExportMode: prev } : c,
          );
          this.connections = { ...this.connections, allConnections: reverted };
          displaySonnerError(error);
        },
      });
    },

    runSelectedExport(conn) {
      const mode = this.resolveExportMode(conn);
      if (mode === EXPORT_MODE_PRICE) {
        return this.handleExportPricesToConnection(conn);
      }
      if (mode === EXPORT_MODE_STOCK) {
        return this.handleExportStocksToConnection(conn);
      }
      return this.handleExportToConnection(conn);
    },

    runSelectedExportAll() {
      const mode = this.resolveExportMode("all");
      if (mode === EXPORT_MODE_PRICE) {
        return this.handleExportPricesToAll();
      }
      if (mode === EXPORT_MODE_STOCK) {
        return this.handleExportStocksToAll();
      }
      return this.handleExportToAll();
    },

    isSelectedExportBusy(conn) {
      const mode = this.resolveExportMode(conn);
      if (mode === EXPORT_MODE_PRICE) {
        return this.exportingPricesConnectionId === conn.connectionId;
      }
      if (mode === EXPORT_MODE_STOCK) {
        return this.exportingStocksConnectionId === conn.connectionId;
      }
      return this.exportingConnectionId === conn.connectionId;
    },

    // Export to a specific marketplace
    async handleExportToConnection(conn) {
      if (conn.mappingMissing) {
        displaySonnerError(t("productMarketplaceIntegrations.mappingDeletedError"));
        return;
      }

      if (conn.availableMappings?.length > 1 && !this.mappingSelections[conn.connectionId]) {
        displaySonnerError(t("productMarketplaceIntegrations.chooseMappingBeforeExport"));
        return;
      }

      this.exportingConnectionId = conn.connectionId;

      const operationId = randomUuidCompact();
      const title = conn.isLinked
        ? t("productMarketplaceIntegrations.exportTitleLinked", { id: this.productId })
        : t("productMarketplaceIntegrations.exportTitleUnlinked", { id: this.productId });
      await this.$store.dispatch("operations/startOperation", {
        id: operationId,
        type: "single-export",
        title,
        connectionId: conn.connectionId,
        connectionName: conn.connectionName,
      });

      if (conn.isLinked) {
        await this.exportFromLink({
          linkId: conn.linkId,
          operationId,
          onSuccess: () => {
            displaySonnerSuccess(t("productMarketplaceIntegrations.productUpdatedOnAdapter", { adapter: conn.adapterName }));
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } else {
        await this.exportProduct({
          connectionId: conn.connectionId,
          productId: this.productId,
          operationId,
          onSuccess: () => {
            displaySonnerSuccess(t("productMarketplaceIntegrations.productExportedToAdapter", { adapter: conn.adapterName }));
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      }

      this.exportingConnectionId = null;
    },

    async handleExportPricesToConnection(conn) {
      if (!conn?.linkId || !conn.canExportPrices) {
        displaySonnerError(t("productMarketplaceIntegrations.noLinkedForPriceExport"));
        return;
      }

      this.exportingPricesConnectionId = conn.connectionId;
      await this.exportPrices({
        connectionId: conn.connectionId,
        linkIds: [conn.linkId],
        onSuccess: () => {
          displaySonnerSuccess(
            t("productMarketplaceIntegrations.priceExportedToAdapter", { adapter: conn.adapterName }),
          );
          this.fetchData();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.exportingPricesConnectionId = null;
    },

    async handleExportStocksToConnection(conn) {
      if (!conn?.linkId || !conn.canExportStocks) {
        displaySonnerError(t("productMarketplaceIntegrations.noLinkedForStockExport"));
        return;
      }

      this.exportingStocksConnectionId = conn.connectionId;
      await this.exportStocks({
        connectionId: conn.connectionId,
        linkIds: [conn.linkId],
        onSuccess: () => {
          displaySonnerSuccess(
            t("productMarketplaceIntegrations.stockExportedToAdapter", { adapter: conn.adapterName }),
          );
          this.fetchData();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.exportingStocksConnectionId = null;
    },

    openRecreateDialog(conn) {
      this.recreateConn = conn;
      this.recreateNewSku = "";
      this.showRecreateDialog = true;
    },

    async submitRecreate() {
      if (!this.recreateConn || !this.recreateNewSku.trim()) return;
      this.isRecreating = true;
      await this.recreateProductOnMarketplace({
        linkId: this.recreateConn.linkId,
        newSku: this.recreateNewSku.trim(),
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          let msg = t("productMarketplaceIntegrations.productRecreated");
          if (result.archiveError) {
            msg += t("productMarketplaceIntegrations.archiveErrorSuffix", { error: result.archiveError });
          }
          displaySonnerSuccess(msg);
          this.showRecreateDialog = false;
          this.recreateConn = null;
          this.fetchData();
          this.$emit("product-updated");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isRecreating = false;
    },

    openLinkDialog(conn, isRelink = false) {
      this.linkConn = conn;
      this.linkExternalId = isRelink ? conn.externalProductId || "" : "";
      this.isRelinking = isRelink;
      this.showLinkDialog = true;
    },

    async submitLink() {
      if (!this.linkConn || !this.linkExternalId.trim()) return;
      this.isLinking = true;
      if (this.isRelinking) {
        await this.relinkProduct({
          linkId: this.linkConn.linkId,
          externalProductId: this.linkExternalId.trim(),
          onSuccess: () => {
            displaySonnerSuccess(t("productMarketplaceIntegrations.productRelinked", { adapter: this.linkConn.adapterName }));
            this.showLinkDialog = false;
            this.linkConn = null;
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } else {
        await this.linkProduct({
          connectionId: this.linkConn.connectionId,
          productId: this.productId,
          externalProductId: this.linkExternalId.trim(),
          onSuccess: () => {
            displaySonnerSuccess(t("productMarketplaceIntegrations.productLinked", { adapter: this.linkConn.adapterName }));
            this.showLinkDialog = false;
            this.linkConn = null;
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      }
      this.isLinking = false;
    },

    async handleUnlink(conn) {
      if (!conn.isLinked || !conn.linkId || this.unlinkingLinkId) return;

      const ok = await confirm({
        title: t("productMarketplaceIntegrations.unlinkConfirmTitle"),
        description: t("productMarketplaceIntegrations.unlinkConfirmDescription", { adapter: conn.adapterName }),
        confirmLabel: t("productMarketplaceIntegrations.unlinkConfirmLabel"),
      });
      if (!ok) return;

      this.unlinkingLinkId = conn.linkId;
      try {
        await this.unlinkProduct({
          linkId: conn.linkId,
          onSuccess: () => {
            displaySonnerSuccess(t("productMarketplaceIntegrations.productUnlinked", { adapter: conn.adapterName }));
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.unlinkingLinkId = null;
      }
    },

    async handleVerifyLink(conn) {
      if (!conn.isLinked || !conn.linkId || this.verifyingLinkId) return;

      this.verifyingLinkId = conn.linkId;
      try {
        await this.verifyLink({
          linkId: conn.linkId,
          onSuccess: (response) => {
            const result = extractResponseData(response, {});
            if (result.broken > 0 || result.status === "RemoteMissing") {
              displaySonnerError(t("productMarketplaceIntegrations.verifyBroken"));
            } else {
              displaySonnerSuccess(t("productMarketplaceIntegrations.verifySuccess"));
            }
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error || t("productMarketplaceIntegrations.verifyError"));
          },
        });
      } finally {
        this.verifyingLinkId = null;
      }
    },

    getSelectedMapping(conn) {
      const selectedId = this.mappingSelections[conn.connectionId];
      if (!selectedId || !conn.availableMappings) return null;
      return conn.availableMappings.find((m) => m.id === selectedId) || null;
    },

    getSelectedMappingName(conn) {
      const m = this.getSelectedMapping(conn);
      return m ? (m.name || m.externalCategoryName || t("productMarketplaceIntegrations.mappingFallback", { id: m.id })) : null;
    },

    getMappingRoute(conn) {
      const m = this.getSelectedMapping(conn);
      if (!m) return null;
      return {
        name: PAGE_MARKETPLACE_FIELD_MAPPING,
        params: { connId: conn.connectionId, mappingId: m.id },
        query: { previewProduct: String(this.productId) },
      };
    },

    isConnectionBusy(conn) {
      if (this.exportingConnectionId === conn.connectionId) return true;
      if (this.exportingPricesConnectionId === conn.connectionId) return true;
      if (this.exportingStocksConnectionId === conn.connectionId) return true;
      if (this.isExportingAll && conn.hasMapping && conn.canExport && !conn.mappingMissing) return true;
      if (this.isExportingPricesAll && conn.isLinked && conn.canExportPrices) return true;
      if (this.isExportingStocksAll && conn.isLinked && conn.canExportStocks) return true;
      if (conn.isLinked) {
        if (this.importingLinkId === conn.linkId) return true;
        if (this.unlinkingLinkId === conn.linkId) return true;
        if (this.verifyingLinkId === conn.linkId) return true;
        if (this.isApplyingImport && this.previewLinkId === conn.linkId) return true;
        if (this.isRecreating && this.recreateConn?.connectionId === conn.connectionId) return true;
      }
      return false;
    },

    getMarketplaceProductUrl(conn) {
      if (!conn.externalProductId || !conn.productEditUrlTemplate) return null;
      const publicCreds = conn.publicCredentials || conn.PublicCredentials || {};
      return fillProductEditUrl(conn.productEditUrlTemplate, {
        ...publicCreds,
        externalId: conn.externalProductId,
      });
    },

    getConnectionStatusInfo(conn) {
      if (conn.mappingMissing) {
        return {
          variant: "destructive",
          icon: AlertTriangle,
          label: t("productMarketplaceIntegrations.noMapping"),
          color: "text-error",
        };
      }
      return this.getStatusInfo(conn.linkStatus);
    },

    getStatusInfo(status) {
      switch (status) {
        case "Synced":
          return {
            variant: "default",
            icon: CheckCircle,
            label: t("productMarketplaceIntegrations.statusSynced"),
            color: "text-success",
          };
        case "NotSynced":
          return {
            variant: "secondary",
            icon: AlertTriangle,
            label: t("productMarketplaceIntegrations.statusNotSynced"),
            color: "text-muted-foreground",
          };
        case "SyncError":
          return {
            variant: "destructive",
            icon: XCircle,
            label: t("productMarketplaceIntegrations.statusError"),
            color: "text-error",
          };
        case "Modified":
          return {
            variant: "outline",
            icon: AlertTriangle,
            label: t("productMarketplaceIntegrations.statusModified"),
            color: "text-warning",
          };
        case "RemoteMissing":
          return {
            variant: "destructive",
            icon: Unlink,
            label: t("productMarketplaceIntegrations.statusBroken"),
            color: "text-error",
          };
        default:
          return {
            variant: "secondary",
            icon: AlertTriangle,
            label: status,
            color: "text-muted-foreground",
          };
      }
    },

    formatDate(date) {
      if (!date) return "-";
      const d = new Date(date);
      if (Number.isNaN(d.getTime())) return "-";
      const now = new Date();
      const diffMs = now - d;
      const diffMins = Math.floor(diffMs / 60000);

      if (diffMins < 1) return t("productMarketplaceIntegrations.justNow");
      if (diffMins < 60) return t("productMarketplaceIntegrations.minutesAgo", { count: diffMins });
      if (diffMins < 1440) return t("productMarketplaceIntegrations.hoursAgo", { count: Math.floor(diffMins / 60) });
      return this.formatStoreDate(date);
    },
  },
  watch: {
    productId() {
      this.connections = null;
      this.variantsData = {};
      this.mappingSelections = {};
      this.fetchData();
    },
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <!-- Header -->
    <div class="flex h-11 shrink-0 items-center border-b px-4">
      <p class="flex items-center gap-2 text-sm font-medium">
        <Link2 class="size-4" />
        {{ $t("productMarketplaceIntegrations.header") }}
      </p>
    </div>

    <div
      v-if="isLoading"
      class="flex min-h-0 flex-1 items-center justify-center py-12"
    >
      <Spinner size="2xl" class="text-muted-foreground" />
    </div>

    <!-- No category warning -->
    <EmptyState
      v-else-if="connections && !connections.categoryId"
      :framed="false"
      :title="$t('productMarketplaceIntegrations.noCategoryTitle')"
      :description="$t('productMarketplaceIntegrations.noCategoryDescription')"
    >
      <template #icon>
        <AlertTriangle />
      </template>
    </EmptyState>

    <!-- No connections -->
    <EmptyState
      v-else-if="!connections?.allConnections?.length"
      :framed="false"
      :title="$t('productMarketplaceIntegrations.noConnectionsTitle')"
      :description="$t('productMarketplaceIntegrations.noConnectionsDescription')"
    >
      <template #icon>
        <Link2 />
      </template>
      <Button variant="outline" class="mt-2" @click="$router.push(PAGE_MARKETPLACE)">
        <Settings class="mr-2 size-4" />
        {{ $t("productMarketplaceIntegrations.goToConnections") }}
      </Button>
    </EmptyState>

    <!-- Connection cards -->
    <div v-else class="min-h-0 flex-1 space-y-6 overflow-y-auto p-4">
      <!-- Export all banner -->
      <div
        v-if="connectionsWithMapping.length > 0"
        class="flex items-center justify-between gap-4 rounded-lg border bg-muted/40 px-4 py-3"
      >
        <div class="flex items-center gap-3 min-w-0">
          <div class="size-8 rounded-md bg-primary/10 flex items-center justify-center shrink-0">
            <Upload class="size-4 text-primary" />
          </div>
          <div class="min-w-0">
            <div class="text-sm font-medium">{{ $t("productMarketplaceIntegrations.exportToAllTitle") }}</div>
            <div class="text-xs text-muted-foreground">
              {{
                connectionsWithMapping.length < 5
                  ? $t("productMarketplaceIntegrations.readyCountFew", { count: connectionsWithMapping.length })
                  : $t("productMarketplaceIntegrations.readyCountMany", { count: connectionsWithMapping.length })
              }}
            </div>
          </div>
        </div>
        <ButtonGroup class="border-0 shadow-none [&_[data-slot=button]]:border-0 [&_[data-slot=button]]:shadow-none">
          <Button
            size="sm"
            @click="runSelectedExportAll"
            :disabled="isExportingAnyAll || isLoading"
          >
            <Spinner v-if="isExportingAnyAll" class="mr-2" />
            <Upload v-else class="size-4 mr-2" />
            {{ exportAllModeLabel }}
          </Button>
          <DropdownMenu>
            <DropdownMenuTrigger as-child>
              <Button
                size="sm"
                class="px-2"
                :disabled="isExportingAnyAll || isLoading"
                :aria-label="$t('productMarketplaceIntegrations.menuExportMode')"
              >
                <ChevronRight class="size-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" class="w-52">
              <DropdownMenuRadioGroup
                :model-value="exportAllMode"
                @update:model-value="setExportMode('all', $event)"
              >
                <DropdownMenuRadioItem value="full">
                  {{ $t("productMarketplaceIntegrations.menuExportFull") }}
                </DropdownMenuRadioItem>
                <DropdownMenuRadioItem
                  value="price"
                  :disabled="linkedForPriceExport.length === 0"
                >
                  {{ $t("productMarketplaceIntegrations.menuExportPrice") }}
                </DropdownMenuRadioItem>
                <DropdownMenuRadioItem
                  value="stock"
                  :disabled="linkedForStockExport.length === 0"
                >
                  {{ $t("productMarketplaceIntegrations.menuExportStock") }}
                </DropdownMenuRadioItem>
              </DropdownMenuRadioGroup>
            </DropdownMenuContent>
          </DropdownMenu>
        </ButtonGroup>
      </div>

      <!-- Linked connections (already exported) -->
      <div
        v-for="conn in linkedConnections"
        :key="conn.connectionId"
        class="relative border rounded-lg"
      >
        <div class="flex items-center gap-3 px-4 py-3">
        <div v-if="isConnectionBusy(conn)" class="absolute inset-0 bg-background/60 flex items-center justify-center rounded-lg z-10">
          <Spinner size="lg" class="text-primary" />
        </div>
        <a
          v-if="getMarketplaceProductUrl(conn)"
          :href="getMarketplaceProductUrl(conn)"
          target="_blank"
          rel="noopener noreferrer"
          class="size-9 rounded-lg bg-muted flex items-center justify-center shrink-0 hover:bg-muted/80 transition-colors"
          :title="$t('productMarketplaceIntegrations.openOnAdapter')"
        >
          <AdapterIcon :adapter-key="conn.adapterKey" class="size-5" />
        </a>
        <div v-else class="size-9 rounded-lg bg-muted flex items-center justify-center shrink-0">
          <AdapterIcon :adapter-key="conn.adapterKey" class="size-5" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-2 mt-0.5">
            <code class="bg-muted px-1.5 py-0.5 rounded text-xs text-muted-foreground">{{ conn.externalProductId }}</code>
            <span v-if="conn.externalSku" class="text-xs text-muted-foreground">
              {{ $t("productMarketplaceIntegrations.skuPrefix", { sku: conn.externalSku }) }}
            </span>
            <router-link
              v-if="getMappingRoute(conn)"
              :to="getMappingRoute(conn)"
              class="text-xs text-primary hover:underline truncate"
            >
              {{ getSelectedMappingName(conn) }}
            </router-link>
            <span v-else-if="conn.mappingMissing" class="text-xs text-destructive">
              {{ $t("productMarketplaceIntegrations.mappingDeleted") }}
            </span>
          </div>
        </div>
        <Badge :variant="getConnectionStatusInfo(conn).variant" class="shrink-0">
          <component
            :is="getConnectionStatusInfo(conn).icon"
            v-if="getConnectionStatusInfo(conn).icon"
            class="size-3 mr-1"
          />
          {{ getConnectionStatusInfo(conn).label }}
        </Badge>
        <TooltipProvider v-if="connections?.hasSizes">
          <Tooltip>
            <TooltipTrigger as-child>
              <Button variant="ghost" size="icon" class="size-8 shrink-0" @click="toggleVariants(conn)">
                <Ruler class="size-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent side="top">
              <p>{{ $t("productMarketplaceIntegrations.sizesTooltip") }}</p>
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button
              variant="ghost"
              size="icon"
              class="size-8 shrink-0"
              :aria-label="$t('common.actions')"
            >
              <EllipsisVertical class="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-56">
            <DropdownMenuLabel class="font-normal">
              <div class="flex flex-col gap-0.5">
                <span class="text-sm font-medium text-foreground truncate">{{ conn.adapterName }}</span>
                <span class="text-xs text-muted-foreground font-normal truncate tabular-nums">
                  {{ conn.externalProductId }}
                  <template v-if="conn.connectionName"> · {{ conn.connectionName }}</template>
                </span>
              </div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />

            <DropdownMenuGroup>
              <DropdownMenuLabel class="text-xs text-muted-foreground font-medium">
                {{ $t("productMarketplaceIntegrations.menuSectionSync") }}
              </DropdownMenuLabel>
              <div class="flex items-stretch">
                <DropdownMenuItem
                  class="flex-1"
                  @click="runSelectedExport(conn)"
                  :disabled="isSelectedExportBusy(conn) || conn.mappingMissing"
                >
                  <Upload />
                  {{ getExportModeLabel(resolveExportMode(conn)) }}
                </DropdownMenuItem>
                <DropdownMenuSub>
                  <DropdownMenuSubTrigger
                    class="w-8 shrink-0 justify-center px-0 [&>svg:last-child]:ml-0"
                    :disabled="conn.mappingMissing"
                    :aria-label="$t('productMarketplaceIntegrations.menuExportMode')"
                  />
                  <DropdownMenuSubContent class="w-52">
                    <DropdownMenuRadioGroup
                      :model-value="resolveExportMode(conn)"
                      @update:model-value="setExportMode(conn, $event)"
                    >
                      <DropdownMenuRadioItem value="full">
                        <Package />
                        {{ $t("productMarketplaceIntegrations.menuExportFull") }}
                      </DropdownMenuRadioItem>
                      <DropdownMenuRadioItem
                        v-if="conn.canExportPrices"
                        value="price"
                      >
                        <DollarSign />
                        {{ $t("productMarketplaceIntegrations.menuExportPrice") }}
                      </DropdownMenuRadioItem>
                      <DropdownMenuRadioItem
                        v-if="conn.canExportStocks"
                        value="stock"
                      >
                        <Boxes />
                        {{ $t("productMarketplaceIntegrations.menuExportStock") }}
                      </DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                  </DropdownMenuSubContent>
                </DropdownMenuSub>
              </div>
              <DropdownMenuItem
                v-if="conn.canImport"
                @click="handleImportFromLink(conn)"
                :disabled="importingLinkId === conn.linkId"
              >
                <Download />
                {{ $t("productMarketplaceIntegrations.menuImport") }}
              </DropdownMenuItem>
            </DropdownMenuGroup>

            <DropdownMenuSeparator />

            <DropdownMenuGroup>
              <DropdownMenuLabel class="text-xs text-muted-foreground font-medium">
                {{ $t("productMarketplaceIntegrations.menuSectionLink") }}
              </DropdownMenuLabel>
              <DropdownMenuItem
                @click="handleVerifyLink(conn)"
                :disabled="verifyingLinkId === conn.linkId || !conn.canImport"
              >
                <Spinner v-if="verifyingLinkId === conn.linkId" />
                <SearchCheck v-else />
                {{ $t("productMarketplaceIntegrations.menuVerify") }}
              </DropdownMenuItem>
              <DropdownMenuItem @click="openLinkDialog(conn, true)">
                <Link2 />
                {{ $t("productMarketplaceIntegrations.menuRelink") }}
              </DropdownMenuItem>
              <DropdownMenuSub
                v-if="conn.availableMappings?.length > 1 || (conn.mappingMissing && conn.availableMappings?.length > 0)"
              >
                <DropdownMenuSubTrigger>
                  <Layers />
                  {{ $t("productMarketplaceIntegrations.menuMapping") }}
                </DropdownMenuSubTrigger>
                <DropdownMenuSubContent class="w-52">
                  <DropdownMenuRadioGroup
                    :model-value="String(mappingSelections[conn.connectionId] ?? '')"
                    @update:model-value="handleMappingChange(conn, Number($event))"
                  >
                    <DropdownMenuRadioItem
                      v-for="m in conn.availableMappings"
                      :key="m.id"
                      :value="String(m.id)"
                      class="truncate"
                    >
                      {{ m.name || m.externalCategoryName || $t("productMarketplaceIntegrations.mappingFallback", { id: m.id }) }}
                    </DropdownMenuRadioItem>
                  </DropdownMenuRadioGroup>
                </DropdownMenuSubContent>
              </DropdownMenuSub>
              <DropdownMenuItem
                v-if="getMarketplaceProductUrl(conn)"
                as-child
              >
                <a
                  :href="getMarketplaceProductUrl(conn)"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <ExternalLink />
                  {{ $t("productMarketplaceIntegrations.openOnAdapter") }}
                </a>
              </DropdownMenuItem>
            </DropdownMenuGroup>

            <DropdownMenuSeparator />

            <DropdownMenuGroup>
              <DropdownMenuLabel class="text-xs text-muted-foreground font-medium">
                {{ $t("productMarketplaceIntegrations.menuSectionDanger") }}
              </DropdownMenuLabel>
              <DropdownMenuItem
                variant="destructive"
                @click="handleUnlink(conn)"
                :disabled="unlinkingLinkId === conn.linkId"
              >
                <Spinner v-if="unlinkingLinkId === conn.linkId" />
                <Unlink v-else />
                {{ $t("productMarketplaceIntegrations.menuUnlink") }}
              </DropdownMenuItem>
              <DropdownMenuItem
                v-if="conn.canArchive && conn.canExport"
                @click="openRecreateDialog(conn)"
              >
                <RefreshCw />
                {{ $t("productMarketplaceIntegrations.menuRecreate") }}
              </DropdownMenuItem>
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
        </div>
        <!-- Variants panel -->
        <div v-if="variantsData[conn.connectionId]" class="border-t px-4 py-2">
          <div v-if="variantsData[conn.connectionId].loading" class="flex items-center gap-2 py-2">
            <Spinner />
            <span class="text-xs text-muted-foreground">{{ $t("productMarketplaceIntegrations.loadingSizes") }}</span>
          </div>
          <div v-else-if="variantsData[conn.connectionId].variants.length === 0" class="py-2">
            <span class="text-xs text-muted-foreground">{{ $t("productMarketplaceIntegrations.noSizesForConnection") }}</span>
          </div>
          <Table v-else>
            <TableHeader>
              <TableRow>
                <TableHead class="text-xs h-8">{{ $t("productMarketplaceIntegrations.sizeHeader") }}</TableHead>
                <TableHead class="text-xs h-8">SKU</TableHead>
                <TableHead class="text-xs h-8">{{ $t("productMarketplaceIntegrations.barcodeHeader") }}</TableHead>
                <TableHead class="text-xs h-8">{{ $t("productMarketplaceIntegrations.priceHeader") }}</TableHead>
                <TableHead class="text-xs h-8">{{ $t("productMarketplaceIntegrations.stockHeader") }}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="v in variantsData[conn.connectionId].variants" :key="v.id">
                <TableCell class="text-xs py-1">{{ v.size || '-' }}</TableCell>
                <TableCell class="text-xs py-1">
                  <code class="bg-muted px-1 rounded">{{ v.sku || '-' }}</code>
                </TableCell>
                <TableCell class="text-xs py-1">
                  <code class="bg-muted px-1 rounded">{{ v.barcode || '-' }}</code>
                </TableCell>
                <TableCell class="text-xs py-1">{{ v.price != null ? v.price : '-' }}</TableCell>
                <TableCell class="text-xs py-1">{{ v.quantity != null ? v.quantity : '-' }}</TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>

      <!-- Ready to export connections (has mapping, not linked) -->
      <div
        v-for="conn in readyToExportConnections"
        :key="conn.connectionId"
        class="relative flex items-center gap-3 border border-dashed rounded-lg px-4 py-3"
      >
        <div v-if="isConnectionBusy(conn)" class="absolute inset-0 bg-background/60 flex items-center justify-center rounded-lg z-10">
          <Spinner size="lg" class="text-primary" />
        </div>
        <div class="size-9 rounded-lg bg-muted flex items-center justify-center shrink-0">
          <AdapterIcon :adapter-key="conn.adapterKey" class="size-5" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-2 mt-0.5">
            <span class="text-xs text-muted-foreground">{{ $t("productMarketplaceIntegrations.notExported") }}</span>
            <router-link
              v-if="getMappingRoute(conn)"
              :to="getMappingRoute(conn)"
              class="text-xs text-primary hover:underline truncate"
            >
              · {{ getSelectedMappingName(conn) }}
            </router-link>
          </div>
        </div>
        <Badge variant="outline" class="text-success border-success shrink-0">
          <CheckCircle class="size-3 mr-1" />
          {{ $t("productMarketplaceIntegrations.ready") }}
        </Badge>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button
              variant="ghost"
              size="icon"
              class="size-8 shrink-0"
              :aria-label="$t('common.actions')"
            >
              <EllipsisVertical class="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" class="w-56">
            <DropdownMenuLabel class="font-normal">
              <div class="flex flex-col gap-0.5">
                <span class="text-sm font-medium text-foreground truncate">{{ conn.adapterName }}</span>
                <span class="text-xs text-muted-foreground font-normal truncate">
                  {{ conn.connectionName }}
                </span>
              </div>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              <DropdownMenuItem
                @click="handleExportToConnection(conn)"
                :disabled="exportingConnectionId === conn.connectionId"
              >
                <Upload />
                {{ $t("productMarketplaceIntegrations.export") }}
              </DropdownMenuItem>
              <DropdownMenuItem @click="openLinkDialog(conn)">
                <Link2 />
                {{ $t("productMarketplaceIntegrations.linkManually") }}
              </DropdownMenuItem>
              <DropdownMenuSub v-if="conn.availableMappings?.length > 1">
                <DropdownMenuSubTrigger>
                  <Layers />
                  {{ $t("productMarketplaceIntegrations.menuMapping") }}
                </DropdownMenuSubTrigger>
                <DropdownMenuSubContent class="w-52">
                  <DropdownMenuRadioGroup
                    :model-value="String(mappingSelections[conn.connectionId] ?? '')"
                    @update:model-value="handleMappingChange(conn, Number($event))"
                  >
                    <DropdownMenuRadioItem
                      v-for="m in conn.availableMappings"
                      :key="m.id"
                      :value="String(m.id)"
                      class="truncate"
                    >
                      {{ m.name || m.externalCategoryName || $t("productMarketplaceIntegrations.mappingFallback", { id: m.id }) }}
                    </DropdownMenuRadioItem>
                  </DropdownMenuRadioGroup>
                </DropdownMenuSubContent>
              </DropdownMenuSub>
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <!-- Connections without mapping - muted "needs setup" state, not an error -->
      <div
        v-for="conn in connectionsWithoutMapping"
        :key="conn.connectionId"
        class="flex items-center gap-3 rounded-lg px-4 py-3 border border-dashed"
      >
        <div class="size-9 rounded-lg bg-muted flex items-center justify-center shrink-0">
          <AdapterIcon :adapter-key="conn.adapterKey" class="size-5" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-1.5 mt-0.5">
            <span class="text-xs text-muted-foreground">{{ $t("productMarketplaceIntegrations.mappingNotConfigured", { category: connections.categoryName }) }}</span>
            <router-link
              :to="{
                name: PAGE_MARKETPLACE_CONNECTION,
                params: { connId: conn.connectionId },
                query: {
                  tab: 'mappings',
                  createMapping: 'true',
                  localCategoryId: connections.categoryId,
                  localCategoryName: connections.categoryName,
                },
              }"
              class="text-xs text-primary hover:underline"
            >
              {{ $t("productMarketplaceIntegrations.configure") }}
            </router-link>
          </div>
        </div>
      </div>

      <!-- Empty state when all connections have issues -->
      <div
        v-if="
          !linkedConnections.length &&
          !readyToExportConnections.length &&
          !connectionsWithoutMapping.length
        "
      >
        <EmptyState
          :framed="false"
          size="sm"
          :title="$t('productMarketplaceIntegrations.noAvailableAdaptersTitle')"
          :description="$t('productMarketplaceIntegrations.noAvailableAdaptersDescription')"
        >
          <template #icon>
            <Link2 />
          </template>
        </EmptyState>
      </div>
    </div>

    <!-- Import Preview Dialog -->
    <FieldChangesPreviewDialog
      v-if="showImportPreviewDialog"
      :open="showImportPreviewDialog"
      :title="$t('productMarketplaceIntegrations.importDialogTitle')"
      :description="$t('productMarketplaceIntegrations.importDialogDescription')"
      :items="importPreviewItems"
      selectable
      :excluded-fields="importExcludedFields"
      :irreversible-fields="importIrreversibleFields"
      :warning="importWarning"
      :confirm-loading="isApplyingImport"
      @update:open="(v) => (showImportPreviewDialog = v)"
      @update:excluded-fields="importExcludedFields = $event"
      @confirm="confirmImport"
      @cancel="resetImportPreview"
    />

    <!-- Bulk Mapping Selection Dialog -->
    <Dialog v-model:open="showBulkMappingDialog">
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>{{ $t("productMarketplaceIntegrations.bulkMappingDialogTitle") }}</DialogTitle>
          <DialogDescription>
            {{ $t("productMarketplaceIntegrations.bulkMappingDialogDescription") }}
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div
            v-for="conn in bulkMappingConflicts"
            :key="conn.connectionId"
            class="flex items-center gap-3"
          >
            <span class="text-sm font-medium w-[120px] shrink-0">{{ conn.adapterName }}</span>
            <Select
              :model-value="String(bulkMappingSelections[conn.connectionId] || '')"
              @update:model-value="(val) => (bulkMappingSelections = { ...bulkMappingSelections, [conn.connectionId]: Number(val) })"
            >
              <SelectTrigger class="h-8 flex-1">
                <SelectValue :placeholder="$t('productMarketplaceIntegrations.choosePlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="m in conn.availableMappings"
                  :key="m.id"
                  :value="String(m.id)"
                >
                  {{ m.name || m.externalCategoryName || $t("productMarketplaceIntegrations.mappingFallback", { id: m.id }) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child><Button variant="outline">{{ $t("common.cancel") }}</Button></DialogClose>
          <Button @click="confirmBulkMappingAndExport" :disabled="!allBulkMappingsSelected()">
            <Upload class="size-4 mr-2" />
            {{ $t("productMarketplaceIntegrations.export") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Recreate Product Dialog -->
    <Dialog v-model:open="showRecreateDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{ $t("productMarketplaceIntegrations.recreateDialogTitle") }}</DialogTitle>
          <DialogDescription>
            {{ $t("productMarketplaceIntegrations.recreateDialogDescription", { adapter: recreateConn?.adapterName }) }}
          </DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <FieldGroup class="gap-4">
            <SettingsFormField
              :label="$t('productMarketplaceIntegrations.newSkuLabel')"
              label-for="recreate-sku"
            >
              <Input
                id="recreate-sku"
                v-model="recreateNewSku"
                maxlength="50"
                :placeholder="$t('productMarketplaceIntegrations.newSkuPlaceholder')"
                @keyup.enter="submitRecreate"
              />
            </SettingsFormField>
          </FieldGroup>
        </div>
        <DialogFooter>
          <DialogClose as-child><Button variant="outline" :disabled="isRecreating">{{ $t("common.cancel") }}</Button></DialogClose>
          <Button @click="submitRecreate" :disabled="isRecreating || !recreateNewSku.trim()">
            <Spinner v-if="isRecreating" class="mr-2" />
            <RefreshCw v-else class="size-4 mr-2" />
            {{ $t("productMarketplaceIntegrations.menuRecreate") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Manual Link Dialog -->
    <Dialog v-model:open="showLinkDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{ isRelinking ? $t("productMarketplaceIntegrations.relinkProductTitle") : $t("productMarketplaceIntegrations.linkProductTitle") }}</DialogTitle>
          <DialogDescription>
            {{
              isRelinking
                ? $t("productMarketplaceIntegrations.relinkDescription", { adapter: linkConn?.adapterName })
                : $t("productMarketplaceIntegrations.linkDescription", { adapter: linkConn?.adapterName })
            }}
          </DialogDescription>
        </DialogHeader>
        <div class="py-4">
          <FieldGroup class="gap-4">
            <SettingsFormField
              :label="$t('productMarketplaceIntegrations.externalIdLabel')"
              label-for="link-external-id"
            >
              <Input
                id="link-external-id"
                v-model="linkExternalId"
                maxlength="100"
                :placeholder="$t('productMarketplaceIntegrations.externalIdPlaceholder')"
                @keyup.enter="submitLink"
              />
            </SettingsFormField>
          </FieldGroup>
        </div>
        <DialogFooter>
          <DialogClose as-child><Button variant="outline" :disabled="isLinking">{{ $t("common.cancel") }}</Button></DialogClose>
          <Button @click="submitLink" :disabled="isLinking || !linkExternalId.trim()">
            <Spinner v-if="isLinking" class="mr-2" />
            <Link2 v-else class="size-4 mr-2" />
            {{ isRelinking ? $t("productMarketplaceIntegrations.relinkAction") : $t("productMarketplaceIntegrations.linkAction") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
