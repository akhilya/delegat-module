<script>
// ProductMarketplaceIntegrations - Manages marketplace connections for a product: export, import, and sync status.
import Button from "@/components/ui/button/Button.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
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
  Layers,
} from "lucide-vue-next";
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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuSubContent,
} from "@/components/ui/dropdown-menu";
import { PAGE_MARKETPLACE_CONNECTION } from "@/router";

export default {
  components: {
    Button,
    Badge,
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
    EllipsisVertical,
    Layers,
  },
  props: {
    productId: {
      type: Number,
      required: true,
    },
  },
  data() {
    return {
      connections: null, // All connections with status info
      isLoading: false,
      isExportingAll: false,
      exportingConnectionId: null,
      importingLinkId: null,
      PAGE_MARKETPLACE_CONNECTION,
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
      importExcludedFields: new Set(),
      // Mapping selection
      mappingSelections: {}, // { connectionId: categoryMappingId }
      // Bulk mapping dialog
      showBulkMappingDialog: false,
      bulkMappingConflicts: [],
      bulkMappingSelections: {}, // temp selections in dialog
    };
  },
  computed: {
    // Connections with mapping (can be exported)
    connectionsWithMapping() {
      if (!this.connections?.allConnections) return [];
      return this.connections.allConnections.filter(
        (c) => c.hasMapping && c.canExport,
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
      } catch {
        displaySonnerError("Ошибка сохранения маппинга");
      }
    },

    // Export to all marketplaces that have category mapping
    async handleExportToAll() {
      const toExport = this.connectionsWithMapping.filter((c) => !c.isLinked);

      if (toExport.length === 0) {
        displaySonnerSuccess(
          "Товар уже экспортирован на все маркетплейсы с маппингом",
        );
        return;
      }

      // Check for connections with multiple mappings but no selection
      const conflicts = toExport.filter(
        (c) => c.availableMappings?.length > 1 && !this.mappingSelections[c.connectionId],
      );
      if (conflicts.length > 0) {
        this.bulkMappingConflicts = conflicts;
        this.bulkMappingSelections = {};
        this.showBulkMappingDialog = true;
        return;
      }

      const withoutMapping = this.connectionsWithoutMapping.length;
      if (withoutMapping > 0) {
        // Could show a warning, but continue with export
      }

      this.isExportingAll = true;
      let successCount = 0;
      let errorCount = 0;

      for (const conn of toExport) {
        try {
          await this.exportProduct({
            connectionId: conn.connectionId,
            productId: this.productId,
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
          `Экспортировано на ${successCount} маркетплейс(ов)`,
        );
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
      await this.importFromLink({
        linkId: this.previewLinkId,
        excludeFields: excludeFields.length > 0 ? excludeFields : undefined,
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
        return new Intl.NumberFormat("ru-RU", {
          style: "currency",
          currency: "RUB",
          minimumFractionDigits: 0,
        }).format(value);
      }
      if (field === "description") {
        const str = String(value);
        return str.length > 80 ? str.substring(0, 80) + "..." : str;
      }
      return String(value);
    },

    // Export to a specific marketplace
    async handleExportToConnection(conn) {
      if (conn.availableMappings?.length > 1 && !this.mappingSelections[conn.connectionId]) {
        displaySonnerError("Выберите маппинг перед экспортом");
        return;
      }

      this.exportingConnectionId = conn.connectionId;

      if (conn.isLinked) {
        // Already linked - use export via link
        await this.exportFromLink({
          linkId: conn.linkId,
          onSuccess: () => {
            displaySonnerSuccess(`Товар обновлён на ${conn.adapterName}`);
            this.fetchData();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } else {
        // New export (first time)
        await this.exportProduct({
          connectionId: conn.connectionId,
          productId: this.productId,
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
          let msg = "Товар пересоздан на маркетплейсе";
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

    getSelectedMappingName(conn) {
      const selectedId = this.mappingSelections[conn.connectionId];
      if (!selectedId || !conn.availableMappings) return null;
      const m = conn.availableMappings.find((m) => m.id === selectedId);
      return m ? (m.name || m.externalCategoryName || `Маппинг #${m.id}`) : null;
    },

    isConnectionBusy(conn) {
      if (this.exportingConnectionId === conn.connectionId) return true;
      if (this.isExportingAll && !conn.isLinked && conn.hasMapping && conn.canExport) return true;
      if (conn.isLinked) {
        if (this.importingLinkId === conn.linkId) return true;
        if (this.isApplyingImport && this.previewLinkId === conn.linkId) return true;
        if (this.isRecreating && this.recreateConn?.connectionId === conn.connectionId) return true;
      }
      return false;
    },

    getStatusInfo(status) {
      switch (status) {
        case "Synced":
          return {
            variant: "default",
            icon: CheckCircle,
            label: "Синхронизирован",
            color: "text-green-500",
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
            color: "text-red-500",
          };
        case "Modified":
          return {
            variant: "outline",
            icon: AlertTriangle,
            label: "Изменён",
            color: "text-orange-500",
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
      const now = new Date();
      const diffMs = now - d;
      const diffMins = Math.floor(diffMs / 60000);

      if (diffMins < 1) return "только что";
      if (diffMins < 60) return `${diffMins} мин назад`;
      if (diffMins < 1440) return `${Math.floor(diffMins / 60)} ч назад`;
      return d.toLocaleDateString("ru-RU");
    },
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <div class="p-4 space-y-6">
    <!-- Header -->
    <div>
      <h2 class="text-lg font-semibold flex items-center gap-2">
        <Link2 class="size-5" />
        Маркетплейсы
      </h2>
      <p class="text-sm text-muted-foreground">
        Управление связями товара с маркетплейсами
      </p>
    </div>

    <!-- Loading state -->
    <div v-if="isLoading" class="flex justify-center py-12">
      <Loader2 class="size-8 animate-spin text-muted-foreground" />
    </div>

    <!-- No category warning -->
    <div
      v-else-if="connections && !connections.categoryId"
      class="flex flex-col items-center justify-center py-12 text-center border rounded-lg bg-orange-50 dark:bg-orange-950/20"
    >
      <AlertTriangle class="size-12 mb-4 text-orange-500" />
      <h3 class="font-medium mb-1">Товар не привязан к категории</h3>
      <p class="text-sm text-muted-foreground">
        Укажите категорию товара для экспорта на маркетплейсы
      </p>
    </div>

    <!-- No connections -->
    <div
      v-else-if="!connections?.allConnections?.length"
      class="flex flex-col items-center justify-center py-12 text-center border rounded-lg"
    >
      <Link2 class="size-12 mb-4 text-muted-foreground" />
      <h3 class="font-medium mb-1">Нет подключений к маркетплейсам</h3>
      <p class="text-sm text-muted-foreground">
        Создайте подключение к маркетплейсу в настройках магазина
      </p>
    </div>

    <!-- Connection cards -->
    <div v-else class="space-y-4">
      <!-- Export all banner -->
      <div
        v-if="connections?.allConnections?.length > 1 && readyToExportConnections.length > 0"
        class="flex items-center justify-between gap-4 rounded-lg border bg-muted/40 px-4 py-3"
      >
        <div class="flex items-center gap-3 min-w-0">
          <div class="size-8 rounded-md bg-primary/10 flex items-center justify-center shrink-0">
            <Upload class="size-4 text-primary" />
          </div>
          <div class="min-w-0">
            <div class="text-sm font-medium">Экспорт на все маркетплейсы</div>
            <div class="text-xs text-muted-foreground">
              {{ readyToExportConnections.length }}
              {{ readyToExportConnections.length < 5 ? 'подключения готовы' : 'подключений готово' }}
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
        class="relative flex items-center gap-3 border rounded-lg px-4 py-3"
      >
        <div v-if="isConnectionBusy(conn)" class="absolute inset-0 bg-background/60 flex items-center justify-center rounded-lg z-10">
          <Loader2 class="size-5 animate-spin text-primary" />
        </div>
        <div class="size-9 rounded-lg bg-primary/10 flex items-center justify-center shrink-0">
          <ExternalLink class="size-4 text-primary" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="flex items-center gap-2 mt-0.5">
            <code class="bg-muted px-1.5 py-0.5 rounded text-xs text-muted-foreground">{{ conn.externalProductId }}</code>
            <span v-if="conn.availableMappings?.length > 1 && getSelectedMappingName(conn)" class="text-xs text-muted-foreground truncate">
              {{ getSelectedMappingName(conn) }}
            </span>
          </div>
        </div>
        <Badge :variant="getStatusInfo(conn.linkStatus).variant" class="shrink-0">
          <component :is="getStatusInfo(conn.linkStatus).icon" class="size-3 mr-1" />
          {{ getStatusInfo(conn.linkStatus).label }}
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
            <DropdownMenuSub v-if="conn.availableMappings?.length > 1">
              <DropdownMenuSubTrigger>
                <Layers class="size-4 mr-2" />
                Маппинг
              </DropdownMenuSubTrigger>
              <DropdownMenuSubContent>
                <DropdownMenuItem
                  v-for="m in conn.availableMappings"
                  :key="m.id"
                  @click="handleMappingChange(conn, m.id)"
                >
                  <CheckCircle v-if="mappingSelections[conn.connectionId] === m.id" class="size-4 mr-2 text-primary" />
                  <span v-else class="size-4 mr-2" />
                  {{ m.name || m.externalCategoryName || `Маппинг #${m.id}` }}
                </DropdownMenuItem>
              </DropdownMenuSubContent>
            </DropdownMenuSub>
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
            <span v-if="conn.availableMappings?.length > 1 && getSelectedMappingName(conn)" class="text-xs text-muted-foreground truncate">
              · {{ getSelectedMappingName(conn) }}
            </span>
          </div>
        </div>
        <Badge variant="outline" class="text-green-600 border-green-600 shrink-0">
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
            <DropdownMenuSub v-if="conn.availableMappings?.length > 1">
              <DropdownMenuSubTrigger>
                <Layers class="size-4 mr-2" />
                Маппинг
              </DropdownMenuSubTrigger>
              <DropdownMenuSubContent>
                <DropdownMenuItem
                  v-for="m in conn.availableMappings"
                  :key="m.id"
                  @click="handleMappingChange(conn, m.id)"
                >
                  <CheckCircle v-if="mappingSelections[conn.connectionId] === m.id" class="size-4 mr-2 text-primary" />
                  <span v-else class="size-4 mr-2" />
                  {{ m.name || m.externalCategoryName || `Маппинг #${m.id}` }}
                </DropdownMenuItem>
              </DropdownMenuSubContent>
            </DropdownMenuSub>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <!-- Connections without mapping -->
      <div
        v-for="conn in connectionsWithoutMapping"
        :key="conn.connectionId"
        class="flex items-center gap-3 rounded-lg px-4 py-3 border border-orange-300 bg-orange-50 dark:bg-orange-950/20"
      >
        <div class="size-9 rounded-lg bg-orange-100 dark:bg-orange-900/30 flex items-center justify-center shrink-0">
          <AlertTriangle class="size-4 text-orange-500" />
        </div>
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="font-medium text-sm truncate">{{ conn.adapterName }}</span>
            <span class="text-xs text-muted-foreground truncate hidden sm:inline">{{ conn.connectionName }}</span>
          </div>
          <div class="text-xs text-muted-foreground mt-0.5">
            Нет маппинга для «{{ connections.categoryName }}»
          </div>
        </div>
        <router-link
          :to="{
            name: PAGE_MARKETPLACE_CONNECTION,
            params: { connId: conn.connectionId },
            query: {
              tab: 'categories',
              createMapping: 'true',
              localCategoryId: connections.categoryId,
              localCategoryName: connections.categoryName,
            },
          }"
        >
          <Button variant="outline" size="sm" class="shrink-0">
            <Plus class="size-4 mr-2" />
            Создать маппинг
          </Button>
        </router-link>
      </div>

      <!-- Empty state when all connections have issues -->
      <div
        v-if="
          !linkedConnections.length &&
          !readyToExportConnections.length &&
          !connectionsWithoutMapping.length
        "
        class="text-center py-8 text-muted-foreground"
      >
        <p>Нет доступных маркетплейсов для экспорта</p>
      </div>
    </div>

    <!-- Import Preview Dialog -->
    <Dialog v-model:open="showImportPreviewDialog">
      <DialogContent class="sm:max-w-3xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Подтверждение импорта</DialogTitle>
          <DialogDescription>
            Следующие поля будут обновлены из маркетплейса
          </DialogDescription>
        </DialogHeader>
        <div v-if="importPreviewData" class="py-4 max-h-[60vh] overflow-y-auto">
          <!-- Warning banner for irreversible fields -->
          <div
            v-if="hasIrreversibleFields()"
            class="flex items-start gap-2 p-3 mb-3 rounded-md bg-amber-50 border border-amber-200 text-amber-800 dark:bg-amber-950/30 dark:border-amber-800 dark:text-amber-200 text-sm"
          >
            <AlertTriangle class="size-4 mt-0.5 shrink-0" />
            <span>Некоторые поля содержат необратимые модификаторы и были исключены из импорта. Вы можете включить их вручную.</span>
          </div>
          <table class="w-full table-fixed text-sm">
            <thead>
              <tr class="border-b">
                <th class="w-[40px] py-2 px-2">
                  <Checkbox
                    :model-value="allFieldsIncluded()"
                    @update:model-value="toggleAllFields"
                  />
                </th>
                <th class="w-[100px] text-left py-2 px-2 font-medium text-muted-foreground">Поле</th>
                <th class="text-left py-2 px-2 font-medium text-muted-foreground">Текущее</th>
                <th class="text-left py-2 px-2 font-medium text-muted-foreground">Новое</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="field in importPreviewData.updatedFields"
                :key="field"
                class="border-b"
              >
                <td class="py-2 px-2 align-top">
                  <Checkbox
                    :model-value="isFieldIncluded(field)"
                    @update:model-value="toggleFieldIncluded(field)"
                  />
                </td>
                <td class="py-2 px-2 font-medium align-top">
                  <div class="flex items-center gap-1">
                    {{ getFieldLabel(field) }}
                    <span
                      v-if="isFieldIrreversible(field)"
                      class="inline-flex items-center gap-0.5 text-amber-600 dark:text-amber-400"
                      title="Необратимые модификаторы"
                    >
                      <AlertTriangle class="size-3.5" />
                    </span>
                  </div>
                </td>
                <td class="py-2 px-2 text-muted-foreground break-words whitespace-pre-wrap align-top">
                  {{ formatFieldValue(field, importPreviewData.oldValues[field]) }}
                </td>
                <td class="py-2 px-2 text-green-600 font-medium break-words whitespace-pre-wrap align-top">
                  {{ formatFieldValue(field, importPreviewData.newValues[field]) }}
                </td>
              </tr>
            </tbody>
          </table>
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
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Выберите маппинг</DialogTitle>
          <DialogDescription>
            Для некоторых подключений доступно несколько маппингов. Выберите нужный для каждого.
          </DialogDescription>
        </DialogHeader>
        <div class="space-y-3 py-4">
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
          <Button variant="outline" @click="showBulkMappingDialog = false">Отмена</Button>
          <Button @click="confirmBulkMappingAndExport" :disabled="!allBulkMappingsSelected()">
            <Upload class="size-4 mr-2" />
            Экспортировать
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Recreate Product Dialog -->
    <Dialog v-model:open="showRecreateDialog">
      <DialogContent class="sm:max-w-md">
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
          <Button variant="outline" @click="showRecreateDialog = false" :disabled="isRecreating">
            Отмена
          </Button>
          <Button @click="submitRecreate" :disabled="isRecreating || !recreateNewSku.trim()">
            <Loader2 v-if="isRecreating" class="size-4 mr-2 animate-spin" />
            <RefreshCw v-else class="size-4 mr-2" />
            Пересоздать
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
