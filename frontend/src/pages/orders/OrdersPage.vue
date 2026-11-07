<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageListToolbar from "@/components/PageListToolbar.vue";
import FilterMenuGroup from "@/components/FilterMenuGroup.vue";
import OrderDetailSheet from "@/components/orders/OrderDetailSheet.vue";
import OrderItemsCell from "@/components/orders/OrderItemsCell.vue";
import OrderRowContextMenu from "@/components/orders/OrderRowContextMenu.vue";
import OrderJobConfirmDialog from "@/components/orders/OrderJobConfirmDialog.vue";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ContextMenu, ContextMenuTrigger } from "@/components/ui/context-menu";
import { LogTableFrame } from "@/components/logs";
import { http } from "@/main";
import { getCachedMe } from "@/lib/meCache";
import { getLocale, toBcp47 } from "@/i18n";
import { isCabinetViewOnly } from "@/lib/permissions";
import { triggerBrowserDownload } from "@/lib/artifactDownload";
import {
  adapterCanGetOrders,
  adapterCanGetOrderLabels,
  adapterCanGetFulfillmentLocations,
  adapterCanManageShipments,
  adapterCanUpdateOrderStatus,
} from "@/lib/adapterCapabilities";
import {
  connectionNavLabel,
  connectionIdOf,
  adapterKeyOf,
  isConnectionActive,
  formatOrderMoney,
  filterOrdersBySearch,
  mergeOrderLists,
  ORDER_ACTION_LABEL,
  orderBucketById,
  orderBulkCapabilities,
  orderChannelLabelKey,
  orderDisplayId,
  orderExternalId,
  orderMailboxPath,
  orderMailboxesForAdapter,
  orderSearchHitPage,
  orderSearchLookupIds,
  orderStatusClass,
  orderStatusLabelKey,
  orderStatusOf,
  parseOrdersPath,
  postMarketplaceOrderStatus,
  readStoredConnectionId,
  readStoredMailbox,
  resolveOrderSearchNavigation,
  resolveOrdersLocation,
  runPendingOrderJob,
  saveOrderLabelFile,
  marketplaceOrderLabelUrl,
  shouldFetchMailboxAfterRouteSync,
  statusesForBucket,
  applyOrderStatusToList,
  storedOrderListPageParams,
  tabCountsFromPayload,
  writeStoredConnectionId,
  writeStoredMailbox,
} from "@/lib/ordersUi";
import { shouldPullLiveOrders, startOrderLiveSync } from "@/lib/orderLiveSync";
import { ORDER_ACTION_ICONS } from "@/lib/orderActionIcons";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { mapActions } from "vuex";
import { setBreadcrumbParentLabel } from "@/router";
import { Barcode, ClipboardList, Plug, X, XCircle } from "lucide-vue-next";

const CHANNEL_ALL = "__all__";
const PAGE_SIZES = [25, 50, 100];
const DEFAULT_PAGE_SIZE = 50;

