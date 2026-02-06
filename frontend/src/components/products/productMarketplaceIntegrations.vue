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
} from "lucide-vue-next";
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
      // Import preview dialog
      showImportPreviewDialog: false,
      importPreviewData: null,
      isLoadingPreview: false,
      isApplyingImport: false,
      previewLinkId: null,
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
    ]),

    async fetchData() {
      this.isLoading = true;
      await this.getAvailableConnectionsForProduct({
        productId: this.productId,
        onSuccess: (response) => {
          this.connections = extractResponseData(response, {});
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
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

    async confirmImport() {
      if (!this.previewLinkId) return;
      this.isApplyingImport = true;
      await this.importFromLink({
        linkId: this.previewLinkId,
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
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-lg font-semibold flex items-center gap-2">
          <Link2 class="size-5" />
          Маркетплейсы
        </h2>
        <p class="text-sm text-muted-foreground">
          Управление связями товара с маркетплейсами
        </p>
      </div>
      <Button
        v-if="connections?.allConnections?.length > 1"
        @click="handleExportToAll"
        :disabled="isExportingAll || !canExportToAll || isLoading"
      >
        <Loader2 v-if="isExportingAll" class="size-4 mr-2 animate-spin" />
        <Upload v-else class="size-4 mr-2" />
        Экспортировать во все маркетплейсы
      </Button>
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
      <!-- Linked connections (already exported) -->
      <div
        v-for="conn in linkedConnections"
        :key="conn.connectionId"
        class="border rounded-lg p-4"
      >
        <div class="flex items-start justify-between">
          <div class="flex items-center gap-3">
            <div
              class="size-10 rounded-lg bg-primary/10 flex items-center justify-center"
            >
              <ExternalLink class="size-5 text-primary" />
            </div>
            <div>
              <h3 class="font-medium">{{ conn.adapterName }}</h3>
              <p class="text-sm text-muted-foreground">
                {{ conn.connectionName }}
              </p>
            </div>
          </div>
          <Badge :variant="getStatusInfo(conn.linkStatus).variant">
            <component
              :is="getStatusInfo(conn.linkStatus).icon"
              class="size-3 mr-1"
            />
            {{ getStatusInfo(conn.linkStatus).label }}
          </Badge>
        </div>

        <Separator class="my-3" />

        <div class="flex items-center justify-between text-sm">
          <div class="flex items-center gap-4 text-muted-foreground">
            <span>
              ID:
              <code class="bg-muted px-1.5 py-0.5 rounded text-xs">{{
                conn.externalProductId
              }}</code>
            </span>
          </div>
          <div class="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              @click="handleImportFromLink(conn)"
              :disabled="importingLinkId === conn.linkId || !conn.canImport"
            >
              <Loader2
                v-if="importingLinkId === conn.linkId"
                class="size-4 mr-2 animate-spin"
              />
              <Download v-else class="size-4 mr-2" />
              Импорт
            </Button>
            <Button
              size="sm"
              @click="handleExportToConnection(conn)"
              :disabled="exportingConnectionId === conn.connectionId"
            >
              <Loader2
                v-if="exportingConnectionId === conn.connectionId"
                class="size-4 mr-2 animate-spin"
              />
              <Upload v-else class="size-4 mr-2" />
              Экспорт
            </Button>
          </div>
        </div>
      </div>

      <!-- Ready to export connections (has mapping, not linked) -->
      <div
        v-for="conn in readyToExportConnections"
        :key="conn.connectionId"
        class="border rounded-lg p-4 border-dashed"
      >
        <div class="flex items-start justify-between">
          <div class="flex items-center gap-3">
            <div
              class="size-10 rounded-lg bg-muted flex items-center justify-center"
            >
              <ExternalLink class="size-5 text-muted-foreground" />
            </div>
            <div>
              <h3 class="font-medium">{{ conn.adapterName }}</h3>
              <p class="text-sm text-muted-foreground">
                {{ conn.connectionName }}
              </p>
            </div>
          </div>
          <Badge variant="outline" class="text-green-600 border-green-600">
            <CheckCircle class="size-3 mr-1" />
            Готов к экспорту
          </Badge>
        </div>

        <Separator class="my-3" />

        <div class="flex items-center justify-between text-sm">
          <p class="text-muted-foreground">
            Товар ещё не экспортирован на этот маркетплейс
          </p>
          <Button
            size="sm"
            @click="handleExportToConnection(conn)"
            :disabled="exportingConnectionId === conn.connectionId"
          >
            <Loader2
              v-if="exportingConnectionId === conn.connectionId"
              class="size-4 mr-2 animate-spin"
            />
            <Upload v-else class="size-4 mr-2" />
            Экспортировать
          </Button>
        </div>
      </div>

      <!-- Connections without mapping -->
      <div
        v-for="conn in connectionsWithoutMapping"
        :key="conn.connectionId"
        class="border rounded-lg p-4 border-orange-300 bg-orange-50 dark:bg-orange-950/20"
      >
        <div class="flex items-start justify-between">
          <div class="flex items-center gap-3">
            <div
              class="size-10 rounded-lg bg-orange-100 dark:bg-orange-900/30 flex items-center justify-center"
            >
              <AlertTriangle class="size-5 text-orange-500" />
            </div>
            <div>
              <h3 class="font-medium">{{ conn.adapterName }}</h3>
              <p class="text-sm text-muted-foreground">
                {{ conn.connectionName }}
              </p>
            </div>
          </div>
          <Badge variant="outline" class="text-orange-600 border-orange-500">
            <AlertTriangle class="size-3 mr-1" />
            Нет маппинга категории
          </Badge>
        </div>

        <Separator class="my-3" />

        <div class="flex items-center justify-between text-sm">
          <p class="text-muted-foreground">
            Категория "{{ connections.categoryName }}" не связана с категорией
            на {{ conn.adapterName }}
          </p>
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
            <Button variant="outline" size="sm">
              <Plus class="size-4 mr-2" />
              Создать маппинг
            </Button>
          </router-link>
        </div>
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
                v-for="field in importPreviewData.updatedFields"
                :key="field"
                class="border-b"
              >
                <td class="py-2 px-2 font-medium align-top">{{ getFieldLabel(field) }}</td>
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
          <Button @click="confirmImport" :disabled="isApplyingImport">
            <Loader2 v-if="isApplyingImport" class="size-4 mr-2 animate-spin" />
            <Download v-else class="size-4 mr-2" />
            Подтвердить
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
