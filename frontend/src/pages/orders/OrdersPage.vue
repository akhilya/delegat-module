<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import OrderDetailSheet from "@/components/orders/OrderDetailSheet.vue";
import OrderItemsCell from "@/components/orders/OrderItemsCell.vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import Input from "@/components/ui/input/Input.vue";
import { EmptyState } from "@/components/ui/empty-state";
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
import { LogTableFrame } from "@/components/logs";
import { http } from "@/main";
import { getCachedMe } from "@/lib/meCache";
import { getLocale, toBcp47 } from "@/i18n";
import { isCabinetViewOnly } from "@/lib/permissions";
import {
  adapterCanGetOrders,
  adapterCanGetOrderLabels,
  adapterCanUpdateOrderStatus,
} from "@/lib/adapterCapabilities";
import {
  connectionNavLabel,
  formatOrderMoney,
  mergeOrderLists,
  orderBucketById,
  orderChannelLabelKey,
  orderDisplayId,
  orderExternalId,
  orderStatusClass,
  orderStatusLabelKey,
  orderStatusOf,
  readStoredConnectionId,
  statusesForBucket,
  writeStoredConnectionId,
} from "@/lib/ordersUi";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { displaySonnerError, extractResponseData } from "@/store/sonnerHelper";
import { mapActions } from "vuex";
import { ClipboardList, Plug, Search } from "lucide-vue-next";

