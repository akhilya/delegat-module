<script>
import Input from "@/components/ui/input/Input.vue";
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
  LogDateCell,
  LogMethodCell,
  LogPathCell,
  LogStatusBadge,
  LogFilterSelect,
  LogHttpMethodFilter,
  LogHttpStatusFilter,
  LogExchangeDetail,
  LogCodeBlock,
  LOG_FILTER_ALL,
  formatLogDate as formatLogDateRaw,
} from "@/components/logs";
import { http } from "@/main";
import {
  displaySonnerError,
  extractResponseData,
} from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { ScrollText } from "lucide-vue-next";

export default {
  name: "ApiRequestLogsPanel",
  emits: ["loading"],
  components: {
    Input,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    LogToolbar,
    LogListPanel,
    LogDateCell,
    LogMethodCell,
    LogPathCell,
    LogStatusBadge,
    LogFilterSelect,
    LogHttpMethodFilter,
    LogHttpStatusFilter,
    LogExchangeDetail,
    LogCodeBlock,
    ScrollText,
  },
  props: {
    apiKeys: { type: Array, default: () => [] },
    /** When true, fetch on mount (e.g. panel already visible) */
    autoLoad: { type: Boolean, default: false },
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone, LOG_FILTER_ALL };
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
    summaryLabel() {
      if (this.errorCount === 0) return this.$t("pages.apiPage.logs.noErrors");
      return this.$t("pages.apiPage.logs.errorCount", { count: this.errorCount });
    },
    keyOptions() {
      return (this.apiKeys || []).map((k) => ({
        value: String(k.id),
        label: `${k.name} · ${k.displayHint}`,
      }));
    },
    hasMore() {
      return this.filters.offset + this.items.length < this.total;
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
          value: this.$t("components.logs.durationMs", { ms: this.detail.durationMs }),
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
    buildParams() {
      const f = this.filters;
      return {
        apiKeyId: f.apiKeyId === LOG_FILTER_ALL ? undefined : f.apiKeyId,
        method: f.method === LOG_FILTER_ALL ? undefined : f.method,
        statusClass: f.statusClass === LOG_FILTER_ALL ? undefined : f.statusClass,
        q: f.q || undefined,
        limit: f.limit,
        offset: f.offset,
      };
    },

    async fetchLogs({ append = false } = {}) {
      this.isLoading = true;
      this.$emit("loading", true);
      if (!append) this.filters.offset = 0;
      try {
        const response = await http.get("/api-keys/logs", {
          params: this.buildParams(),
          cache: false,
        });
        const data = extractResponseData(response, {}) || response.data?.value || response.data || {};
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
        const response = await http.get(`/api-keys/logs/${row.id ?? row.Id}`, { cache: false });
        this.detail = extractResponseData(response, null) || response.data?.value || response.data;
      } catch (error) {
        displaySonnerError(error);
        this.detailOpen = false;
      } finally {
        this.isDetailLoading = false;
      }
    },
  },
  mounted() {
    if (this.autoLoad) this.refresh();
  },
};
</script>

<template>
  <div>
    <LogToolbar :summary="summaryLabel" :summary-alert="errorCount > 0">
      <LogFilterSelect
        :model-value="filters.apiKeyId"
        :all-value="LOG_FILTER_ALL"
        :all-label="$t('pages.apiPage.logs.allKeys')"
        :placeholder="$t('pages.apiPage.logs.filterKey')"
        :options="keyOptions"
        trigger-class="h-9 w-full sm:w-52"
        @update:model-value="(v) => setFilter('apiKeyId', v)"
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

      <Input
        v-model="filters.q"
        class="h-9 w-full sm:max-w-xs"
        :placeholder="$t('pages.apiPage.logs.searchPlaceholder')"
        @keydown.enter="refresh"
      />
    </LogToolbar>

    <LogListPanel
      :loading="isLoading && items.length === 0"
      :empty="!isLoading && items.length === 0"
      :empty-icon="ScrollText"
      :empty-title="$t('pages.apiPage.logs.emptyTitle')"
      :empty-description="$t('pages.apiPage.logs.emptyDescription')"
      :show-footer="items.length > 0"
      :footer-count="items.length"
      :footer-total="total"
      :has-more="hasMore"
      :more-loading="isLoading"
      @load-more="loadMore"
    >
      <Table class="table-fixed">
        <TableHeader>
          <TableRow>
            <TableHead class="w-40">{{ $t("pages.apiPage.logs.columnDate") }}</TableHead>
            <TableHead class="w-20">{{ $t("pages.apiPage.logs.columnMethod") }}</TableHead>
            <TableHead class="min-w-0">{{ $t("pages.apiPage.logs.columnPath") }}</TableHead>
            <TableHead class="w-36">{{ $t("pages.apiPage.logs.columnKey") }}</TableHead>
            <TableHead class="w-24 text-right">{{ $t("pages.apiPage.logs.columnDuration") }}</TableHead>
            <TableHead class="w-20 text-right">{{ $t("pages.apiPage.logs.columnCode") }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="row in items"
            :key="row.id"
            class="cursor-pointer hover:bg-muted/40"
            @click="openDetail(row)"
          >
            <TableCell>
              <LogDateCell :value="row.createdAt" :timezone="storeTimezone" />
            </TableCell>
            <TableCell>
              <LogMethodCell :method="row.method" />
            </TableCell>
            <TableCell class="max-w-0">
              <LogPathCell :path="row.path" />
            </TableCell>
            <TableCell class="text-sm">
              <code class="text-xs text-muted-foreground">{{ row.keyDisplayHint || "-" }}</code>
            </TableCell>
            <TableCell class="text-right text-sm text-muted-foreground tabular-nums">
              {{ $t("components.logs.durationMs", { ms: row.durationMs }) }}
            </TableCell>
            <TableCell class="text-right">
              <LogStatusBadge :status-code="row.statusCode" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </LogListPanel>

    <LogExchangeDetail
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
      body-download-base-url="/api-keys/logs"
      json-bodies
    >
      <template #overview-extra="{ detail: d }">
        <div v-if="d.userAgent" class="space-y-1">
          <div class="text-xs text-muted-foreground">{{ $t("pages.apiPage.logs.userAgent") }}</div>
          <p class="break-all text-xs">{{ d.userAgent }}</p>
        </div>
      </template>

      <template #request-extra="{ detail: d }">
        <LogCodeBlock
          v-if="d.queryString"
          label="Query"
          :text="d.queryString"
        />
      </template>
    </LogExchangeDetail>
  </div>
</template>
