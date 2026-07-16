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
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
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
import {
  displaySonnerError,
  extractResponseData,
} from "@/store/sonnerHelper";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { Loader2, RefreshCw, ScrollText } from "lucide-vue-next";

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
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    LogToolbar,
    LogTableFrame,
    LogCodeBlock,
    LogMetaGrid,
    LogDetailSheet,
    Loader2,
    RefreshCw,
    ScrollText,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone };
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
    adapterOptions() {
      const keys = new Set(
        (this.connections || []).map((c) => c.adapterKey).filter(Boolean),
      );
      return [...keys].sort();
    },
    hasMore() {
      return this.filters.offset + this.items.length < this.total;
    },
    detailMeta() {
      if (!this.detail) return [];
      return [
        { label: "Время", value: this.formatLogDate(this.detail.createdAt) },
        { label: "Длительность", value: `${this.detail.durationMs} мс` },
        { label: "Адаптер", value: this.detail.adapterKey },
        {
          label: "Подключение",
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
    httpStatusVariant,
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
      title="Логи адаптеров"
      description="Исходящие HTTP-запросы к API маркетплейсов"
    >
      <Button variant="outline" :disabled="isLoading" @click="refresh">
        <Loader2 v-if="isLoading" class="mr-2 size-4 animate-spin" />
        <RefreshCw v-else class="mr-2 size-4" />
        Обновить
      </Button>
    </PageHeader>

    <LogToolbar :summary="summaryLabel" :summary-alert="errorCount > 0">
      <Select
        :model-value="filters.connectionId"
        @update:model-value="(v) => setFilter('connectionId', v)"
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
        @update:model-value="(v) => setFilter('adapterKey', v)"
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
        @update:model-value="(v) => setFilter('method', v)"
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
        @update:model-value="(v) => setFilter('statusClass', v)"
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
    </LogToolbar>

    <PageSection
      :loading="isLoading && items.length === 0"
      :empty="!isLoading && items.length === 0"
      :empty-icon="ScrollText"
      empty-title="Запросов пока нет"
      empty-description="После вызовов API адаптеров записи появятся здесь автоматически"
    >
      <LogTableFrame>
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
              <TableCell class="text-sm text-muted-foreground tabular-nums">
                {{ formatLogDate(row.createdAt) }}
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
                <div
                  class="truncate"
                  :title="connectionLabel(row.connectionId)"
                >
                  {{ row.adapterKey }}
                </div>
              </TableCell>
              <TableCell
                class="text-right text-sm text-muted-foreground tabular-nums"
              >
                {{ row.durationMs }} мс
              </TableCell>
              <TableCell class="text-right">
                <Badge :variant="httpStatusVariant(row.statusCode)">
                  {{ row.statusCode || "—" }}
                </Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>

        <template v-if="items.length" #footer>
          <span class="tabular-nums">{{ items.length }} из {{ total }}</span>
          <Button
            v-if="hasMore"
            variant="outline"
            size="sm"
            :disabled="isLoading"
            @click="loadMore"
          >
            Ещё
          </Button>
        </template>
      </LogTableFrame>
    </PageSection>

    <LogDetailSheet
      v-model:open="detailOpen"
      :loading="isDetailLoading"
      :title="
        detail ? `${detail.method} · ${detail.statusCode}` : 'Запрос'
      "
      :description="detail?.path || '…'"
    >
      <Tabs
        v-if="detail"
        default-value="overview"
        class="flex min-h-0 flex-1 flex-col"
      >
        <TabsList class="grid w-full grid-cols-3">
          <TabsTrigger value="overview">Обзор</TabsTrigger>
          <TabsTrigger value="request">Запрос</TabsTrigger>
          <TabsTrigger value="response">Ответ</TabsTrigger>
        </TabsList>

        <div class="mt-4 min-h-0 flex-1 overflow-y-auto pb-2">
          <TabsContent value="overview" class="mt-0 space-y-4">
            <LogMetaGrid :items="detailMeta">
              <template #item-3="{ item }">
                <span class="truncate">{{ item.value }}</span>
              </template>
            </LogMetaGrid>
            <div class="flex items-center justify-between gap-2 text-sm">
              <span class="text-muted-foreground">Код</span>
              <Badge :variant="httpStatusVariant(detail.statusCode)">
                {{ detail.statusCode }}
              </Badge>
            </div>
            <LogCodeBlock label="URL" :text="detail.url" max-height-class="max-h-28" />
            <LogCodeBlock
              v-if="detail.errorMessage"
              label="Ошибка"
              :text="detail.errorMessage"
              tone="destructive"
              max-height-class="max-h-40"
            />
            <div class="space-y-1 text-xs text-muted-foreground">
              <div>storeId: {{ detail.storeId }}</div>
              <div>connectionId: {{ detail.connectionId }}</div>
              <div v-if="detail.operationId">
                operationId: {{ detail.operationId }}
              </div>
            </div>
          </TabsContent>

          <TabsContent value="request" class="mt-0 space-y-3">
            <LogCodeBlock
              label="Заголовки"
              :text="detail.requestHeadersJson"
              json
              max-height-class="max-h-40"
            />
            <LogCodeBlock
              label="Тело"
              :text="detail.requestBody"
              json
            />
          </TabsContent>

          <TabsContent value="response" class="mt-0 space-y-3">
            <LogCodeBlock
              label="Заголовки"
              :text="detail.responseHeadersJson"
              json
              max-height-class="max-h-40"
            />
            <LogCodeBlock
              label="Тело"
              :text="detail.responseBody"
              json
            />
          </TabsContent>
        </div>
      </Tabs>
    </LogDetailSheet>
  </div>
</template>