const CHANNEL_ALL = "__all__";
const PAGE_SIZES = [25, 50, 100];
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
    OrderDetailSheet,
    OrderItemsCell,
    AdapterIcon,
    Button,
    Spinner,
    Input,
    EmptyState,
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
    LogTableFrame,
    ClipboardList,
    Plug,
    Search,
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
    };
  },
  data() {
    return {
      CHANNEL_ALL,
      PAGE_SIZES,
      bootstrapping: true,
      isLoading: false,
      isLoadingMore: false,
      connections: [],
      adapters: [],
      connectionId: null,
      searchQuery: "",
      channel: CHANNEL_ALL,
      pageSize: DEFAULT_PAGE_SIZE,
      statusPages: {},
      fetchGen: 0,
    };
  },
  computed: {
    bucketId() {
      return String(this.$route.params.bucket || "");
    },
    bucket() {
      return orderBucketById(this.bucketId);
    },
    breadcrumbs() {
      return [
        {
          label: this.$t("pages.ordersPage.breadcrumb"),
          to: "/orders",
        },
        {
          label: this.$t(this.bucket?.labelKey || "nav.orders"),
        },
      ];
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
    items() {
      return mergeOrderLists(
        Object.values(this.statusPages).map((page) => page.items || []),
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
    mailboxHintKey() {
      return this.bucket?.descriptionKey || "";
    },
    mailboxEmptyDescriptionKey() {
      return (
        this.bucket?.emptyDescriptionKey || "pages.ordersPage.emptyDescription"
      );
    },
    moneyLocale() {
      return toBcp47(getLocale());
    },
  },
  watch: {
    "$route.params.bucket"() {
      this.resetAndFetch();
    },
    "$route.query.connectionId"(raw) {
      const id = Number(raw);
      if (!id || id === this.connectionId) return;
      if (
        this.orderConnections.some(
          (connection) => connectionIdOf(connection) === id,
        )
      ) {
        this.connectionId = id;
        writeStoredConnectionId(this.storeId, id);
        this.resetAndFetch();
      }
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

    pickConnectionId() {
      const fromQuery = Number(this.$route.query.connectionId);
      if (
        fromQuery &&
        this.orderConnections.some(
          (connection) => connectionIdOf(connection) === fromQuery,
        )
      ) {
        return fromQuery;
      }
      const stored = readStoredConnectionId(this.storeId);
      if (
        stored &&
        this.orderConnections.some(
          (connection) => connectionIdOf(connection) === stored,
        )
      ) {
        return stored;
      }
      return connectionIdOf(this.orderConnections[0]) || null;
    },

    async persistConnection(id) {
      this.connectionId = id;
      writeStoredConnectionId(this.storeId, id);
      const query = { ...this.$route.query };
      if (id) query.connectionId = String(id);
      else delete query.connectionId;
      const same =
        String(this.$route.query.connectionId || "") === String(id || "");
      if (!same) {
        await this.$router.replace({ path: this.$route.path, query });
      }
    },

    onConnectionChange(value) {
      const id = Number(value);
      if (!id || id === this.connectionId) return;
      const query = { ...this.$route.query };
      delete query.order;
      query.connectionId = String(id);
      this.connectionId = id;
      writeStoredConnectionId(this.storeId, id);
      this.$router.replace({ path: this.$route.path, query });
      this.resetAndFetch();
    },

    applySearch() {
      this.searchQuery = (this.searchQuery || "").trim();
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

    onOrderUpdated() {
      this.fetchOrders({ silent: true });
    },

    resetAndFetch() {
      this.statusPages = {};
      this.fetchOrders();
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
      const id = this.pickConnectionId();
      if (id) await this.persistConnection(id);
      await this.fetchOrders();
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

      const params = {
        status,
        pageSize: this.pageSize,
        search: this.searchQuery.trim() || undefined,
        channel: this.channel !== CHANNEL_ALL ? this.channel : undefined,
      };
      if (append && prev.nextCursor) {
        params.cursor = prev.nextCursor;
      } else if (append) {
        params.page = (prev.page || 1) + 1;
      } else {
        params.page = 1;
      }

      const response = await http.get(
        `/marketplace/connections/${this.connectionId}/orders`,
        { params, cache: false },
      );
      const data = this.unwrapPage(response);
      const items = Array.isArray(data.items) ? data.items : [];
      return {
        items: append ? [...(prev.items || []), ...items] : items,
        page: Number(data.page) || params.page || 1,
        nextCursor: data.nextCursor || null,
        hasMore: Boolean(data.hasMore),
      };
    },

    async fetchOrders({ append = false, silent = false } = {}) {
      if (!this.connectionId || !this.canListOrders) {
        this.statusPages = {};
        this.isLoading = false;
        this.isLoadingMore = false;
        return;
      }

      if (!append) this.fetchGen += 1;
      const gen = this.fetchGen;
      if (append) this.isLoadingMore = true;
      else if (!silent) this.isLoading = true;

      const statuses = statusesForBucket(this.bucketId);
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
            displaySonnerError(result.reason);
          } else {
            displaySonnerError(result.reason);
          }
        });
        this.statusPages = next;
      } catch (error) {
        if (gen === this.fetchGen) displaySonnerError(error);
      } finally {
        if (gen === this.fetchGen) {
          this.isLoading = false;
          this.isLoadingMore = false;
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
  },
};
</script>

<template>
  <AppPageChrome
    :breadcrumb-items="breadcrumbs"
    :has-nav-center="orderConnections.length > 0"
  >
    <template v-if="orderConnections.length > 0" #nav-center>
      <Select
        :model-value="connectionId ? String(connectionId) : ''"
        :disabled="bootstrapping"
        @update:model-value="onConnectionChange"
      >
        <SelectTrigger
          class="h-9 w-full"
          :aria-label="$t('pages.ordersPage.connectionLabel')"
        >
          <SelectValue
            :placeholder="$t('pages.ordersPage.connectionPlaceholder')"
          />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="connection in orderConnections"
            :key="connectionIdOf(connection)"
            :value="String(connectionIdOf(connection))"
            :text-value="connectionLabel(connection)"
          >
            <span class="flex min-w-0 items-center gap-2">
              <AdapterIcon
                :adapter-key="adapterKeyOf(connection)"
                class="size-4"
              />
              <span class="truncate">{{ connectionLabel(connection) }}</span>
            </span>
          </SelectItem>
        </SelectContent>
      </Select>
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

    <p
      v-if="mailboxHintKey && orderConnections.length > 0"
      class="mb-3 text-xs text-muted-foreground"
    >
      {{ $t(mailboxHintKey) }}
    </p>

    <EmptyState
      v-if="bootstrapping || isLoading"
      loading
      :framed="false"
      class="min-h-[12rem]"
    />

    <EmptyState
      v-else-if="emptyKind === 'noConnections'"
      :framed="false"
      :title="$t('pages.ordersPage.emptyNoConnectionsTitle')"
      :description="$t('pages.ordersPage.emptyNoConnectionsDescription')"
      class="min-h-[12rem]"
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
      class="min-h-[12rem]"
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
      class="min-h-[12rem]"
    >
      <template #icon>
        <ClipboardList />
      </template>
    </EmptyState>

    <LogTableFrame v-else>
      <Table contain class="table-fixed">
        <colgroup>
          <col class="w-40" />
          <col class="w-full" />
          <col class="w-32" />
          <col class="w-24" />
          <col class="w-32" />
          <col class="w-40" />
          <col class="w-36" />
        </colgroup>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t("pages.ordersPage.columnId") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnItems") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnStatus") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnChannel") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnTotal") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnCreated") }}</TableHead>
            <TableHead>{{ $t("pages.ordersPage.columnCustomer") }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow
            v-for="row in items"
            :key="orderExternalId(row)"
            class="cursor-pointer"
            :data-state="
              orderExternalId(row) === selectedOrderId ? 'selected' : undefined
            "
            :aria-label="
              $t('pages.ordersPage.openOrderAria', { id: orderDisplayId(row) })
            "
            @click="openOrder(row)"
          >
            <TableCell class="px-3 py-3 align-top font-medium whitespace-nowrap">
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
      </Table>

      <template #footer>
        <p class="text-sm text-muted-foreground tabular-nums">
          {{ $t("pages.ordersPage.loadedCount", { count: items.length }) }}
        </p>
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
      :preview="selectedPreview"
      :can-mutate="canMutate"
      :can-download-label="canDownloadLabel"
      @updated="onOrderUpdated"
    />
  </AppPageChrome>
</template>
