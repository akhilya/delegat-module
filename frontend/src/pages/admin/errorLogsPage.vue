<script>
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
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
  LogTableFrame,
  LogCodeBlock,
  LogMetaGrid,
  LogDetailSheet,
  formatLogDate as formatLogDateRaw,
  httpStatusVariant,
} from "@/components/logs";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import {
  AlertTriangle,
  Check,
  Copy,
  Loader2,
  RefreshCw,
  Trash2,
} from "lucide-vue-next";

export default {
  components: {
    PageSection,
    Button,
    Badge,
    PageHeader,
    Input,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    LogToolbar,
    LogTableFrame,
    LogCodeBlock,
    LogMetaGrid,
    LogDetailSheet,
    AlertTriangle,
    Check,
    Copy,
    Loader2,
    RefreshCw,
    Trash2,
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
    };
  },
  computed: {
    filteredLogs() {
      const q = this.query.trim().toLowerCase();
      if (!q) return this.logs;
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
    summaryLabel() {
      const n = this.logs.length;
      if (n === 0) return "Ошибок нет";
      return `${n} запис${n === 1 ? "ь" : n < 5 ? "и" : "ей"}`;
    },
    detailMeta() {
      if (!this.selected) return [];
      return [
        { label: "Время", value: this.formatLogDate(this.selected.createdAt) },
        { label: "Метод", value: this.selected.method },
        {
          label: "Пользователь",
          value: this.selected.userName || this.selected.userId || "—",
        },
        {
          label: "Тип",
          value: this.selected.exceptionType || "—",
        },
      ];
    },
  },
  methods: {
    ...mapActions(["getErrorLogs", "deleteErrorLog"]),
    httpStatusVariant,
    formatLogDate(value) {
      return formatLogDateRaw(value, this.storeTimezone);
    },

    async fetchLogs() {
      this.isLoading = true;
      await this.getErrorLogs({
        limit: 200,
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
        `Время: ${formatLogDate(log.createdAt)}`,
        `Статус: ${log.statusCode}`,
        `Метод: ${log.method}`,
        `Путь: ${this.formatPath(log)}`,
        `Сообщение: ${log.message || "—"}`,
        `Тип ошибки: ${log.exceptionType || "—"}`,
        `Пользователь: ${log.userName || log.userId || "—"}`,
        `Trace ID: ${log.traceIdentifier || "—"}`,
        "",
        "Stack trace:",
        log.stackTrace || "—",
      ].join("\n");
    },

    async copyLogDetails(log) {
      try {
        await navigator.clipboard.writeText(this.buildLogDetails(log));
        this.copiedLogId = log.id;
        displaySonnerSuccess("Детали ошибки скопированы");
        window.setTimeout(() => {
          if (this.copiedLogId === log.id) this.copiedLogId = null;
        }, 1500);
      } catch {
        displaySonnerError("Не удалось скопировать детали ошибки");
      }
    },

    async removeLog(log) {
      const ok = await confirm({
        title: "Удалить лог ошибки?",
        description: "Запись будет удалена безвозвратно.",
      });
      if (!ok) return;

      this.deletingLogId = log.id;
      await this.deleteErrorLog({
        id: log.id,
        onSuccess: () => {
          this.logs = this.logs.filter((item) => item.id !== log.id);
          if (this.selected?.id === log.id) {
            this.detailOpen = false;
            this.selected = null;
          }
          displaySonnerSuccess("Лог удалён");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.deletingLogId = null;
    },
  },
  mounted() {
    this.fetchLogs();
  },
};
</script>

<template>
  <div>
    <PageHeader
      title="Логи ошибок"
      description="Последние сохранённые серверные ошибки"
    >
      <Button variant="outline" :disabled="isLoading" @click="fetchLogs">
        <Loader2 v-if="isLoading" class="mr-2 size-4 animate-spin" />
        <RefreshCw v-else class="mr-2 size-4" />
        Обновить
      </Button>
    </PageHeader>

    <LogToolbar :summary="summaryLabel" :summary-alert="logs.length > 0">
      <Input
        v-model="query"
        class="h-9 w-full sm:max-w-sm"
        placeholder="Поиск по сообщению, пути, пользователю…"
      />
    </LogToolbar>

    <PageSection
      :loading="isLoading"
      :empty="!isLoading && filteredLogs.length === 0"
      :empty-icon="AlertTriangle"
      :empty-title="
        query.trim() && logs.length ? 'Ничего не найдено' : 'Ошибок нет'
      "
      :empty-description="
        query.trim() && logs.length
          ? 'Попробуйте изменить поисковый запрос'
          : 'Новые неожиданные ошибки backend появятся здесь автоматически'
      "
    >
      <LogTableFrame>
        <Table class="table-fixed">
          <TableHeader>
            <TableRow>
              <TableHead class="w-40">Дата и время</TableHead>
              <TableHead class="w-20">Метод</TableHead>
              <TableHead class="min-w-0">Ошибка</TableHead>
              <TableHead class="w-36">Пользователь</TableHead>
              <TableHead class="w-20 text-right">Код</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow
              v-for="log in filteredLogs"
              :key="log.id"
              class="cursor-pointer hover:bg-muted/40"
              @click="openDetail(log)"
            >
              <TableCell class="text-sm text-muted-foreground tabular-nums">
                {{ formatLogDate(log.createdAt) }}
              </TableCell>
              <TableCell>
                <code class="text-xs font-medium">{{ log.method }}</code>
              </TableCell>
              <TableCell class="max-w-0">
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
              <TableCell class="truncate text-sm">
                {{ log.userName || log.userId || "—" }}
              </TableCell>
              <TableCell class="text-right">
                <Badge :variant="httpStatusVariant(log.statusCode)">
                  {{ log.statusCode }}
                </Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>

        <template v-if="filteredLogs.length" #footer>
          <span class="tabular-nums">
            {{ filteredLogs.length
            }}<template v-if="query.trim()">
              из {{ logs.length }}</template
            >
          </span>
        </template>
      </LogTableFrame>
    </PageSection>

    <LogDetailSheet
      v-model:open="detailOpen"
      :title="
        selected
          ? `${selected.method} · ${selected.statusCode}`
          : 'Ошибка'
      "
      :description="selected ? formatPath(selected) : '…'"
    >
      <template v-if="selected" #actions>
        <Button
          variant="outline"
          size="sm"
          class="h-8"
          @click="copyLogDetails(selected)"
        >
          <Check v-if="copiedLogId === selected.id" class="mr-1.5 size-3.5" />
          <Copy v-else class="mr-1.5 size-3.5" />
          Копировать всё
        </Button>
        <Button
          variant="destructive"
          size="sm"
          class="h-8"
          :disabled="deletingLogId === selected.id"
          @click="removeLog(selected)"
        >
          <Loader2
            v-if="deletingLogId === selected.id"
            class="mr-1.5 size-3.5 animate-spin"
          />
          <Trash2 v-else class="mr-1.5 size-3.5" />
          Удалить
        </Button>
      </template>

      <div
        v-if="selected"
        class="min-h-0 flex-1 space-y-4 overflow-y-auto pb-2"
      >
        <LogMetaGrid :items="detailMeta" />
        <div class="flex items-center justify-between gap-2 text-sm">
          <span class="text-muted-foreground">Код</span>
          <Badge :variant="httpStatusVariant(selected.statusCode)">
            {{ selected.statusCode }}
          </Badge>
        </div>
        <LogCodeBlock
          label="Сообщение"
          :text="selected.message || '—'"
          tone="destructive"
          max-height-class="max-h-28"
        />
        <LogCodeBlock
          label="Запрос"
          :text="formatPath(selected)"
          max-height-class="max-h-24"
        />
        <LogCodeBlock
          label="Trace ID"
          :text="selected.traceIdentifier || '—'"
          max-height-class="max-h-20"
        />
        <LogCodeBlock
          label="Stack trace"
          :text="selected.stackTrace || '—'"
          max-height-class="max-h-96"
        />
      </div>
    </LogDetailSheet>
  </div>
</template>
