<script>
import PageListToolbar from "@/components/PageListToolbar.vue";
import SettingsNavbarTools from "@/components/SettingsNavbarTools.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  LogListPanel,
  LogRefreshButton,
  LogDateCell,
  LogMethodCell,
  LogPathCell,
  LogStatusBadge,
  LogFilterSelect,
  LogHttpMethodFilter,
  LogHttpStatusFilter,
  AdapterLogDetailSheet,
  LogSelectCheckbox,
  LogSortHeader,
  LOG_FILTER_ALL,
  formatLogDate as formatLogDateRaw,
  downloadJsonFile,
  mapPool,
  stampFilename,
  cycleLogSort,
  LOG_SORT_DEFAULT,
} from "@/components/logs";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { ScrollText } from "lucide-vue-next";

export default {
  components: {
    PageListToolbar,
    SettingsNavbarTools,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    LogListPanel,
    LogRefreshButton,
    LogDateCell,
    LogMethodCell,
    LogPathCell,
    LogStatusBadge,
    LogFilterSelect,
    LogHttpMethodFilter,
    LogHttpStatusFilter,
    AdapterLogDetailSheet,
    LogSelectCheckbox,
    LogSortHeader,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone, LOG_FILTER_ALL, ScrollText };
  },
  data() {
    return {
      items: [],
      total: 0,
      errorCount: 0,
      isLoading: false,
      isDetailLoading: false,
      detailOpen: false,
      detail: null,
      connections: [],
      selectedIds: [],
      downloading: false,
      deleting: false,
      sortBy: LOG_SORT_DEFAULT.sortBy,
      sortDir: LOG_SORT_DEFAULT.sortDir,
      filters: {
        connectionId: LOG_FILTER_ALL,
        adapterKey: LOG_FILTER_ALL,
        method: LOG_FILTER_ALL,
        statusClass: LOG_FILTER_ALL,
        q: "",
        limit: 50,
        offset: 0,
      },
    };
  },
  computed: {
    activeFilterCount() {
      return ["connectionId", "adapterKey", "method", "statusClass"].filter(
        (key) => this.filters[key] !== LOG_FILTER_ALL,
      ).length;
    },
    connectionOptions() {
      return (this.connections || []).map((c) => ({
        value: String(c.id),
        label: `${c.name || c.adapterKey} · #${c.id}`,
      }));
    },
    adapterKeyOptions() {
      const keys = new Set(
        (this.connections || []).map((c) => c.adapterKey).filter(Boolean),
      );
      return [...keys].sort().map((k) => ({ value: k, label: k }));
    },
    hasMore() {
      return this.filters.offset + this.items.length < this.total;
    },
    pageIds() {
      return this.items.map((r) => r.id ?? r.Id).filter((id) => id != null);
    },
    allPageSelected() {
      return (
        this.pageIds.length > 0 &&
        this.pageIds.every((id) => this.selectedIds.includes(id))
      );
    },
    somePageSelected() {
      return (
        !this.allPageSelected &&
        this.pageIds.some((id) => this.selectedIds.includes(id))
      );
    },
    detailMeta() {
      if (!this.detail) return [];
      return [
        {
          label: this.$t("marketplace.adapterLogsPage.dateTime"),
          value: this.formatLogDate(this.detail.createdAt),
        },
        {
          label: this.$t("marketplace.adapterLogsPage.duration"),
          value: this.$t("components.logs.durationMs", {
            ms: this.detail.durationMs,
          }),
        },
        {
          label: this.$t("marketplace.adapterLogsPage.adapter"),
          value: this.detail.adapterKey,
        },
        {
          label: this.$t("marketplace.adapterLogsPage.connection"),
          value: this.connectionLabel(this.detail.connectionId),
        },
      ];
    },
  },
  methods: {
    ...mapActions([
      "getAdapterHttpLogs",
      "getAdapterHttpLogDetail",
      "deleteAdapterHttpLogs",
      "getConnections",
    ]),
    formatLogDate(value) {
      return formatLogDateRaw(value, this.storeTimezone);
    },

    rowId(row) {
      return row.id ?? row.Id;
    },

    isSelected(id) {
      return this.selectedIds.includes(id);
    },

    clearSelection() {
      this.selectedIds = [];
    },

    toggleSelect(id, checked) {
      if (checked === true) {
        if (!this.selectedIds.includes(id))
          this.selectedIds = [...this.selectedIds, id];
      } else {
        this.selectedIds = this.selectedIds.filter((x) => x !== id);
      }
    },

    toggleSelectAll(checked) {
      if (checked === true) {
        const set = new Set(this.selectedIds);
        this.pageIds.forEach((id) => set.add(id));
        this.selectedIds = [...set];
      } else {
        const page = new Set(this.pageIds);
        this.selectedIds = this.selectedIds.filter((id) => !page.has(id));
      }
    },

    async fetchConnections() {
      await this.getConnections({
        onSuccess: (response) => {
          this.connections = extractResponseData(response, []) || [];
        },
        onError: () => {
          this.connections = [];
        },
      });
    },

    buildParams() {
      const f = this.filters;
      return {
        connectionId:
          f.connectionId === LOG_FILTER_ALL ? undefined : f.connectionId,
        adapterKey: f.adapterKey === LOG_FILTER_ALL ? undefined : f.adapterKey,
        method: f.method === LOG_FILTER_ALL ? undefined : f.method,
        statusClass:
          f.statusClass === LOG_FILTER_ALL ? undefined : f.statusClass,
        q: f.q || undefined,
        sortBy: this.sortBy,
        sortDir: this.sortDir,
        limit: f.limit,
        offset: f.offset,
      };
    },

    toggleSort(column) {
      const next = cycleLogSort(this.sortBy, this.sortDir, column);
      this.sortBy = next.sortBy;
      this.sortDir = next.sortDir;
      this.refresh();
    },

    async fetchLogs({ append = false } = {}) {
      this.isLoading = true;
      if (!append) {
        this.filters.offset = 0;
        this.clearSelection();
      }
      await this.getAdapterHttpLogs({
        params: this.buildParams(),
        onSuccess: (response) => {
          const data = extractResponseData(response, {}) || {};
          const items = data.items || data.Items || [];
          this.total = data.total ?? data.Total ?? 0;
          this.errorCount = data.errorCount ?? data.ErrorCount ?? 0;
          this.items = append ? [...this.items, ...items] : items;
        },
        onError: (error) => displaySonnerError(error),
      });
      this.isLoading = false;
    },

    async refresh() {
      await this.fetchLogs({ append: false });
    },

    async loadMore() {
      this.filters.offset = this.items.length;
      await this.fetchLogs({ append: true });
    },

    setFilter(key, value) {
      this.filters[key] = value;
      this.refresh();
    },
    resetFilters() {
      for (const key of [
        "connectionId",
        "adapterKey",
        "method",
        "statusClass",
      ]) {
        this.filters[key] = LOG_FILTER_ALL;
      }
      this.refresh();
    },

    async openDetail(row) {
      this.detailOpen = true;
      this.detail = null;
      this.isDetailLoading = true;
      await this.getAdapterHttpLogDetail({
        id: this.rowId(row),
        onSuccess: (response) => {
          this.detail = extractResponseData(response, null);
        },
        onError: (error) => {
          displaySonnerError(error);
          this.detailOpen = false;
        },
      });
      this.isDetailLoading = false;
    },

    connectionLabel(connectionId) {
      const c = this.connections.find(
        (x) => String(x.id) === String(connectionId),
      );
      if (!c) return `#${connectionId}`;
      return c.name
        ? `${c.adapterKey} · ${c.name}`
        : c.adapterKey || `#${connectionId}`;
    },

    async downloadSelected() {
      if (this.selectedIds.length === 0) return;
      this.downloading = true;
      try {
        const details = await mapPool(
          this.selectedIds,
          (id) =>
            new Promise((resolve) => {
              this.getAdapterHttpLogDetail({
                id,
                onSuccess: (response) =>
                  resolve(extractResponseData(response, null)),
                onError: () => resolve(null),
              });
            }),
        );
        downloadJsonFile(details, stampFilename("logs-adapter"));
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.downloading = false;
      }
    },

    async deleteSelected() {
      if (this.selectedIds.length === 0) return;
      const ok = await confirm({
        title: this.$t("components.logs.deleteConfirmTitle"),
        description: this.$t("components.logs.deleteConfirmDescription", {
          count: this.selectedIds.length,
        }),
      });
      if (!ok) return;

      this.deleting = true;
      const ids = [...this.selectedIds];
      await this.deleteAdapterHttpLogs({
        ids,
        onSuccess: () => {
          const removed = new Set(ids);
          this.items = this.items.filter((r) => !removed.has(this.rowId(r)));
          this.total = Math.max(0, this.total - ids.length);
          if (this.detail && removed.has(this.detail.id ?? this.detail.Id)) {
            this.detailOpen = false;
            this.detail = null;
          }
          this.clearSelection();
          displaySonnerSuccess(this.$t("components.logs.deleteSuccess"));
        },
        onError: (error) => displaySonnerError(error),
      });
      this.deleting = false;
    },
  },
  async mounted() {
    await this.fetchConnections();
    await this.fetchLogs();
  },
};
</script>

