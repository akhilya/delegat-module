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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import {
  Activity,
  Check,
  Copy,
  Loader2,
  RefreshCw,
  ScrollText,
} from "lucide-vue-next";

const NONE = "__all__";

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
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Sheet,
    SheetContent,
    SheetDescription,
    SheetHeader,
    SheetTitle,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    Activity,
    Check,
    Copy,
    Loader2,
    RefreshCw,
    ScrollText,
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
      copiedKey: null,
      connections: [],
      filters: {
        connectionId: NONE,
        adapterKey: NONE,
        method: NONE,
        statusClass: NONE,
        q: "",
        limit: 50,
        offset: 0,
      },
    };
  },
  computed: {
    summaryLabel() {
      if (this.errorCount === 0) return "Ошибок нет";
      return `${this.errorCount} с ошибкой`;
    },
    summaryOk() {
      return this.errorCount === 0;
    },
    adapterOptions() {
      const keys = new Set(
        (this.connections || [])
          .map((c) => c.adapterKey)
          .filter(Boolean),
      );
      return [...keys].sort();
    },
    hasMore() {
      return this.filters.offset + this.items.length < this.total;
    },
  },
  methods: {
    ...mapActions([
      "getAdapterHttpLogs",
      "getAdapterHttpLogDetail",
      "getConnections",
    ]),

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
        connectionId: f.connectionId === NONE ? undefined : f.connectionId,
        adapterKey: f.adapterKey === NONE ? undefined : f.adapterKey,
        method: f.method === NONE ? undefined : f.method,
        statusClass: f.statusClass === NONE ? undefined : f.statusClass,
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
      return c.name ? `${c.adapterKey} · ${c.name}` : c.adapterKey || `#${connectionId}`;
    },

    formatDate(value) {
      if (!value) return "—";
      return new Date(value).toLocaleString("ru-RU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      });
    },

    statusVariant(code) {
      if (code >= 200 && code < 300) return "secondary";
      if (code >= 400 && code < 500) return "destructive";
      if (code >= 500) return "destructive";
      if (code === 0) return "outline";
      return "outline";
    },

    prettyJson(raw) {
      if (!raw) return "—";
      try {
        return JSON.stringify(JSON.parse(raw), null, 2);
      } catch {
        return raw;
      }
    },

    async copyText(key, text) {
      try {
        await navigator.clipboard.writeText(text || "");
        this.copiedKey = key;
        displaySonnerSuccess("Скопировано");
        window.setTimeout(() => {
          if (this.copiedKey === key) this.copiedKey = null;
        }, 1500);
      } catch {
        displaySonnerError("Не удалось скопировать");
      }
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
      title="Логи адаптеров"
      description="Исходящие HTTP-запросы к API маркетплейсов"
    >
      <Button variant="outline" :disabled="isLoading" @click="refresh">
        <Loader2 v-if="isLoading" class="mr-2 size-4 animate-spin" />
        <RefreshCw v-else class="mr-2 size-4" />
        Обновить
      </Button>
    </PageHeader>

    <div
      class="mb-4 flex flex-col gap-3 rounded-lg border bg-card p-3 sm:flex-row sm:flex-wrap sm:items-center"
    >
      <Select
        :model-value="filters.connectionId"
        @update:model-value="
          (v) => {
            filters.connectionId = v;
            refresh();
          }
        "
      >
        <SelectTrigger class="h-9 w-full sm:w-52">
          <SelectValue placeholder="Подключение" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="NONE">Все подключения</SelectItem>
          <SelectItem
            v-for="c in connections"
            :key="c.id"
            :value="String(c.id)"
          >
            {{ c.name || c.adapterKey }} · #{{ c.id }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Select
        :model-value="filters.adapterKey"
        @update:model-value="
          (v) => {
            filters.adapterKey = v;
            refresh();
          }
        "
      >
        <SelectTrigger class="h-9 w-full sm:w-40">
          <SelectValue placeholder="Адаптер" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="NONE">Все адаптеры</SelectItem>
          <SelectItem v-for="k in adapterOptions" :key="k" :value="k">
            {{ k }}
          </SelectItem>
        </SelectContent>
      </Select>

      <Select
        :model-value="filters.method"
        @update:model-value="
          (v) => {
            filters.method = v;
            refresh();
          }
        "
      >
        <SelectTrigger class="h-9 w-full sm:w-32">
          <SelectValue placeholder="Метод" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="NONE">Любой</SelectItem>
          <SelectItem value="GET">GET</SelectItem>
          <SelectItem value="POST">POST</SelectItem>
          <SelectItem value="PUT">PUT</SelectItem>
          <SelectItem value="DELETE">DELETE</SelectItem>
        </SelectContent>
      </Select>

      <Select
        :model-value="filters.statusClass"
        @update:model-value="
          (v) => {
            filters.statusClass = v;
            refresh();
          }
        "
      >
        <SelectTrigger class="h-9 w-full sm:w-36">
          <SelectValue placeholder="Код" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="NONE">Любой код</SelectItem>
          <SelectItem value="2xx">2xx</SelectItem>
          <SelectItem value="4xx">4xx</SelectItem>
          <SelectItem value="5xx">5xx</SelectItem>
          <SelectItem value="error">Ошибки</SelectItem>
        </SelectContent>
      </Select>

      <Input
        v-model="filters.q"
        class="h-9 w-full sm:max-w-xs"
        placeholder="Поиск по path…"
        @keydown.enter="refresh"
      />

      <Badge
        :variant="summaryOk ? 'secondary' : 'destructive'"
        class="h-8 shrink-0 gap-1.5 px-3"
      >
        <Check v-if="summaryOk" class="size-3.5" />
        <Activity v-else class="size-3.5" />
        {{ summaryLabel }}
      </Badge>
    </div>

    <PageSection
      :loading="isLoading && items.length === 0"
      :empty="!isLoading && items.length === 0"
      :empty-icon="ScrollText"
      empty-title="Запросов пока нет"
      empty-description="После вызовов API адаптеров записи появятся здесь автоматически"
    >
      <div class="overflow-hidden rounded-lg border">
        <div class="overflow-x-auto">
          <Table class="table-fixed">
            <TableHeader>
              <TableRow>
                <TableHead class="w-40">Дата и время</TableHead>
                <TableHead class="w-20">Метод</TableHead>
                <TableHead class="min-w-0">Ресурс</TableHead>
                <TableHead class="w-36">Адаптер</TableHead>
                <TableHead class="w-24 text-right">Ответ</TableHead>
                <TableHead class="w-20 text-right">Код</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="row in items"
                :key="row.id"
                class="cursor-pointer hover:bg-muted/40"
                @click="openDetail(row)"
              >
                <TableCell class="text-sm text-muted-foreground">
                  {{ formatDate(row.createdAt) }}
                </TableCell>
                <TableCell>
                  <code class="text-xs font-medium">{{ row.method }}</code>
                </TableCell>
                <TableCell class="max-w-0">
                  <div
                    class="truncate font-mono text-xs"
                    :title="row.path || row.url"
                  >
                    {{ row.path || row.url }}
                  </div>
                </TableCell>
                <TableCell class="text-sm">
                  <div class="truncate" :title="connectionLabel(row.connectionId)">
                    {{ row.adapterKey }}
                  </div>
                </TableCell>
                <TableCell class="text-right text-sm text-muted-foreground tabular-nums">
                  {{ row.durationMs }} мс
                </TableCell>
                <TableCell class="text-right">
                  <Badge :variant="statusVariant(row.statusCode)">
                    {{ row.statusCode || "—" }}
                  </Badge>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>

      <div
        v-if="items.length"
        class="mt-3 flex items-center justify-between text-sm text-muted-foreground"
      >
        <span>
          {{ items.length }} из {{ total }}
        </span>
        <Button
          v-if="hasMore"
          variant="outline"
          size="sm"
          :disabled="isLoading"
          @click="loadMore"
        >
          Ещё
        </Button>
      </div>
    </PageSection>

    <Sheet v-model:open="detailOpen">
      <SheetContent
        side="right"
        class="flex w-full flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
      >
        <SheetHeader class="border-b px-4 py-3 text-left">
          <SheetTitle class="font-mono text-base">
            <template v-if="detail">
              {{ detail.method }} · {{ detail.statusCode }}
            </template>
            <template v-else>Запрос</template>
          </SheetTitle>
          <SheetDescription class="truncate font-mono text-xs">
            {{ detail?.path || "…" }}
          </SheetDescription>
        </SheetHeader>

        <div
          v-if="isDetailLoading"
          class="flex flex-1 items-center justify-center text-muted-foreground"
        >
          <Loader2 class="size-5 animate-spin" />
        </div>

        <Tabs
          v-else-if="detail"
          default-value="overview"
          class="flex min-h-0 flex-1 flex-col"
        >
          <TabsList class="mx-4 mt-3 grid w-auto grid-cols-3">
            <TabsTrigger value="overview">Обзор</TabsTrigger>
            <TabsTrigger value="request">Запрос</TabsTrigger>
            <TabsTrigger value="response">Ответ</TabsTrigger>
          </TabsList>

          <div class="min-h-0 flex-1 overflow-y-auto px-4 py-3">
            <TabsContent value="overview" class="mt-0 space-y-3">
              <div class="grid gap-2 text-sm">
                <div class="flex justify-between gap-2">
                  <span class="text-muted-foreground">Время</span>
                  <span>{{ formatDate(detail.createdAt) }}</span>
                </div>
                <div class="flex justify-between gap-2">
                  <span class="text-muted-foreground">Длительность</span>
                  <span class="tabular-nums">{{ detail.durationMs }} мс</span>
                </div>
                <div class="flex justify-between gap-2">
                  <span class="text-muted-foreground">Адаптер</span>
                  <span>{{ detail.adapterKey }}</span>
                </div>
                <div class="flex justify-between gap-2">
                  <span class="text-muted-foreground">Подключение</span>
                  <span class="truncate text-right">
                    {{ connectionLabel(detail.connectionId) }}
                  </span>
                </div>
                <div class="flex justify-between gap-2">
                  <span class="text-muted-foreground">Код</span>
                  <Badge :variant="statusVariant(detail.statusCode)">
                    {{ detail.statusCode }}
                  </Badge>
                </div>
              </div>
              <div class="space-y-1">
                <div class="text-xs font-medium text-muted-foreground">URL</div>
                <pre
                  class="overflow-x-auto rounded-md border bg-muted/40 p-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap break-all"
                >{{ detail.url }}</pre>
              </div>
              <div v-if="detail.errorMessage" class="space-y-1">
                <div class="text-xs font-medium text-destructive">Ошибка</div>
                <pre
                  class="overflow-x-auto rounded-md border border-destructive/30 bg-destructive/5 p-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap"
                >{{ detail.errorMessage }}</pre>
              </div>
              <div class="grid gap-1 text-xs text-muted-foreground">
                <div>storeId: {{ detail.storeId }}</div>
                <div>connectionId: {{ detail.connectionId }}</div>
                <div v-if="detail.operationId">
                  operationId: {{ detail.operationId }}
                </div>
              </div>
            </TabsContent>

            <TabsContent value="request" class="mt-0 space-y-3">
              <div class="flex items-center justify-between">
                <span class="text-xs font-medium text-muted-foreground"
                  >Заголовки</span
                >
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7"
                  @click="
                    copyText('req-h', prettyJson(detail.requestHeadersJson))
                  "
                >
                  <Copy class="mr-1 size-3.5" />
                  Копировать
                </Button>
              </div>
              <pre
                class="max-h-40 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap"
              >{{ prettyJson(detail.requestHeadersJson) }}</pre>

              <div class="flex items-center justify-between">
                <span class="text-xs font-medium text-muted-foreground"
                  >Тело</span
                >
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7"
                  @click="copyText('req-b', detail.requestBody || '')"
                >
                  <Copy class="mr-1 size-3.5" />
                  Копировать
                </Button>
              </div>
              <pre
                class="max-h-80 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap"
              >{{ prettyJson(detail.requestBody) }}</pre>
            </TabsContent>

            <TabsContent value="response" class="mt-0 space-y-3">
              <div class="flex items-center justify-between">
                <span class="text-xs font-medium text-muted-foreground"
                  >Заголовки</span
                >
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7"
                  @click="
                    copyText('res-h', prettyJson(detail.responseHeadersJson))
                  "
                >
                  <Copy class="mr-1 size-3.5" />
                  Копировать
                </Button>
              </div>
              <pre
                class="max-h-40 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap"
              >{{ prettyJson(detail.responseHeadersJson) }}</pre>

              <div class="flex items-center justify-between">
                <span class="text-xs font-medium text-muted-foreground"
                  >Тело</span
                >
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-7"
                  @click="copyText('res-b', detail.responseBody || '')"
                >
                  <Copy class="mr-1 size-3.5" />
                  Копировать
                </Button>
              </div>
              <pre
                class="max-h-80 overflow-auto rounded-md border bg-muted/40 p-2 font-mono text-[11px] leading-relaxed whitespace-pre-wrap"
              >{{ prettyJson(detail.responseBody) }}</pre>
            </TabsContent>
          </div>
        </Tabs>
      </SheetContent>
    </Sheet>
  </div>
</template>
