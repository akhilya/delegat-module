<script>
import NavSearchField from "@/components/NavSearchField.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  LogToolbar,
  LogListPanel,
  LogRefreshButton,
  LogDateCell,
  LogMethodCell,
  LogPathCell,
  LogStatusBadge,
  LogFilterSelect,
  LogHttpMethodFilter,
  LogHttpStatusFilter,
  ApiRequestLogSheet,
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
import { http } from "@/main";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { ScrollText } from "lucide-vue-next";

export default {
  name: "ApiRequestLogsPanel",
  emits: ["loading"],
  components: {
    NavSearchField,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    LogToolbar,
    LogListPanel,
    LogRefreshButton,
    LogDateCell,
    LogMethodCell,
    LogPathCell,
    LogStatusBadge,
    LogFilterSelect,
    LogHttpMethodFilter,
    LogHttpStatusFilter,
    ApiRequestLogSheet,
    LogSelectCheckbox,
    LogSortHeader,
  },
  props: {
    apiKeys: { type: Array, default: () => [] },
    /** When true, fetch on mount (e.g. panel already visible) */
    autoLoad: { type: Boolean, default: false },
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
      loadedOnce: false,
      selectedIds: [],
      downloading: false,
      deleting: false,
      sortBy: LOG_SORT_DEFAULT.sortBy,
      sortDir: LOG_SORT_DEFAULT.sortDir,
      filters: {
        apiKeyId: LOG_FILTER_ALL,
        method: LOG_FILTER_ALL,
        statusClass: LOG_FILTER_ALL,
        q: "",
        limit: 50,
        offset: 0,
      },
    };
  },
  computed: {
    keyOptions() {
      return (this.apiKeys || []).map((k) => ({
        value: String(k.id),
        label: `${k.name} · ${k.displayHint}`,
      }));
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
          label: this.$t("pages.apiPage.logs.metaTime"),
          value: formatLogDateRaw(this.detail.createdAt, this.storeTimezone),
        },
        {
          label: this.$t("pages.apiPage.logs.metaDuration"),
          value: this.$t("components.logs.durationMs", {
            ms: this.detail.durationMs,
          }),
        },
        {
          label: this.$t("pages.apiPage.logs.metaKey"),
          value: this.detail.keyDisplayHint || "-",
        },
        {
          label: this.$t("pages.apiPage.logs.metaIp"),
          value: this.detail.remoteIpAddress || "-",
        },
      ];
    },
  },
  methods: {
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

    buildParams() {
      const f = this.filters;
      return {
        apiKeyId: f.apiKeyId === LOG_FILTER_ALL ? undefined : f.apiKeyId,
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
      this.$emit("loading", true);
      if (!append) {
        this.filters.offset = 0;
        this.clearSelection();
      }
      try {
        const response = await http.get("/api-keys/logs", {
          params: this.buildParams(),
          cache: false,
        });
        const data =
          extractResponseData(response, {}) ||
          response.data?.value ||
          response.data ||
          {};
        const items = data.items || [];
        this.total = data.total ?? 0;
        this.errorCount = data.errorCount ?? 0;
        this.items = append ? [...this.items, ...items] : items;
        this.loadedOnce = true;
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isLoading = false;
        this.$emit("loading", false);
      }
    },

    async refresh() {
      await this.fetchLogs({ append: false });
    },

    async ensureLoaded() {
      if (!this.loadedOnce) await this.refresh();
    },

    async loadMore() {
      this.filters.offset = this.items.length;
      await this.fetchLogs({ append: true });
    },

    setFilter(key, value) {
      this.filters[key] = value;
      this.refresh();
    },

    async openDetail(row) {
      this.detailOpen = true;
      this.detail = null;
      this.isDetailLoading = true;
      try {
        const response = await http.get(`/api-keys/logs/${this.rowId(row)}`, {
          cache: false,
        });
        this.detail =
          extractResponseData(response, null) ||
          response.data?.value ||
          response.data;
      } catch (error) {
        displaySonnerError(error);
        this.detailOpen = false;
      } finally {
        this.isDetailLoading = false;
      }
    },

    async downloadSelected() {
      if (this.selectedIds.length === 0) return;
      this.downloading = true;
      try {
        const details = await mapPool(this.selectedIds, async (id) => {
          try {
            const response = await http.get(`/api-keys/logs/${id}`, {
              cache: false,
            });
            return (
              extractResponseData(response, null) ||
              response.data?.value ||
              response.data
            );
          } catch {
            return null;
          }
        });
        downloadJsonFile(details, stampFilename("logs-api"));
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
      try {
        await http.post("/api-keys/logs/delete", { ids }, { cache: false });
        const removed = new Set(ids);
        this.items = this.items.filter((r) => !removed.has(this.rowId(r)));
        this.total = Math.max(0, this.total - ids.length);
        if (this.detail && removed.has(this.detail.id ?? this.detail.Id)) {
          this.detailOpen = false;
          this.detail = null;
        }
        this.clearSelection();
        displaySonnerSuccess(this.$t("components.logs.deleteSuccess"));
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.deleting = false;
      }
    },
  },
  mounted() {
    if (this.autoLoad) this.refresh();
  },
};
</script>

<template>
  <div class="flex flex-col gap-3">
    <LogToolbar class="shrink-0">
      <LogFilterSelect
        :model-value="filters.apiKeyId"
        :all-value="LOG_FILTER_ALL"
        :all-label="$t('pages.apiPage.logs.allKeys')"
        :placeholder="$t('pages.apiPage.logs.filterKey')"
        :options="keyOptions"
        trigger-class="h-9 w-full min-w-0 sm:w-44"
        @update:model-value="(v) => setFilter('apiKeyId', v)"
      />

      <LogHttpMethodFilter
        :model-value="filters.method"
        :all-value="LOG_FILTER_ALL"
        trigger-class="h-9 w-full min-w-0 sm:w-28"
        @update:model-value="(v) => setFilter('method', v)"
      />

      <LogHttpStatusFilter
        :model-value="filters.statusClass"
        :all-value="LOG_FILTER_ALL"
        trigger-class="h-9 w-full min-w-0 sm:w-28"
        @update:model-value="(v) => setFilter('statusClass', v)"
      />

      <NavSearchField
        v-model="filters.q"
        class="w-full min-w-0 sm:max-w-[14rem]"
        :placeholder="$t('pages.apiPage.logs.searchPlaceholder')"
        @submit="refresh"
      />
      <template #actions>
        <LogRefreshButton :loading="isLoading" @click="refresh" />
      </template>
    </LogToolbar>

    <LogListPanel
      class="mb-0"
      :loading="isLoading && items.length === 0"
      :empty="!isLoading && items.length === 0"
      :empty-icon="ScrollText"
      :empty-title="$t('pages.apiPage.logs.emptyTitle')"
      :empty-description="$t('pages.apiPage.logs.emptyDescription')"
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
              column="key"
              :label="$t('components.logs.columnKey')"
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
              <LogPathCell :path="row.path" />
            </TableCell>
            <TableCell class="overflow-hidden text-sm">
              <code
                class="block truncate text-xs text-muted-foreground"
                :title="row.keyDisplayHint || ''"
              >
                {{ row.keyDisplayHint || "-" }}
              </code>
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

    <ApiRequestLogSheet
      v-model:open="detailOpen"
      :loading="isDetailLoading"
      :detail="detail"
      :meta-items="detailMeta"
      :title="
        detail
          ? `${detail.method} · ${detail.statusCode}`
          : $t('pages.apiPage.logs.request')
      "
      :description="detail?.path || '…'"
    />
  </div>
</template>
