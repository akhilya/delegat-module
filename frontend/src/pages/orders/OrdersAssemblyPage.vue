<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import OrderDetailSheet from "@/components/orders/OrderDetailSheet.vue";
import OrderAssemblyRow from "@/components/orders/OrderAssemblyRow.vue";
import OrderRowContextMenu from "@/components/orders/OrderRowContextMenu.vue";
import OrderJobConfirmDialog from "@/components/orders/OrderJobConfirmDialog.vue";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import Input from "@/components/ui/input/Input.vue";
import { EmptyState } from "@/components/ui/empty-state";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  ContextMenu,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";
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
  assemblyCounts,
  connectionNavLabel,
  filterOrdersBySearch,
  formatOrderMoney,
  mailboxForOverviewTab,
  mergeOrderLists,
  ORDER_OVERVIEW_TABS,
  orderDisplayId,
  orderExternalId,
  orderMailboxPath,
  orderSearchLookupIds,
  orderStatusLabelKey,
  orderStatusOf,
  overviewTabEmptyDescriptionKey,
  overviewTabEmptyTitleKey,
  overviewTabForStatus,
  overviewTabPath,
  parseOrdersOverviewTab,
  marketplaceOrderLabelUrl,
  postMarketplaceOrderStatus,
  resolveOrderSearchNavigation,
  runPendingOrderJob,
  saveOrderLabelFile,
} from "@/lib/ordersUi";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { mapActions } from "vuex";
import {
  ChevronDown,
  Package,
  Plug,
  Search,
} from "lucide-vue-next";

const CHANNEL_ALL = "__all__";
const DEFAULT_PAGE_SIZE = 50;

function connectionIdOf(connection) {
  return Number(connection?.id ?? connection?.Id ?? 0);
}

function adapterKeyOf(connection) {
  return connection?.adapterKey ?? connection?.AdapterKey ?? "";
}

function isConnectionActive(connection) {
  const active = connection?.isActive ?? connection?.IsActive;
  return active !== false;
}

