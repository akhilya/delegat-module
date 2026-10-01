<script>
import { Spinner } from "@/components/ui/spinner";
// ProductMarketplaceIntegrations - Manages marketplace connections for a product: export, import, and sync status.
import { Button } from "@/components/ui/button";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  displayImportFieldsUpdated,
  extractResponseData,
} from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { fillProductEditUrl } from "@/lib/productEditUrl";
import { randomUuidCompact } from "@/utils/uuid";
import { mappingDisplayName } from "@/lib/mappingDisplayName";
import {
  Upload,
  CheckCircle,
  XCircle,
  AlertTriangle,
  Link2,
  Settings,
  RefreshCw,
  EllipsisVertical,
  Unlink,
  ChevronDown,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import Input from "@/components/ui/input/Input.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from "@/components/ui/dropdown-menu";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";
import { ButtonGroup } from "@/components/ui/button-group";
import {
  PAGE_MARKETPLACE,
  PAGE_MARKETPLACE_CONNECTION,
  PAGE_MARKETPLACE_FIELD_MAPPING,
} from "@/router";
import FieldChangesPreviewDialog from "@/components/products/FieldChangesPreviewDialog.vue";
import ImportImagesPreviewDialog from "@/components/products/ImportImagesPreviewDialog.vue";
import ProductAdapterConnectionMenu from "@/components/products/ProductAdapterConnectionMenu.vue";
import ProductAdapterConnectionMenuItems from "@/components/products/ProductAdapterConnectionMenuItems.vue";
import { formatMoney } from "@/lib/currency";
import { displayFieldValue } from "@/lib/fieldDiff";
import { useSelectedStore } from "@/composables/useSelectedStore";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { t } from "@/i18n";
import {
  EXPORT_MODE_FULL,
  EXPORT_MODE_PRICE,
  EXPORT_MODE_STOCK,
  exportModeLabel,
  getConnectionStatusInfo as sharedConnectionStatusInfo,
} from "@/lib/adapterConnectionStatus";
import { dispatchAdapterCardContextMenu } from "@/lib/adapterCardContextMenu";
import { formatAdapterError } from "@/lib/adapterErrorText";
import { getCachedMe } from "@/lib/meCache";
import { canAct, PERMS } from "@/lib/permissions";

export default {
  components: {
    Spinner,
    Button,
    EmptyState,
    AdapterIcon,
    ProductAdapterConnectionMenu,
    ProductAdapterConnectionMenuItems,
    ContextMenu,
    ContextMenuContent,
    ContextMenuTrigger,
    Upload,
    AlertTriangle,
    Link2,
    Settings,
    RefreshCw,
    Input,
    SettingsFormField,
    FieldGroup,
    Dialog,
    DialogClose,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogDescription,
    DialogFooter,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuTrigger,
    DropdownMenuRadioGroup,
    DropdownMenuRadioItem,
    EllipsisVertical,
    ChevronDown,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    ButtonGroup,
    FieldChangesPreviewDialog,
    ImportImagesPreviewDialog,
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
      /** Per-connection export busy flags (parallel exports must not clobber each other). */
      exportingByConnectionId: {},
      exportingPricesByConnectionId: {},
      exportingStocksByConnectionId: {},
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
      // Import photos preview
      showImportImagesDialog: false,
      importImagesPreview: null,
      isApplyingImportImages: false,
      importImagesLinkId: null,
      importImagesConnection: null,
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
      return (
        this.isExportingAll ||
        this.isExportingPricesAll ||
        this.isExportingStocksAll
      );
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
      return this.linkedConnections.filter(
        (c) => c.canExportPrices && c.linkId,
      );
    },
    linkedForStockExport() {
      return this.linkedConnections.filter(
        (c) => c.canExportStocks && c.linkId,
      );
    },
    importPreviewItems() {
      if (!this.importPreviewData?.updatedFields) return [];
      return [
        {
          fields: this.importPreviewData.updatedFields.map((field) => ({
            key: field,
            label: this.getFieldLabel(field),
            oldValue: this.formatFieldValue(
              field,
              this.importPreviewData.oldValues[field],
            ),
            newValue: this.formatFieldValue(
              field,
              this.importPreviewData.newValues[field],
            ),
          })),
        },
      ];
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
    mappingDisplayName,
    ...mapActions([
      "getAvailableConnectionsForProduct",
      "exportProduct",
      "importFromLink",
      "importFromLinkPreview",
      "importImagesFromLinkPreview",
      "importImagesFromLink",
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

    onAdapterCardClick(event) {
      if (!this.canManageAdapters) return;
      dispatchAdapterCardContextMenu(event);
    },
    formatAdapterError,

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
      this.mappingSelections = {
        ...this.mappingSelections,
        [conn.connectionId]: mappingId,
      };
      try {
        await this.saveMappingSelections({
          connectionId: conn.connectionId,
          selections: [
            { productId: this.productId, categoryMappingId: mappingId },
          ],
        });
        await this.fetchData();
      } catch {
        displaySonnerError(
          t("productMarketplaceIntegrations.errorSavingMapping"),
        );
      }
    },

    async loadVariants(connectionId) {
      this.variantsData = {
        ...this.variantsData,
        [connectionId]: {
          loading: true,
          variants: this.variantsData[connectionId]?.variants || [],
        },
      };
      try {
        const data = await this.getProductVariants({
          productId: this.productId,
        });
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
        displaySonnerSuccess(
          t("productMarketplaceIntegrations.noExportableConnections"),
        );
        return;
      }

      const toLink = allExportable.filter((c) => !c.isLinked);

      // Check for unlinked connections with multiple mappings but no selection
      const conflicts = toLink.filter(
        (c) =>
          c.availableMappings?.length > 1 &&
          !this.mappingSelections[c.connectionId],
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
          ? t("productMarketplaceIntegrations.exportTitleLinked", {
              id: this.productId,
            })
          : t("productMarketplaceIntegrations.exportTitleUnlinked", {
              id: this.productId,
            });
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
        displaySonnerSuccess(
          t("productMarketplaceIntegrations.exportedToAdapters", {
            count: successCount,
          }),
        );
      }
      if (errorCount > 0) {
        displaySonnerError(
          t("productMarketplaceIntegrations.exportErrors", {
            count: errorCount,
          }),
        );
      }

      this.isExportingAll = false;
      await this.fetchData();
    },

    async handleExportPricesToAll() {
      const targets = this.linkedForPriceExport;
      if (targets.length === 0) {
        displaySonnerError(
          t("productMarketplaceIntegrations.noLinkedForPriceExport"),
        );
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
          t("productMarketplaceIntegrations.exportedPricesToAdapters", {
            count: successCount,
          }),
        );
      }
      if (errorCount > 0) {
        displaySonnerError(
          t("productMarketplaceIntegrations.exportErrors", {
            count: errorCount,
          }),
        );
      }

      this.isExportingPricesAll = false;
      await this.fetchData();
    },

    async handleExportStocksToAll() {
      const targets = this.linkedForStockExport;
      if (targets.length === 0) {
        displaySonnerError(
          t("productMarketplaceIntegrations.noLinkedForStockExport"),
        );
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
          t("productMarketplaceIntegrations.exportedStocksToAdapters", {
            count: successCount,
          }),
        );
      }
      if (errorCount > 0) {
        displaySonnerError(
          t("productMarketplaceIntegrations.exportErrors", {
            count: errorCount,
          }),
        );
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
          this.mappingSelections = {
            ...this.mappingSelections,
            [conn.connectionId]: mappingId,
          };
          try {
            await this.saveMappingSelections({
              connectionId: conn.connectionId,
              selections: [
                { productId: this.productId, categoryMappingId: mappingId },
              ],
            });
          } catch {
            displaySonnerError(
              t("productMarketplaceIntegrations.errorSavingMappingFor", {
                adapter: conn.adapterName,
              }),
            );
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
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.noChangesFound"),
            );
          } else {
            this.importPreviewData = result;
            this.importExcludedFields = new Set(
              result.irreversibleFields || [],
            );
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
        title: t("productMarketplaceIntegrations.importTitle", {
          id: this.productId,
        }),
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
            displayImportFieldsUpdated(result.updatedFields);
            this.$emit("product-updated");
          } else {
            displayImportFieldsUpdated([]);
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

    async handleImportImagesFromLink(conn) {
      this.importingLinkId = conn.linkId;
      this.isLoadingPreview = true;
      await this.importImagesFromLinkPreview({
        linkId: conn.linkId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          const images = result.images || [];
          if (images.length === 0) {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.importImagesNoneOnAdapter"),
            );
          } else {
            this.importImagesPreview = result;
            this.importImagesLinkId = conn.linkId;
            this.importImagesConnection = conn;
            this.showImportImagesDialog = true;
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoadingPreview = false;
      this.importingLinkId = null;
    },

    async confirmImportImages(urls) {
      if (!this.importImagesLinkId) return;
      this.isApplyingImportImages = true;
      const operationId = randomUuidCompact();
      const conn = this.importImagesConnection;
      await this.$store.dispatch("operations/startOperation", {
        id: operationId,
        type: "image-download",
        title: t("productMarketplaceIntegrations.importImagesOpTitle", {
          id: this.productId,
        }),
        connectionId: conn?.connectionId || null,
        connectionName: conn?.connectionName || null,
        batchType: "image-download",
      });

      await this.importImagesFromLink({
        linkId: this.importImagesLinkId,
        urls,
        operationId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          const queued = result.queued ?? 0;
          if (queued > 0) {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.importImagesQueuedToast", {
                count: queued,
              }),
            );
            this.$emit("product-updated");
          } else {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.importImagesNoneSelected"),
            );
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isApplyingImportImages = false;
      this.cancelImportImages();
    },

    cancelImportImages() {
      this.showImportImagesDialog = false;
      this.importImagesPreview = null;
      this.importImagesLinkId = null;
      this.importImagesConnection = null;
    },

    resetImportImagesPreview() {
      this.importImagesPreview = null;
      this.importImagesLinkId = null;
      this.importImagesConnection = null;
    },

    getFieldLabel(field) {
      if (field.startsWith("attr:")) return field.substring(5);
      const known = [
        "name",
        "description",
        "price",
        "storageQuantity",
        "barcode",
      ];
      return known.includes(field)
        ? t(`productMarketplaceIntegrations.fieldLabels.${field}`)
        : field;
    },

    formatFieldValue(field, value) {
      if (value === null || value === undefined) return null;
      if (field === "price") {
        return formatMoney(value, this.storeBaseCurrency);
      }
      return displayFieldValue(value, "");
    },

    resolveExportMode(target) {
      let mode;
      if (target === "all") {
        mode = this.connections?.preferredExportAllMode;
      } else {
        mode = target.preferredExportMode;
      }
      if (
        mode !== EXPORT_MODE_PRICE &&
        mode !== EXPORT_MODE_STOCK &&
        mode !== EXPORT_MODE_FULL
      ) {
        mode = EXPORT_MODE_FULL;
      }

      if (target === "all") {
        if (
          mode === EXPORT_MODE_PRICE &&
          this.linkedForPriceExport.length === 0
        ) {
          return EXPORT_MODE_FULL;
        }
        if (
          mode === EXPORT_MODE_STOCK &&
          this.linkedForStockExport.length === 0
        ) {
          return EXPORT_MODE_FULL;
        }
        return mode;
      }

      if (mode === EXPORT_MODE_PRICE && !target.canExportPrices)
        return EXPORT_MODE_FULL;
      if (mode === EXPORT_MODE_STOCK && !target.canExportStocks)
        return EXPORT_MODE_FULL;
      return mode;
    },

    getExportModeLabel(mode) {
      return exportModeLabel(mode);
    },

    async setExportMode(target, mode) {
      if (target === "all") {
        const prev = this.connections?.preferredExportAllMode;
        if (this.connections) {
          this.connections = {
            ...this.connections,
            preferredExportAllMode: mode,
          };
        }
        await this.setProductPreferredExportAllMode({
          productId: this.productId,
          mode,
          onSuccess: () => {},
          onError: (error) => {
            if (this.connections) {
              this.connections = {
                ...this.connections,
                preferredExportAllMode: prev,
              };
            }
            displaySonnerError(error);
          },
        });
        return;
      }

      const prev = target.preferredExportMode;
      const updated = (this.connections?.allConnections || []).map((c) =>
        c.connectionId === target.connectionId
          ? { ...c, preferredExportMode: mode }
          : c,
      );
      this.connections = { ...this.connections, allConnections: updated };

      await this.setLinkPreferredExportMode({
        linkId: target.linkId,
        mode,
        onSuccess: () => {},
        onError: (error) => {
          const reverted = (this.connections?.allConnections || []).map((c) =>
            c.connectionId === target.connectionId
              ? { ...c, preferredExportMode: prev }
              : c,
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
        return !!this.exportingPricesByConnectionId[conn.connectionId];
      }
      if (mode === EXPORT_MODE_STOCK) {
        return !!this.exportingStocksByConnectionId[conn.connectionId];
      }
      return !!this.exportingByConnectionId[conn.connectionId];
    },

    setConnectionBusyFlag(mapKey, connectionId, busy) {
      const next = { ...this[mapKey] };
      if (busy) next[connectionId] = true;
      else delete next[connectionId];
      this[mapKey] = next;
    },

    // Export to a specific marketplace
    async handleExportToConnection(conn) {
      if (conn.mappingMissing) {
        displaySonnerError(
          t("productMarketplaceIntegrations.mappingDeletedError"),
        );
        return;
      }

      if (
        conn.availableMappings?.length > 1 &&
        !this.mappingSelections[conn.connectionId]
      ) {
        displaySonnerError(
          t("productMarketplaceIntegrations.chooseMappingBeforeExport"),
        );
        return;
      }

      if (this.exportingByConnectionId[conn.connectionId]) return;
      this.setConnectionBusyFlag(
        "exportingByConnectionId",
        conn.connectionId,
        true,
      );

      try {
        const operationId = randomUuidCompact();
        const title = conn.isLinked
          ? t("productMarketplaceIntegrations.exportTitleLinked", {
              id: this.productId,
            })
          : t("productMarketplaceIntegrations.exportTitleUnlinked", {
              id: this.productId,
            });
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
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.productUpdatedOnAdapter", {
                  adapter: conn.adapterName,
                }),
              );
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
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.productExportedToAdapter", {
                  adapter: conn.adapterName,
                }),
              );
              this.fetchData();
            },
            onError: (error) => {
              displaySonnerError(error);
            },
          });
        }
      } finally {
        this.setConnectionBusyFlag(
          "exportingByConnectionId",
          conn.connectionId,
          false,
        );
      }
    },

    async handleExportPricesToConnection(conn) {
      if (!conn?.linkId || !conn.canExportPrices) {
        displaySonnerError(
          t("productMarketplaceIntegrations.noLinkedForPriceExport"),
        );
        return;
      }

      if (this.exportingPricesByConnectionId[conn.connectionId]) return;
      this.setConnectionBusyFlag(
        "exportingPricesByConnectionId",
        conn.connectionId,
        true,
      );
      try {
        await this.exportPrices({
          connectionId: conn.connectionId,
          linkIds: [conn.linkId],
          onSuccess: () => {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.priceExportedToAdapter", {
                adapter: conn.adapterName,
              }),
            );
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.setConnectionBusyFlag(
          "exportingPricesByConnectionId",
          conn.connectionId,
          false,
        );
      }
    },

    async handleExportStocksToConnection(conn) {
      if (!conn?.linkId || !conn.canExportStocks) {
        displaySonnerError(
          t("productMarketplaceIntegrations.noLinkedForStockExport"),
        );
        return;
      }

      if (this.exportingStocksByConnectionId[conn.connectionId]) return;
      this.setConnectionBusyFlag(
        "exportingStocksByConnectionId",
        conn.connectionId,
        true,
      );
      try {
        await this.exportStocks({
          connectionId: conn.connectionId,
          linkIds: [conn.linkId],
          onSuccess: () => {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.stockExportedToAdapter", {
                adapter: conn.adapterName,
              }),
            );
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.setConnectionBusyFlag(
          "exportingStocksByConnectionId",
          conn.connectionId,
          false,
        );
      }
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
            msg += t("productMarketplaceIntegrations.archiveErrorSuffix", {
              error: result.archiveError,
            });
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
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.productRelinked", {
                adapter: this.linkConn.adapterName,
              }),
            );
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
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.productLinked", {
                adapter: this.linkConn.adapterName,
              }),
            );
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
        description: t(
          "productMarketplaceIntegrations.unlinkConfirmDescription",
          { adapter: conn.adapterName },
        ),
        confirmLabel: t("productMarketplaceIntegrations.unlinkConfirmLabel"),
      });
      if (!ok) return;

      this.unlinkingLinkId = conn.linkId;
      try {
        await this.unlinkProduct({
          linkId: conn.linkId,
          onSuccess: () => {
            displaySonnerSuccess(
              t("productMarketplaceIntegrations.productUnlinked", {
                adapter: conn.adapterName,
              }),
            );
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
              displaySonnerError(
                t("productMarketplaceIntegrations.verifyBroken"),
              );
            } else {
              displaySonnerSuccess(
                t("productMarketplaceIntegrations.verifySuccess"),
              );
            }
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(
              error || t("productMarketplaceIntegrations.verifyError"),
            );
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
      return m
        ? mappingDisplayName(
            m,
            t("productMarketplaceIntegrations.mappingFallback", { id: m.id }),
          )
        : null;
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

    configureMappingTo(conn) {
      return {
        path: `/adapter/${conn.connectionId}`,
        query: {
          tab: "mappings",
          createMapping: "true",
          localCategoryId:
            this.connections?.categoryId != null
              ? String(this.connections.categoryId)
              : undefined,
          localCategoryName: this.connections?.categoryName || undefined,
        },
      };
    },

    isConnectionBusy(conn) {
      if (this.exportingByConnectionId[conn.connectionId]) return true;
      if (this.exportingPricesByConnectionId[conn.connectionId]) return true;
      if (this.exportingStocksByConnectionId[conn.connectionId]) return true;
      if (
        this.isExportingAll &&
        conn.hasMapping &&
        conn.canExport &&
        !conn.mappingMissing
      )
        return true;
      if (this.isExportingPricesAll && conn.isLinked && conn.canExportPrices)
        return true;
      if (this.isExportingStocksAll && conn.isLinked && conn.canExportStocks)
        return true;
      if (conn.isLinked) {
        if (this.importingLinkId === conn.linkId) return true;
        if (this.unlinkingLinkId === conn.linkId) return true;
        if (this.verifyingLinkId === conn.linkId) return true;
        if (this.isApplyingImport && this.previewLinkId === conn.linkId)
          return true;
        if (
          this.isRecreating &&
          this.recreateConn?.connectionId === conn.connectionId
        )
          return true;
      }
      return false;
    },

    getMarketplaceProductUrl(conn) {
      if (!conn.externalProductId || !conn.productEditUrlTemplate) return null;
      const publicCreds =
        conn.publicCredentials || conn.PublicCredentials || {};
      return fillProductEditUrl(conn.productEditUrlTemplate, {
        ...publicCreds,
        externalId: conn.externalProductId,
      });
    },

    getConnectionStatusInfo(conn) {
      return sharedConnectionStatusInfo(conn, {
        CheckCircle,
        AlertTriangle,
        XCircle,
        Unlink,
      });
    },

    getStatusInfo(status) {
      return sharedConnectionStatusInfo(
        { linkStatus: status },
        {
          CheckCircle,
          AlertTriangle,
          XCircle,
          Unlink,
        },
      );
    },

    formatDate(date) {
      if (!date) return "-";
      const d = new Date(date);
      if (Number.isNaN(d.getTime())) return "-";
      const now = new Date();
      const diffMs = now - d;
      const diffMins = Math.floor(diffMs / 60000);

      if (diffMins < 1) return t("productMarketplaceIntegrations.justNow");
      if (diffMins < 60)
        return t("productMarketplaceIntegrations.minutesAgo", {
          count: diffMins,
        });
      if (diffMins < 1440)
        return t("productMarketplaceIntegrations.hoursAgo", {
          count: Math.floor(diffMins / 60),
        });
      return this.formatStoreDate(date);
    },

    canManageAdapters() {
      return canAct(getCachedMe(), PERMS.MarketplaceManage);
    },
    headerStatus(conn) {
      if (conn.mappingUnselected) {
        return {
          label: t("productMarketplaceIntegrations.mappingNotSelected"),
          color: "text-destructive",
        };
      }
      if (conn.isLinked) return this.getConnectionStatusInfo(conn);
      if (conn.hasMapping && !conn.mappingMissing) {
        return {
          label: t("productMarketplaceIntegrations.ready"),
          color: "text-success",
        };
      }
      return {
        label: t("productMarketplaceIntegrations.noMapping"),
        color: "text-muted-foreground",
      };
    },

    rowCaption(conn) {
      const bits = [conn.connectionName].filter(Boolean);
      if (conn.isLinked) {
        bits.push(this.formatLastSync(conn));
        if (conn.externalPrice != null && conn.externalPrice !== "") {
          bits.push(this.formatExternalPrice(conn));
        }
        if (conn.externalStock != null && conn.externalStock !== "") {
          bits.push(this.formatExternalStock(conn));
        }
      } else if (conn.hasMapping && !conn.mappingMissing) {
        bits.push(this.$t("productMarketplaceIntegrations.notExported"));
      }
      return bits.join(" · ");
    },

    formatFact(value) {
      if (value == null || value === "") return this.$t("common.emDash");
      return String(value);
    },

    formatExternalPrice(conn) {
      if (conn.externalPrice == null || conn.externalPrice === "") {
        return this.$t("common.emDash");
      }
      return this.formatMoney(conn.externalPrice, this.storeBaseCurrency);
    },

    formatExternalStock(conn) {
      if (conn.externalStock == null || conn.externalStock === "") {
        return this.$t("common.emDash");
      }
      return String(conn.externalStock);
    },

    formatLastSync(conn) {
      if (!conn.lastSyncAt) {
        return t("productMarketplaceIntegrations.neverSynced");
      }
      return this.formatDate(conn.lastSyncAt);
    },

    openMapping(conn) {
      const route = this.getMappingRoute(conn);
      if (route) this.$router.push(route);
    },

    openConfigureMapping(conn) {
      this.$router.push(this.configureMappingTo(conn));
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
  <div>
    <div v-if="isLoading" class="flex items-center justify-center py-12">
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
      :description="
        $t('productMarketplaceIntegrations.noConnectionsDescription')
      "
    >
      <template #icon>
        <Link2 />
      </template>
      <Button
        variant="outline"
        class="mt-2"
        @click="$router.push(PAGE_MARKETPLACE)"
      >
        <Settings class="mr-2 size-4" />
        {{ $t("productMarketplaceIntegrations.goToConnections") }}
      </Button>
    </EmptyState>

    <!-- Connection rows -->
    <div v-else class="space-y-6 px-1">
      <div
        v-if="canManageAdapters && connectionsWithMapping.length > 0"
        class="space-y-2"
      >
        <InsetGroupLabel>{{
          $t("productMarketplaceIntegrations.exportSection")
        }}</InsetGroupLabel>
        <InsetGroup>
          <InsetGroupItem as="div" class="min-h-12 px-3.5 py-2.5">
            <p>{{ $t("productMarketplaceIntegrations.exportToAllTitle") }}</p>
            <p class="mt-0.5 text-xs font-normal text-muted-foreground">
              {{
                connectionsWithMapping.length < 5
                  ? $t("productMarketplaceIntegrations.readyCountFew", {
                      count: connectionsWithMapping.length,
                    })
                  : $t("productMarketplaceIntegrations.readyCountMany", {
                      count: connectionsWithMapping.length,
                    })
              }}
            </p>
            <template #trailing>
              <ButtonGroup
                class="border-0 shadow-none [&_[data-slot=button]]:border-0 [&_[data-slot=button]]:shadow-none"
              >
                <Button
                  size="sm"
                  @click="runSelectedExportAll"
                  :disabled="isExportingAnyAll || isLoading"
                >
                  <Spinner v-if="isExportingAnyAll" class="mr-2" />
                  <Upload v-else class="mr-2 size-4" />
                  {{ exportAllModeLabel }}
                </Button>
                <DropdownMenu>
                  <DropdownMenuTrigger as-child>
                    <Button
                      size="sm"
                      class="px-2"
                      :disabled="isExportingAnyAll || isLoading"
                      :aria-label="
                        $t('productMarketplaceIntegrations.menuExportMode')
                      "
                    >
                      <ChevronDown class="size-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end" class="w-52">
                    <DropdownMenuRadioGroup
                      :model-value="exportAllMode"
                      @update:model-value="setExportMode('all', $event)"
                    >
                      <DropdownMenuRadioItem value="full">
                        {{
                          $t("productMarketplaceIntegrations.menuExportFull")
                        }}
                      </DropdownMenuRadioItem>
                      <DropdownMenuRadioItem
                        value="price"
                        :disabled="linkedForPriceExport.length === 0"
                      >
                        {{
                          $t("productMarketplaceIntegrations.menuExportPrice")
                        }}
                      </DropdownMenuRadioItem>
                      <DropdownMenuRadioItem
                        value="stock"
                        :disabled="linkedForStockExport.length === 0"
                      >
                        {{
                          $t("productMarketplaceIntegrations.menuExportStock")
                        }}
                      </DropdownMenuRadioItem>
                    </DropdownMenuRadioGroup>
                  </DropdownMenuContent>
                </DropdownMenu>
              </ButtonGroup>
            </template>
          </InsetGroupItem>
        </InsetGroup>
      </div>

      <div v-if="linkedConnections.length" class="space-y-2">
        <InsetGroupLabel>{{
          $t("productMarketplaceIntegrations.linkedSection")
        }}</InsetGroupLabel>
        <InsetGroup>
          <template
            v-for="conn in linkedConnections"
            :key="conn.connectionId"
          >
            <ContextMenu>
              <ContextMenuTrigger as-child>
                <InsetGroupItem
                  as="div"
                  class="relative min-h-12 cursor-pointer px-3.5 py-2.5"
                  @click="onAdapterCardClick"
                >
                  <div
                    v-if="isConnectionBusy(conn)"
                    class="absolute inset-0 z-10 flex items-center justify-center rounded-lg bg-background/60"
                  >
                    <Spinner class="text-primary" />
                  </div>
                  <template #leading>
                    <AdapterIcon
                      :adapter-key="conn.adapterKey"
                      class="size-5"
                    />
                  </template>
                  <p class="truncate">{{ conn.adapterName }}</p>
                  <p
                    class="mt-0.5 truncate text-xs font-normal text-muted-foreground"
                  >
                    {{ rowCaption(conn) }}
                  </p>
                  <p
                    v-if="conn.lastErrorMessage"
                    class="mt-0.5 line-clamp-2 text-xs font-normal text-destructive"
                  >
                    {{ formatAdapterError(conn.lastErrorMessage) }}
                  </p>
                  <template #trailing>
                    <div class="flex items-center gap-1">
                      <span
                        class="text-sm"
                        :class="headerStatus(conn).color"
                      >
                        {{ headerStatus(conn).label }}
                      </span>
                      <Button
                        v-if="connections?.hasSizes"
                        variant="ghost"
                        size="icon"
                        class="size-8 shrink-0"
                        :aria-expanded="!!variantsData[conn.connectionId]"
                        :aria-label="
                          variantsData[conn.connectionId]
                            ? $t('productMarketplaceIntegrations.hideSizes')
                            : $t('productMarketplaceIntegrations.showSizes')
                        "
                        @click.stop="toggleVariants(conn)"
                      >
                        <ChevronDown
                          class="size-4 transition-transform duration-[var(--duration-ui)]"
                          :class="
                            variantsData[conn.connectionId]
                              ? 'rotate-180'
                              : null
                          "
                        />
                      </Button>
                      <ProductAdapterConnectionMenu
                        v-if="canManageAdapters || getMarketplaceProductUrl(conn)"
                        :connection="conn"
                        :selected-mapping-id="
                          mappingSelections[conn.connectionId]
                        "
                        :marketplace-url="getMarketplaceProductUrl(conn)"
                        :configure-mapping-to="configureMappingTo(conn)"
                        :category-name="connections?.categoryName"
                        align="end"
                        :export-busy="isSelectedExportBusy(conn)"
                        :import-busy="importingLinkId === conn.linkId"
                        :verify-busy="verifyingLinkId === conn.linkId"
                        :unlink-busy="unlinkingLinkId === conn.linkId"
                        show-relink
                        show-recreate
                        show-danger-section
                        @export="runSelectedExport(conn)"
                        @update:export-mode="setExportMode(conn, $event)"
                        @import="handleImportFromLink(conn)"
                        @import-images="handleImportImagesFromLink(conn)"
                        @verify="handleVerifyLink(conn)"
                        @unlink="handleUnlink(conn)"
                        @update:mapping="handleMappingChange(conn, $event)"
                        @relink="openLinkDialog(conn, true)"
                        @recreate="openRecreateDialog(conn)"
                      >
                        <template #trigger>
                          <Button
                            variant="ghost"
                            size="icon"
                            class="size-8 shrink-0"
                            :aria-label="$t('common.actions')"
                          >
                            <EllipsisVertical class="size-4" />
                          </Button>
                        </template>
                      </ProductAdapterConnectionMenu>
                    </div>
                  </template>
                </InsetGroupItem>
              </ContextMenuTrigger>
              <ContextMenuContent class="w-56">
                <ProductAdapterConnectionMenuItems
                  surface="context"
                  :connection="conn"
                  :selected-mapping-id="mappingSelections[conn.connectionId]"
                  :marketplace-url="getMarketplaceProductUrl(conn)"
                  :configure-mapping-to="configureMappingTo(conn)"
                  :category-name="connections?.categoryName"
                  :export-busy="isSelectedExportBusy(conn)"
                  :import-busy="importingLinkId === conn.linkId"
                  :verify-busy="verifyingLinkId === conn.linkId"
                  :unlink-busy="unlinkingLinkId === conn.linkId"
                  show-relink
                  show-recreate
                  show-danger-section
                  @export="runSelectedExport(conn)"
                  @update:export-mode="setExportMode(conn, $event)"
                  @import="handleImportFromLink(conn)"
                  @import-images="handleImportImagesFromLink(conn)"
                  @verify="handleVerifyLink(conn)"
                  @unlink="handleUnlink(conn)"
                  @update:mapping="handleMappingChange(conn, $event)"
                  @relink="openLinkDialog(conn, true)"
                  @recreate="openRecreateDialog(conn)"
                />
              </ContextMenuContent>
            </ContextMenu>
            <template v-if="variantsData[conn.connectionId]">
              <InsetGroupItem
                v-if="variantsData[conn.connectionId].loading"
                as="div"
                class="min-h-12 px-3.5 py-2.5"
              >
                <span class="flex items-center gap-2 text-muted-foreground">
                  <Spinner />
                  {{ $t("productMarketplaceIntegrations.loadingSizes") }}
                </span>
              </InsetGroupItem>
              <InsetGroupItem
                v-else-if="
                  variantsData[conn.connectionId].variants.length === 0
                "
                as="div"
                class="min-h-12 px-3.5 py-2.5"
              >
                <span class="text-muted-foreground">{{
                  $t("productMarketplaceIntegrations.noSizesForConnection")
                }}</span>
              </InsetGroupItem>
              <template v-else>
                <InsetGroupItem
                  v-for="v in variantsData[conn.connectionId].variants"
                  :key="`${conn.connectionId}-${v.id}`"
                  as="div"
                  class="min-h-12 px-3.5 py-2.5"
                >
                  <p>{{ v.size || $t("common.emDash") }}</p>
                  <p
                    class="mt-0.5 truncate text-xs font-normal text-muted-foreground"
                  >
                    {{ formatFact(v.sku) }}
                    <span class="text-muted-foreground/50"> · </span>
                    {{ formatFact(v.barcode) }}
                  </p>
                  <template #trailing>
                    <span
                      class="text-right font-normal tabular-nums text-muted-foreground"
                    >
                      {{
                        v.price != null
                          ? formatFact(v.price)
                          : $t("common.emDash")
                      }}
                      <span class="text-muted-foreground/50"> · </span>
                      {{
                        v.quantity != null
                          ? formatFact(v.quantity)
                          : $t("common.emDash")
                      }}
                    </span>
                  </template>
                </InsetGroupItem>
              </template>
            </template>
          </template>
        </InsetGroup>
      </div>

      <div v-if="readyToExportConnections.length" class="space-y-2">
        <InsetGroupLabel>{{
          $t("productMarketplaceIntegrations.readySection")
        }}</InsetGroupLabel>
        <InsetGroup>
          <ContextMenu
            v-for="conn in readyToExportConnections"
            :key="conn.connectionId"
          >
            <ContextMenuTrigger as-child>
              <InsetGroupItem
                as="div"
                class="relative min-h-12 cursor-pointer px-3.5 py-2.5"
                @click="onAdapterCardClick"
              >
                <div
                  v-if="isConnectionBusy(conn)"
                  class="absolute inset-0 z-10 flex items-center justify-center rounded-lg bg-background/60"
                >
                  <Spinner class="text-primary" />
                </div>
                <template #leading>
                  <AdapterIcon
                    :adapter-key="conn.adapterKey"
                    class="size-5"
                  />
                </template>
                <p class="truncate">{{ conn.adapterName }}</p>
                <p
                  class="mt-0.5 truncate text-xs font-normal text-muted-foreground"
                >
                  {{ rowCaption(conn) }}
                </p>
                <template #trailing>
                  <div class="flex items-center gap-1">
                    <span class="text-sm" :class="headerStatus(conn).color">
                      {{ headerStatus(conn).label }}
                    </span>
                    <ProductAdapterConnectionMenu
                      v-if="canManageAdapters"
                      :connection="conn"
                      :selected-mapping-id="
                        mappingSelections[conn.connectionId]
                      "
                      :configure-mapping-to="configureMappingTo(conn)"
                      :category-name="connections?.categoryName"
                      align="end"
                      :export-busy="exportingByConnectionId[conn.connectionId]"
                      show-link-manually
                      @export="handleExportToConnection(conn)"
                      @link-manually="openLinkDialog(conn)"
                      @update:mapping="handleMappingChange(conn, $event)"
                    >
                      <template #trigger>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8 shrink-0"
                          :aria-label="$t('common.actions')"
                        >
                          <EllipsisVertical class="size-4" />
                        </Button>
                      </template>
                    </ProductAdapterConnectionMenu>
                  </div>
                </template>
              </InsetGroupItem>
            </ContextMenuTrigger>
            <ContextMenuContent class="w-56">
              <ProductAdapterConnectionMenuItems
                surface="context"
                :connection="conn"
                :selected-mapping-id="mappingSelections[conn.connectionId]"
                :configure-mapping-to="configureMappingTo(conn)"
                :category-name="connections?.categoryName"
                :export-busy="exportingByConnectionId[conn.connectionId]"
                show-link-manually
                @export="handleExportToConnection(conn)"
                @link-manually="openLinkDialog(conn)"
                @update:mapping="handleMappingChange(conn, $event)"
              />
            </ContextMenuContent>
          </ContextMenu>
        </InsetGroup>
      </div>

      <div v-if="connectionsWithoutMapping.length" class="space-y-2">
        <InsetGroupLabel>{{
          $t("productMarketplaceIntegrations.needsMappingSection")
        }}</InsetGroupLabel>
        <InsetGroup>
          <InsetGroupItem
            v-for="conn in connectionsWithoutMapping"
            :key="conn.connectionId"
            :show-chevron="canManageAdapters"
            class="min-h-12 px-3.5 py-2.5"
            @click="canManageAdapters && openConfigureMapping(conn)"
          >
            <template #leading>
              <AdapterIcon :adapter-key="conn.adapterKey" class="size-5" />
            </template>
            <p class="truncate">{{ conn.adapterName }}</p>
            <p
              class="mt-0.5 truncate text-xs font-normal text-muted-foreground"
            >
              {{
                $t("productMarketplaceIntegrations.mappingNotConfigured", {
                  category: connections.categoryName,
                })
              }}
            </p>
            <template #trailing>
              <span class="text-sm" :class="headerStatus(conn).color">
                {{ headerStatus(conn).label }}
              </span>
            </template>
          </InsetGroupItem>
        </InsetGroup>
      </div>

      <EmptyState
        v-if="
          !linkedConnections.length &&
          !readyToExportConnections.length &&
          !connectionsWithoutMapping.length
        "
        :framed="false"
        size="sm"
        :title="$t('productMarketplaceIntegrations.noAvailableAdaptersTitle')"
        :description="
          $t('productMarketplaceIntegrations.noAvailableAdaptersDescription')
        "
      >
        <template #icon>
          <Link2 />
        </template>
      </EmptyState>
    </div>

    <!-- Import Preview Dialog -->
    <FieldChangesPreviewDialog
      v-if="showImportPreviewDialog"
      :open="showImportPreviewDialog"
      :title="$t('productMarketplaceIntegrations.importDialogTitle')"
      :description="
        $t('productMarketplaceIntegrations.importDialogDescription')
      "
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

    <ImportImagesPreviewDialog
      v-if="showImportImagesDialog"
      :open="showImportImagesDialog"
      :images="importImagesPreview?.images || []"
      :local-gallery-count="importImagesPreview?.localGalleryCount || 0"
      :confirm-loading="isApplyingImportImages"
      @update:open="(v) => (showImportImagesDialog = v)"
      @confirm="confirmImportImages"
      @cancel="resetImportImagesPreview"
    />

    <!-- Bulk Mapping Selection Dialog -->
    <Dialog v-model:open="showBulkMappingDialog">
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>{{
            $t("productMarketplaceIntegrations.bulkMappingDialogTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{
              $t("productMarketplaceIntegrations.bulkMappingDialogDescription")
            }}
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-4 py-4">
          <div
            v-for="conn in bulkMappingConflicts"
            :key="conn.connectionId"
            class="flex items-center gap-3"
          >
            <span class="text-sm font-medium w-[120px] shrink-0">{{
              conn.adapterName
            }}</span>
            <Select
              :model-value="
                String(bulkMappingSelections[conn.connectionId] || '')
              "
              @update:model-value="
                (val) =>
                  (bulkMappingSelections = {
                    ...bulkMappingSelections,
                    [conn.connectionId]: Number(val),
                  })
              "
            >
              <SelectTrigger class="h-8 flex-1">
                <SelectValue
                  :placeholder="
                    $t('productMarketplaceIntegrations.choosePlaceholder')
                  "
                />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="m in conn.availableMappings"
                  :key="m.id"
                  :value="String(m.id)"
                >
                  {{
                    mappingDisplayName(
                      m,
                      $t("productMarketplaceIntegrations.mappingFallback", {
                        id: m.id,
                      }),
                    )
                  }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child
            ><Button variant="outline">{{
              $t("common.cancel")
            }}</Button></DialogClose
          >
          <Button
            @click="confirmBulkMappingAndExport"
            :disabled="!allBulkMappingsSelected()"
          >
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
          <DialogTitle>{{
            $t("productMarketplaceIntegrations.recreateDialogTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{
              $t("productMarketplaceIntegrations.recreateDialogDescription", {
                adapter: recreateConn?.adapterName,
              })
            }}
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
                :placeholder="
                  $t('productMarketplaceIntegrations.newSkuPlaceholder')
                "
                @keyup.enter="submitRecreate"
              />
            </SettingsFormField>
          </FieldGroup>
        </div>
        <DialogFooter>
          <DialogClose as-child
            ><Button variant="outline" :disabled="isRecreating">{{
              $t("common.cancel")
            }}</Button></DialogClose
          >
          <Button
            @click="submitRecreate"
            :disabled="isRecreating || !recreateNewSku.trim()"
          >
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
          <DialogTitle>{{
            isRelinking
              ? $t("productMarketplaceIntegrations.relinkProductTitle")
              : $t("productMarketplaceIntegrations.linkProductTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{
              isRelinking
                ? $t("productMarketplaceIntegrations.relinkDescription", {
                    adapter: linkConn?.adapterName,
                  })
                : $t("productMarketplaceIntegrations.linkDescription", {
                    adapter: linkConn?.adapterName,
                  })
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
                :placeholder="
                  $t('productMarketplaceIntegrations.externalIdPlaceholder')
                "
                @keyup.enter="submitLink"
              />
            </SettingsFormField>
          </FieldGroup>
        </div>
        <DialogFooter>
          <DialogClose as-child
            ><Button variant="outline" :disabled="isLinking">{{
              $t("common.cancel")
            }}</Button></DialogClose
          >
          <Button
            @click="submitLink"
            :disabled="isLinking || !linkExternalId.trim()"
          >
            <Spinner v-if="isLinking" class="mr-2" />
            <Link2 v-else class="size-4 mr-2" />
            {{
              isRelinking
                ? $t("productMarketplaceIntegrations.relinkAction")
                : $t("productMarketplaceIntegrations.linkAction")
            }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
