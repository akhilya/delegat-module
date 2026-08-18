<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import Input from "@/components/ui/input/Input.vue";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
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
import {
  adapterCanImportAll,
  adapterCanLink,
} from "@/lib/adapterCapabilities";
import { catalogConnectMatchQueue } from "@/lib/catalogConnectMatchQueue";
import { PAGE_PRODUCTS, PAGE_TOOLS } from "@/router";
import { http } from "@/main";
import { Cable } from "lucide-vue-next";
import { randomUuidCompact } from "@/utils/uuid";

const STEP_SOURCE = "source";
const STEP_IMPORT = "import";
const STEP_MATCH = "match";
const STEP_REVIEW = "review";
const STEP_SUMMARY = "summary";

// Platform Tools catalog: id `catalog-connect`, mode `page`.

export default {
  components: {
    AppPageChrome,
    Button,
    Checkbox,
    Input,
    Spinner,
    EmptyState,
    InsetGroup,
    InsetGroupItem,
    Tabs,
    TabsList,
    TabsTrigger,
    AdapterIcon,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Cable,
  },
  data() {
    return {
      loading: true,
      connections: [],
      adapters: [],
      localProductCount: 0,
      step: STEP_SOURCE,
      sourceId: null,
      matchConnectionId: null,
      remainingMatchIds: [],
      importBatchId: null,
      importBusy: false,
      previewBusy: false,
      applyBusy: false,
      applyOperationId: null,
      previewRows: [],
      previewMeta: {
        matchedCount: 0,
        ambiguousCount: 0,
        unmatchedCount: 0,
        alreadyLinkedCount: 0,
        mode: "fromRemote",
      },
      reviewTab: "matched",
      selectedMatched: {},
      ambiguousPicks: {},
      importUnmatchedIds: {},
      pastedRemoteIds: {},
      searchQuery: "",
      searchResults: [],
      searchBusy: false,
      searchForExternalId: null,
      lastApply: null,
      STEP_SOURCE,
      STEP_IMPORT,
      STEP_MATCH,
      STEP_REVIEW,
      STEP_SUMMARY,
    };
  },
  computed: {
    breadcrumbs() {
      return [
        { label: this.$t("pages.toolsPage.breadcrumb"), to: PAGE_TOOLS },
        { label: this.$t("pages.catalogConnect.breadcrumb") },
      ];
    },
    hasLocalProducts() {
      return this.localProductCount > 0;
    },
    fromLocal() {
      return this.previewMeta.mode === "fromLocal";
    },
    importCapable() {
      return this.connections.filter((c) => this.canImportAll(c));
    },
    linkCapable() {
      return this.connections.filter((c) => this.canLink(c));
    },
    matchQueueIds() {
      return catalogConnectMatchQueue(this.linkCapable, {
        sourceId: this.sourceId,
        localProductCount: this.localProductCount,
      });
    },
    sourceConnection() {
      return this.connections.find((c) => c.id === this.sourceId) || null;
    },
    matchConnection() {
      return this.connections.find((c) => c.id === this.matchConnectionId) || null;
    },
    matchedRows() {
      return this.previewRows.filter((r) => r.status === "matched");
    },
    ambiguousRows() {
      return this.previewRows.filter((r) => r.status === "ambiguous");
    },
    unmatchedRows() {
      return this.previewRows.filter((r) => r.status === "unmatched");
    },
    visibleRows() {
      if (this.reviewTab === "ambiguous") return this.ambiguousRows;
      if (this.reviewTab === "unmatched") return this.unmatchedRows;
      return this.matchedRows;
    },
    reviewHeaderLocal() {
      return this.$t("pages.catalogConnect.columnLocal");
    },
    reviewHeaderRemote() {
      return this.matchConnection?.name
        || this.$t("pages.catalogConnect.columnRemoteFallback");
    },
    reviewHeaderLeft() {
      return this.fromLocal ? this.reviewHeaderLocal : this.reviewHeaderRemote;
    },
    reviewHeaderRight() {
      return this.fromLocal ? this.reviewHeaderRemote : this.reviewHeaderLocal;
    },
    reviewSelectableRows() {
      if (this.reviewTab === "matched") return this.matchedRows;
      if (this.reviewTab === "unmatched" && !this.fromLocal) return this.unmatchedRows;
      return [];
    },
    reviewHasSelectAll() {
      return this.reviewSelectableRows.length > 0;
    },
    allReviewSelected() {
      const rows = this.reviewSelectableRows;
      if (rows.length === 0) return false;
      return rows.every((row) => this.isReviewRowSelected(row));
    },
    someReviewSelected() {
      if (this.allReviewSelected) return false;
      return this.reviewSelectableRows.some((row) => this.isReviewRowSelected(row));
    },
    summaryRows() {
      const result = this.lastApply;
      if (!result) return [];
      return [
        {
          key: "linked",
          label: this.$t("pages.catalogConnect.statLinkedLabel"),
          count: result.linked ?? 0,
        },
        {
          key: "mappings",
          label: this.$t("pages.catalogConnect.statMappingsLabel"),
          count: result.mappingsCreated ?? 0,
        },
        {
          key: "imported",
          label: this.$t("pages.catalogConnect.statImportedLabel"),
          count: result.imported ?? 0,
        },
        {
          key: "skipped",
          label: this.$t("pages.catalogConnect.statSkippedLabel"),
          count: result.skipped ?? 0,
        },
        {
          key: "fields",
          label: this.$t("pages.catalogConnect.statFieldsLabel"),
          count: result.attributesImported ?? 0,
        },
      ];
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
        return this.$t("pages.catalogConnect.applyProgress", {
          completed: op.progress.completed ?? 0,
          total: op.progress.total ?? 0,
        });
      }
      return this.$t("pages.catalogConnect.applyWorking");
    },
  },
  async mounted() {
    await this.bootstrap();
  },
  methods: {
    ...mapActions([
      "getConnections",
      "getAvailableAdapters",
      "importUnlinkedProducts",
      "previewCatalogMatch",
      "applyCatalogMatch",
      "repairCatalogMatch",
      "getAllProducts",
    ]),
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
    adapterFor(conn) {
      return this.adapters.find((a) => a.key === conn.adapterKey) || conn;
    },
    canImportAll(conn) {
      return adapterCanImportAll(this.adapterFor(conn));
    },
    canLink(conn) {
      return adapterCanLink(this.adapterFor(conn));
    },
    adapterName(conn) {
      return this.adapterFor(conn)?.displayName || conn.adapterKey;
    },
    rowKey(row) {
      return row.rowKey || row.externalProductId || `local:${row.productId}`;
    },
    isReviewRowSelected(row) {
      const key = this.rowKey(row);
      if (row.status === "matched") return Boolean(this.selectedMatched[key]);
      if (row.status === "unmatched" && !this.fromLocal) {
        return Boolean(this.importUnmatchedIds[key]);
      }
      return false;
    },
    toggleSelectAllVisible(checked) {
      const on = checked === true;
      if (this.reviewTab === "matched") {
        const next = { ...this.selectedMatched };
        for (const row of this.matchedRows) {
          next[this.rowKey(row)] = on;
        }
        this.selectedMatched = next;
        return;
      }
      if (this.reviewTab === "unmatched" && !this.fromLocal) {
        const next = { ...this.importUnmatchedIds };
        for (const row of this.unmatchedRows) {
          next[this.rowKey(row)] = on;
        }
        this.importUnmatchedIds = next;
      }
    },
    async bootstrap() {
      this.loading = true;
      await Promise.all([this.loadConnections(), this.loadProductCount()]);
      this.loading = false;
      this.startFlow();
    },
    async loadProductCount() {
      await new Promise((resolve) => {
        this.getAllProducts({
          searchQuery: "",
          startIndex: 0,
          pageSize: 1,
          onSuccess: (response) => {
            const data = this.unwrapResult(response);
            this.localProductCount = Number(data?.totalCount ?? data?.total ?? data?.items?.length ?? 0) || 0;
            resolve();
          },
          onError: () => {
            this.localProductCount = 0;
            resolve();
          },
        });
      });
    },
    async loadConnections() {
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
            onError: (error) => {
              displaySonnerError(error);
              resolve();
            },
          });
        }),
      ]);
    },
    startFlow() {
      this.remainingMatchIds = [...this.matchQueueIds];
      if (this.hasLocalProducts) {
        this.beginNextMatch();
        return;
      }
      if (this.importCapable.length === 1) {
        this.sourceId = this.importCapable[0].id;
      }
    },
    continueFromSource() {
      if (!this.sourceId) return;
      const source = this.sourceConnection;
      const alreadyImported = (source?.productLinksCount || 0) > 0;
      this.remainingMatchIds = [...this.matchQueueIds];
      if (alreadyImported) {
        this.beginNextMatch();
      } else {
        this.step = STEP_IMPORT;
      }
    },
    async runSourceImport() {
      if (!this.sourceId || this.importBusy) return;
      this.importBusy = true;
      try {
        await this.importUnlinkedProducts({
          connectionId: this.sourceId,
          onSuccess: async (response) => {
            const result = this.unwrapResult(response);
            this.importBatchId = result.batchId;
            this.$store.dispatch("operations/openPanel");
            if (result.queued > 0 && result.batchId) {
              await this.waitForOperation(result.batchId);
            }
            displaySonnerSuccess(
              this.$t("pages.catalogConnect.importQueued", { count: result.queued ?? 0 }),
            );
            await this.loadConnections();
            await this.loadProductCount();
            this.remainingMatchIds = [...this.matchQueueIds];
            this.beginNextMatch();
          },
          onError: (error) => {
            displaySonnerError(error);
          },
        });
      } finally {
        this.importBusy = false;
      }
    },
    beginNextMatch() {
      if (this.remainingMatchIds.length === 0) {
        this.step = STEP_SUMMARY;
        if (!this.lastApply) {
          this.lastApply = { linked: 0, imported: 0, skipped: 0, failed: 0, mappingsCreated: 0, done: true };
        }
        return;
      }
      this.matchConnectionId = this.remainingMatchIds.shift();
      this.step = STEP_MATCH;
      this.loadPreview();
    },
    async skipCurrentMatch() {
      await this.repairCurrentConnectionMappings();
      this.beginNextMatch();
    },
    async loadPreview() {
      if (!this.matchConnectionId || this.previewBusy) return;
      this.previewBusy = true;
      this.previewRows = [];
      try {
        const all = [];
        let page = 1;
        let meta = null;
        while (page <= 50) {
          const result = await new Promise((resolve, reject) => {
            this.previewCatalogMatch({
              connectionId: this.matchConnectionId,
              page,
              pageSize: 200,
              status: "all",
              onSuccess: (response) => resolve(this.unwrapResult(response)),
              onError: (error) => reject(error),
            });
          });
          meta = result;
          const items = result.items || [];
          all.push(...items);
          if (all.length >= (result.total ?? items.length)) break;
          page += 1;
        }
        if (all.length === 0) {
          await this.repairCurrentConnectionMappings();
          this.previewBusy = false;
          this.beginNextMatch();
          return;
        }
        this.previewRows = all;
        this.previewMeta = {
          matchedCount: meta?.matchedCount ?? all.filter((r) => r.status === "matched").length,
          ambiguousCount: meta?.ambiguousCount ?? all.filter((r) => r.status === "ambiguous").length,
          unmatchedCount: meta?.unmatchedCount ?? all.filter((r) => r.status === "unmatched").length,
          alreadyLinkedCount: meta?.alreadyLinkedCount ?? 0,
          mode: meta?.mode || "fromRemote",
        };
        const selected = {};
        for (const row of all) {
          if (row.status === "matched") selected[this.rowKey(row)] = true;
        }
        this.selectedMatched = selected;
        this.ambiguousPicks = {};
        this.importUnmatchedIds = {};
        this.pastedRemoteIds = {};
        this.reviewTab = all.some((r) => r.status === "matched")
          ? "matched"
          : all.some((r) => r.status === "ambiguous")
            ? "ambiguous"
            : "unmatched";
        this.step = STEP_REVIEW;
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.previewBusy = false;
      }
    },
    async confirmMatches() {
      if (!this.matchConnectionId || this.applyBusy) return;
      const items = [];
      for (const row of this.matchedRows) {
        if (this.selectedMatched[this.rowKey(row)] && row.productId && row.externalProductId) {
          items.push({
            productId: row.productId,
            externalProductId: row.externalProductId,
            externalCategoryId: row.externalCategoryId,
            externalCategoryName: row.externalCategoryName,
          });
        }
      }
      for (const row of this.ambiguousRows) {
        const pick = this.ambiguousPicks[this.rowKey(row)];
        if (pick && row.externalProductId) {
          items.push({
            productId: Number(pick),
            externalProductId: row.externalProductId,
            externalCategoryId: row.externalCategoryId,
            externalCategoryName: row.externalCategoryName,
          });
        }
      }
      if (this.fromLocal) {
        for (const row of this.unmatchedRows) {
          const pasted = (this.pastedRemoteIds[this.rowKey(row)] || "").trim();
          if (pasted && row.productId) {
            items.push({
              productId: row.productId,
              externalProductId: pasted,
            });
          }
        }
      }
      const unmatchedToImport = this.fromLocal
        ? []
        : this.unmatchedRows.filter((row) => this.importUnmatchedIds[this.rowKey(row)]);
      for (const row of unmatchedToImport) {
        items.push({
          externalProductId: row.externalProductId,
          externalCategoryId: row.externalCategoryId,
          externalCategoryName: row.externalCategoryName,
        });
      }

      if (items.length === 0) {
        await this.skipCurrentMatch();
        return;
      }

      const operationId = randomUuidCompact();
      this.applyOperationId = operationId;
      this.applyBusy = true;
      try {
        await this.$store.dispatch("operations/startOperation", {
          id: operationId,
          type: "catalog-match",
          title: this.$t("pages.catalogConnect.applyOperationTitle", {
            name: this.matchConnection?.name || this.adapterName(this.matchConnection),
          }),
          connectionId: this.matchConnectionId,
          connectionName: this.matchConnection?.name,
          batchType: "catalog-match",
        });
        await this.applyCatalogMatch({
          connectionId: this.matchConnectionId,
          items,
          importUnmatched: unmatchedToImport.length > 0,
          operationId,
          onSuccess: (response) => {
            const result = this.unwrapResult(response);
            this.lastApply = result;
            displaySonnerSuccess(
              this.$t("pages.catalogConnect.applyDone", {
                linked: result.linked ?? 0,
                imported: result.imported ?? 0,
                mappings: result.mappingsCreated ?? 0,
                fields: result.attributesImported ?? 0,
              }),
            );
            this.beginNextMatch();
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
    async repairCurrentConnectionMappings() {
      if (!this.matchConnectionId) return;
      await this.repairCatalogMatch({
        connectionId: this.matchConnectionId,
        onSuccess: (response) => {
          const result = this.unwrapResult(response);
          const created = Number(result.mappingsCreated ?? 0) || 0;
          if (created <= 0) return;
          const previous = this.lastApply || {
            linked: 0,
            imported: 0,
            skipped: 0,
            failed: 0,
            mappingsCreated: 0,
          };
          this.lastApply = {
            ...previous,
            mappingsCreated: (previous.mappingsCreated ?? 0) + created,
          };
          displaySonnerSuccess(
            this.$t("pages.catalogConnect.repairDone", { count: created }),
          );
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },
    async searchProducts() {
      const q = this.searchQuery.trim();
      if (!q) {
        this.searchResults = [];
        return;
      }
      this.searchBusy = true;
      await this.getAllProducts({
        searchQuery: q,
        startIndex: 0,
        pageSize: 8,
        onSuccess: (response) => {
          const data = response?.data?.value ?? response?.data;
          this.searchResults = data?.items || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.searchBusy = false;
    },
    pickSearchResult(product) {
      if (!this.searchForExternalId) return;
      const row = this.previewRows.find((r) => this.rowKey(r) === this.searchForExternalId);
      if (!row) return;
      row.status = "matched";
      row.productId = product.id;
      row.productName = product.name;
      row.productSku = product.sku;
      this.selectedMatched = {
        ...this.selectedMatched,
        [this.rowKey(row)]: true,
      };
      const nextPicks = { ...this.ambiguousPicks };
      delete nextPicks[this.rowKey(row)];
      this.ambiguousPicks = nextPicks;
      const nextUnmatched = { ...this.importUnmatchedIds };
      delete nextUnmatched[this.rowKey(row)];
      this.importUnmatchedIds = nextUnmatched;
      this.searchForExternalId = null;
      this.searchQuery = "";
      this.searchResults = [];
    },
    async waitForOperation(operationId) {
      for (let i = 0; i < 450; i += 1) {
        try {
          const { data } = await http.get(`/operations/${operationId}/status`);
          const status = data?.status || data?.value?.status;
          if (["completed", "failed", "partial", "cancelled"].includes(status)) return;
        } catch {
          /* keep polling */
        }
        await new Promise((r) => setTimeout(r, 2000));
      }
    },
    finish() {
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

    <template v-else-if="step === STEP_SOURCE">
      <div class="flex w-full max-w-xl flex-col">
        <p class="mb-6 text-sm text-muted-foreground">
          {{ $t("pages.catalogConnect.sourceHint") }}
        </p>
        <EmptyState
          v-if="connections.length === 0"
          framed
          :title="$t('pages.catalogConnect.noConnections')"
        >
          <template #icon>
            <Cable />
          </template>
        </EmptyState>
        <EmptyState
          v-else-if="importCapable.length === 0"
          framed
          :title="$t('pages.catalogConnect.noImportAdapters')"
        >
          <template #icon>
            <Cable />
          </template>
        </EmptyState>
        <template v-else>
          <InsetGroup
            role="radiogroup"
            :aria-label="$t('pages.catalogConnect.sourceListLabel')"
          >
            <InsetGroupItem
              v-for="conn in importCapable"
              :key="conn.id"
              role="radio"
              :aria-checked="sourceId === conn.id"
              :selected="sourceId === conn.id"
              @click="sourceId = conn.id"
            >
              <template #leading>
                <AdapterIcon :adapter-key="conn.adapterKey" class="size-4" />
              </template>
              <p class="truncate">{{ conn.name }}</p>
              <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
                {{ adapterName(conn) }}
                · {{ $t("pages.catalogConnect.linkedCount", { count: conn.productLinksCount || 0 }) }}
              </p>
            </InsetGroupItem>
          </InsetGroup>
          <p class="text-muted-foreground mt-3 px-1 text-xs leading-relaxed">
            {{ $t("pages.catalogConnect.modelBody") }}
          </p>
          <Button class="mt-6 self-start" :disabled="!sourceId" @click="continueFromSource">
            {{ $t("common.continue") }}
          </Button>
        </template>
      </div>
    </template>

    <template v-else-if="step === STEP_IMPORT">
      <div class="flex w-full max-w-xl flex-col">
        <p class="mb-6 text-sm text-muted-foreground">
          {{ $t("pages.catalogConnect.importHint") }}
        </p>
        <InsetGroup v-if="sourceConnection">
          <InsetGroupItem as="div">
            <template #leading>
              <AdapterIcon :adapter-key="sourceConnection.adapterKey" class="size-4" />
            </template>
            <p class="truncate">{{ sourceConnection.name }}</p>
            <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
              {{ adapterName(sourceConnection) }}
            </p>
          </InsetGroupItem>
        </InsetGroup>
        <Button class="mt-6 self-start" :disabled="importBusy" @click="runSourceImport">
          <Spinner v-if="importBusy" />
          {{ $t("pages.catalogConnect.importAction") }}
        </Button>
      </div>
    </template>

    <template v-else-if="step === STEP_MATCH">
      <div class="flex w-full max-w-xl flex-col">
        <EmptyState
          loading
          framed
          :title="$t('pages.catalogConnect.matchHint', { name: matchConnection?.name || '' })"
        />
        <Button
          class="mt-4 self-start"
          variant="ghost"
          :disabled="previewBusy"
          @click="skipCurrentMatch"
        >
          {{ $t("pages.catalogConnect.skipConnection") }}
        </Button>
      </div>
    </template>

    <template v-else-if="step === STEP_REVIEW">
      <div class="flex w-full flex-col">
        <p class="mb-4 max-w-2xl text-sm text-muted-foreground">
          {{
            $t("pages.catalogConnect.matchHint", {
              name: matchConnection?.name || "",
            })
          }}
        </p>
        <Tabs
          class="mb-4"
          :model-value="reviewTab"
          @update:model-value="reviewTab = $event"
        >
          <TabsList class="grid h-auto w-full grid-cols-3 sm:inline-flex sm:w-fit">
            <TabsTrigger value="matched">
              {{ $t("pages.catalogConnect.tabMatched", { count: matchedRows.length }) }}
            </TabsTrigger>
            <TabsTrigger value="ambiguous">
              {{ $t("pages.catalogConnect.tabAmbiguous", { count: ambiguousRows.length }) }}
            </TabsTrigger>
            <TabsTrigger value="unmatched">
              {{ $t("pages.catalogConnect.tabUnmatched", { count: unmatchedRows.length }) }}
            </TabsTrigger>
          </TabsList>
        </Tabs>
        <p
          v-if="previewMeta.alreadyLinkedCount"
          class="mb-3 text-xs text-muted-foreground"
        >
          {{
            $t("pages.catalogConnect.alreadyLinked", {
              count: previewMeta.alreadyLinkedCount,
            })
          }}
        </p>

        <div class="mb-4 overflow-hidden rounded-xl ring-1 ring-border/70">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead class="w-10">
                  <Checkbox
                    v-if="reviewHasSelectAll"
                    :model-value="allReviewSelected"
                    :indeterminate="someReviewSelected"
                    :aria-label="$t('pages.catalogConnect.selectAll')"
                    @update:model-value="toggleSelectAllVisible"
                  />
                </TableHead>
                <TableHead>{{ reviewHeaderLeft }}</TableHead>
                <TableHead>{{ reviewHeaderRight }}</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="row in visibleRows"
                :key="rowKey(row)"
              >
                <TableCell class="w-10">
                  <Checkbox
                    v-if="row.status === 'matched'"
                    :model-value="Boolean(selectedMatched[rowKey(row)])"
                    :aria-label="$t('pages.catalogConnect.selectRow')"
                    @update:model-value="selectedMatched[rowKey(row)] = $event === true"
                  />
                  <Checkbox
                    v-else-if="row.status === 'unmatched' && !fromLocal"
                    :model-value="Boolean(importUnmatchedIds[rowKey(row)])"
                    :aria-label="$t('pages.catalogConnect.selectRow')"
                    @update:model-value="importUnmatchedIds[rowKey(row)] = $event === true"
                  />
                </TableCell>
                <TableCell class="max-w-0 whitespace-normal">
                  <p class="truncate text-sm font-medium">
                    {{ fromLocal ? (row.productName || row.name) : (row.name || row.externalProductId) }}
                  </p>
                  <p class="text-muted-foreground truncate text-xs">
                    {{ row.sku || row.productSku || $t("common.emDash") }}
                    <span v-if="row.barcode"> · {{ row.barcode }}</span>
                  </p>
                </TableCell>
                <TableCell class="max-w-0 whitespace-normal">
                  <template v-if="row.status === 'matched'">
                    <p class="truncate text-sm">
                      {{ fromLocal ? (row.name || row.externalProductId) : (row.productName || row.name) }}
                    </p>
                    <p class="text-muted-foreground truncate text-xs">
                      {{ fromLocal ? (row.externalProductId || $t("common.emDash")) : (row.productSku || $t("common.emDash")) }}
                    </p>
                  </template>
                  <template v-else-if="row.status === 'ambiguous'">
                    <div class="flex min-w-0 flex-col gap-2 sm:flex-row sm:items-center">
                      <Select
                        :model-value="ambiguousPicks[rowKey(row)]"
                        @update:model-value="ambiguousPicks[rowKey(row)] = $event"
                      >
                        <SelectTrigger class="w-full">
                          <SelectValue :placeholder="$t('pages.catalogConnect.pickLocal')" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem
                            v-for="c in row.candidates"
                            :key="c.productId"
                            :value="String(c.productId)"
                          >
                            {{ c.name }} ({{ c.sku }})
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <Button
                        variant="ghost"
                        size="sm"
                        class="shrink-0"
                        @click="searchForExternalId = rowKey(row)"
                      >
                        {{ $t("pages.catalogConnect.linkManually") }}
                      </Button>
                    </div>
                  </template>
                  <template v-else-if="fromLocal">
                    <Input
                      :model-value="pastedRemoteIds[rowKey(row)] || ''"
                      :placeholder="$t('pages.catalogConnect.pasteRemotePlaceholder')"
                      @update:model-value="pastedRemoteIds[rowKey(row)] = $event"
                    />
                  </template>
                  <template v-else>
                    <div class="flex min-w-0 items-center gap-2">
                      <span class="truncate text-sm">
                        {{ $t("pages.catalogConnect.importAsNew") }}
                      </span>
                      <Button
                        variant="ghost"
                        size="sm"
                        class="shrink-0"
                        @click="searchForExternalId = rowKey(row)"
                      >
                        {{ $t("pages.catalogConnect.linkManually") }}
                      </Button>
                    </div>
                  </template>
                </TableCell>
              </TableRow>
              <TableRow v-if="visibleRows.length === 0">
                <TableCell
                  colspan="3"
                  class="text-muted-foreground py-6 text-center whitespace-normal"
                >
                  {{ $t("pages.catalogConnect.emptyBucket") }}
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div v-if="searchForExternalId" class="mb-4 max-w-xl space-y-3">
          <p class="text-sm font-medium">{{ $t("pages.catalogConnect.searchLocal") }}</p>
          <div class="flex gap-2">
            <Input
              v-model="searchQuery"
              :placeholder="$t('pages.productsPage.searchPlaceholder')"
              @keyup.enter="searchProducts"
            />
            <Button variant="outline" :disabled="searchBusy" @click="searchProducts">
              <Spinner v-if="searchBusy" />
              {{ $t("common.search") }}
            </Button>
          </div>
          <InsetGroup v-if="searchResults.length">
            <InsetGroupItem
              v-for="product in searchResults"
              :key="product.id"
              @click="pickSearchResult(product)"
            >
              <p class="truncate">{{ product.name }}</p>
              <p class="text-muted-foreground truncate text-xs font-normal">
                {{ product.sku }}
              </p>
            </InsetGroupItem>
          </InsetGroup>
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <Button :disabled="applyBusy" @click="confirmMatches">
            <Spinner v-if="applyBusy" />
            {{ $t("pages.catalogConnect.confirm") }}
          </Button>
          <Button variant="ghost" :disabled="applyBusy" @click="skipCurrentMatch">
            {{ $t("pages.catalogConnect.skipConnection") }}
          </Button>
        </div>
        <p v-if="applyBusy" class="mt-3 text-sm text-muted-foreground">
          {{ applyLiveLog }}
        </p>
      </div>
    </template>

    <template v-else>
      <div class="flex w-full max-w-xl flex-col">
        <p class="mb-6 text-sm text-muted-foreground">
          {{
            (lastApply?.mappingsCreated ?? 0) > 0
              ? $t("pages.catalogConnect.summaryBodyMapped")
              : $t("pages.catalogConnect.summaryBody")
          }}
        </p>
        <InsetGroup v-if="summaryRows.length">
          <InsetGroupItem
            v-for="row in summaryRows"
            :key="row.key"
            as="div"
          >
            {{ row.label }}
            <template #trailing>
              <span class="tabular-nums text-sm">{{ row.count }}</span>
            </template>
          </InsetGroupItem>
        </InsetGroup>
        <Button class="mt-6 self-start" @click="finish">
          {{ $t("pages.setup.openProducts") }}
        </Button>
      </div>
    </template>
  </AppPageChrome>
</template>
