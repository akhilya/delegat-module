<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import AppImage from "@/components/AppImage.vue";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import Input from "@/components/ui/input/Input.vue";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import { adapterCanSearchPublicCatalog } from "@/lib/adapterCapabilities";
import { PAGE_PRODUCTS, PAGE_TOOLS } from "@/router";
import { Search } from "lucide-vue-next";
import { randomUuidCompact } from "@/utils/uuid";

// Platform Tools catalog: id `catalog-scrape`, mode `page`.

export default {
  components: {
    AppPageChrome,
    AppImage,
    Button,
    Checkbox,
    Input,
    Spinner,
    EmptyState,
    InsetGroup,
    InsetGroupItem,
    FieldGroup,
    SettingsFormField,
    AdapterIcon,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Search,
  },
  data() {
    return {
      loading: true,
      connections: [],
      adapters: [],
      adapterKey: null,
      query: "",
      results: [],
      nextCursor: null,
      hasMore: false,
      selected: {},
      searchBusy: false,
      applyBusy: false,
      applyOperationId: null,
      lastApply: null,
    };
  },
  computed: {
    breadcrumbs() {
      return [
        { label: this.$t("pages.toolsPage.breadcrumb"), to: PAGE_TOOLS },
        { label: this.$t("pages.catalogScrape.breadcrumb") },
      ];
    },
    scrapeAdapters() {
      return this.adapters
        .filter((adapter) => adapterCanSearchPublicCatalog(adapter))
        .slice()
        .sort((a, b) =>
          String(a.displayName || a.key).localeCompare(String(b.displayName || b.key)),
        );
    },
    selectedAdapter() {
      return this.scrapeAdapters.find((a) => a.key === this.adapterKey) || null;
    },
    preferredConnection() {
      return this.connectionFor(this.adapterKey);
    },
    selectedIds() {
      return this.results
        .map((row) => row.externalId)
        .filter((id) => this.selected[id]);
    },
    allSelected() {
      return this.results.length > 0 && this.selectedIds.length === this.results.length;
    },
    someSelected() {
      return this.selectedIds.length > 0 && !this.allSelected;
    },
    applyLiveLog() {
      if (!this.applyBusy || !this.applyOperationId) return "";
      const op = this.$store.state.operations.operations.find(
        (o) => o.id === this.applyOperationId,
      );
      const logs = op?.logs || [];
      const last = logs.length ? logs[logs.length - 1] : null;
      if (last?.message) return last.message;
      if (op?.progress) {
        return this.$t("pages.catalogScrape.applyProgress", {
          completed: op.progress.completed ?? 0,
          total: op.progress.total ?? 0,
        });
      }
      return this.$t("pages.catalogScrape.applyWorking");
    },
  },
  async mounted() {
    await this.bootstrap();
  },
  methods: {
    ...mapActions(["getConnections", "getAvailableAdapters", "searchCatalogScrape", "applyCatalogScrape"]),
    unwrap(response) {
      const data = extractResponseData(response, []);
      if (Array.isArray(data)) return data;
      if (Array.isArray(data?.items)) return data.items;
      return data?.value ?? data;
    },
    unwrapResult(response) {
      const data = extractResponseData(response, {});
      return data?.value && typeof data.value === "object" ? data.value : data;
    },
    adapterSubtitle(adapter) {
      const conn = this.connectionFor(adapter.key);
      return conn?.name || this.$t("pages.catalogScrape.notConnected");
    },
    connectionFor(adapterKey) {
      const list = this.connections.filter((c) => c.adapterKey === adapterKey);
      if (!list.length) return null;
      return [...list].sort((a, b) => {
        if (Boolean(a.isActive) !== Boolean(b.isActive)) return a.isActive ? -1 : 1;
        return (a.id ?? 0) - (b.id ?? 0);
      })[0];
    },
    pickAdapter(adapter) {
      if (this.adapterKey === adapter.key) return;
      this.adapterKey = adapter.key;
      this.results = [];
      this.selected = {};
      this.nextCursor = null;
      this.hasMore = false;
      this.lastApply = null;
      this.$router.replace({
        path: this.$route.path,
        query: { adapter: adapter.key },
      });
    },
    toggleSelectAll(checked) {
      const on = checked === true;
      const next = { ...this.selected };
      for (const row of this.results) {
        next[row.externalId] = on;
      }
      this.selected = next;
    },
    formatPrice(value) {
      if (value == null || value === "") return this.$t("common.emDash");
      const n = Number(value);
      if (Number.isNaN(n)) return String(value);
      return n.toLocaleString(undefined, { maximumFractionDigits: 2 });
    },
    async bootstrap() {
      this.loading = true;
      await Promise.all([
        new Promise((resolve) => {
          this.getConnections({
            onSuccess: (response) => {
              this.connections = this.unwrap(response);
              if (!Array.isArray(this.connections)) this.connections = [];
              resolve();
            },
            onError: (error) => {
              displaySonnerError(error);
              resolve();
            },
          });
        }),
        new Promise((resolve) => {
          this.getAvailableAdapters({
            onSuccess: (response) => {
              this.adapters = this.unwrap(response);
              if (!Array.isArray(this.adapters)) this.adapters = [];
              resolve();
            },
            onError: () => resolve(),
          });
        }),
      ]);
      const requestedAdapter = String(this.$route.query.adapter || "").trim().toLowerCase();
      const requestedConn = Number(this.$route.query.connectionId);
      const fromQuery = this.scrapeAdapters.find((a) => a.key === requestedAdapter);
      const fromConnection = requestedConn
        ? this.connections.find((c) => c.id === requestedConn)
        : null;
      const adapterFromConnection = fromConnection
        ? this.scrapeAdapters.find((a) => a.key === fromConnection.adapterKey)
        : null;
      if (fromQuery) {
        this.adapterKey = fromQuery.key;
      } else if (adapterFromConnection) {
        this.adapterKey = adapterFromConnection.key;
      } else if (this.scrapeAdapters.length > 0) {
        this.adapterKey = this.scrapeAdapters[0].key;
      }
      this.loading = false;
    },
    async runSearch({ append = false } = {}) {
      if (!this.adapterKey || this.searchBusy) return;
      const q = this.query.trim();
      if (!q && !append) {
        this.results = [];
        this.hasMore = false;
        this.nextCursor = null;
        return;
      }
      this.searchBusy = true;
      await this.searchCatalogScrape({
        adapterKey: this.adapterKey,
        query: q,
        pageSize: 50,
        cursor: append ? this.nextCursor : null,
        onSuccess: (response) => {
          const result = this.unwrapResult(response);
          const items = Array.isArray(result.items) ? result.items : [];
          this.results = append ? [...this.results, ...items] : items;
          this.hasMore = Boolean(result.hasMore);
          this.nextCursor = result.nextCursor || null;
          if (!append) this.selected = {};
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.searchBusy = false;
    },
    async confirmImport() {
      if (!this.adapterKey || this.applyBusy || this.selectedIds.length === 0) return;
      const operationId = randomUuidCompact();
      this.applyOperationId = operationId;
      this.applyBusy = true;
      try {
        await this.$store.dispatch("operations/startOperation", {
          id: operationId,
          type: "catalog-scrape",
          title: this.$t("pages.catalogScrape.applyOperationTitle", {
            name:
              this.preferredConnection?.name
              || this.selectedAdapter?.displayName
              || this.adapterKey,
          }),
          connectionId: this.preferredConnection?.id ?? null,
          connectionName: this.preferredConnection?.name
            || this.selectedAdapter?.displayName,
          batchType: "catalog-scrape",
        });
        await this.applyCatalogScrape({
          adapterKey: this.adapterKey,
          publicIds: this.selectedIds,
          operationId,
          onSuccess: (response) => {
            const result = this.unwrapResult(response);
            this.lastApply = result;
            displaySonnerSuccess(
              this.$t("pages.catalogScrape.applyDone", {
                imported: result.imported ?? 0,
                skipped: result.skipped ?? 0,
                failed: result.failed ?? 0,
              }),
            );
            this.selected = {};
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.applyBusy = false;
        this.applyOperationId = null;
      }
    },
    goProducts() {
      this.$router.push(PAGE_PRODUCTS);
    },
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <EmptyState
      v-if="loading"
      loading
      class="min-h-0 flex-1"
    />

    <div
      v-else
      class="page-region-in flex w-full max-w-3xl flex-col"
    >
      <p class="mb-2 text-xs font-medium text-warning">
        {{ $t("pages.toolsPage.stageExperimental") }}
      </p>
      <p class="mb-6 text-sm text-muted-foreground">
        {{ $t("pages.catalogScrape.modelBody") }}
      </p>

      <EmptyState
        v-if="scrapeAdapters.length === 0"
        framed
        :title="$t('pages.catalogScrape.noScrapeAdapters')"
      >
        <template #icon>
          <Search />
        </template>
      </EmptyState>

      <template v-else>
        <InsetGroup
          class="mb-6"
          role="radiogroup"
          :aria-label="$t('pages.catalogScrape.marketplaceListLabel')"
        >
          <InsetGroupItem
            v-for="adapter in scrapeAdapters"
            :key="adapter.key"
            role="radio"
            :aria-checked="adapterKey === adapter.key"
            :selected="adapterKey === adapter.key"
            @click="pickAdapter(adapter)"
          >
            <template #leading>
              <AdapterIcon :adapter-key="adapter.key" class="size-4" />
            </template>
            <p class="truncate">{{ adapter.displayName || adapter.key }}</p>
            <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
              {{ adapterSubtitle(adapter) }}
            </p>
          </InsetGroupItem>
        </InsetGroup>

        <p
          v-if="adapterKey && !preferredConnection"
          class="text-muted-foreground mb-6 text-xs leading-relaxed"
        >
          {{ $t("pages.catalogScrape.noConnectionHint") }}
        </p>
        <p
          v-else-if="preferredConnection"
          class="text-muted-foreground mb-6 text-xs leading-relaxed"
        >
          {{ $t("pages.catalogScrape.usesConnectionHint", { name: preferredConnection.name }) }}
        </p>

        <form
          class="flex flex-col gap-6"
          @submit.prevent="runSearch()"
        >
          <FieldGroup>
            <SettingsFormField
              :label="$t('pages.catalogScrape.queryLabel')"
              label-for="catalog-scrape-query"
              :description="$t('pages.catalogScrape.queryHint')"
            >
              <Input
                id="catalog-scrape-query"
                v-model="query"
                type="search"
                autocomplete="off"
                :disabled="!adapterKey || applyBusy"
                :placeholder="$t('pages.catalogScrape.queryPlaceholder')"
              />
            </SettingsFormField>
          </FieldGroup>
          <div class="flex flex-wrap items-center gap-2">
            <Button type="submit" :disabled="!adapterKey || searchBusy || applyBusy || !query.trim()">
              <Spinner v-if="searchBusy" />
              {{ $t("pages.catalogScrape.searchAction") }}
            </Button>
          </div>
        </form>

        <p class="text-muted-foreground mt-3 text-xs leading-relaxed">
          {{ $t("pages.catalogScrape.unlinkedHint") }}
        </p>

        <EmptyState
          v-if="!searchBusy && results.length === 0 && query.trim()"
          class="mt-8"
          framed
          :title="$t('pages.catalogScrape.emptyResults')"
        >
          <template #icon>
            <Search />
          </template>
        </EmptyState>

        <div
          v-else-if="results.length > 0"
          class="mt-8"
        >
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead class="w-10">
                  <Checkbox
                    :model-value="allSelected"
                    :indeterminate="someSelected"
                    :aria-label="$t('pages.catalogScrape.selectAll')"
                    @update:model-value="toggleSelectAll"
                  />
                </TableHead>
                <TableHead class="w-14">{{ $t("pages.catalogScrape.columnPhoto") }}</TableHead>
                <TableHead>{{ $t("pages.catalogScrape.columnName") }}</TableHead>
                <TableHead>{{ $t("pages.catalogScrape.columnBrand") }}</TableHead>
                <TableHead>{{ $t("pages.catalogScrape.columnSeller") }}</TableHead>
                <TableHead>{{ $t("pages.catalogScrape.columnPrice") }}</TableHead>
                <TableHead>{{ $t("pages.catalogScrape.columnCategory") }}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="row in results"
                :key="row.externalId"
              >
                <TableCell class="w-10">
                  <Checkbox
                    :model-value="Boolean(selected[row.externalId])"
                    :aria-label="$t('pages.catalogScrape.selectRow')"
                    @update:model-value="selected[row.externalId] = $event === true"
                  />
                </TableCell>
                <TableCell class="w-14">
                  <div class="bg-muted size-10 overflow-hidden rounded-md">
                    <AppImage
                      v-if="row.thumbnailUrl"
                      :src="row.thumbnailUrl"
                      :alt="row.name || row.externalId"
                      class="size-10"
                      img-class="relative z-10 size-full object-cover"
                    />
                  </div>
                </TableCell>
                <TableCell class="max-w-0 whitespace-normal">
                  <p class="truncate text-sm font-medium">{{ row.name || row.externalId }}</p>
                  <p class="text-muted-foreground truncate text-xs">{{ row.externalId }}</p>
                </TableCell>
                <TableCell class="text-muted-foreground max-w-[8rem] truncate text-sm">
                  {{ row.brand || $t("common.emDash") }}
                </TableCell>
                <TableCell class="text-muted-foreground max-w-[8rem] truncate text-sm">
                  {{ row.seller || $t("common.emDash") }}
                </TableCell>
                <TableCell class="text-sm tabular-nums">
                  {{ formatPrice(row.price) }}
                </TableCell>
                <TableCell class="text-muted-foreground max-w-[8rem] truncate text-sm">
                  {{ row.categoryName || $t("common.emDash") }}
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div
          v-if="results.length > 0"
          class="mt-4 flex flex-wrap items-center gap-2"
        >
          <Button
            :disabled="applyBusy || selectedIds.length === 0"
            @click="confirmImport"
          >
            <Spinner v-if="applyBusy" />
            {{ $t("pages.catalogScrape.importAction") }}
          </Button>
          <Button
            v-if="hasMore"
            variant="outline"
            :disabled="searchBusy || applyBusy"
            @click="runSearch({ append: true })"
          >
            {{ $t("pages.catalogScrape.loadMore") }}
          </Button>
          <p class="text-muted-foreground text-xs">
            {{ $t("pages.catalogScrape.selectedCount", { count: selectedIds.length }) }}
          </p>
        </div>
        <p
          v-if="applyBusy"
          class="mt-3 text-sm text-muted-foreground"
        >
          {{ applyLiveLog }}
        </p>
        <p
          v-if="lastApply && !applyBusy"
          class="mt-3 text-sm text-muted-foreground"
        >
          {{
            $t("pages.catalogScrape.applyDone", {
              imported: lastApply.imported ?? 0,
              skipped: lastApply.skipped ?? 0,
              failed: lastApply.failed ?? 0,
            })
          }}
          <Button
            variant="link"
            class="h-auto px-1"
            @click="goProducts"
          >
            {{ $t("pages.catalogScrape.openProducts") }}
          </Button>
        </p>
      </template>
    </div>
  </AppPageChrome>
</template>