export default {
  components: {
    AppPageChrome,
    PageListToolbar,
    FilterMenuGroup,
    OrderDetailSheet,
    OrderItemsCell,
    OrderRowContextMenu,
    OrderJobConfirmDialog,
    Button,
    Checkbox,
    Spinner,
    EmptyState,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Tabs,
    TabsList,
    TabsTrigger,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    ContextMenu,
    ContextMenuTrigger,
    LogTableFrame,
    Barcode,
    ClipboardList,
    Plug,
    X,
    XCircle,
  },
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } =
      useStoreDateTime();
    loadMemberships();
    return {
      formatStoreDateTime,
      connectionIdOf,
      adapterKeyOf,
      orderExternalId,
      orderDisplayId,
      orderStatusClass,
      orderStatusOf,
      orderStatusLabelKey,
      orderChannelLabelKey,
      ACTION_ICONS: ORDER_ACTION_ICONS,
      ACTION_LABEL: ORDER_ACTION_LABEL,
    };
  },
  data() {
    return {
      CHANNEL_ALL,
      PAGE_SIZES,
      bootstrapping: true,
      syncingRoute: false,
      isLoading: false,
      isLoadingMore: false,
      connections: [],
      adapters: [],
      connectionId: null,
      searchQuery: "",
      channel: CHANNEL_ALL,
      pageSize: DEFAULT_PAGE_SIZE,
      statusPages: {},
      mailboxCounts: {},
      fetchGen: 0,
      fetchedConnectionId: null,
      fetchedMailbox: "",
      selectedIds: [],
      contextMenuRow: null,
      pendingJob: null,
      confirmOpen: false,
      bulkBusy: false,
    };
  },
  computed: {
    bucketId() {
      return String(this.$route.params.bucket || "");
    },
    bucket() {
      return orderBucketById(this.bucketId);
    },
    mailboxes() {
      if (!this.selectedConnection) return [];
      return orderMailboxesForAdapter(adapterKeyOf(this.selectedConnection));
    },
    storeId() {
      return getCachedMe()?.selectedStoreId ?? null;
    },
    orderConnections() {
      return (this.connections || [])
        .filter(
          (connection) =>
            isConnectionActive(connection) &&
            adapterCanGetOrders(this.adapterFor(connection)),
        )
        .slice()
        .sort((a, b) => {
          const aName = this.connectionLabel(a);
          const bName = this.connectionLabel(b);
          return aName.localeCompare(bName, toBcp47(getLocale()));
        });
    },
    selectedConnection() {
      return (
        this.orderConnections.find(
          (connection) => connectionIdOf(connection) === this.connectionId,
        ) || null
      );
    },
    selectedAdapter() {
      return this.adapterFor(this.selectedConnection);
    },
    canListOrders() {
      return adapterCanGetOrders(this.selectedAdapter);
    },
    canMutate() {
      return (
        Boolean(this.selectedConnection) &&
        adapterCanUpdateOrderStatus(this.selectedAdapter) &&
        !isCabinetViewOnly(getCachedMe())
      );
    },
    canDownloadLabel() {
      return adapterCanGetOrderLabels(this.selectedAdapter);
    },
    canBulkDownloadLabel() {
      return this.canDownloadLabel && !isCabinetViewOnly(getCachedMe());
    },
    canManageShipments() {
      return adapterCanManageShipments(this.selectedAdapter);
    },
    canGetFulfillmentLocations() {
      return adapterCanGetFulfillmentLocations(this.selectedAdapter);
    },
    items() {
      return filterOrdersBySearch(
        mergeOrderLists(
          Object.values(this.statusPages).map((page) => page.items || []),
        ),
        this.searchQuery,
      );
    },
    hasMore() {
      return Object.values(this.statusPages).some((page) => page.hasMore);
    },
    selectedOrderId() {
      return String(this.$route.query.order || "").trim();
    },
    sheetOpen: {
      get() {
        return Boolean(this.selectedOrderId && this.connectionId);
      },
      set(open) {
        if (open) return;
        this.closeSheet();
      },
    },
    selectedPreview() {
      if (!this.selectedOrderId) return null;
      return (
        this.items.find(
          (row) => orderExternalId(row) === this.selectedOrderId,
        ) || null
      );
    },
    emptyKind() {
      if (this.orderConnections.length === 0) return "noConnections";
      if (!this.canListOrders) return "unsupported";
      return "empty";
    },
    mailboxEmptyDescriptionKey() {
      return (
        this.bucket?.emptyDescriptionKey || "pages.ordersPage.emptyDescription"
      );
    },
    moneyLocale() {
      return toBcp47(getLocale());
    },
    selectedOrders() {
      const selected = new Set(this.selectedIds);
      return this.items.filter((row) => selected.has(orderExternalId(row)));
    },
    selectedCount() {
      return this.selectedOrders.length;
    },
    allVisibleSelected() {
      return (
        this.items.length > 0 &&
        this.items.every((row) =>
          this.selectedIds.includes(orderExternalId(row)),
        )
      );
    },
    someVisibleSelected() {
      return this.selectedCount > 0 && !this.allVisibleSelected;
    },
    bulkCaps() {
      return orderBulkCapabilities(this.selectedOrders, this.rowAdapterKey, {
        canMutate: this.canMutate,
        canDownloadLabel: this.canBulkDownloadLabel,
      });
    },
    rowAdapterKey() {
      return adapterKeyOf(this.selectedConnection);
    },
  },
  watch: {
    selectedConnection: {
      immediate: true,
      handler() {
        this.syncOrderBreadcrumbParent();
      },
    },
    "$route.fullPath"() {
      this.syncOrderBreadcrumbParent();
      if (this.bootstrapping || this.syncingRoute) return;
      this.syncFromRoute();
    },
  },
  async mounted() {
    await this.bootstrap();
    if (this.liveOrdersStopped) return;
    this.stopLiveOrders = startOrderLiveSync(() => this.pullLiveOrders());
  },
  beforeUnmount() {
    this.liveOrdersStopped = true;
    this.stopLiveOrders?.();
    this.stopLiveOrders = null;
  },
  methods: {
    ...mapActions(["getConnections", "getAvailableAdapters"]),

    unwrapList(response) {
      const data = extractResponseData(response, []);
      if (Array.isArray(data)) return data;
      if (Array.isArray(data?.items)) return data.items;
      if (Array.isArray(data?.value)) return data.value;
      return [];
    },

    unwrapPage(response) {
      const data = extractResponseData(response, {});
      if (
        data?.value &&
        typeof data.value === "object" &&
        !Array.isArray(data.value)
      ) {
        return data.value;
      }
      return data || {};
    },

    adapterFor(connection) {
      return this.adapterByKey(adapterKeyOf(connection));
    },

    adapterByKey(key) {
      if (!key) return null;
      return (
        (this.adapters || []).find(
          (adapter) => (adapter.key ?? adapter.Key) === key,
        ) || null
      );
    },

    sameAdapterCount(connection) {
      const key = adapterKeyOf(connection);
      return this.orderConnections.filter((item) => adapterKeyOf(item) === key)
        .length;
    },

    connectionLabel(connection) {
      return connectionNavLabel(
        connection,
        this.adapters,
        this.sameAdapterCount(connection),
      );
    },

    syncOrderBreadcrumbParent() {
      setBreadcrumbParentLabel(
        this.selectedConnection
          ? this.connectionLabel(this.selectedConnection)
          : null,
      );
    },

    compactQuery(query) {
      const out = {};
      for (const [key, value] of Object.entries(query || {})) {
        if (value == null || value === "") continue;
        out[key] = Array.isArray(value) ? value.map(String) : String(value);
      }
      return out;
    },

    queryEqual(a, b) {
      return (
        JSON.stringify(this.compactQuery(a)) ===
        JSON.stringify(this.compactQuery(b))
      );
    },

    async syncFromRoute({ fetch = false } = {}) {
      if (this.syncingRoute) return;
      this.syncingRoute = true;
      try {
        const connections = this.orderConnections.map((connection) => ({
          id: connectionIdOf(connection),
          adapterKey: adapterKeyOf(connection),
        }));
        const next = resolveOrdersLocation({
          connections,
          storedConnectionId: readStoredConnectionId(this.storeId),
          preferredMailbox: readStoredMailbox(this.storeId),
          routePath: this.$route.path,
          query: { ...this.$route.query },
        });
        if (
          next.path !== this.$route.path ||
          !this.queryEqual(next.query, this.$route.query)
        ) {
          if (connections.length > 0 || next.path === "/orders") {
            await this.$router.replace({ path: next.path, query: next.query });
          }
        }

        const parsed = parseOrdersPath(this.$route.path);
        if (
          this.fetchedConnectionId != null &&
          parsed.connectionId &&
          this.fetchedConnectionId !== parsed.connectionId
        ) {
          this.searchQuery = "";
        }
        this.connectionId = parsed.connectionId;
        if (parsed.connectionId) {
          writeStoredConnectionId(this.storeId, parsed.connectionId);
        }
        if (parsed.bucket) {
          writeStoredMailbox(this.storeId, parsed.bucket);
        }
        if (
          shouldFetchMailboxAfterRouteSync({
            fetch,
            connectionChanged: this.fetchedConnectionId !== this.connectionId,
            mailboxChanged: this.fetchedMailbox !== parsed.bucket,
            searchQuery: this.searchQuery,
          })
        ) {
          this.resetAndFetch();
        }
      } finally {
        this.syncingRoute = false;
      }
    },

    mailboxCount(id) {
      return Number(this.mailboxCounts?.[id]) || 0;
    },

    onMailboxTab(id) {
      const mailbox = String(id || "");
      if (!mailbox || mailbox === this.bucketId || !this.connectionId) return;
      this.searchQuery = "";
      const query = { ...this.$route.query };
      delete query.order;
      this.$router.replace({
        path: orderMailboxPath(
          this.connectionId,
          mailbox,
          adapterKeyOf(this.selectedConnection),
        ),
        query,
      });
    },

    applySearch() {
      const q = (this.searchQuery || "").trim();
      this.searchQuery = q;
      if (q) {
        this.lookupAndOpenOrder(q);
        return;
      }
      this.resetAndFetch();
    },

    async lookupAndOpenOrder(orderNumber) {
      if (!this.connectionId || !this.canListOrders) {
        this.resetAndFetch();
        return;
      }
      this.fetchGen += 1;
      const gen = this.fetchGen;
      this.isLoading = true;
      let found = null;
      for (const id of orderSearchLookupIds(orderNumber)) {
        try {
          const response = await http.get(
            `/marketplace/connections/${this.connectionId}/orders/${encodeURIComponent(id)}`,
            { cache: false },
          );
          const data = extractResponseData(response, null);
          if (orderExternalId(data)) {
            found = data;
            break;
          }
        } catch (error) {
          const status = error?.response?.status;
          const code = error?.response?.data?.code;
          if (status === 404 || code === "order_not_found") continue;
          displaySonnerError(error);
          if (gen === this.fetchGen) this.isLoading = false;
          return;
        }
      }
      if (gen !== this.fetchGen) return;

      const next = resolveOrderSearchNavigation({
        found,
        connectionId: this.connectionId,
        adapterKey: adapterKeyOf(this.selectedConnection),
        currentPath: this.$route.path,
      });
      const page = found ? orderSearchHitPage(found) : null;
      if (page) {
        this.statusPages = { search: page };
        this.selectedIds = [];
        this.isLoading = false;
        this.isLoadingMore = false;
        if (!next) {
          this.markFetchedLocation();
          return;
        }
        const query = { ...this.$route.query, ...next.query };
        if (
          next.path !== this.$route.path ||
          !this.queryEqual(query, this.$route.query)
        ) {
          this.syncingRoute = true;
          try {
            await this.$router.replace({ path: next.path, query });
            const parsed = parseOrdersPath(this.$route.path);
            this.connectionId = parsed.connectionId;
            if (parsed.connectionId) {
              writeStoredConnectionId(this.storeId, parsed.connectionId);
            }
            if (parsed.bucket) {
              writeStoredMailbox(this.storeId, parsed.bucket);
            }
            await this.$nextTick();
          } finally {
            this.syncingRoute = false;
          }
        }
        this.markFetchedLocation();
        return;
      }
      this.resetAndFetch();
    },

    onChannelChange(value) {
      this.channel = value || CHANNEL_ALL;
      this.resetAndFetch();
    },

    onPageSizeChange(value) {
      const next = Number(value);
      if (!PAGE_SIZES.includes(next) || next === this.pageSize) return;
      this.pageSize = next;
      this.resetAndFetch();
    },

    openOrder(row) {
      const id = orderExternalId(row);
      if (!id) return;
      this.$router.replace({
        path: this.$route.path,
        query: {
          ...this.$route.query,
          order: id,
        },
      });
    },

    closeSheet() {
      if (!this.$route.query.order) return;
      const query = { ...this.$route.query };
      delete query.order;
      this.$router.replace({ path: this.$route.path, query });
    },

    onOrderUpdated(order) {
      if (order) this.applySellerStatus(order);
      if (this.searchQuery.trim()) {
        const page = order ? orderSearchHitPage(order) : null;
        if (page) this.statusPages = { search: page };
        return;
      }
      this.fetchOrders({ silent: true });
    },

    applySellerStatus(order) {
      const statuses = statusesForBucket(this.bucketId, this.rowAdapterKey);
      const next = { ...this.statusPages };
      for (const key of Object.keys(next)) {
        const page = next[key];
        if (!page) continue;
        next[key] = {
          ...page,
          items: applyOrderStatusToList(page.items, order, statuses),
        };
      }
      this.statusPages = next;
      this.pruneSelection();
    },

    resetAndFetch() {
      this.statusPages = {};
      this.selectedIds = [];
      this.contextMenuRow = null;
      this.fetchOrders();
    },

    markFetchedLocation() {
      this.fetchedConnectionId = this.connectionId;
      this.fetchedMailbox = this.bucketId;
    },

    async bootstrap() {
      this.bootstrapping = true;
      await Promise.all([
        new Promise((resolve) => {
          this.getConnections({
            onSuccess: (response) => {
              this.connections = this.unwrapList(response);
              resolve();
            },
            onError: (error) => {
              displaySonnerError(error);
              this.connections = [];
              resolve();
            },
          });
        }),
        new Promise((resolve) => {
          this.getAvailableAdapters({
            onSuccess: (response) => {
              this.adapters = this.unwrapList(response);
              resolve();
            },
            onError: () => {
              this.adapters = [];
              resolve();
            },
          });
        }),
      ]);
      await this.syncFromRoute({ fetch: true });
      this.bootstrapping = false;
    },

    async fetchStatusPage(status, { append = false } = {}) {
      const prev = this.statusPages[status] || {
        items: [],
        page: 1,
        nextCursor: null,
        hasMore: false,
      };
      if (append && !prev.hasMore) return prev;

      const params = storedOrderListPageParams({
        append,
        page: prev.page,
        nextCursor: prev.nextCursor,
        pageSize: this.pageSize,
        status,
        search: this.searchQuery.trim() || undefined,
        channel: this.channel !== CHANNEL_ALL ? this.channel : undefined,
      });

      const response = await http.get(
        `/marketplace/connections/${this.connectionId}/orders`,
        { params, cache: false },
      );
      const data = this.unwrapPage(response);
      const items = Array.isArray(data.items) ? data.items : [];
      this.mailboxCounts = tabCountsFromPayload(
        data,
        this.mailboxes.map((box) => box.id),
      );
      return {
        items: append ? [...(prev.items || []), ...items] : items,
        page: Number(data.page) || params.page || 1,
        nextCursor: data.nextCursor || null,
        hasMore: Boolean(data.hasMore),
      };
    },

    pullLiveOrders() {
      const pagedFurther = Object.values(this.statusPages).some(
        (page) => Number(page?.page) > 1,
      );
      if (
        !shouldPullLiveOrders({
          bootstrapping: this.bootstrapping,
          busy: this.isLoading || this.isLoadingMore,
          search: this.searchQuery,
          pagedFurther,
          hidden: document.visibilityState === "hidden",
        })
      ) {
        return;
      }
      return this.fetchOrders({ silent: true, quiet: true });
    },

    async fetchOrders({ append = false, silent = false, quiet = false } = {}) {
      if (!this.connectionId || !this.canListOrders) {
        this.statusPages = {};
        this.mailboxCounts = {};
        this.selectedIds = [];
        this.isLoading = false;
        this.isLoadingMore = false;
        return;
      }

      if (!append) this.fetchGen += 1;
      const gen = this.fetchGen;
      if (append) this.isLoadingMore = true;
      else if (!silent) this.isLoading = true;

      const statuses = statusesForBucket(
        this.bucketId,
        adapterKeyOf(this.selectedConnection),
      );
      try {
        const settled = await Promise.allSettled(
          statuses.map((status) => this.fetchStatusPage(status, { append })),
        );
        if (gen !== this.fetchGen) return;

        const next = { ...this.statusPages };
        settled.forEach((result, index) => {
          const status = statuses[index];
          if (result.status === "fulfilled") {
            next[status] = result.value;
          } else if (!append) {
            next[status] = {
              items: [],
              page: 1,
              nextCursor: null,
              hasMore: false,
            };
            if (!quiet) displaySonnerError(result.reason);
          } else if (!quiet) {
            displaySonnerError(result.reason);
          }
        });
        this.statusPages = next;
        this.pruneSelection();
        if (!append) {
          const syncResponse = await http.post(
            `/marketplace/connections/${this.connectionId}/orders/sync`,
            null,
            { params: { statuses: statuses.join(",") }, cache: false },
          );
          const payload = extractResponseData(syncResponse, {});
          const pulled = Boolean(payload?.pulled);
          if (pulled && gen === this.fetchGen) {
            const refreshed = await Promise.allSettled(
              statuses.map((status) => this.fetchStatusPage(status)),
            );
            const again = { ...this.statusPages };
            refreshed.forEach((result, index) => {
              if (result.status === "fulfilled") {
                again[statuses[index]] = result.value;
              }
            });
            this.statusPages = again;
            this.pruneSelection();
          }
        }
      } catch (error) {
        if (gen === this.fetchGen && !quiet) displaySonnerError(error);
      } finally {
        if (gen === this.fetchGen) {
          this.isLoading = false;
          this.isLoadingMore = false;
          if (!append) this.markFetchedLocation();
        }
      }
    },

    loadMore() {
      if (!this.hasMore || this.isLoadingMore || this.isLoading) return;
      this.fetchOrders({ append: true });
    },

    rowMoney(row) {
      return formatOrderMoney(
        row.total ?? row.Total,
        row.currency ?? row.Currency,
        this.moneyLocale,
      );
    },

    rowCreated(row) {
      return this.formatStoreDateTime(row.createdAt ?? row.CreatedAt);
    },

    rowCustomer(row) {
      return row.customerName ?? row.CustomerName ?? "";
    },

    isRowChecked(row) {
      return this.selectedIds.includes(orderExternalId(row));
    },

    toggleRow(row, checked) {
      const id = orderExternalId(row);
      if (!id) return;
      if (checked) {
        if (!this.selectedIds.includes(id)) {
          this.selectedIds = [...this.selectedIds, id];
        }
        return;
      }
      this.selectedIds = this.selectedIds.filter((item) => item !== id);
    },

    toggleAllVisible(checked) {
      const ids = this.items.map((row) => orderExternalId(row)).filter(Boolean);
      if (checked) {
        const set = new Set(this.selectedIds);
        for (const id of ids) set.add(id);
        this.selectedIds = [...set];
        return;
      }
      const drop = new Set(ids);
      this.selectedIds = this.selectedIds.filter((id) => !drop.has(id));
    },

    clearSelection() {
      this.selectedIds = [];
    },

    pruneSelection() {
      const visible = new Set(this.items.map((row) => orderExternalId(row)));
      this.selectedIds = this.selectedIds.filter((id) => visible.has(id));
    },

    onRowContextMenu(row) {
      this.contextMenuRow = row;
    },

    orderById(id) {
      const key = String(id || "");
      return (
        this.items.find((row) => orderExternalId(row) === key) ||
        (this.contextMenuRow && orderExternalId(this.contextMenuRow) === key
          ? this.contextMenuRow
          : null)
      );
    },

    askStatus(action, ids) {
      const list = (ids || []).filter(Boolean);
      if (!list.length || !this.canMutate) return;
      this.pendingJob = {
        kind: "status",
        action,
        ids: list,
        connectionId: this.connectionId,
        adapterKey: this.rowAdapterKey,
      };
      this.confirmOpen = true;
    },

    askLabel(ids) {
      const list = (ids || []).filter(Boolean);
      if (!list.length || !this.canBulkDownloadLabel) return;
      this.pendingJob = {
        kind: "label",
        ids: list,
        connectionId: this.connectionId,
        adapterKey: this.rowAdapterKey,
      };
      this.confirmOpen = true;
    },

    onContextOpen() {
      if (this.contextMenuRow) this.openOrder(this.contextMenuRow);
    },

    onContextAction(action) {
      const id = orderExternalId(this.contextMenuRow);
      this.askStatus(action, [id]);
    },

    onContextDownloadLabel() {
      const id = orderExternalId(this.contextMenuRow);
      this.askLabel([id]);
    },

    async runPendingJob(update = {}) {
      const job = this.pendingJob;
      this.confirmOpen = false;
      this.pendingJob = null;
      if (!job || this.bulkBusy) return;
      this.bulkBusy = true;
      try {
        const { failedIds } = await runPendingOrderJob(job, {
          t: (key, params) => this.$t(key, params),
          runOne: (id, current) =>
            current.kind === "label"
              ? this.downloadOrderLabel(id, current.connectionId)
              : this.postOrderStatus(id, { ...current, statusUpdate: update }),
          onItemError: displaySonnerError,
          onSuccess: displaySonnerSuccess,
          onPartial: displaySonnerError,
        });
        this.selectedIds = failedIds;
        await this.fetchOrders({ silent: true });
      } finally {
        this.bulkBusy = false;
      }
    },

    unwrapPayload(response) {
      return extractResponseData(response, null);
    },

    async postOrderStatus(orderId, job) {
      const next = await postMarketplaceOrderStatus({
        http,
        connectionId: job.connectionId ?? this.connectionId,
        orderId,
        action: job.action,
        order: this.orderById(orderId),
        adapterKey: job.adapterKey ?? this.rowAdapterKey,
        update: job.statusUpdate,
      });
      if (next) this.applySellerStatus(next);
    },

    async downloadOrderLabel(orderId, connectionId = this.connectionId) {
      const response = await http.get(
        marketplaceOrderLabelUrl(connectionId, orderId),
        { cache: false },
      );
      saveOrderLabelFile(this.unwrapPayload(response), `label-${orderId}`, {
        t: (key) => this.$t(key),
        triggerDownload: triggerBrowserDownload,
      });
    },
  },
};
</script>

