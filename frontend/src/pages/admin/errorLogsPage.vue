<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { PageHeader } from "@/components/ui/page-header";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { AlertTriangle, ChevronDown, ChevronRight, Loader2, RefreshCw, Trash2 } from "lucide-vue-next";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Badge,
    PageHeader,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    AlertTriangle,
    ChevronDown,
    ChevronRight,
    Loader2,
    RefreshCw,
    Trash2,
  },
  data() {
    return {
      logs: [],
      expandedLogId: null,
      isLoading: false,
      deletingLogId: null,
      breadcrumbs: [{ label: "Логи ошибок" }],
    };
  },
  methods: {
    ...mapActions(["getErrorLogs", "deleteErrorLog"]),

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

    toggleExpanded(logId) {
      this.expandedLogId = this.expandedLogId === logId ? null : logId;
    },

    async removeLog(log) {
      this.deletingLogId = log.id;
      await this.deleteErrorLog({
        id: log.id,
        onSuccess: () => {
          this.logs = this.logs.filter((item) => item.id !== log.id);
          if (this.expandedLogId === log.id) {
            this.expandedLogId = null;
          }
          displaySonnerSuccess("Лог удалён");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.deletingLogId = null;
    },

    formatDate(value) {
      if (!value) return "-";
      return new Date(value).toLocaleString("ru-RU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      });
    },

    formatPath(log) {
      return `${log.path}${log.queryString || ""}`;
    },
  },
  mounted() {
    this.fetchLogs();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Логи ошибок"
      description="Последние сохранённые серверные ошибки"
    >
      <Button variant="outline" :disabled="isLoading" @click="fetchLogs">
        <Loader2 v-if="isLoading" class="size-4 mr-2 animate-spin" />
        <RefreshCw v-else class="size-4 mr-2" />
        Обновить
      </Button>
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="logs.length === 0"
      :empty-icon="AlertTriangle"
      empty-title="Ошибок нет"
      empty-description="Новые неожиданные ошибки backend появятся здесь автоматически"
    >
      <div class="overflow-hidden rounded-lg border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-10"></TableHead>
              <TableHead class="w-44">Время</TableHead>
              <TableHead class="w-24">Статус</TableHead>
              <TableHead class="w-28">Метод</TableHead>
              <TableHead>Ошибка</TableHead>
              <TableHead class="w-44">Пользователь</TableHead>
              <TableHead class="w-20"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <template v-for="log in logs" :key="log.id">
              <TableRow>
                <TableCell>
                  <Button variant="ghost" size="icon" @click="toggleExpanded(log.id)">
                    <ChevronDown v-if="expandedLogId === log.id" class="size-4" />
                    <ChevronRight v-else class="size-4" />
                    <span class="sr-only">Показать детали</span>
                  </Button>
                </TableCell>
                <TableCell class="text-sm text-muted-foreground">
                  {{ formatDate(log.createdAt) }}
                </TableCell>
                <TableCell>
                  <Badge variant="destructive">{{ log.statusCode }}</Badge>
                </TableCell>
                <TableCell>
                  <code class="text-xs">{{ log.method }}</code>
                </TableCell>
                <TableCell>
                  <div class="min-w-0">
                    <div class="truncate font-medium">{{ log.message }}</div>
                    <div class="truncate text-xs text-muted-foreground">
                      {{ log.exceptionType }}
                    </div>
                  </div>
                </TableCell>
                <TableCell class="text-sm">
                  {{ log.userName || log.userId || "-" }}
                </TableCell>
                <TableCell class="text-right">
                  <Button
                    variant="ghost"
                    size="icon"
                    :disabled="deletingLogId === log.id"
                    @click="removeLog(log)"
                  >
                    <Loader2 v-if="deletingLogId === log.id" class="size-4 animate-spin" />
                    <Trash2 v-else class="size-4" />
                    <span class="sr-only">Удалить лог</span>
                  </Button>
                </TableCell>
              </TableRow>
              <TableRow v-if="expandedLogId === log.id" class="bg-muted/30 hover:bg-muted/30">
                <TableCell colspan="7" class="p-0">
                  <div class="grid gap-3 p-4 text-sm">
                    <div class="grid gap-1">
                      <div class="text-xs font-medium text-muted-foreground">Запрос</div>
                      <code class="rounded-md bg-background px-3 py-2 text-xs">
                        {{ formatPath(log) }}
                      </code>
                    </div>
                    <div class="grid gap-1">
                      <div class="text-xs font-medium text-muted-foreground">Trace ID</div>
                      <code class="rounded-md bg-background px-3 py-2 text-xs">
                        {{ log.traceIdentifier }}
                      </code>
                    </div>
                    <div class="grid gap-1">
                      <div class="text-xs font-medium text-muted-foreground">Stack trace</div>
                      <pre class="max-h-96 overflow-auto rounded-md bg-background p-3 text-xs leading-relaxed">{{ log.stackTrace }}</pre>
                    </div>
                  </div>
                </TableCell>
              </TableRow>
            </template>
          </TableBody>
        </Table>
      </div>
    </PageSection>
  </Page>
</template>
