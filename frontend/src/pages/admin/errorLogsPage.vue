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
  LogStatusBadge,
  ErrorLogDetailSheet,
  LogSelectCheckbox,
  LogSortHeader,
  formatLogDate as formatLogDateRaw,
  downloadJsonFile,
  stampFilename,
  cycleLogSort,
  LOG_SORT_DEFAULT,
} from "@/components/logs";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { AlertTriangle } from "lucide-vue-next";

export default {
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
    LogStatusBadge,
    ErrorLogDetailSheet,
    LogSelectCheckbox,
    LogSortHeader,
    AlertTriangle,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone };
  },
  data() {
    return {
      logs: [],
      query: "",
      isLoading: false,
      deletingLogId: null,
      copiedLogId: null,
      detailOpen: false,
      selected: null,
      selectedIds: [],
      downloading: false,
      deleting: false,
      sortBy: LOG_SORT_DEFAULT.sortBy,
      sortDir: LOG_SORT_DEFAULT.sortDir,
    };
  },
  computed: {
    filteredLogs() {
      const q = this.query.trim().toLowerCase();
      if (!q) return this.logs;
      // Filter only — order stays as returned by the backend.
      return this.logs.filter((log) => {
        const hay = [
          log.message,
          log.exceptionType,
          log.method,
          log.path,
          log.queryString,
          log.userName,
          String(log.userId || ""),
          String(log.statusCode || ""),
          log.traceIdentifier,
        ]
          .filter(Boolean)
          .join(" ")
          .toLowerCase();
        return hay.includes(q);
      });
    },
    detailMeta() {
      if (!this.selected) return [];
      return [
        { label: this.$t("pages.errorLogsPage.metaTime"), value: this.formatLogDate(this.selected.createdAt) },
        { label: this.$t("pages.errorLogsPage.metaMethod"), value: this.selected.method },
        {
          label: this.$t("pages.errorLogsPage.metaUser"),
          value: this.selected.userName || this.selected.userId || "-",
        },
        {
          label: this.$t("pages.errorLogsPage.metaType"),
          value: this.selected.exceptionType || "-",
        },
        {
          label: this.$t("pages.errorLogsPage.codeLabel"),
          value: this.selected.statusCode || "-",
        },
      ];
    },
    footerLabel() {
      const n = this.filteredLogs.length;
      if (!this.query.trim()) return String(n);
      return `${n}${this.$t("pages.errorLogsPage.ofTotal", { total: this.logs.length })}`;
    },
    pageIds() {
      return this.filteredLogs.map((r) => r.id).filter((id) => id != null);
    },
    allPageSelected() {
      return this.pageIds.length > 0 && this.pageIds.every((id) => this.selectedIds.includes(id));
    },
    somePageSelected() {
      return !this.allPageSelected && this.pageIds.some((id) => this.selectedIds.includes(id));
    },
  },
  methods: {
    ...mapActions(["getErrorLogs", "deleteErrorLog", "deleteErrorLogs"]),
    formatLogDate(value) {
      return formatLogDateRaw(value, this.storeTimezone);
    },

    toggleSort(column) {
      const next = cycleLogSort(this.sortBy, this.sortDir, column);
      this.sortBy = next.sortBy;
      this.sortDir = next.sortDir;
      this.fetchLogs();
    },

    isSelected(id) {
      return this.selectedIds.includes(id);
    },

    clearSelection() {
      this.selectedIds = [];
    },

    toggleSelect(id, checked) {
      if (checked === true) {
        if (!this.selectedIds.includes(id)) this.selectedIds = [...this.selectedIds, id];
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

    async fetchLogs() {
      this.isLoading = true;
      this.clearSelection();
      await this.getErrorLogs({
        limit: 200,
        sortBy: this.sortBy,
        sortDir: this.sortDir,
        onSuccess: (response) => {
          this.logs = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    openDetail(log) {
      this.selected = log;
      this.detailOpen = true;
    },

    formatPath(log) {
      return `${log.path || ""}${log.queryString || ""}`;
    },

    buildLogDetails(log) {
      return [
        `${this.$t("pages.errorLogsPage.metaTime")}: ${this.formatLogDate(log.createdAt)}`,
        `${this.$t("pages.errorLogsPage.clipboardStatus")}: ${log.statusCode}`,
        `${this.$t("pages.errorLogsPage.metaMethod")}: ${log.method}`,
        `${this.$t("pages.errorLogsPage.clipboardPath")}: ${this.formatPath(log)}`,
        `${this.$t("pages.errorLogsPage.messageLabel")}: ${log.message || "-"}`,
        `${this.$t("pages.errorLogsPage.clipboardErrorType")}: ${log.exceptionType || "-"}`,
        `${this.$t("pages.errorLogsPage.metaUser")}: ${log.userName || log.userId || "-"}`,
        `Trace ID: ${log.traceIdentifier || "-"}`,
        "",
        "Stack trace:",
        log.stackTrace || "-",
      ].join("\n");
    },

    async copyLogDetails(log) {
      try {
        await navigator.clipboard.writeText(this.buildLogDetails(log));
        this.copiedLogId = log.id;
        displaySonnerSuccess(this.$t("pages.errorLogsPage.copySuccess"));
        window.setTimeout(() => {
          if (this.copiedLogId === log.id) this.copiedLogId = null;
        }, 1500);
      } catch {
        displaySonnerError(this.$t("pages.errorLogsPage.copyError"));
      }
    },

    async removeLog(log) {
      if (!log?.id) return;
      this.deletingLogId = log.id;
      await this.deleteErrorLog({
        id: log.id,
        onSuccess: () => {
          this.logs = this.logs.filter((item) => item.id !== log.id);
          this.selectedIds = this.selectedIds.filter((id) => id !== log.id);
          if (this.selected?.id === log.id) {
            this.detailOpen = false;
            this.selected = null;
          }
          displaySonnerSuccess(this.$t("pages.errorLogsPage.deleteSuccess"));
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.deletingLogId = null;
    },

    downloadSelected() {
      if (this.selectedIds.length === 0) return;
      this.downloading = true;
      try {
        const idSet = new Set(this.selectedIds);
        const details = this.logs.filter((log) => idSet.has(log.id));
        downloadJsonFile(details, stampFilename("logs-error"));
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
      await this.deleteErrorLogs({
        ids,
        onSuccess: () => {
          const removed = new Set(ids);
          this.logs = this.logs.filter((item) => !removed.has(item.id));
          if (this.selected && removed.has(this.selected.id)) {
            this.detailOpen = false;
            this.selected = null;
          }
          this.clearSelection();
          displaySonnerSuccess(this.$t("components.logs.deleteSuccess"));
        },
        onError: (error) => displaySonnerError(error),
      });
      this.deleting = false;
    },
  },
  mounted() {
    this.fetchLogs();
  },
};
</script>


<template>
  <div class="flex flex-col pb-4">
    <LogToolbar class="mb-3 shrink-0">
      <NavSearchField
        v-model="query"
        class="w-full min-w-0 sm:max-w-xs"
        :placeholder="$t('pages.errorLogsPage.searchPlaceholder')"
      />
      <template #actions>
        <LogRefreshButton :loading="isLoading" @click="fetchLogs" />
      </template>
    </LogToolbar>

    <LogListPanel
      :loading="isLoading"
      :empty="!isLoading && filteredLogs.length === 0"
      :empty-icon="AlertTriangle"
      :empty-title="
        query.trim() && logs.length
          ? $t('pages.errorLogsPage.emptyNotFoundTitle')
          : $t('pages.errorLogsPage.emptyNoErrorsTitle')
      "
      :empty-description="
        query.trim() && logs.length
          ? $t('pages.errorLogsPage.emptyNotFoundDescription')
          : $t('pages.errorLogsPage.emptyNoErrorsDescription')
      "
      :show-footer="filteredLogs.length > 0 || selectedIds.length > 0"
      :footer-label="footerLabel"
      :selected-count="selectedIds.length"
      :downloading="downloading"
      :deleting="deleting"
      @download="downloadSelected"
      @delete="deleteSelected"
      @clear-selection="clearSelection"
    >
      <Table :contain="false" class="w-full">
        <TableHeader class="sticky top-0 z-10 bg-background [&_th]:bg-background">
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
              column="message"
              :label="$t('components.logs.columnError')"
              :sort-by="sortBy"
              :sort-dir="sortDir"
              @sort="toggleSort"
            />
            <LogSortHeader
              class="w-32"
              column="userName"
              :label="$t('components.logs.columnUser')"
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
            v-for="log in filteredLogs"
            :key="log.id"
            class="cursor-pointer hover:bg-muted/40"
            @click="openDetail(log)"
          >
            <TableCell class="overflow-hidden">
              <LogSelectCheckbox
                :model-value="isSelected(log.id)"
                @update:model-value="(v) => toggleSelect(log.id, v)"
              />
            </TableCell>
            <TableCell class="overflow-hidden">
              <LogDateCell :value="log.createdAt" :timezone="storeTimezone" />
            </TableCell>
            <TableCell class="overflow-hidden">
              <LogMethodCell :method="log.method" />
            </TableCell>
            <TableCell class="max-w-0 overflow-hidden">
              <div class="min-w-0 overflow-hidden">
                <div class="truncate font-medium" :title="log.message">
                  {{ log.message }}
                </div>
                <div
                  class="truncate font-mono text-xs text-muted-foreground"
                  :title="formatPath(log)"
                >
                  {{ log.exceptionType || formatPath(log) }}
                </div>
              </div>
            </TableCell>
            <TableCell class="overflow-hidden text-sm">
              <div class="truncate" :title="String(log.userName || log.userId || '')">
                {{ log.userName || log.userId || "-" }}
              </div>
            </TableCell>
            <TableCell class="overflow-hidden text-right">
              <LogStatusBadge :status-code="log.statusCode" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </LogListPanel>

    <ErrorLogDetailSheet
      v-model:open="detailOpen"
      :log="selected"
      :meta-items="detailMeta"
      :copied="selected ? copiedLogId === selected.id : false"
      :deleting="selected ? deletingLogId === selected.id : false"
      @copy="copyLogDetails"
      @remove="removeLog"
    />
  </div>
</template>
