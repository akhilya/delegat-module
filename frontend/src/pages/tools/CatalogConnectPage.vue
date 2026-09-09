<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import NavSearchField from "@/components/NavSearchField.vue";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import Input from "@/components/ui/input/Input.vue";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
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
import {
  catalogConnectMatchQueue,
  isCatalogConnectConnectionActive,
} from "@/lib/catalogConnectMatchQueue";
import {
  isCatalogConnectJobActive,
  readDismissedPreviewOpId,
  shouldRestorePreview,
  writeDismissedPreviewOpId,
} from "@/lib/catalogConnectJobs";
import { PAGE_MARKETPLACE, PAGE_PRODUCTS } from "@/router";
import { getDocsPageUrl } from "@/utils/docsUrl";
import { http } from "@/main";
import { BookOpen, Cable, ExternalLink } from "lucide-vue-next";
import { RouterLink } from "vue-router";
import { randomUuidCompact } from "@/utils/uuid";

const STEP_HOME = "home";
const STEP_SOURCE = "source";
const STEP_IMPORT = "import";
const STEP_MATCH = "match";
const STEP_REVIEW = "review";
const STEP_SUMMARY = "summary";

// Platform Tools catalog: id `catalog-connect`, mode `page`.

export default {
  components: {
    AppPageChrome,
    NavSearchField,
    Button,
    Checkbox,
    Input,
    Spinner,
    EmptyState,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
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
    BookOpen,
    ExternalLink,
    RouterLink,
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
      importBatchId: null,
      importBusy: false,
      previewBusy: false,
      applyBusy: false,
      applyOperationId: null,
      scanOperationId: null,
      scanProcessed: 0,
      scanTotal: 0,
      previewOperationId: null,
      jobPollTimer: null,
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
      changingSource: false,
      STEP_HOME,
      STEP_SOURCE,
      STEP_IMPORT,
      STEP_MATCH,
      STEP_REVIEW,
      STEP_SUMMARY,
      PAGE_MARKETPLACE,
    };
  },
  computed: {
    hasLocalProducts() {
      return this.localProductCount > 0;
    },
    docsHref() {
      return getDocsPageUrl("workspace/catalog-connect", this.$i18n.locale);
    },
    sourceHint() {
      if (this.changingSource || this.hasLocalProducts) {
        return this.$t("pages.catalogConnect.changeSourceHint");
      }
      return this.$t("pages.catalogConnect.sourceHint");
    },
    importHint() {
      if (this.changingSource || this.hasLocalProducts) {
        return this.$t("pages.catalogConnect.changeSourceImportHint");
      }
      return this.$t("pages.catalogConnect.importHint");
    },
    fromLocal() {
      return this.previewMeta.mode === "fromLocal";
    },
    importCapable() {
      return this.connections.filter(
        (c) => this.isConnectionActive(c) && this.canImportAll(c),
      );
    },
    linkCapable() {
      return this.connections.filter(
        (c) => this.isConnectionActive(c) && this.canLink(c),
      );
    },
    inactiveImportCapable() {
      return this.connections.filter(
        (c) => !this.isConnectionActive(c) && this.canImportAll(c),
      );
    },
    inactiveLinkCapable() {
      return this.connections.filter(
        (c) => !this.isConnectionActive(c) && this.canLink(c),
      );
    },
    remainingConnections() {
      return this.matchQueueIds
        .map((id) => this.connections.find((c) => Number(c.id) === Number(id)))
        .filter(Boolean);
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
      const op = this.applyLiveOp;
      const logs = op?.logs || [];
      const last = logs.length ? logs[logs.length - 1] : null;
      if (last?.message) return last.message;
      if (op?.progress) {
        const completed = op.progress.completed ?? 0;
        const total = op.progress.total ?? 0;
        if (total > 0) {
          return this.$t("pages.catalogConnect.scanProgress", { completed, total });
        }
        return this.$t("pages.catalogConnect.applyProgress", { completed, total });
      }
      return this.$t("pages.catalogConnect.applyWorking");
    },
    scanLiveOp() {
      if (!this.scanOperationId) return null;
      return this.$store.state.operations.operations.find(
        (o) => o.id === this.scanOperationId,
      ) || null;
    },
    applyLiveOp() {
      if (!this.applyOperationId) return null;
      return this.$store.state.operations.operations.find(
        (o) => o.id === this.applyOperationId,
      ) || null;
    },
    scanProgressText() {
      const completed = this.scanLiveOp?.progress?.completed ?? this.scanProcessed;
      const total = this.scanLiveOp?.progress?.total ?? this.scanTotal;
      if (total > 0) {
        return this.$t("pages.catalogConnect.scanProgress", { completed, total });
      }
      if (completed > 0) {
        return this.$t("pages.catalogConnect.scanProgressUnknown", { completed });
      }
      if (this.step === STEP_IMPORT && this.importBatchId && !this.scanOperationId) {
        return this.$t("pages.catalogConnect.importWaitingProducts");
      }
      if (this.step === STEP_IMPORT) {
        return this.$t("pages.catalogConnect.importScanWorking");
      }
      return this.$t("pages.catalogConnect.matchScanWorking");
    },
  },
  watch: {
    "scanLiveOp.status"(status) {
      if (["completed", "failed", "partial", "cancelled"].includes(status)) {
        this.syncActiveJob();
      }
    },
    "applyLiveOp.status"(status) {
      if (["completed", "failed", "partial", "cancelled"].includes(status)) {
        this.syncActiveJob();
      }
    },
  },
  async mounted() {
    await this.bootstrap();
  },
  beforeUnmount() {
    this.stopJobPoll();
  },
  methods: {
    ...mapActions([
      "getConnections",
      "getAvailableAdapters",
      "importUnlinkedProducts",
      "getCatalogConnectJobs",
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
    stopJobPoll() {
      if (this.jobPollTimer) {
        clearInterval(this.jobPollTimer);
        this.jobPollTimer = null;
      }
    },
    startJobPoll() {
      this.stopJobPoll();
      this.jobPollTimer = setInterval(() => {
        this.syncActiveJob();
      }, 2000);
    },
    fetchJobsState() {
      return new Promise((resolve, reject) => {
        this.getCatalogConnectJobs({
          onSuccess: (response) => resolve(this.unwrapResult(response) || {}),
          onError: (error) => reject(error),
        });
      });
    },
    applyScanProgress(job) {
      if (!job) return;
      this.scanOperationId = job.operationId || this.scanOperationId;
      this.scanProcessed = job.processedItems ?? this.scanProcessed;
      this.scanTotal = job.totalItems ?? this.scanTotal;
      if (job.mode) this.previewMeta = { ...this.previewMeta, mode: job.mode };
    },
    hydratePreview(preview, operationId) {
      if (operationId) this.previewOperationId = operationId;
      const items = preview?.items || [];
      this.previewRows = items;
      this.previewMeta = {
        matchedCount: preview?.matchedCount ?? items.filter((r) => r.status === "matched").length,
        ambiguousCount: preview?.ambiguousCount ?? items.filter((r) => r.status === "ambiguous").length,
        unmatchedCount: preview?.unmatchedCount ?? items.filter((r) => r.status === "unmatched").length,
        alreadyLinkedCount: preview?.alreadyLinkedCount ?? 0,
        mode: preview?.mode || "fromRemote",
      };
      const selected = {};
      for (const row of items) {
        if (row.status === "matched") selected[this.rowKey(row)] = true;
      }
      this.selectedMatched = selected;
      this.ambiguousPicks = {};
      this.importUnmatchedIds = {};
      this.pastedRemoteIds = {};
      this.reviewTab = items.some((r) => r.status === "matched")
        ? "matched"
        : items.some((r) => r.status === "ambiguous")
          ? "ambiguous"
          : "unmatched";
    },
    dismissPreview(operationId) {
      if (operationId) writeDismissedPreviewOpId(operationId);
    },
    async restoreFromJobs() {
      let state;
      try {
        state = await this.fetchJobsState();
      } catch {
        return false;
      }
      const active = state.active;
      if (isCatalogConnectJobActive(active)) {
        this.applyScanProgress(active);
        this.startJobPoll();
        if (active.kind === "import") {
          this.sourceId = active.connectionId;
          this.step = STEP_IMPORT;
          this.importBusy = true;
          return true;
        }
        if (active.kind === "match") {
          this.matchConnectionId = active.connectionId;
          this.step = STEP_MATCH;
          this.previewBusy = true;
          return true;
        }
        if (active.kind === "apply") {
          this.matchConnectionId = active.connectionId;
          this.applyOperationId = active.operationId;
          this.applyBusy = true;
          if (state.latestPreview?.preview) {
            this.hydratePreview(state.latestPreview.preview, state.latestPreview.operationId);
          }
          this.step = STEP_REVIEW;
          return true;
        }
      }
      if (shouldRestorePreview(state.latestPreview, readDismissedPreviewOpId())) {
        this.matchConnectionId = state.latestPreview.connectionId;
        this.hydratePreview(state.latestPreview.preview, state.latestPreview.operationId);
        this.step = STEP_REVIEW;
        return true;
      }
      const latestImport = state.latestCompleted;
      if (latestImport?.kind === "import" && latestImport.importBatchId) {
        try {
          const { data } = await http.get(`/operations/${latestImport.importBatchId}/status`);
          const status = data?.status || data?.value?.status;
          if (status === "running" || status === "paused") {
            this.sourceId = latestImport.connectionId;
            this.importBatchId = latestImport.importBatchId;
            this.importBusy = true;
            this.step = STEP_IMPORT;
            this.waitForOperation(latestImport.importBatchId).then(() =>
              this.finishImportJob(latestImport),
            );
            return true;
          }
        } catch {
          /* batch already gone */
        }
      }
      return false;
    },
    async syncActiveJob() {
      let state;
      try {
        state = await this.fetchJobsState();
      } catch {
        return;
      }
      const active = state.active;
      if (isCatalogConnectJobActive(active)) {
        this.applyScanProgress(active);
        return;
      }
      this.stopJobPoll();
      const done = state.latestCompleted;
      if (this.importBusy) {
        await this.finishImportJob(done?.kind === "import" ? done : { status: "completed", queuedCount: 0 });
        return;
      }
      if (this.previewBusy) {
        const matchDone = done?.kind === "match" ? done : state.latestPreview;
        await this.finishMatchJob(matchDone);
        return;
      }
      if (this.applyBusy) {
        const applyJob = done?.kind === "apply" ? done : null;
        if (applyJob?.status === "failed" || applyJob?.status === "cancelled") {
          this.applyBusy = false;
          this.applyOperationId = null;
          displaySonnerError(applyJob.error || this.$t("errors.generic"));
          return;
        }
        await this.finishApplyJob(applyJob);
      }
    },
    async finishImportJob(job) {
      if (job?.status === "failed" || job?.status === "cancelled") {
        this.importBusy = false;
        this.scanOperationId = null;
        if (job.status === "failed") {
          displaySonnerError(job.error || this.$t("errors.generic"));
        }
        return;
      }
      const queued = job?.import?.queued ?? job?.queuedCount ?? 0;
      const batchId = job?.import?.batchId || job?.importBatchId;
      this.importBatchId = batchId || null;
      this.scanOperationId = null;
      if (queued > 0 && batchId) {
        await this.waitForOperation(batchId);
      }
      if (queued > 0) {
        displaySonnerSuccess(
          this.$t("pages.catalogConnect.importQueued", { count: queued }),
        );
      }
      this.importBusy = false;
      await this.loadConnections();
      await this.loadProductCount();
      this.changingSource = false;
      if (this.hasLocalProducts) {
        this.step = STEP_HOME;
      } else {
        this.step = STEP_SOURCE;
      }
    },
    async finishMatchJob(job) {
      this.previewBusy = false;
      this.scanOperationId = null;
      if (!job || job.status === "failed" || job.status === "cancelled") {
        if (job?.status === "failed") {
          displaySonnerError(job.error || this.$t("errors.generic"));
        }
        await this.goHome();
        return;
      }
      const items = job.preview?.items || [];
      if (items.length === 0) {
        await this.repairCurrentConnectionMappings();
        await this.goHome();
        return;
      }
      this.hydratePreview(job.preview, job.operationId);
      this.step = STEP_REVIEW;
    },
    async finishApplyJob(job) {
      const result = job?.apply;
      this.applyBusy = false;
      this.applyOperationId = null;
      if (result) {
        this.lastApply = result;
        this.dismissPreview(job?.operationId);
        displaySonnerSuccess(
          this.$t("pages.catalogConnect.applyDone", {
            linked: result.linked ?? 0,
            imported: result.imported ?? 0,
            mappings: result.mappingsCreated ?? 0,
            fields: result.attributesImported ?? 0,
          }),
        );
      }
      await this.goHome();
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
    isConnectionActive(conn) {
      return isCatalogConnectConnectionActive(conn);
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
      const restored = await this.restoreFromJobs();
      this.loading = false;
      if (!restored) this.startFlow();
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
      this.changingSource = false;
      this.matchConnectionId = null;
      if (this.hasLocalProducts) {
        this.step = STEP_HOME;
        return;
      }
      if (this.importCapable.length === 1) {
        this.sourceId = this.importCapable[0].id;
      }
      this.step = STEP_SOURCE;
    },
    continueFromSource() {
      if (!this.sourceId) return;
      this.step = STEP_IMPORT;
    },
    openChangeSource() {
      this.changingSource = true;
      this.sourceId =
        this.importCapable.length === 1 ? this.importCapable[0].id : null;
      this.step = STEP_SOURCE;
    },
    startMatch(connectionId) {
      this.matchConnectionId = connectionId;
      this.step = STEP_MATCH;
      this.loadPreview();
    },
    async goHome() {
      this.dismissPreview(this.previewOperationId || this.scanOperationId);
      this.matchConnectionId = null;
      this.changingSource = false;
      await Promise.all([this.loadConnections(), this.loadProductCount()]);
      if (this.hasLocalProducts) {
        this.step = STEP_HOME;
        return;
      }
      this.startFlow();
    },
    cancelSourceImport() {
      if (this.hasLocalProducts) {
        this.goHome();
        return;
      }
      this.step = STEP_SOURCE;
    },
    async runSourceImport() {
      if (!this.sourceId || this.importBusy) return;
      const operationId = randomUuidCompact();
      this.scanOperationId = operationId;
      this.scanProcessed = 0;
      this.scanTotal = 0;
      this.importBusy = true;
      try {
        await this.$store.dispatch("operations/startOperation", {
          id: operationId,
          type: "catalog-connect-import",
          title: this.$t("pages.catalogConnect.importScanTitle", {
            name: this.sourceConnection?.name || this.adapterName(this.sourceConnection),
          }),
          connectionId: this.sourceId,
          connectionName: this.sourceConnection?.name,
          batchType: "catalog-connect",
        });
        await this.importUnlinkedProducts({
          connectionId: this.sourceId,
          operationId,
          onSuccess: () => {
            this.startJobPoll();
          },
          onError: (error) => {
            this.importBusy = false;
            this.scanOperationId = null;
            displaySonnerError(error);
          },
        });
      } catch (error) {
        this.importBusy = false;
        this.scanOperationId = null;
        displaySonnerError(error);
      }
    },
    async skipCurrentMatch() {
      this.dismissPreview(this.scanOperationId);
      await this.repairCurrentConnectionMappings();
      await this.goHome();
    },
    async loadPreview() {
      if (!this.matchConnectionId || this.previewBusy) return;
      this.previewBusy = true;
      this.previewRows = [];
      const operationId = randomUuidCompact();
      this.scanOperationId = operationId;
      this.scanProcessed = 0;
      this.scanTotal = 0;
      try {
        await this.$store.dispatch("operations/startOperation", {
          id: operationId,
          type: "catalog-connect-match",
          title: this.$t("pages.catalogConnect.matchScanTitle", {
            name: this.matchConnection?.name || this.adapterName(this.matchConnection),
          }),
          connectionId: this.matchConnectionId,
          connectionName: this.matchConnection?.name,
          batchType: "catalog-connect",
        });
        await this.previewCatalogMatch({
          connectionId: this.matchConnectionId,
          operationId,
          onSuccess: () => {
            this.startJobPoll();
          },
          onError: (error) => {
            this.previewBusy = false;
            this.scanOperationId = null;
            displaySonnerError(error);
          },
        });
      } catch (error) {
        this.previewBusy = false;
        this.scanOperationId = null;
        displaySonnerError(error);
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
          onSuccess: () => {
            this.startJobPoll();
          },
          onError: (error) => {
            this.applyBusy = false;
            this.applyOperationId = null;
            displaySonnerError(error);
          },
        });
      } catch (error) {
        this.applyBusy = false;
        this.applyOperationId = null;
        displaySonnerError(error);
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
  <AppPageChrome>
    <EmptyState
      v-if="loading"
      loading
      class="min-h-0 flex-1"
    />

    <template v-else-if="step === STEP_HOME || step === STEP_SUMMARY">
      <div class="flex w-full max-w-xl flex-col">
        <p class="mb-6 text-sm text-muted-foreground">
          {{ $t("pages.catalogConnect.homeHint") }}
        </p>

        <InsetGroup v-if="summaryRows.length" class="mb-8">
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

        <EmptyState
          v-if="connections.length === 0"
          framed
          :title="$t('pages.catalogConnect.noConnections')"
        >
          <template #icon>
            <Cable />
          </template>
          <Button as-child>
            <RouterLink :to="PAGE_MARKETPLACE">
              {{ $t("pages.catalogConnect.openAdapters") }}
            </RouterLink>
          </Button>
        </EmptyState>

        <EmptyState
          v-else-if="remainingConnections.length === 0 && inactiveLinkCapable.length > 0"
          framed
          :title="$t('pages.catalogConnect.inactiveTitle')"
          :description="$t('pages.catalogConnect.inactiveDescription')"
        >
          <template #icon>
            <Cable />
          </template>
          <Button as-child>
            <RouterLink :to="PAGE_MARKETPLACE">
              {{ $t("pages.catalogConnect.openAdapters") }}
            </RouterLink>
          </Button>
        </EmptyState>

        <EmptyState
          v-else-if="remainingConnections.length === 0"
          framed
          :title="$t('pages.catalogConnect.homeAllLinked')"
        >
          <template #icon>
            <Cable />
          </template>
        </EmptyState>

        <template v-else>
          <InsetGroupLabel>
            {{ $t("pages.catalogConnect.remainingListLabel") }}
          </InsetGroupLabel>
          <InsetGroup>
            <InsetGroupItem
              v-for="conn in remainingConnections"
              :key="conn.id"
              show-chevron
              @click="startMatch(conn.id)"
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
        </template>

        <p class="text-muted-foreground mt-3 px-1 text-xs leading-relaxed">
          {{ $t("pages.catalogConnect.modelBody") }}
        </p>

        <div class="mt-6 flex flex-wrap items-center gap-2">
          <Button variant="outline" @click="finish">
            {{ $t("pages.setup.openProducts") }}
          </Button>
          <Button
            v-if="importCapable.length > 0"
            variant="ghost"
            @click="openChangeSource"
          >
            {{ $t("pages.catalogConnect.changeSourceAction") }}
          </Button>
        </div>
        <p
          v-if="importCapable.length === 0 && inactiveImportCapable.length > 0"
          class="text-muted-foreground mt-3 px-1 text-xs leading-relaxed"
        >
          {{ $t("pages.catalogConnect.inactiveDescription") }}
        </p>
        <a
          :href="docsHref"
          target="_blank"
          rel="noopener noreferrer"
          class="mt-6 inline-flex items-center gap-1.5 self-start px-1 text-xs font-medium text-primary hover:underline"
        >
          <BookOpen class="size-3.5 shrink-0" />
          {{ $t("pages.catalogConnect.docsLink") }}
          <ExternalLink class="size-3 shrink-0 opacity-70" />
        </a>
      </div>
    </template>

    <template v-else-if="step === STEP_SOURCE">
      <div class="flex w-full max-w-xl flex-col">
        <p class="mb-6 text-sm text-muted-foreground">
          {{ sourceHint }}
        </p>
        <EmptyState
          v-if="connections.length === 0"
          framed
          :title="$t('pages.catalogConnect.noConnections')"
        >
          <template #icon>
            <Cable />
          </template>
          <Button as-child>
            <RouterLink :to="PAGE_MARKETPLACE">
              {{ $t("pages.catalogConnect.openAdapters") }}
            </RouterLink>
          </Button>
        </EmptyState>
        <EmptyState
          v-else-if="importCapable.length === 0 && inactiveImportCapable.length > 0"
          framed
          :title="$t('pages.catalogConnect.inactiveTitle')"
          :description="$t('pages.catalogConnect.inactiveDescription')"
        >
          <template #icon>
            <Cable />
          </template>
          <Button as-child>
            <RouterLink :to="PAGE_MARKETPLACE">
              {{ $t("pages.catalogConnect.openAdapters") }}
            </RouterLink>
          </Button>
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
          <div class="mt-6 flex flex-wrap items-center gap-2">
            <Button :disabled="!sourceId" @click="continueFromSource">
              {{ $t("common.continue") }}
            </Button>
            <Button
              v-if="hasLocalProducts"
              variant="ghost"
              @click="goHome"
            >
              {{ $t("common.back") }}
            </Button>
          </div>
        </template>
        <Button
          v-if="hasLocalProducts && importCapable.length === 0"
          class="mt-6 self-start"
          variant="ghost"
          @click="goHome"
        >
          {{ $t("common.back") }}
        </Button>
        <a
          :href="docsHref"
          target="_blank"
          rel="noopener noreferrer"
          class="mt-6 inline-flex items-center gap-1.5 self-start px-1 text-xs font-medium text-primary hover:underline"
        >
          <BookOpen class="size-3.5 shrink-0" />
          {{ $t("pages.catalogConnect.docsLink") }}
          <ExternalLink class="size-3 shrink-0 opacity-70" />
        </a>
      </div>
    </template>

    <template v-else-if="step === STEP_IMPORT">
      <div class="flex w-full max-w-xl flex-col">
        <EmptyState
          v-if="importBusy"
          loading
          framed
          class="mb-6 tabular-nums"
          :title="scanProgressText"
          :description="importHint"
        />
        <p v-else class="mb-6 text-sm text-muted-foreground">
          {{ importHint }}
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
        <Button
          class="mt-2 self-start"
          variant="ghost"
          :disabled="importBusy"
          @click="cancelSourceImport"
        >
          {{ $t("common.back") }}
        </Button>
      </div>
    </template>

    <template v-else-if="step === STEP_MATCH">
      <div class="flex w-full max-w-xl flex-col">
        <EmptyState
          loading
          framed
          class="tabular-nums"
          :title="scanProgressText"
          :description="$t('pages.catalogConnect.matchHint', { name: matchConnection?.name || '' })"
        />
        <Button
          class="mt-4 self-start"
          variant="ghost"
          :disabled="previewBusy"
          @click="skipCurrentMatch"
        >
          {{ $t("pages.catalogConnect.skipConnection") }}
        </Button>
        <Button
          class="mt-2 self-start"
          variant="ghost"
          :disabled="previewBusy"
          @click="goHome"
        >
          {{ $t("common.back") }}
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

        <div class="mb-4">
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
            <NavSearchField
              v-model="searchQuery"
              :placeholder="$t('pages.productsPage.searchPlaceholder')"
              @submit="searchProducts"
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
          <Button variant="ghost" :disabled="applyBusy" @click="goHome">
            {{ $t("common.back") }}
          </Button>
        </div>
        <p v-if="applyBusy" class="mt-3 text-sm text-muted-foreground tabular-nums">
          {{ applyLiveLog }}
        </p>
      </div>
    </template>
  </AppPageChrome>
</template>
