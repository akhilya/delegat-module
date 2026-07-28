<script>
import { PageHeader } from "@/components/ui/page-header";
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
  LogRefreshButton,
  LogDateCell,
  LogMethodCell,
  LogPathCell,
  LogStatusBadge,
  LogFilterSelect,
  LogHttpMethodFilter,
  LogHttpStatusFilter,
  LogExchangeDetail,
  LogCodeBlock,
  LogMetaGrid,
  LOG_FILTER_ALL,
  formatLogDate as formatLogDateRaw,
} from "@/components/logs";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  extractResponseData,
} from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { ScrollText } from "lucide-vue-next";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { PAGE_SETTINGS } from "@/router";

export default {
  components: {
    PageHeader,
    Input,
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
    LogExchangeDetail,
    LogCodeBlock,
    LogMetaGrid,
    ScrollText,
    Alert,
    AlertDescription,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone, LOG_FILTER_ALL };
  },
  data() {
    return {
      PAGE_SETTINGS,
      items: [],
      total: 0,
      errorCount: 0,
      isLoading: false,
      isDetailLoading: false,
      detailOpen: false,
      detail: null,
      connections: [],
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
    summaryLabel() {
      if (this.errorCount === 0) return this.$t("marketplace.adapterLogsPage.noErrors");
      return this.$t("marketplace.adapterLogsPage.errorCount", { count: this.errorCount });
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
    detailMeta() {
      if (!this.detail) return [];
      return [
        { label: this.$t("marketplace.adapterLogsPage.dateTime"), value: this.formatLogDate(this.detail.createdAt) },
        {
          label: this.$t("marketplace.adapterLogsPage.duration"),
          value: this.$t("components.logs.durationMs", { ms: this.detail.durationMs }),
        },
        { label: this.$t("marketplace.adapterLogsPage.adapter"), value: this.detail.adapterKey },
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
      "getConnections",
    ]),
    formatLogDate(value) {
      return formatLogDateRaw(value, this.storeTimezone);
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
        connectionId: f.connectionId === LOG_FILTER_ALL ? undefined : f.connectionId,
        adapterKey: f.adapterKey === LOG_FILTER_ALL ? undefined : f.adapterKey,
        method: f.method === LOG_FILTER_ALL ? undefined : f.method,
        statusClass: f.statusClass === LOG_FILTER_ALL ? undefined : f.statusClass,
        q: f.q || undefined,
        limit: f.limit,
        offset: f.offset,
      };
    },

    async fetchLogs({ append = false } = {}) {
      this.isLoading = true;
      if (!append) this.filters.offset = 0;
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

    async openDetail(row) {
      this.detailOpen = true;
      this.detail = null;
      this.isDetailLoading = true;
      await this.getAdapterHttpLogDetail({
        id: row.id ?? row.Id,
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
  },
  async mounted() {
    await this.fetchConnections();
    await this.fetchLogs();
  },
};
</script>

<template>
  <div>
    <PageHeader
      :title="$t('marketplace.adapterLogsPage.title')"
      :description="$t('marketplace.adapterLogsPage.description')"
    >
      <LogRefreshButton :loading="isLoading" @click="refresh" />
    </PageHeader>

    <Alert class="mb-4">
      <AlertDescription class="text-sm text-muted-foreground">
        {{ $t("marketplace.adapterLogsPage.loggingHint") }}
        <RouterLink :to="PAGE_SETTINGS" class="underline underline-offset-2 hover:text-foreground">
          {{ $t("marketplace.adapterLogsPage.loggingHintLink") }}
        </RouterLink>
      </AlertDescription>
    </Alert>

    <LogToolbar :summary="summaryLabel" :summary-alert="errorCount > 0">
      <LogFilterSelect
        :model-value="filters.connectionId"
        :all-value="LOG_FILTER_ALL"
        :all-label="$t('marketplace.adapterLogsPage.allConnections')"
        :placeholder="$t('marketplace.adapterLogsPage.connection')"
        :options="connectionOptions"
        trigger-class="h-9 w-full sm:w-52"
        @update:model-value="(v) => setFilter('connectionId', v)"
      />

      <LogFilterSelect
        :model-value="filters.adapterKey"
        :all-value="LOG_FILTER_ALL"
        :all-label="$t('marketplace.adapterLogsPage.allAdapters')"
        :placeholder="$t('marketplace.adapterLogsPage.adapter')"
        :options="adapterKeyOptions"
        trigger-class="h-9 w-full sm:w-40"
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

      <Input
        v-model="filters.q"
        class="h-9 w-full sm:max-w-xs"
        :placeholder="$t('marketplace.adapterLogsPage.searchByPath')"
        @keydown.enter="refresh"
      />
    </LogToolbar>

    <LogListPanel
      :loading="isLoading && items.length === 0"
      :empty="!isLoading && items.length === 0"
      :empty-icon="ScrollText"
      :empty-title="$t('marketplace.adapterLogsPage.emptyTitle')"
      :empty-description="$t('marketplace.adapterLogsPage.emptyDescription')"
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
            <TableHead class="w-40">{{ $t("marketplace.adapterLogsPage.dateTime") }}</TableHead>
            <TableHead class="w-20">{{ $t("marketplace.adapterLogsPage.method") }}</TableHead>
            <TableHead class="min-w-0">{{ $t("marketplace.adapterLogsPage.resource") }}</TableHead>
            <TableHead class="w-36">{{ $t("marketplace.adapterLogsPage.adapter") }}</TableHead>
            <TableHead class="w-24 text-right">{{ $t("marketplace.adapterLogsPage.response") }}</TableHead>
            <TableHead class="w-20 text-right">{{ $t("marketplace.adapterLogsPage.code") }}</TableHead>
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
              <LogPathCell :path="row.path || row.url" />
            </TableCell>
            <TableCell class="text-sm">
              <div class="truncate" :title="connectionLabel(row.connectionId)">
                {{ row.adapterKey }}
              </div>
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
          : $t('marketplace.adapterLogsPage.request')
      "
      :description="detail?.path || '…'"
      show-headers
      json-bodies
      body-download-base-url="/adapter-http-logs"
    >
      <template #meta>
        <LogMetaGrid :items="detailMeta">
          <template #item-3="{ item }">
            <span class="truncate">{{ item.value }}</span>
          </template>
        </LogMetaGrid>
      </template>

      <template #overview-extra="{ detail: d }">
        <LogCodeBlock label="URL" :text="d.url" max-height-class="max-h-28" />
        <div class="space-y-1 text-xs text-muted-foreground">
          <div>storeId: {{ d.storeId }}</div>
          <div>connectionId: {{ d.connectionId }}</div>
          <div v-if="d.operationId">operationId: {{ d.operationId }}</div>
        </div>
      </template>
    </LogExchangeDetail>
  </div>
</template>