<template>
  <div class="flex flex-col pb-4">
    <SettingsNavbarTools>
      <PageListToolbar
        v-model="filters.q"
        :placeholder="$t('marketplace.adapterLogsPage.searchByPath')"
        :active-filter-count="activeFilterCount"
        @submit="refresh"
        @reset="resetFilters"
      >
        <template #filters>
          <LogFilterSelect
            :model-value="filters.connectionId"
            :all-value="LOG_FILTER_ALL"
            :all-label="$t('marketplace.adapterLogsPage.allConnections')"
            :placeholder="$t('marketplace.adapterLogsPage.connection')"
            :options="connectionOptions"
            @update:model-value="(v) => setFilter('connectionId', v)"
          />

          <LogFilterSelect
            :model-value="filters.adapterKey"
            :all-value="LOG_FILTER_ALL"
            :all-label="$t('marketplace.adapterLogsPage.allAdapters')"
            :placeholder="$t('marketplace.adapterLogsPage.adapter')"
            :options="adapterKeyOptions"
            @update:model-value="(v) => setFilter('adapterKey', v)"
          />

          <LogHttpMethodFilter
            :model-value="filters.method"
            :all-value="LOG_FILTER_ALL"
            @update:model-value="(v) => setFilter('method', v)"
          />

          <LogHttpStatusFilter
            :model-value="filters.statusClass"
            :all-value="LOG_FILTER_ALL"
            @update:model-value="(v) => setFilter('statusClass', v)"
          />
        </template>
        <template #actions>
          <LogRefreshButton compact :loading="isLoading" @click="refresh" />
        </template>
      </PageListToolbar>
    </SettingsNavbarTools>

    <LogListPanel
      :loading="isLoading && items.length === 0"
      :empty="!isLoading && items.length === 0"
      :empty-icon="ScrollText"
      :empty-title="$t('marketplace.adapterLogsPage.emptyTitle')"
      :empty-description="$t('marketplace.adapterLogsPage.emptyDescription')"
      :show-footer="items.length > 0 || selectedIds.length > 0"
      :footer-count="items.length"
      :footer-total="total"
      :has-more="hasMore"
      :more-loading="isLoading"
      :selected-count="selectedIds.length"
      :downloading="downloading"
      :deleting="deleting"
      @load-more="loadMore"
      @download="downloadSelected"
      @delete="deleteSelected"
      @clear-selection="clearSelection"
    >
      <Table :contain="false" class="w-full">
        <TableHeader
          class="sticky top-0 z-10 bg-background [&_th]:bg-background"
        >
          <TableRow class="hover:bg-transparent">
            <TableHead class="w-10">
              <LogSelectCheckbox
                :model-value="allPageSelected"
                :indeterminate="somePageSelected"
                @update:model-value="toggleSelectAll"
              />
            </TableHead>
            <LogSortHeader
              class="w-36"
              column="createdAt"
              :label="$t('components.logs.columnTime')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
            <LogSortHeader
              class="w-20"
              column="method"
              :label="$t('components.logs.columnMethod')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
            <LogSortHeader
              class="w-auto"
              column="path"
              :label="$t('components.logs.columnPath')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
            <LogSortHeader
              class="w-32"
              column="adapterKey"
              :label="$t('components.logs.columnAdapter')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
            <LogSortHeader
              class="w-24"
              column="durationMs"
              align="right"
              :label="$t('components.logs.columnDuration')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
            <LogSortHeader
              class="w-20"
              column="statusCode"
              align="right"
              :label="$t('components.logs.columnStatus')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="row in items"
            :key="row.id"
            class="cursor-pointer hover:bg-muted/40"
            @click="openDetail(row)"
          >
            <TableCell class="overflow-hidden">
              <LogSelectCheckbox
                :model-value="isSelected(rowId(row))"
                @update:model-value="(v) => toggleSelect(rowId(row), v)"
              />
            </TableCell>
            <TableCell class="overflow-hidden">
              <LogDateCell :value="row.createdAt" :timezone="storeTimezone" />
            </TableCell>
            <TableCell class="overflow-hidden">
              <LogMethodCell :method="row.method" />
            </TableCell>
            <TableCell class="max-w-0 overflow-hidden">
              <LogPathCell :path="row.path || row.url" />
            </TableCell>
            <TableCell class="overflow-hidden text-sm">
              <div class="truncate" :title="connectionLabel(row.connectionId)">
                {{ row.adapterKey }}
              </div>
            </TableCell>
            <TableCell
              class="overflow-hidden text-right text-sm text-muted-foreground tabular-nums"
            >
              {{ $t("components.logs.durationMs", { ms: row.durationMs }) }}
            </TableCell>
            <TableCell class="overflow-hidden text-right">
              <LogStatusBadge :status-code="row.statusCode" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </LogListPanel>

    <AdapterLogDetailSheet
      v-model:open="detailOpen"
      :loading="isDetailLoading"
      :detail="detail"
      :meta-items="detailMeta"
      :title="
        detail
          ? `${detail.method} · ${detail.statusCode}`
          : $t('marketplace.adapterLogsPage.request')
      "
      :description="detail?.path || '…'"
    />
  </div>
</template>