<template>
  <AppPageChrome :has-nav-center="orderConnections.length > 0">
    <template v-if="mailboxes.length > 0" #nav-tabs>
      <Tabs
        class="mr-auto"
        :model-value="bucketId"
        @update:model-value="onMailboxTab"
      >
        <TabsList>
          <TabsTrigger v-for="box in mailboxes" :key="box.id" :value="box.id">
            {{ $t(box.labelKey) }}
            <span
              v-if="mailboxCount(box.id) > 0"
              class="text-muted-foreground text-xs font-normal tabular-nums"
              >{{ mailboxCount(box.id) }}</span
            >
          </TabsTrigger>
        </TabsList>
      </Tabs>
    </template>

    <template v-if="orderConnections.length > 0" #nav-center>
      <PageListToolbar
        v-model="searchQuery"
        :placeholder="$t('pages.ordersPage.searchPlaceholder')"
        :active-filter-count="channel !== CHANNEL_ALL ? 1 : 0"
        @submit="applySearch"
        @reset="onChannelChange(CHANNEL_ALL)"
      >
        <template #filters>
          <FilterMenuGroup
            :model-value="channel"
            :label="$t('pages.ordersPage.columnChannel')"
            :options="[
              { value: CHANNEL_ALL, label: $t('pages.ordersPage.channelAll') },
              { value: 'fbs', label: $t('pages.ordersPage.channel.fbs') },
              { value: 'fbo', label: $t('pages.ordersPage.channel.fbo') },
              { value: 'shop', label: $t('pages.ordersPage.channel.shop') },
            ]"
            @update:model-value="onChannelChange"
          />
        </template>
      </PageListToolbar>
    </template>

    <EmptyState
      v-if="bootstrapping || isLoading"
      loading
      :framed="false"
      class="min-h-0 flex-1"
    />

    <EmptyState
      v-else-if="emptyKind === 'noConnections'"
      :framed="false"
      :title="$t('pages.ordersPage.emptyNoConnectionsTitle')"
      :description="$t('pages.ordersPage.emptyNoConnectionsDescription')"
      class="min-h-0 flex-1"
    >
      <template #icon>
        <Plug />
      </template>
    </EmptyState>

    <EmptyState
      v-else-if="emptyKind === 'unsupported'"
      :framed="false"
      :title="$t('pages.ordersPage.emptyUnsupportedTitle')"
      :description="$t('pages.ordersPage.emptyUnsupportedDescription')"
      class="min-h-0 flex-1"
    >
      <template #icon>
        <ClipboardList />
      </template>
    </EmptyState>

    <EmptyState
      v-else-if="items.length === 0"
      :framed="false"
      :title="$t('pages.ordersPage.emptyTitle')"
      :description="$t(mailboxEmptyDescriptionKey)"
      class="min-h-0 flex-1"
    >
      <template #icon>
        <ClipboardList />
      </template>
    </EmptyState>

    <LogTableFrame v-else dock-footer>
      <Table
        :contain="false"
        class="w-full table-fixed border-separate border-spacing-0"
      >
        <colgroup>
          <col class="w-10" />
          <col class="w-40" />
          <col class="w-full" />
          <col class="w-32" />
          <col class="w-24" />
          <col class="w-32" />
          <col class="w-40" />
          <col class="w-36" />
        </colgroup>
        <TableHeader
          class="bg-background [&_th]:sticky [&_th]:top-0 [&_th]:z-[var(--z-sticky)] [&_th]:bg-background [&_th]:border-b"
        >
          <TableRow class="bg-background hover:bg-transparent">
            <TableHead class="w-10 px-3">
              <Checkbox
                :model-value="allVisibleSelected"
                :indeterminate="someVisibleSelected"
                :aria-label="$t('pages.ordersPage.selectAllLoaded')"
                @update:model-value="(v) => toggleAllVisible(v === true)"
              />
            </TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnId") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnItems") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnStatus") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnChannel") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnTotal") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnCreated") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnCustomer") }}</TableHead>
          </TableRow>
        </TableHeader>
        <ContextMenu>
          <ContextMenuTrigger as-child :disabled="items.length === 0">
            <TableBody class="[&_td]:border-b">
              <TableRow
                v-for="row in items"
                :key="orderExternalId(row)"
                class="cursor-pointer"
                :data-state="
                  isRowChecked(row) || orderExternalId(row) === selectedOrderId
                    ? 'selected'
                    : undefined
                "
                :aria-label="
                  $t('pages.ordersPage.openOrderAria', {
                    id: orderDisplayId(row),
                  })
                "
                @click="openOrder(row)"
                @contextmenu="onRowContextMenu(row)"
              >
                <TableCell class="px-3 py-3 align-top" @click.stop>
                  <Checkbox
                    :model-value="isRowChecked(row)"
                    :aria-label="
                      $t('pages.ordersPage.selectOrderAria', {
                        id: orderDisplayId(row),
                      })
                    "
                    @update:model-value="(v) => toggleRow(row, v === true)"
                  />
                </TableCell>
                <TableCell
                  class="px-3 py-3 align-top font-medium whitespace-nowrap"
                >
                  {{ orderDisplayId(row) }}
                </TableCell>
                <TableCell class="max-w-0 px-3 py-3 align-top">
                  <OrderItemsCell :row="row" />
                </TableCell>
                <TableCell class="px-3 py-3 align-top">
                  <span :class="orderStatusClass(orderStatusOf(row))">
                    {{ $t(orderStatusLabelKey(orderStatusOf(row))) }}
                  </span>
                </TableCell>
                <TableCell class="px-3 py-3 align-top text-muted-foreground">
                  {{ $t(orderChannelLabelKey(row.channel ?? row.Channel)) }}
                </TableCell>
                <TableCell class="px-3 py-3 align-top tabular-nums">
                  {{ rowMoney(row) || $t("common.emDash") }}
                </TableCell>
                <TableCell
                  class="px-3 py-3 align-top text-muted-foreground whitespace-nowrap"
                >
                  {{ rowCreated(row) }}
                </TableCell>
                <TableCell class="max-w-40 truncate px-3 py-3 align-top">
                  {{ rowCustomer(row) || $t("common.emDash") }}
                </TableCell>
              </TableRow>
            </TableBody>
          </ContextMenuTrigger>
          <OrderRowContextMenu
            v-if="contextMenuRow"
            :order="contextMenuRow"
            :adapter-key="rowAdapterKey"
            :can-mutate="canMutate"
            :can-download-label="canBulkDownloadLabel"
            @open="onContextOpen"
            @action="onContextAction"
            @download-label="onContextDownloadLabel"
          />
        </ContextMenu>
      </Table>

      <template #footer>
        <div class="flex min-w-0 flex-wrap items-center gap-2">
          <p class="text-sm text-muted-foreground tabular-nums">
            {{ $t("pages.ordersPage.loadedCount", { count: items.length }) }}
          </p>
          <template v-if="selectedCount > 0">
            <div class="h-4 w-px bg-border" />
            <span class="text-sm font-medium text-foreground">
              {{
                $t("pages.ordersPage.selectedCount", { count: selectedCount })
              }}
            </span>
            <Button
              variant="ghost"
              size="sm"
              class="h-8"
              :aria-label="$t('pages.ordersPage.clearSelection')"
              @click="clearSelection"
            >
              <X class="size-4" />
            </Button>
            <Button
              v-for="action in bulkCaps.actions"
              :key="action"
              variant="default"
              size="sm"
              class="h-8"
              :disabled="bulkBusy"
              @click="askStatus(action, selectedIds)"
            >
              <component :is="ACTION_ICONS[action]" class="size-4" />
              {{ $t(ACTION_LABEL[action]) }}
            </Button>
            <Button
              v-if="bulkCaps.canDownloadLabel"
              variant="default"
              size="sm"
              class="h-8"
              :disabled="bulkBusy"
              @click="askLabel(selectedIds)"
            >
              <Barcode class="size-4" />
              {{ $t("pages.ordersPage.actionDownloadLabel") }}
            </Button>
            <Button
              v-if="bulkCaps.canCancel"
              variant="destructive"
              size="sm"
              class="h-8"
              :disabled="bulkBusy"
              @click="askStatus('cancel', selectedIds)"
            >
              <XCircle class="size-4" />
              {{ $t("pages.ordersPage.actionCancel") }}
            </Button>
          </template>
        </div>
        <div class="flex items-center gap-2">
          <Select
            :model-value="String(pageSize)"
            @update:model-value="onPageSizeChange"
          >
            <SelectTrigger
              class="h-8 w-[4.75rem]"
              :aria-label="$t('pages.ordersPage.pageSize')"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="size in PAGE_SIZES"
                :key="size"
                :value="String(size)"
              >
                {{ size }}
              </SelectItem>
            </SelectContent>
          </Select>
          <Button
            v-if="hasMore"
            variant="outline"
            size="sm"
            class="h-8"
            :disabled="isLoadingMore"
            @click="loadMore"
          >
            <Spinner v-if="isLoadingMore" class="mr-1.5" size="sm" />
            {{ $t("pages.ordersPage.loadMore") }}
          </Button>
        </div>
      </template>
    </LogTableFrame>

    <OrderDetailSheet
      v-model:open="sheetOpen"
      :connection-id="connectionId"
      :order-id="selectedOrderId"
      :adapter-key="adapterKeyOf(selectedConnection)"
      :preview="selectedPreview"
      :can-mutate="canMutate"
      :can-download-label="canDownloadLabel"
      :can-manage-shipments="canManageShipments"
      :can-get-fulfillment-locations="canGetFulfillmentLocations"
      :adapter="adapterFor(selectedConnection)"
      @updated="onOrderUpdated"
    />

    <OrderJobConfirmDialog
      v-model:open="confirmOpen"
      :job="pendingJob"
      :adapter-key="pendingJob?.adapterKey || rowAdapterKey"
      :adapter="adapterByKey(pendingJob?.adapterKey || rowAdapterKey)"
      @confirm="runPendingJob"
    />
  </AppPageChrome>
</template>
