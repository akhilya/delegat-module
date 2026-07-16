<script>
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
import {
  Loader2,
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
  Ban,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
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
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuSubContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from "@/components/ui/dropdown-menu";
import { PAGE_MARKETPLACE, PAGE_MARKETPLACE_CONNECTION, PAGE_MARKETPLACE_FIELD_MAPPING } from "@/router";
import FieldChangesTable from "@/components/products/FieldChangesTable.vue";
import { formatMoney } from "@/lib/currency";
import { useSelectedStore } from "@/composables/useSelectedStore";
import { useStoreDateTime } from "@/composables/useStoreDateTime";

export default {
  components: {
    Button,
    Badge,
    EmptyState,
    Loader2,
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
    Label,
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
    DropdownMenuItem,
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
    Ban,
    Tooltip,
    TooltipContent,
    TooltipProvider,
    TooltipTrigger,
    FieldChangesTable,
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
      exportingConnectionId: null,
      importingLinkId: null,
      unlinkingLinkId: null,
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
      return this.hasIrreversibleFields()
        ? "Некоторые поля содержат необратимые модификаторы и были исключены из импорта. Вы можете включить их вручную."
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
      "recreateProductOnMarketplace",
      "saveMappingSelections",
      "getProductVariants",
      "updateProductVariant",
      "linkProduct",
      "relinkProduct",
      "unlinkProduct",
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
        displaySonnerError("Ошибка сохранения маппинга");
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
        displaySonnerSuccess("Нет подключений с маппингом для экспорта");
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
        const operationId = crypto.randomUUID().replace(/-/g, "");
        const title = conn.isLinked
          ? `Экспорт: ${"товар #" + this.productId}`
          : `Экспорт товара #${this.productId}`;
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
        displaySonnerSuccess(`Экспортировано на ${successCount} адаптер(ов)`);
      }
      if (errorCount > 0) {
        displaySonnerError(`Ошибок при экспорте: ${errorCount}`);
      }

      this.isExportingAll = false;
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
            displaySonnerError(`Ошибка сохранения маппинга для ${conn.adapterName}`);
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

    // Import from a specific marketplace (pull changes) — preview first
    async handleImportFromLink(conn) {
      this.importingLinkId = conn.linkId;
      this.isLoadingPreview = true;
      await this.importFromLinkPreview({
        linkId: conn.linkId,
        onSuccess: (response) => {
          const result = extractResponseData(response, {});
          if (!result.hasChanges) {
            displaySonnerSuccess("Изменений не обнаружено");
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

    isFieldIncluded(field) {
      return !this.importExcludedFields.has(field);
    },
    toggleFieldIncluded(field) {
      const next = new Set(this.importExcludedFields);
      if (next.has(field)) {
        next.delete(field);
      } else {
        next.add(field);
      }
      this.importExcludedFields = next;
    },
    isFieldIrreversible(field) {
      return this.importPreviewData?.irreversibleFields?.includes(field);
    },
    allFieldsExcluded() {
      if (!this.importPreviewData?.updatedFields) return true;
      return this.importPreviewData.updatedFields.every((f) => this.importExcludedFields.has(f));
    },
    allFieldsIncluded() {
      if (!this.importPreviewData?.updatedFields) return false;
      return this.importPreviewData.updatedFields.every((f) => !this.importExcludedFields.has(f));
    },
    toggleAllFields() {
      if (!this.importPreviewData?.updatedFields) return;
      if (this.allFieldsIncluded()) {
        this.importExcludedFields = new Set(this.importPreviewData.updatedFields);
      } else {
        this.importExcludedFields = new Set();
      }
    },
    hasIrreversibleFields() {
      return this.importPreviewData?.irreversibleFields?.length > 0;
    },
    async confirmImport() {
      if (!this.previewLinkId) return;
      this.isApplyingImport = true;
      const excludeFields = [...this.importExcludedFields];

      const operationId = crypto.randomUUID().replace(/-/g, "");
      const conn = this.previewConnection;
      await this.$store.dispatch("operations/startOperation", {
        id: operationId,
        type: "single-import",
        title: `Импорт: ${"товар #" + this.productId}`,
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
              `Обновлено: ${result.updatedFields.join(", ")}`,
            );
            this.$emit("product-updated");
          } else {
            displaySonnerSuccess("Изменений не обнаружено");
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

    getFieldLabel(field) {
      const labels = {
        name: "Название",
        description: "Описание",
        price: "Цена",
        storageQuantity: "Остаток",
      };
      if (field.startsWith("attr:")) return field.substring(5);
      return labels[field] || field;
    },

    formatFieldValue(field, value) {
      if (value === null || value === undefined) return "—";
      if (field === "price") {
        return formatMoney(value, this.storeBaseCurrency);
      }
      if (field === "description") {
        const str = String(value);
        return str.length > 80 ? str.substring(0, 80) + "..." : str;
      }
      return String(value);
    },

    // Export to a specific marketplace
    async handleExportToConnection(conn) {
      if (conn.mappingMissing) {
        displaySonnerError("Маппинг этой связи удалён. Отвяжите товар или выберите новый маппинг.");
        return;
      }

      if (conn.availableMappings?.length > 1 && !this.mappingSelections[conn.connectionId]) {
        displaySonnerError("Выберите маппинг перед экспортом");
        return;
      }

      this.exportingConnectionId = conn.connectionId;

      const operationId = crypto.randomUUID().replace(/-/g, "");
      const title = conn.isLinked
        ? `Экспорт: ${"товар #" + this.productId}`
        : `Экспорт товара #${this.productId}`;
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
            displaySonnerSuccess(`Товар обновлён на ${conn.adapterName}`);
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
            displaySonnerSuccess(`Товар экспортирован на ${conn.adapterName}`);
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      }

      this.exportingConnectionId = null;
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
          let msg = "Товар пересоздан на адаптере";
          if (result.archiveError) {
            msg += ` (архивация старого: ${result.archiveError})`;
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
            displaySonnerSuccess(`Товар перепривязан на ${this.linkConn.adapterName}`);
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
            displaySonnerSuccess(`Товар привязан к ${this.linkConn.adapterName}`);
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
        title: "Отвязать товар?",
        description: `Товар будет отвязан от ${conn.adapterName}. Экспорт в этот маркетплейс прекратится.`,
        confirmLabel: "Отвязать",
      });
      if (!ok) return;

      this.unlinkingLinkId = conn.linkId;
      try {
        await this.unlinkProduct({
          linkId: conn.linkId,
          onSuccess: () => {
            displaySonnerSuccess(`Товар отвязан от ${conn.adapterName}`);
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

    getSelectedMapping(conn) {
      const selectedId = this.mappingSelections[conn.connectionId];
      if (!selectedId || !conn.availableMappings) return null;
      return conn.availableMappings.find((m) => m.id === selectedId) || null;
    },

    getSelectedMappingName(conn) {
      const m = this.getSelectedMapping(conn);
      return m ? (m.name || m.externalCategoryName || `Маппинг #${m.id}`) : null;
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
      if (this.isExportingAll && conn.hasMapping && conn.canExport && !conn.mappingMissing) return true;
      if (conn.isLinked) {
        if (this.importingLinkId === conn.linkId) return true;
        if (this.unlinkingLinkId === conn.linkId) return true;
        if (this.isApplyingImport && this.previewLinkId === conn.linkId) return true;
        if (this.isRecreating && this.recreateConn?.connectionId === conn.connectionId) return true;
      }
      return false;
    },

    getMarketplaceProductUrl(conn) {
      if (!conn.externalProductId || !conn.productEditUrlTemplate) return null;
      return conn.productEditUrlTemplate.replace("{externalId}", conn.externalProductId);
    },

    getConnectionStatusInfo(conn) {
      if (conn.mappingMissing) {
        return {
          variant: "destructive",
          icon: AlertTriangle,
          label: "Нет маппинга",
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
            icon: AlertTriangle,
            label: status,
            color: "text-muted-foreground",
          };
      }
    },

    formatDate(date) {
      if (!date) return "—";
      const d = new Date(date);
      if (Number.isNaN(d.getTime())) return "—";
      const now = new Date();
      const diffMs = now - d;
      const diffMins = Math.floor(diffMs / 60000);

      if (diffMins < 1) return "только что";
      if (diffMins < 60) return `${diffMins} мин назад`;
      if (diffMins < 1440) return `${Math.floor(diffMins / 60)} ч назад`;
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
  <div class="flex flex-col min-h-0">
    <!-- Header -->
    <div class="flex items-center px-4 h-11 border-b shrink-0">
      <p class="text-sm font-medium flex items-center gap-2">
        <Link2 class="size-4" />
        Связи с адаптерами
      </p>
    </div>

    <div class="flex-1 overflow-y-auto p-4 space-y-6">
    <!-- Loading state -->
    <div v-if="isLoading" class="flex justify-center py-12">
      <Loader2 class="size-8 animate-spin text-muted-foreground" />
    </div>

    <!-- No category warning -->
    <EmptyState
      v-else-if="connections && !connections.categoryId"
      title="Товар не привязан к категории"
      description="Укажите категорию товара для экспорта на адаптеры"
    >
      <template #icon>
        <AlertTriangle />
      </template>
    </EmptyState>

    <!-- No connections -->
    <EmptyState
      v-else-if="!connections?.allConnections?.length"
      title="Нет подключений к адаптерам"
      description="Создайте подключение к адаптеру, чтобы экспортировать и синхронизировать товары"
    >
      <template #icon>
        <Link2 />
      </template>
      <Button variant="outline" class="mt-2" @click="$router.push(PAGE_MARKETPLACE)">
        <Settings class="mr-2 size-4" />
        Перейти к адаптерам
      </Button>
    </EmptyState>

    <!-- Connection cards -->
    <div v-else class="space-y-4">
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
            <div class="text-sm font-medium">Экспорт на все адаптеры</div>
            <div class="text-xs text-muted-foreground">
              {{ connectionsWithMapping.length }}
              {{ connectionsWithMapping.length < 5 ? 'подключения готовы' : 'подключений готово' }}
              к экспорту
            </div>
          </div>
        </div>
        <Button
          size="sm"
          @click="handleExportToAll"
          :disabled="isExportingAll || isLoading"
        >
          <Loader2 v-if="isExportingAll" class="size-4 mr-2 animate-spin" />
          <Upload v-else class="size-4 mr-2" />
          Экспортировать
        </Button>
      </div>

      <!-- Linked connections (already exported) -->
      <div
        v-for="conn in linkedConnections"
        :key="conn.connectionId"
        class="relative border rounded-lg"
      >
        <div class="flex items-center gap-3 px-4 py-3">
        <div v-if="isConnectionBusy(conn)" class="absolute inset-0 bg-background/60 flex items-center justify-center rounded-lg z-10">
          <Loader2 class="size-5 animate-spin text-primary" />
        </div>
        <a
          v-if="getMarketplaceProductUrl(conn)"
          :href="getMarketplaceProductUrl(conn)"
          target="_blank"
          rel="noopener noreferrer"
          class="size-9 rounded-lg bg-primary/10 flex items-center justify-center shrink-0 hover:bg-primary/20 transition-colors"
          title="Открыть на адаптере"
        >
          <ExternalLink class="size-4 text-primary" />
        </a>
        <div v-else class="size-9 rounded-lg bg-primary/10 flex items-center justify-center shrink-0">
          <ExternalLink class="size-4 text-primary" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-2 mt-0.5">
            <code class="bg-muted px-1.5 py-0.5 rounded text-xs text-muted-foreground">{{ conn.externalProductId }}</code>
            <span v-if="conn.externalSku" class="text-xs text-muted-foreground">
              · арт. {{ conn.externalSku }}
            </span>
            <router-link
              v-if="getMappingRoute(conn)"
              :to="getMappingRoute(conn)"
              class="text-xs text-primary hover:underline truncate"
            >
              {{ getSelectedMappingName(conn) }}
            </router-link>
            <span v-else-if="conn.mappingMissing" class="text-xs text-destructive">
              маппинг удалён
            </span>
          </div>
        </div>
        <Badge :variant="getConnectionStatusInfo(conn).variant" class="shrink-0">
          <component :is="getConnectionStatusInfo(conn).icon" class="size-3 mr-1" />
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
              <p>Размеры</p>
            </TooltipContent>
          </Tooltip>
        </TooltipProvider>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon" class="size-8 shrink-0">
              <EllipsisVertical class="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem
              @click="handleExportToConnection(conn)"
              :disabled="exportingConnectionId === conn.connectionId || conn.mappingMissing"
            >
              <Upload class="size-4 mr-2" />
              Экспорт
            </DropdownMenuItem>
            <DropdownMenuItem
              v-if="conn.canImport"
              @click="handleImportFromLink(conn)"
              :disabled="importingLinkId === conn.linkId"
            >
              <Download class="size-4 mr-2" />
              Импорт
            </DropdownMenuItem>
            <DropdownMenuSub v-if="conn.availableMappings?.length > 1 || (conn.mappingMissing && conn.availableMappings?.length > 0)">
              <DropdownMenuSubTrigger>
                <Layers class="size-4 mr-2" />
                Маппинг
              </DropdownMenuSubTrigger>
              <DropdownMenuSubContent>
                <DropdownMenuRadioGroup
                  :model-value="String(mappingSelections[conn.connectionId] ?? '')"
                  @update:model-value="handleMappingChange(conn, Number($event))"
                >
                  <DropdownMenuRadioItem
                    v-for="m in conn.availableMappings"
                    :key="m.id"
                    :value="String(m.id)"
                  >
                    {{ m.name || m.externalCategoryName || `Маппинг #${m.id}` }}
                  </DropdownMenuRadioItem>
                </DropdownMenuRadioGroup>
              </DropdownMenuSubContent>
            </DropdownMenuSub>
            <DropdownMenuSeparator />
            <DropdownMenuItem @click="openLinkDialog(conn, true)">
              <Link2 class="size-4 mr-2" />
              Перепривязать
            </DropdownMenuItem>
            <DropdownMenuItem
              class="text-destructive focus:text-destructive"
              @click="handleUnlink(conn)"
              :disabled="unlinkingLinkId === conn.linkId"
            >
              <Loader2 v-if="unlinkingLinkId === conn.linkId" class="size-4 mr-2 animate-spin" />
              <Ban v-else class="size-4 mr-2" />
              Отвязать
            </DropdownMenuItem>
            <DropdownMenuSeparator v-if="conn.canArchive && conn.canExport" />
            <DropdownMenuItem
              v-if="conn.canArchive && conn.canExport"
              @click="openRecreateDialog(conn)"
            >
              <RefreshCw class="size-4 mr-2" />
              Пересоздать
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        </div>
        <!-- Variants panel -->
        <div v-if="variantsData[conn.connectionId]" class="border-t px-4 py-2">
          <div v-if="variantsData[conn.connectionId].loading" class="flex items-center gap-2 py-2">
            <Loader2 class="size-4 animate-spin" />
            <span class="text-xs text-muted-foreground">Загрузка размеров...</span>
          </div>
          <div v-else-if="variantsData[conn.connectionId].variants.length === 0" class="py-2">
            <span class="text-xs text-muted-foreground">Нет размеров для этого подключения</span>
          </div>
          <Table v-else>
            <TableHeader>
              <TableRow>
                <TableHead class="text-xs h-8">Размер</TableHead>
                <TableHead class="text-xs h-8">SKU</TableHead>
                <TableHead class="text-xs h-8">Баркод</TableHead>
                <TableHead class="text-xs h-8">Цена</TableHead>
                <TableHead class="text-xs h-8">Остаток</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="v in variantsData[conn.connectionId].variants" :key="v.id">
                <TableCell class="text-xs py-1">{{ v.size || '—' }}</TableCell>
                <TableCell class="text-xs py-1">
                  <code class="bg-muted px-1 rounded">{{ v.sku || '—' }}</code>
                </TableCell>
                <TableCell class="text-xs py-1">
                  <code class="bg-muted px-1 rounded">{{ v.barcode || '—' }}</code>
                </TableCell>
                <TableCell class="text-xs py-1">{{ v.price != null ? v.price : '—' }}</TableCell>
                <TableCell class="text-xs py-1">{{ v.quantity != null ? v.quantity : '—' }}</TableCell>
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
          <Loader2 class="size-5 animate-spin text-primary" />
        </div>
        <div class="size-9 rounded-lg bg-muted flex items-center justify-center shrink-0">
          <ExternalLink class="size-4 text-muted-foreground" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-2 mt-0.5">
            <span class="text-xs text-muted-foreground">Не экспортирован</span>
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
          Готов
        </Badge>
        <DropdownMenu>
          <DropdownMenuTrigger as-child>
            <Button variant="ghost" size="icon" class="size-8 shrink-0">
              <EllipsisVertical class="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem
              @click="handleExportToConnection(conn)"
              :disabled="exportingConnectionId === conn.connectionId"
            >
              <Upload class="size-4 mr-2" />
              Экспортировать
            </DropdownMenuItem>
            <DropdownMenuItem @click="openLinkDialog(conn)">
              <Link2 class="size-4 mr-2" />
              Привязать вручную
            </DropdownMenuItem>
            <DropdownMenuSub v-if="conn.availableMappings?.length > 1">
              <DropdownMenuSubTrigger>
                <Layers class="size-4 mr-2" />
                Маппинг
              </DropdownMenuSubTrigger>
              <DropdownMenuSubContent>
                <DropdownMenuRadioGroup
                  :model-value="String(mappingSelections[conn.connectionId] ?? '')"
                  @update:model-value="handleMappingChange(conn, Number($event))"
                >
                  <DropdownMenuRadioItem
                    v-for="m in conn.availableMappings"
                    :key="m.id"
                    :value="String(m.id)"
                  >
                    {{ m.name || m.externalCategoryName || `Маппинг #${m.id}` }}
                  </DropdownMenuRadioItem>
                </DropdownMenuRadioGroup>
              </DropdownMenuSubContent>
            </DropdownMenuSub>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <!-- Connections without mapping — muted "needs setup" state, not an error -->
      <div
        v-for="conn in connectionsWithoutMapping"
        :key="conn.connectionId"
        class="flex items-center gap-3 rounded-lg px-4 py-3 border border-dashed"
      >
        <div class="size-9 rounded-lg bg-muted flex items-center justify-center shrink-0">
          <Link2 class="size-4 text-muted-foreground" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-1.5 mt-0.5">
            <span class="text-xs text-muted-foreground">Маппинг для «{{ connections.categoryName }}» не настроен</span>
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
              · настроить
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
          size="sm"
          title="Нет доступных адаптеров"
          description="Подключите адаптер и создайте маппинг для категории товара, чтобы экспортировать его."
        >
          <template #icon>
            <Link2 />
          </template>
        </EmptyState>
      </div>
    </div>

    <!-- Import Preview Dialog -->
    <Dialog v-model:open="showImportPreviewDialog">
      <DialogContent size="3xl" class="max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Подтверждение импорта</DialogTitle>
          <DialogDescription>
            Следующие поля будут обновлены из адаптера
          </DialogDescription>
        </DialogHeader>
        <div v-if="importPreviewData" class="py-4 max-h-[60vh] overflow-y-auto">
          <FieldChangesTable
            :items="importPreviewItems"
            selectable
            :excluded-fields="importExcludedFields"
            :irreversible-fields="importIrreversibleFields"
            :warning="importWarning"
            @update:excluded-fields="importExcludedFields = $event"
          />
        </div>
        <DialogFooter>
          <Button variant="outline" @click="cancelImport">Отменить</Button>
          <Button @click="confirmImport" :disabled="isApplyingImport || allFieldsExcluded()">
            <Loader2 v-if="isApplyingImport" class="size-4 mr-2 animate-spin" />
            <Download v-else class="size-4 mr-2" />
            Подтвердить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Bulk Mapping Selection Dialog -->
    <Dialog v-model:open="showBulkMappingDialog">
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>Выберите маппинг</DialogTitle>
          <DialogDescription>
            Для некоторых подключений доступно несколько маппингов. Выберите нужный для каждого.
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
                <SelectValue placeholder="Выберите маппинг" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="m in conn.availableMappings"
                  :key="m.id"
                  :value="String(m.id)"
                >
                  {{ m.name || m.externalCategoryName || `Маппинг #${m.id}` }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child><Button variant="outline">Отмена</Button></DialogClose>
          <Button @click="confirmBulkMappingAndExport" :disabled="!allBulkMappingsSelected()">
            <Upload class="size-4 mr-2" />
            Экспортировать
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Recreate Product Dialog -->
    <Dialog v-model:open="showRecreateDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Пересоздать товар</DialogTitle>
          <DialogDescription>
            Товар будет создан заново на {{ recreateConn?.adapterName }} с новым артикулом. Старый товар будет архивирован.
          </DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4">
          <div class="space-y-2">
            <Label for="recreate-sku">Новый артикул</Label>
            <Input
              id="recreate-sku"
              v-model="recreateNewSku"
              maxlength="50"
              placeholder="Введите новый артикул"
              @keyup.enter="submitRecreate"
            />
          </div>
        </div>
        <DialogFooter>
          <DialogClose as-child><Button variant="outline" :disabled="isRecreating">Отмена</Button></DialogClose>
          <Button @click="submitRecreate" :disabled="isRecreating || !recreateNewSku.trim()">
            <Loader2 v-if="isRecreating" class="size-4 mr-2 animate-spin" />
            <RefreshCw v-else class="size-4 mr-2" />
            Пересоздать
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Manual Link Dialog -->
    <Dialog v-model:open="showLinkDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{ isRelinking ? 'Перепривязать товар' : 'Привязать товар' }}</DialogTitle>
          <DialogDescription>
            {{ isRelinking ? 'Введите новый ID товара' : 'Введите ID существующего товара' }} на {{ linkConn?.adapterName }} для {{ isRelinking ? 'перепривязки' : 'ручной привязки' }}.
          </DialogDescription>
        </DialogHeader>
        <div class="py-4 space-y-4">
          <Label for="link-external-id">ID товара на адаптере</Label>
          <Input id="link-external-id" v-model="linkExternalId" maxlength="100"
            placeholder="Например: 12345678" @keyup.enter="submitLink" />
        </div>
        <DialogFooter>
          <DialogClose as-child><Button variant="outline" :disabled="isLinking">Отмена</Button></DialogClose>
          <Button @click="submitLink" :disabled="isLinking || !linkExternalId.trim()">
            <Loader2 v-if="isLinking" class="size-4 mr-2 animate-spin" />
            <Link2 v-else class="size-4 mr-2" />
            {{ isRelinking ? 'Перепривязать' : 'Привязать' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    </div>
  </div>
</template>