export default {
  components: {
    AppPageChrome,
    AdapterIcon,
    OrderDetailSheet,
    OrderAssemblyRow,
    OrderRowContextMenu,
    OrderJobConfirmDialog,
    Button,
    Spinner,
    Input,
    EmptyState,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    InsetGroup,
    InsetGroupItem,
    Collapsible,
    CollapsibleContent,
    CollapsibleTrigger,
    ContextMenu,
    ContextMenuTrigger,
    ChevronDown,
    Package,
    Plug,
    Search,
    Tabs,
    TabsList,
    TabsTrigger,
  },
  setup() {
    const { loadMemberships } = useStoreDateTime();
    loadMemberships();
    return {
      connectionIdOf,
      adapterKeyOf,
      orderExternalId,
      orderDisplayId,
      orderStatusOf,
      orderStatusLabelKey,
      ORDER_OVERVIEW_TABS,
      overviewTabEmptyDescriptionKey,
      overviewTabEmptyTitleKey,
    };
  },
  data() {
    return {
      CHANNEL_ALL,
      bootstrapping: true,
      isLoading: false,
      connections: [],
      adapters: [],
      searchQuery: "",
      channel: CHANNEL_ALL,
      pageSize: DEFAULT_PAGE_SIZE,
      pagesByConnection: {},
      openByConnection: {},
      loadingMore: {},
      fetchGen: 0,
      storePage: 1,
      storeHasMore: false,
      contextMenuRow: null,
      contextMenuGroupId: null,
      pendingJob: null,
      confirmOpen: false,
      jobBusy: false,
    };
  },
  computed: {
    breadcrumbs() {
      const tab = ORDER_OVERVIEW_TABS.find((item) => item.id === this.overviewTab);
      return [
        {
          label: this.$t("pages.ordersPage.breadcrumb"),
          to: "/orders",
        },
        { label: this.$t(tab?.labelKey || "nav.ordersAssembly") },
      ];
    },
    overviewTab() {
      return parseOrdersOverviewTab(this.$route.query.tab);
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
    moneyLocale() {
      return toBcp47(getLocale());
    },
    groups() {
      const groups = this.orderConnections
        .map((connection) => {
          const id = connectionIdOf(connection);
          const adapterKey = adapterKeyOf(connection);
          const pages = this.pagesByConnection[id] || {};
          const orders = filterOrdersBySearch(
            mergeOrderLists(
              Object.values(pages).map((page) => page.items || []),
            ),
            this.searchQuery,
          );
          const counts = assemblyCounts(orders);
          return {
            id,
            connection,
            adapterKey,
            label: this.connectionLabel(connection),
            orders,
            counts,
            hasMore: false,
            loadingMore: Boolean(this.loadingMore[id]),
          };
        })
        .filter((group) => group.orders.length > 0);
      if (groups.length && this.storeHasMore) {
        const last = groups.length - 1;
        groups[last] = {
          ...groups[last],
          hasMore: true,
          loadingMore: Boolean(this.loadingMore.store),
        };
      }
      return groups;
    },
    totalOrders() {
      return this.groups.reduce((sum, group) => sum + group.counts.total, 0);
    },
    emptyKind() {
      if (this.orderConnections.length === 0) return "noConnections";
      if (this.totalOrders === 0) return "empty";
      return "";
    },
    selectedOrderId() {
      return String(this.$route.query.order || "").trim();
    },
    selectedConnectionId() {
      const n = Number(this.$route.query.connectionId);
      return Number.isFinite(n) && n > 0 ? n : null;
    },
    selectedConnection() {
      return (
        this.orderConnections.find(
          (connection) => connectionIdOf(connection) === this.selectedConnectionId,
        ) || null
      );
    },
    selectedAdapter() {
      return this.adapterFor(this.selectedConnection);
    },
    selectedPreview() {
      if (!this.selectedOrderId || !this.selectedConnectionId) return null;
      const group = this.groups.find(
        (item) => item.id === this.selectedConnectionId,
      );
      return (
        group?.orders.find(
          (row) => orderExternalId(row) === this.selectedOrderId,
        ) || null
      );
    },
    sheetOpen: {
      get() {
        return Boolean(this.selectedOrderId && this.selectedConnectionId);
      },
      set(open) {
        if (open) return;
        this.closeSheet();
      },
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
    canManageShipments() {
      return adapterCanManageShipments(this.selectedAdapter);
    },
    canGetFulfillmentLocations() {
      return adapterCanGetFulfillmentLocations(this.selectedAdapter);
    },
    contextMenuGroup() {
      if (this.contextMenuGroupId == null) return null;
      return (
        this.groups.find((group) => group.id === this.contextMenuGroupId) ||
        null
      );
    },
  },
  watch: {
    storeId() {
      this.bootstrap();
    },
    overviewTab() {
      if (!this.bootstrapping) this.fetchOrders();
    },
  },
  async mounted() {
    await this.bootstrap();
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
      const key = adapterKeyOf(connection);
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

    compactQuery(query) {
      const out = {};
      for (const [key, value] of Object.entries(query || {})) {
        if (value == null || value === "") continue;
        out[key] = Array.isArray(value) ? value.map(String) : String(value);
      }
      return out;
    },

    groupCaption(counts) {
      if (this.overviewTab !== "assembly") {
        return this.$t("pages.ordersPage.overviewGroupCount", {
          count: counts.total,
        });
      }
      if (counts.waiting && counts.packing) {
        return this.$t("pages.ordersPage.assemblyWaitingAndPacking", counts);
      }
      if (counts.waiting) {
        return this.$t("pages.ordersPage.assemblyWaiting", {
          count: counts.waiting,
        });
      }
      if (counts.packing) {
        return this.$t("pages.ordersPage.assemblyPacking", {
          count: counts.packing,
        });
      }
      return this.$t("pages.ordersPage.assemblyEmptyGroup");
    },

    isGroupOpen(id) {
      if (Object.prototype.hasOwnProperty.call(this.openByConnection, id)) {
        return this.openByConnection[id] === true;
      }
      const group = this.groups.find((item) => item.id === id);
      return Boolean(group?.counts.total);
    },

    setGroupOpen(id, open) {
      this.openByConnection = { ...this.openByConnection, [id]: open };
    },

    rowMoney(row) {
      return formatOrderMoney(
        row.total ?? row.Total,
        row.currency ?? row.Currency,
        this.moneyLocale,
      );
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
      await this.fetchOrders();
      this.bootstrapping = false;
    },

    async fetchStorePage(page, { append = false } = {}) {
      const params = {
        tab: this.overviewTab,
        page,
        pageSize: this.pageSize,
        search: this.searchQuery.trim() || undefined,
        channel: this.channel !== CHANNEL_ALL ? this.channel : undefined,
      };
      const response = await http.get("/marketplace/orders", {
        params,
        cache: false,
      });
      const data = this.unwrapPage(response);
      const rows = Array.isArray(data.items) ? data.items : [];
      const next = append ? { ...this.pagesByConnection } : {};
      for (const row of rows) {
        const connectionId = Number(row.connectionId ?? row.ConnectionId);
        if (!Number.isFinite(connectionId) || connectionId <= 0) continue;
        const order = row.order ?? row.Order;
        if (!order) continue;
        const existing = next[connectionId]?.all?.items || [];
        next[connectionId] = {
          all: {
            items: [...existing, order],
            page: Number(data.page) || page,
            nextCursor: data.nextCursor || null,
            hasMore: Boolean(data.hasMore),
          },
        };
      }
      this.pagesByConnection = next;
      this.storePage = Number(data.page) || page;
      this.storeHasMore = Boolean(data.hasMore);
    },

    async fetchOrders({ silent = false } = {}) {
      if (!this.orderConnections.length) {
        this.pagesByConnection = {};
        this.storeHasMore = false;
        this.isLoading = false;
        return;
      }

      this.fetchGen += 1;
      const gen = this.fetchGen;
      if (!silent) this.isLoading = true;

      try {
        await this.fetchStorePage(1);
        if (gen !== this.fetchGen) return;
        const sync = await http.post("/marketplace/orders/sync", null, {
          params: { tab: this.overviewTab },
          cache: false,
        });
        const syncData = extractResponseData(sync, {});
        if (syncData?.pulled) await this.fetchStorePage(1);
      } catch (error) {
        if (gen === this.fetchGen) displaySonnerError(error);
      } finally {
        if (gen === this.fetchGen) this.isLoading = false;
      }
    },

    async loadMore() {
      if (!this.storeHasMore || this.isLoading) return;
      this.loadingMore = { ...this.loadingMore, store: true };
      try {
        await this.fetchStorePage(this.storePage + 1, { append: true });
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.loadingMore = { ...this.loadingMore, store: false };
      }
    },

    onChannelChange(value) {
      this.channel = value || CHANNEL_ALL;
      this.fetchOrders();
    },

    applySearch() {
      const q = (this.searchQuery || "").trim();
      this.searchQuery = q;
      if (q) {
        this.lookupAndOpenOrder(q);
        return;
      }
      this.fetchOrders();
    },

    async lookupAndOpenOrder(orderNumber) {
      this.fetchGen += 1;
      const gen = this.fetchGen;
      let found = null;
      let foundConnection = null;
      for (const connection of this.orderConnections) {
        const connectionId = connectionIdOf(connection);
        for (const id of orderSearchLookupIds(orderNumber)) {
          try {
            const response = await http.get(
              `/marketplace/connections/${connectionId}/orders/${encodeURIComponent(id)}`,
              { cache: false },
            );
            const data = extractResponseData(response, null);
            if (orderExternalId(data)) {
              found = data;
              foundConnection = connection;
              break;
            }
          } catch (error) {
            const status = error?.response?.status;
            const code = error?.response?.data?.code;
            if (status === 404 || code === "order_not_found") continue;
            displaySonnerError(error);
            return;
          }
        }
        if (found) break;
      }
      if (gen !== this.fetchGen || !found || !foundConnection) return;

      const connectionId = connectionIdOf(foundConnection);
      const status = orderStatusOf(found);
      const tab = overviewTabForStatus(status);
      if (!tab) {
        const next = resolveOrderSearchNavigation({
          found,
          connectionId,
          adapterKey: adapterKeyOf(foundConnection),
          currentPath: this.$route.path,
        });
        if (next) {
          await this.$router.push({ path: next.path, query: next.query });
        }
        return;
      }
      if (tab !== this.overviewTab) {
        await this.$router.push(
          overviewTabPath(tab, {
            order: orderExternalId(found),
            connectionId: String(connectionId),
          }),
        );
        return;
      }

      const prev = this.pagesByConnection[connectionId] || {};
      const statusPage = prev.all || {
        items: [],
        page: 1,
        nextCursor: null,
        hasMore: false,
      };
      const items = statusPage.items || [];
      const id = orderExternalId(found);
      if (!items.some((row) => orderExternalId(row) === id)) {
        this.pagesByConnection = {
          ...this.pagesByConnection,
          [connectionId]: {
            ...prev,
            all: { ...statusPage, items: [found, ...items] },
          },
        };
      }
      this.setGroupOpen(connectionId, true);
      this.openOrder(found, connectionId);
    },

    openOrder(row, connectionId) {
      const id = orderExternalId(row);
      if (!id || !connectionId) return;
      const query = this.compactQuery({
        ...this.$route.query,
        order: id,
        connectionId: String(connectionId),
      });
      this.$router.replace({ path: "/orders", query });
    },

    closeSheet() {
      if (!this.$route.query.order && !this.$route.query.connectionId) return;
      const query = { ...this.$route.query };
      delete query.order;
      delete query.connectionId;
      this.$router.replace({ path: "/orders", query });
    },

    onOrderUpdated() {
      this.fetchOrders({ silent: true });
    },

    groupCanMutate(group) {
      if (!group) return false;
      return (
        adapterCanUpdateOrderStatus(this.adapterFor(group.connection)) &&
        !isCabinetViewOnly(getCachedMe())
      );
    },

    groupCanDownloadLabel(group) {
      if (!group) return false;
      return (
        adapterCanGetOrderLabels(this.adapterFor(group.connection)) &&
        !isCabinetViewOnly(getCachedMe())
      );
    },

    onRowContextMenu(row, group) {
      this.contextMenuRow = row;
      this.contextMenuGroupId = group?.id ?? null;
    },

    orderById(id) {
      const key = String(id || "");
      if (
        this.contextMenuRow &&
        orderExternalId(this.contextMenuRow) === key
      ) {
        return this.contextMenuRow;
      }
      for (const group of this.groups) {
        const found = group.orders.find(
          (row) => orderExternalId(row) === key,
        );
        if (found) return found;
      }
      return null;
    },

    askStatus(action) {
      const group = this.contextMenuGroup;
      const id = orderExternalId(this.contextMenuRow);
      if (!id || !group || !this.groupCanMutate(group)) return;
      this.pendingJob = {
        kind: "status",
        action,
        ids: [id],
        connectionId: group.id,
        adapterKey: group.adapterKey,
      };
      this.confirmOpen = true;
    },

    askLabel() {
      const group = this.contextMenuGroup;
      const id = orderExternalId(this.contextMenuRow);
      if (!id || !group || !this.groupCanDownloadLabel(group)) return;
      this.pendingJob = {
        kind: "label",
        ids: [id],
        connectionId: group.id,
        adapterKey: group.adapterKey,
      };
      this.confirmOpen = true;
    },

    onContextOpen() {
      const group = this.contextMenuGroup;
      if (this.contextMenuRow && group) {
        this.openOrder(this.contextMenuRow, group.id);
      }
    },

    onContextAction(action) {
      this.askStatus(action);
    },

    onContextDownloadLabel() {
      this.askLabel();
    },

    async runPendingJob() {
      const job = this.pendingJob;
      this.confirmOpen = false;
      this.pendingJob = null;
      if (!job || this.jobBusy) return;
      this.jobBusy = true;
      try {
        await runPendingOrderJob(job, {
          t: (key, params) => this.$t(key, params),
          runOne: (id, current) =>
            current.kind === "label"
              ? this.downloadOrderLabel(id, current.connectionId)
              : this.postOrderStatus(id, current),
          onItemError: displaySonnerError,
          onSuccess: displaySonnerSuccess,
          onPartial: displaySonnerError,
        });
        await this.fetchOrders({ silent: true });
      } finally {
        this.jobBusy = false;
      }
    },

    async postOrderStatus(orderId, job) {
      await postMarketplaceOrderStatus({
        http,
        connectionId: job.connectionId,
        orderId,
        action: job.action,
        order: this.orderById(orderId),
        adapterKey: job.adapterKey,
      });
    },

    async downloadOrderLabel(orderId, connectionId) {
      const response = await http.get(
        marketplaceOrderLabelUrl(connectionId, orderId),
        { cache: false },
      );
      saveOrderLabelFile(extractResponseData(response, null), `label-${orderId}`, {
        t: (key) => this.$t(key),
        triggerDownload: triggerBrowserDownload,
      });
    },

    openMailbox(group) {
      this.$router.push({
        path: orderMailboxPath(
          group.id,
          mailboxForOverviewTab(this.overviewTab, group.adapterKey),
          group.adapterKey,
        ),
      });
    },

    onOverviewTab(tab) {
      const keep = {};
      if (this.$route.query.order) keep.order = this.$route.query.order;
      if (this.$route.query.connectionId) {
        keep.connectionId = this.$route.query.connectionId;
      }
      this.$router.push(overviewTabPath(tab, keep));
    },
  },
};
</script>

<template>
  <AppPageChrome
    :breadcrumb-items="breadcrumbs"
    :has-nav-center="orderConnections.length > 0"
  >
    <template v-if="orderConnections.length > 0" #nav-center>
      <Tabs :model-value="overviewTab" @update:model-value="onOverviewTab">
        <TabsList>
          <TabsTrigger
            v-for="tab in ORDER_OVERVIEW_TABS"
            :key="tab.id"
            :value="tab.id"
          >
            {{ $t(tab.labelKey) }}
          </TabsTrigger>
        </TabsList>
      </Tabs>
    </template>
    <div
      v-if="orderConnections.length > 0"
      class="mb-3 flex shrink-0 flex-wrap items-center gap-2"
    >
      <div class="relative h-9 min-w-0 flex-1 sm:max-w-sm">
        <Input
          v-model="searchQuery"
          class="absolute h-full w-full pr-10"
          :placeholder="$t('pages.ordersPage.searchPlaceholder')"
          @keypress.enter="applySearch"
        />
        <Button
          class="absolute top-1/2 right-0.5 size-8 -translate-y-1/2"
          variant="link"
          :aria-label="$t('pages.ordersPage.searchPlaceholder')"
          @click="applySearch"
        >
          <Search class="size-4" />
        </Button>
      </div>
      <Select :model-value="channel" @update:model-value="onChannelChange">
        <SelectTrigger class="h-9 w-full sm:w-40">
          <SelectValue :placeholder="$t('pages.ordersPage.channelAll')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="CHANNEL_ALL">
            {{ $t("pages.ordersPage.channelAll") }}
          </SelectItem>
          <SelectItem value="fbs">
            {{ $t("pages.ordersPage.channel.fbs") }}
          </SelectItem>
          <SelectItem value="fbo">
            {{ $t("pages.ordersPage.channel.fbo") }}
          </SelectItem>
          <SelectItem value="shop">
            {{ $t("pages.ordersPage.channel.shop") }}
          </SelectItem>
        </SelectContent>
      </Select>
    </div>

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
      v-else-if="emptyKind === 'empty'"
      :framed="false"
      :title="$t(overviewTabEmptyTitleKey(overviewTab))"
      :description="$t(overviewTabEmptyDescriptionKey(overviewTab))"
      class="min-h-0 flex-1"
    >
      <template #icon>
        <Package />
      </template>
    </EmptyState>

    <div v-else class="flex flex-col gap-3">
      <InsetGroup v-for="group in groups" :key="group.id">
        <Collapsible
          :open="isGroupOpen(group.id)"
          class="w-full"
          @update:open="(open) => setGroupOpen(group.id, open)"
        >
          <CollapsibleTrigger as-child>
            <InsetGroupItem>
              <template #leading>
                <AdapterIcon :adapter-key="group.adapterKey" />
              </template>
              <p class="truncate">{{ group.label }}</p>
              <p class="mt-0.5 truncate text-xs font-normal text-muted-foreground">
                {{ groupCaption(group.counts) }}
              </p>
              <template #trailing>
                <span class="flex items-center gap-2">
                  <span class="tabular-nums text-xs text-muted-foreground">
                    {{ group.counts.total }}
                  </span>
                  <ChevronDown
                    class="size-4 shrink-0 text-muted-foreground opacity-40 transition-transform duration-[var(--duration-ui)] ease-[var(--ease-out-quart)] motion-reduce:transition-none"
                    :class="isGroupOpen(group.id) && 'rotate-180'"
                    aria-hidden="true"
                  />
                </span>
              </template>
            </InsetGroupItem>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div class="border-t border-border/60 px-0.5 pb-0.5 pt-0.5">
              <p
                v-if="group.orders.length === 0"
                class="px-2.5 py-2 text-xs text-muted-foreground"
              >
                {{ $t("pages.ordersPage.assemblyEmptyGroup") }}
              </p>
              <ContextMenu v-else>
                <ContextMenuTrigger as-child>
                  <div class="flex flex-col gap-0.5">
                    <OrderAssemblyRow
                      v-for="row in group.orders"
                      :key="orderExternalId(row)"
                      :row="row"
                      :money="rowMoney(row)"
                      :status="$t(orderStatusLabelKey(orderStatusOf(row)))"
                      :aria-label="
                        $t('pages.ordersPage.openOrderAria', {
                          id: orderDisplayId(row),
                        })
                      "
                      @click="openOrder(row, group.id)"
                      @contextmenu="onRowContextMenu(row, group)"
                    />
                  </div>
                </ContextMenuTrigger>
                <OrderRowContextMenu
                  v-if="contextMenuRow && contextMenuGroupId === group.id"
                  :order="contextMenuRow"
                  :adapter-key="group.adapterKey"
                  :can-mutate="groupCanMutate(group)"
                  :can-download-label="groupCanDownloadLabel(group)"
                  @open="onContextOpen"
                  @action="onContextAction"
                  @download-label="onContextDownloadLabel"
                />
              </ContextMenu>
              <div
                v-if="group.hasMore"
                class="flex items-center justify-between px-2.5 py-1.5"
              >
                <Button
                  variant="ghost"
                  size="sm"
                  class="h-8 px-2 text-muted-foreground"
                  @click.stop="openMailbox(group)"
                >
                  {{ $t("pages.ordersPage.assemblyOpenMailbox") }}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  class="h-8"
                  :disabled="group.loadingMore"
                  @click.stop="loadMore"
                >
                  <Spinner v-if="group.loadingMore" class="mr-1.5" size="sm" />
                  {{ $t("pages.ordersPage.loadMore") }}
                </Button>
              </div>
            </div>
          </CollapsibleContent>
        </Collapsible>
      </InsetGroup>
    </div>

    <OrderDetailSheet
      v-model:open="sheetOpen"
      :connection-id="selectedConnectionId"
      :order-id="selectedOrderId"
      :adapter-key="adapterKeyOf(selectedConnection)"
      :preview="selectedPreview"
      :can-mutate="canMutate"
      :can-download-label="canDownloadLabel"
      :can-manage-shipments="canManageShipments"
      :can-get-fulfillment-locations="canGetFulfillmentLocations"
      @updated="onOrderUpdated"
    />

    <OrderJobConfirmDialog
      v-model:open="confirmOpen"
      :job="pendingJob"
      :adapter-key="pendingJob?.adapterKey || ''"
      @confirm="runPendingJob"
    />
  </AppPageChrome>
</template>
