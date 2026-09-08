/** Normalized marketplace order statuses (SDK MarketplaceOrderStatuses). */
export const ORDER_STATUS = {
  New: "new",
  Confirmed: "confirmed",
  AwaitingPack: "awaiting_pack",
  AwaitingDeliver: "awaiting_deliver",
  Delivering: "delivering",
  Delivered: "delivered",
  Cancelled: "cancelled",
  Returned: "returned",
  Unpaid: "unpaid",
  Unknown: "unknown",
};

/** Normalized seller actions (SDK MarketplaceOrderActions). */
export const ORDER_ACTION = {
  Confirm: "confirm",
  Pack: "pack",
  Ship: "ship",
  Deliver: "deliver",
  Complete: "complete",
  Cancel: "cancel",
};

export const ORDER_ACTION_LABEL = {
  confirm: "pages.ordersPage.actionConfirm",
  pack: "pages.ordersPage.actionPack",
  ship: "pages.ordersPage.actionShip",
  deliver: "pages.ordersPage.actionDeliver",
  complete: "pages.ordersPage.actionComplete",
  cancel: "pages.ordersPage.actionCancel",
};

export const ORDER_ACTION_CONFIRM = {
  confirm: {
    title: "pages.ordersPage.confirmConfirmTitle",
    description: "pages.ordersPage.confirmConfirmDescription",
    label: "pages.ordersPage.actionConfirm",
    destructive: false,
  },
  pack: {
    title: "pages.ordersPage.confirmPackTitle",
    description: "pages.ordersPage.confirmPackDescription",
    label: "pages.ordersPage.actionPack",
    destructive: false,
  },
  ship: {
    title: "pages.ordersPage.confirmShipTitle",
    description: "pages.ordersPage.confirmShipDescription",
    label: "pages.ordersPage.actionShip",
    destructive: false,
  },
  deliver: {
    title: "pages.ordersPage.confirmDeliverTitle",
    description: "pages.ordersPage.confirmDeliverDescription",
    label: "pages.ordersPage.actionDeliver",
    destructive: false,
  },
  complete: {
    title: "pages.ordersPage.confirmCompleteTitle",
    description: "pages.ordersPage.confirmCompleteDescription",
    label: "pages.ordersPage.actionComplete",
    destructive: false,
  },
  cancel: {
    title: "pages.ordersPage.cancelTitle",
    description: "pages.ordersPage.cancelDescription",
    label: "pages.ordersPage.cancelConfirm",
    destructive: true,
  },
};

export const DEFAULT_ORDER_BUCKET = "new";

/**
 * Cabinet mailboxes. `statuses` are sent as separate list queries and merged.
 * `icon` is a lucide-compatible export name.
 */
export const ORDER_BUCKETS = [
  {
    id: "new",
    statuses: [ORDER_STATUS.New, ORDER_STATUS.Unpaid],
    icon: "Inbox",
    labelKey: "nav.ordersNew",
    titleKey: "routes.ordersNew",
    emptyDescriptionKey: "pages.ordersPage.emptyNewDescription",
  },
  {
    id: "pack",
    statuses: [ORDER_STATUS.Confirmed, ORDER_STATUS.AwaitingPack],
    icon: "Package",
    labelKey: "nav.ordersPack",
    titleKey: "routes.ordersPack",
    emptyDescriptionKey: "pages.ordersPage.emptyPackDescription",
  },
  {
    id: "ship",
    statuses: [ORDER_STATUS.AwaitingDeliver],
    icon: "Truck",
    labelKey: "nav.ordersShip",
    titleKey: "routes.ordersShip",
    emptyDescriptionKey: "pages.ordersPage.emptyShipDescription",
  },
  {
    id: "delivery",
    statuses: [ORDER_STATUS.Delivering],
    icon: "MapPin",
    labelKey: "nav.ordersDelivery",
    titleKey: "routes.ordersDelivery",
    emptyDescriptionKey: "pages.ordersPage.emptyDeliveryDescription",
  },
  {
    id: "done",
    statuses: [ORDER_STATUS.Delivered],
    icon: "CheckCircle",
    labelKey: "nav.ordersDone",
    titleKey: "routes.ordersDone",
    emptyDescriptionKey: "pages.ordersPage.emptyDoneDescription",
  },
  {
    id: "closed",
    statuses: [ORDER_STATUS.Cancelled, ORDER_STATUS.Returned],
    icon: "XCircle",
    labelKey: "nav.ordersClosed",
    titleKey: "routes.ordersClosed",
    emptyDescriptionKey: "pages.ordersPage.emptyClosedDescription",
  },
];

const BUCKET_BY_ID = new Map(ORDER_BUCKETS.map((b) => [b.id, b]));

export function isOrderBucket(id) {
  return BUCKET_BY_ID.has(String(id || ""));
}

export function orderBucketById(id) {
  return BUCKET_BY_ID.get(String(id || "")) || null;
}

export function orderBucketPath(id) {
  const bucket = isOrderBucket(id) ? id : DEFAULT_ORDER_BUCKET;
  return `/orders/${bucket}`;
}

const ADAPTER_ORDER_MAILBOXES = {
  ozon: ["pack", "ship", "delivery", "done", "closed"],
  wildberries: ["pack", "ship", "delivery", "done", "closed"],
  yandex: ["new", "pack", "ship", "delivery", "done", "closed"],
  woocommerce: ["new", "pack", "done", "closed"],
  opencart: ["new", "pack", "done", "closed"],
};

function normalizeAdapterKey(adapterKey) {
  return String(adapterKey || "")
    .trim()
    .toLowerCase();
}

function connectionIdOf(connection) {
  return Number(connection?.id ?? connection?.Id ?? 0);
}

function adapterKeyOf(connection) {
  return connection?.adapterKey ?? connection?.AdapterKey ?? "";
}

export function orderMailboxesForAdapter(adapterKey) {
  const key = normalizeAdapterKey(adapterKey);
  const ids =
    ADAPTER_ORDER_MAILBOXES[key] || ORDER_BUCKETS.map((box) => box.id);
  return ids.map((id) => orderBucketById(id)).filter(Boolean);
}

export function defaultOrderMailbox(adapterKey) {
  return orderMailboxesForAdapter(adapterKey)[0]?.id || "pack";
}

export function resolveOrderMailbox(adapterKey, requested) {
  const allowed = orderMailboxesForAdapter(adapterKey);
  const hit = allowed.find((box) => box.id === String(requested || ""));
  return hit?.id || allowed[0]?.id || "pack";
}

export function orderMailboxPath(connectionId, bucket, adapterKey) {
  const id = Number(connectionId);
  if (!Number.isFinite(id) || id <= 0) return "/orders";
  return `/orders/${id}/${resolveOrderMailbox(adapterKey, bucket)}`;
}

function pickConnection(connections, id) {
  const n = Number(id);
  if (!Number.isFinite(n) || n <= 0) return null;
  return (connections || []).find((item) => connectionIdOf(item) === n) || null;
}

export function parseOrdersPath(routePath) {
  const path = String(routePath || "").split("?")[0];
  const parts = path.replace(/\/+$/, "").split("/").filter(Boolean);
  if (parts[0] !== "orders") {
    return { connectionId: null, bucket: null, legacyBucket: null };
  }
  if (parts.length < 2) {
    return { connectionId: null, bucket: null, legacyBucket: null };
  }
  if (parts.length === 2) {
    if (isOrderBucket(parts[1])) {
      return { connectionId: null, bucket: null, legacyBucket: parts[1] };
    }
    const id = Number(parts[1]);
    return {
      connectionId: Number.isFinite(id) && id > 0 ? id : null,
      bucket: null,
      legacyBucket: null,
    };
  }
  const id = Number(parts[1]);
  return {
    connectionId: Number.isFinite(id) && id > 0 ? id : null,
    bucket: parts[2] || null,
    legacyBucket: null,
  };
}

export function isOrdersOverviewPath(routePath) {
  const path = String(routePath || "")
    .split("?")[0]
    .replace(/\/+$/, "");
  return path === "/orders";
}

export const ORDER_OVERVIEW_TABS = [
  { id: "assembly", labelKey: "nav.ordersAssembly" },
  { id: "ship", labelKey: "nav.ordersPicking" },
  { id: "delivery", labelKey: "nav.ordersInDelivery" },
  { id: "done", labelKey: "nav.ordersDelivered" },
];

export function parseOrdersOverviewTab(queryTab) {
  const id = String(queryTab || "assembly")
    .trim()
    .toLowerCase();
  return ORDER_OVERVIEW_TABS.some((tab) => tab.id === id) ? id : "assembly";
}

export function overviewTabPath(tab, query = {}) {
  const id = parseOrdersOverviewTab(tab);
  const nextQuery = { ...query };
  if (id === "assembly") delete nextQuery.tab;
  else nextQuery.tab = id;
  return { path: "/orders", query: nextQuery };
}

export function statusesForOverviewTab(tab) {
  switch (parseOrdersOverviewTab(tab)) {
    case "assembly":
      return [
        ORDER_STATUS.New,
        ORDER_STATUS.Unpaid,
        ORDER_STATUS.Confirmed,
        ORDER_STATUS.AwaitingPack,
      ];
    case "ship":
      return [ORDER_STATUS.AwaitingDeliver];
    case "delivery":
      return [ORDER_STATUS.Delivering];
    case "done":
      return [ORDER_STATUS.Delivered];
    default:
      return [];
  }
}

export function mailboxForOverviewTab(tab, adapterKey) {
  const id = parseOrdersOverviewTab(tab);
  const wanted =
    id === "ship"
      ? "ship"
      : id === "delivery"
        ? "delivery"
        : id === "done"
          ? "done"
          : "pack";
  const allowed = orderMailboxesForAdapter(adapterKey);
  if (allowed.some((box) => box.id === wanted)) return wanted;
  return defaultOrderMailbox(adapterKey);
}

export function overviewTabForStatus(status) {
  const s = orderStatusOf({ status });
  if (
    s === ORDER_STATUS.New ||
    s === ORDER_STATUS.Unpaid ||
    s === ORDER_STATUS.Confirmed ||
    s === ORDER_STATUS.AwaitingPack
  ) {
    return "assembly";
  }
  if (s === ORDER_STATUS.AwaitingDeliver) return "ship";
  if (s === ORDER_STATUS.Delivering) return "delivery";
  if (s === ORDER_STATUS.Delivered) return "done";
  return null;
}

export function overviewTabEmptyTitleKey(tab) {
  switch (parseOrdersOverviewTab(tab)) {
    case "ship":
      return "pages.ordersPage.emptyOverviewShipTitle";
    case "delivery":
      return "pages.ordersPage.emptyOverviewDeliveryTitle";
    case "done":
      return "pages.ordersPage.emptyOverviewDoneTitle";
    default:
      return "pages.ordersPage.emptyAssemblyTitle";
  }
}

export function overviewTabEmptyDescriptionKey(tab) {
  switch (parseOrdersOverviewTab(tab)) {
    case "ship":
      return "pages.ordersPage.emptyOverviewShipDescription";
    case "delivery":
      return "pages.ordersPage.emptyOverviewDeliveryDescription";
    case "done":
      return "pages.ordersPage.emptyOverviewDoneDescription";
    default:
      return "pages.ordersPage.emptyAssemblyDescription";
  }
}

export function assemblyMailboxesForAdapter(adapterKey) {
  return orderMailboxesForAdapter(adapterKey).filter(
    (box) => box.id === "new" || box.id === "pack",
  );
}

export function assemblyStatusesForAdapter(adapterKey) {
  const statuses = [];
  for (const box of assemblyMailboxesForAdapter(adapterKey)) {
    for (const status of statusesForBucket(box.id, adapterKey)) {
      if (!statuses.includes(status)) statuses.push(status);
    }
  }
  return statuses;
}

export function isAssemblyOrderStatus(status) {
  const s = orderStatusOf({ status });
  return (
    s === ORDER_STATUS.New ||
    s === ORDER_STATUS.Unpaid ||
    s === ORDER_STATUS.Confirmed ||
    s === ORDER_STATUS.AwaitingPack
  );
}

export function assemblyCounts(orders) {
  let waiting = 0;
  let packing = 0;
  for (const row of Array.isArray(orders) ? orders : []) {
    const s = orderStatusOf(row);
    if (s === ORDER_STATUS.New || s === ORDER_STATUS.Unpaid) waiting += 1;
    else if (s === ORDER_STATUS.Confirmed || s === ORDER_STATUS.AwaitingPack) {
      packing += 1;
    }
  }
  return { waiting, packing, total: waiting + packing };
}

/**
 * Canonical cabinet URL: `/orders` (assembly overview) or
 * `/orders/:connectionId/:mailbox`. Legacy `/orders/:bucket` and
 * `/orders/:connectionId` collapse onto a mailbox.
 */
export function resolveOrdersLocation({
  connections = [],
  storedConnectionId,
  preferredMailbox,
  routePath,
  query = {},
} = {}) {
  const nextQuery = { ...query };
  if (isOrdersOverviewPath(routePath)) {
    return { path: "/orders", query: nextQuery };
  }
  const fromQuery = nextQuery.connectionId;
  delete nextQuery.connectionId;
  const parsed = parseOrdersPath(routePath);

  if (!connections.length) {
    return { path: "/orders", query: nextQuery };
  }

  const connection =
    pickConnection(connections, parsed.connectionId) ||
    pickConnection(connections, fromQuery) ||
    pickConnection(connections, storedConnectionId) ||
    connections[0];

  const requested =
    parsed.bucket || parsed.legacyBucket || preferredMailbox || null;
  const adapterKey = adapterKeyOf(connection);
  return {
    path: orderMailboxPath(connectionIdOf(connection), requested, adapterKey),
    query: nextQuery,
  };
}

export function statusesForBucket(id, adapterKey) {
  const statuses = orderBucketById(id)?.statuses?.slice() || [];
  if (String(id) === "pack" && normalizeAdapterKey(adapterKey) === "ozon") {
    for (const extra of [ORDER_STATUS.New, ORDER_STATUS.Unpaid]) {
      if (!statuses.includes(extra)) statuses.push(extra);
    }
  }
  return statuses;
}

/** Mailbox that lists this normalized status for the adapter, or null if it has no such tab. */
export function mailboxForOrderStatus(status, adapterKey) {
  const s = orderStatusOf({ status });
  for (const box of orderMailboxesForAdapter(adapterKey)) {
    if (statusesForBucket(box.id, adapterKey).includes(s)) return box.id;
  }
  return null;
}

/**
 * After a successful lookup by order number, open that order on the mailbox
 * that actually contains it (search used to stay on the current tab and miss).
 */
export function resolveOrderSearchNavigation({
  found,
  connectionId,
  adapterKey,
  currentPath,
}) {
  const id = orderExternalId(found);
  if (!id) return null;
  const mailbox = mailboxForOrderStatus(orderStatusOf(found), adapterKey);
  const path = mailbox
    ? orderMailboxPath(connectionId, mailbox, adapterKey)
    : currentPath || "/orders";
  if (!path || path === "/orders") return null;
  return { path, query: { order: id } };
}

/** Ids to try on GET …/orders/{id}. Ozon table shows order_number; get needs posting_number. */
export function orderSearchLookupIds(search) {
  const q = String(search || "").trim();
  if (!q) return [];
  const ids = [q];
  const parts = q
    .split("-")
    .map((part) => part.trim())
    .filter(Boolean);
  if (parts.length === 2 && parts.every((part) => /^\d+$/.test(part))) {
    ids.push(`${q}-1`);
  }
  return ids;
}

export function orderMatchesSearch(row, search) {
  const needle = String(search || "")
    .trim()
    .toLowerCase();
  if (!needle) return true;
  const id = orderExternalId(row).toLowerCase();
  const display = orderDisplayId(row).toLowerCase();
  return id.includes(needle) || display.includes(needle);
}

export function filterOrdersBySearch(rows, search) {
  const list = Array.isArray(rows) ? rows : [];
  const q = String(search || "").trim();
  if (!q) return list;
  return list.filter((row) => orderMatchesSearch(row, q));
}

export function orderSearchHitPage(found) {
  if (!orderExternalId(found)) return null;
  return {
    items: [found],
    page: 1,
    nextCursor: null,
    hasMore: false,
  };
}

/** Search pins the found row; do not replace it with the destination mailbox dump. */
export function shouldFetchMailboxAfterRouteSync({
  fetch = false,
  connectionChanged = false,
  mailboxChanged = false,
  searchQuery = "",
} = {}) {
  if (String(searchQuery || "").trim()) return false;
  return Boolean(fetch || connectionChanged || mailboxChanged);
}

export function orderExternalId(row) {
  const id = row?.externalId ?? row?.ExternalId ?? "";
  return String(id);
}

export function orderDisplayId(row) {
  const display = row?.displayId ?? row?.DisplayId;
  if (display) return String(display);
  return orderExternalId(row);
}

export function orderItemsOf(row) {
  const list = row?.items ?? row?.Items ?? [];
  return Array.isArray(list) ? list : [];
}

export function orderItemName(item) {
  return String(
    item?.name ||
      item?.Name ||
      item?.sku ||
      item?.Sku ||
      item?.offerId ||
      item?.OfferId ||
      "",
  ).trim();
}

export function orderItemQuantity(item) {
  const n = Number(item?.quantity ?? item?.Quantity ?? 0);
  return Number.isFinite(n) ? n : 0;
}

export function orderItemImageUrl(item) {
  const url = String(item?.imageUrl || item?.ImageUrl || "").trim();
  return url || null;
}

export function orderItemSku(item) {
  return String(
    item?.sku || item?.Sku || item?.offerId || item?.OfferId || "",
  ).trim();
}

export function orderItemLocalProductId(item) {
  const n = Number(item?.localProductId ?? item?.LocalProductId ?? 0);
  return Number.isFinite(n) && n > 0 ? n : null;
}

export function orderItemProductTo(item) {
  const id = orderItemLocalProductId(item);
  return id ? `/products/${id}/edit` : null;
}

/** Collapsed mailbox shows this many lines; the rest sit behind Show N more. */
export const ORDER_ITEM_PREVIEW_MAX = 3;

export function orderLineCount(row) {
  return orderItemsOf(row).length;
}

export function orderPieceCount(row) {
  return orderItemsOf(row).reduce(
    (sum, item) => sum + orderItemQuantity(item),
    0,
  );
}

export function orderItemNames(row) {
  return orderItemsOf(row).map(orderItemName).filter(Boolean);
}

export function orderPrimaryItemName(row) {
  return orderItemNames(row)[0] || "";
}

export function orderAssemblyTitle(row) {
  return orderPrimaryItemName(row) || orderDisplayId(row);
}

export function orderAssemblyMoreCount(row) {
  return Math.max(0, orderLineCount(row) - 1);
}

export function orderPrimaryItemQuantity(row) {
  const item = orderItemsOf(row)[0];
  return item ? orderItemQuantity(item) : 0;
}

export function orderSecondaryItemNames(row) {
  return orderItemNames(row).slice(1);
}

export function orderHiddenLineCount(row, max = ORDER_ITEM_PREVIEW_MAX) {
  const limit = Number.isFinite(max) && max > 0 ? max : ORDER_ITEM_PREVIEW_MAX;
  return Math.max(0, orderLineCount(row) - limit);
}

export function orderItemsNeedCollapse(row, max = ORDER_ITEM_PREVIEW_MAX) {
  return orderHiddenLineCount(row, max) > 0;
}

/** Lines drawn in the mailbox cell. Collapsed rows stop at `max`. */
export function orderVisibleItems(row, expanded, max = ORDER_ITEM_PREVIEW_MAX) {
  const items = orderItemsOf(row);
  const limit = Number.isFinite(max) && max > 0 ? max : ORDER_ITEM_PREVIEW_MAX;
  if (expanded || items.length <= limit) return items;
  return items.slice(0, limit);
}

/** Distinct item photos, first-seen order, capped. */
export function orderItemImageUrls(row, max = 3) {
  const urls = [];
  const limit = Number.isFinite(max) && max > 0 ? max : 3;
  for (const item of orderItemsOf(row)) {
    const url = orderItemImageUrl(item);
    if (!url || urls.includes(url)) continue;
    urls.push(url);
    if (urls.length >= limit) break;
  }
  return urls;
}

/** Product names with quantities, comma-separated. */
export function orderItemsSummary(row, formatQty) {
  const parts = [];
  for (const item of orderItemsOf(row)) {
    const name = orderItemName(item);
    if (!name) continue;
    const qty = orderItemQuantity(item);
    if (typeof formatQty === "function" && qty > 1) {
      parts.push(`${name} ${formatQty(qty)}`.trim());
    } else if (qty > 1) {
      parts.push(`${name} × ${qty}`);
    } else {
      parts.push(name);
    }
  }
  return parts.join(", ");
}

export function orderStatusOf(row) {
  const raw = row?.status ?? row?.Status ?? ORDER_STATUS.Unknown;
  return String(raw || ORDER_STATUS.Unknown);
}

export function orderChannelOf(row) {
  return String(row?.channel ?? row?.Channel ?? "")
    .trim()
    .toLowerCase();
}

/** Ozon/WB logistics mark delivered; the seller cannot. */
export function orderDeliveryIsMarketplaceOwned(adapterKey) {
  const key = normalizeAdapterKey(adapterKey);
  return key === "ozon" || key === "wildberries";
}

export function orderActionNeedsShipmentFields(action, adapterKey) {
  if (String(action || "") !== ORDER_ACTION.Ship) return false;
  const key = normalizeAdapterKey(adapterKey);
  return key !== "ozon" && key !== "wildberries";
}

export function orderCreatedAtMs(row) {
  const raw = row?.createdAt ?? row?.CreatedAt;
  const t = raw ? Date.parse(raw) : NaN;
  return Number.isFinite(t) ? t : 0;
}

/** Merge list pages (one per status); newest first, first occurrence of an id wins. */
export function mergeOrderLists(lists) {
  const byId = new Map();
  for (const list of lists || []) {
    for (const row of list || []) {
      const id = orderExternalId(row);
      if (!id || byId.has(id)) continue;
      byId.set(id, row);
    }
  }
  return [...byId.values()].sort(
    (a, b) => orderCreatedAtMs(b) - orderCreatedAtMs(a),
  );
}

function shopPrimaryActions(status) {
  switch (status) {
    case ORDER_STATUS.New:
    case ORDER_STATUS.Unpaid:
      return [ORDER_ACTION.Confirm];
    case ORDER_STATUS.Confirmed:
    case ORDER_STATUS.AwaitingPack:
      return [ORDER_ACTION.Pack];
    case ORDER_STATUS.AwaitingDeliver:
      return [ORDER_ACTION.Ship];
    case ORDER_STATUS.Delivering:
      return [ORDER_ACTION.Complete];
    default:
      return [];
  }
}

/**
 * Seller buttons for this marketplace. Ozon/WB have no “mark delivered” API;
 * delivery is theirs after you hand over the parcel.
 */
export function primaryActionsForStatus(status, adapterKey, channel) {
  const s = orderStatusOf({ status });
  const key = normalizeAdapterKey(adapterKey);
  const ch = String(channel || "")
    .trim()
    .toLowerCase();

  if (key === "ozon") {
    if (ch === "fbo") return [];
    if (s === ORDER_STATUS.Confirmed || s === ORDER_STATUS.AwaitingPack) {
      return [ORDER_ACTION.Pack];
    }
    if (s === ORDER_STATUS.AwaitingDeliver) return [ORDER_ACTION.Ship];
    return [];
  }

  if (key === "wildberries") {
    if (s === ORDER_STATUS.AwaitingPack) return [ORDER_ACTION.Pack];
    if (s === ORDER_STATUS.AwaitingDeliver) return [ORDER_ACTION.Ship];
    return [];
  }

  if (key === "yandex") {
    if (ch === "fby") return [];
    if (s === ORDER_STATUS.New || s === ORDER_STATUS.Unpaid) {
      return [ORDER_ACTION.Confirm];
    }
    if (s === ORDER_STATUS.Confirmed || s === ORDER_STATUS.AwaitingPack) {
      return [ORDER_ACTION.Pack];
    }
    if (s === ORDER_STATUS.AwaitingDeliver) return [ORDER_ACTION.Ship];
    if (s === ORDER_STATUS.Delivering && (ch === "dbs" || ch === "express")) {
      return [ORDER_ACTION.Deliver];
    }
    return [];
  }

  return shopPrimaryActions(s);
}

export function canCancelOrder(status, adapterKey, channel) {
  const s = orderStatusOf({ status });
  if (
    s === ORDER_STATUS.Cancelled ||
    s === ORDER_STATUS.Returned ||
    s === ORDER_STATUS.Delivered
  ) {
    return false;
  }
  const key = normalizeAdapterKey(adapterKey);
  const ch = String(channel || "")
    .trim()
    .toLowerCase();

  if (key === "ozon") {
    if (ch === "fbo") return false;
    return (
      s === ORDER_STATUS.New ||
      s === ORDER_STATUS.Confirmed ||
      s === ORDER_STATUS.AwaitingPack ||
      s === ORDER_STATUS.AwaitingDeliver
    );
  }
  if (key === "wildberries") {
    return s === ORDER_STATUS.AwaitingPack;
  }
  if (key === "yandex") {
    if (ch === "fby") return false;
    if (s === ORDER_STATUS.Delivering && ch !== "dbs" && ch !== "express") {
      return false;
    }
    return true;
  }
  return true;
}

/**
 * Packing label / barcode when that marketplace can actually print it.
 * Ozon FBS: after assemble, Ready to ship. Wildberries: in a supply or in
 * delivery (`confirm` / `complete`). Yandex FBS/DBS: while packing
 * (`PROCESSING`). FBO/FBY and shop adapters have no seller label.
 */
export function canDownloadOrderLabel(status, adapterKey, channel) {
  const s = orderStatusOf({ status });
  const key = normalizeAdapterKey(adapterKey);
  const ch = String(channel || "")
    .trim()
    .toLowerCase();

  if (key === "ozon") {
    return ch !== "fbo" && s === ORDER_STATUS.AwaitingDeliver;
  }
  if (key === "wildberries") {
    return (
      s === ORDER_STATUS.AwaitingDeliver || s === ORDER_STATUS.Delivering
    );
  }
  if (key === "yandex") {
    if (ch === "fby") return false;
    return s === ORDER_STATUS.Confirmed || s === ORDER_STATUS.AwaitingPack;
  }
  return false;
}

/** Intersection of seller actions every selected order can run. */
export function orderBulkCapabilities(
  orders,
  adapterKey,
  { canMutate = false, canDownloadLabel = false } = {},
) {
  const list = Array.isArray(orders) ? orders : [];
  if (list.length === 0) {
    return { actions: [], canCancel: false, canDownloadLabel: false };
  }

  let actions = canMutate
    ? primaryActionsForStatus(
        orderStatusOf(list[0]),
        adapterKey,
        orderChannelOf(list[0]),
      ).slice()
    : [];
  let canCancel = canMutate;
  let download = canDownloadLabel;

  for (const order of list) {
    const status = orderStatusOf(order);
    const channel = orderChannelOf(order);
    if (canMutate) {
      const next = primaryActionsForStatus(status, adapterKey, channel);
      actions = actions.filter((action) => next.includes(action));
      canCancel = canCancel && canCancelOrder(status, adapterKey, channel);
    } else {
      actions = [];
      canCancel = false;
    }
    download =
      download && canDownloadOrderLabel(status, adapterKey, channel);
  }

  return {
    actions,
    canCancel,
    canDownloadLabel: download,
  };
}

export function orderStatusUpdatePayload(action, order, adapterKey) {
  const payload = { action: String(action || "") };
  if (
    payload.action === ORDER_ACTION.Ship &&
    normalizeAdapterKey(adapterKey) === "wildberries"
  ) {
    const supplyId = orderSupplyId(order);
    if (supplyId) payload.extra = { supplyId };
  }
  return payload;
}

export function orderExtraOf(order) {
  const extra = order?.extra ?? order?.Extra;
  if (!extra || typeof extra !== "object" || Array.isArray(extra)) return {};
  return extra;
}

export function orderExtraString(order, key) {
  if (!key) return "";
  const extra = orderExtraOf(order);
  const pascal = key.charAt(0).toUpperCase() + key.slice(1);
  const value = extra[key] ?? extra[pascal];
  if (value == null || value === "") return "";
  return String(value).trim();
}

export function orderSupplyId(order) {
  return orderExtraString(order, "supplyId");
}

export function orderBoxId(order) {
  return orderExtraString(order, "boxId");
}

/** Box QR when the order is boxed in a Wildberries supply (Ready to ship). */
export function canDownloadBoxLabel(status, adapterKey, boxId) {
  return (
    Boolean(String(boxId || "").trim()) &&
    canDownloadOrderLabel(status, adapterKey, "fbs") &&
    normalizeAdapterKey(adapterKey) === "wildberries" &&
    orderStatusOf({ status }) === ORDER_STATUS.AwaitingDeliver
  );
}

/** Supply QR after Wildberries deliver (in delivery). */
export function canDownloadShipmentQr(status, adapterKey, supplyId) {
  return (
    Boolean(String(supplyId || "").trim()) &&
    normalizeAdapterKey(adapterKey) === "wildberries" &&
    orderStatusOf({ status }) === ORDER_STATUS.Delivering
  );
}

export function orderStatusClass(status) {
  const s = orderStatusOf({ status });
  if (s === ORDER_STATUS.Cancelled || s === ORDER_STATUS.Returned) {
    return "text-destructive";
  }
  if (s === ORDER_STATUS.Delivered) return "text-success";
  return "text-muted-foreground";
}

export function orderStatusLabelKey(status) {
  const s = orderStatusOf({ status });
  const known = new Set(Object.values(ORDER_STATUS));
  if (known.has(s)) return `pages.ordersPage.status.${s}`;
  return "pages.ordersPage.status.unknown";
}

export function orderChannelLabelKey(channel) {
  const c = String(channel || "").toLowerCase();
  if (c === "fbs" || c === "fbo" || c === "shop") {
    return `pages.ordersPage.channel.${c}`;
  }
  return "pages.ordersPage.channel.unknown";
}

export function formatOrderMoney(total, currency, locale = "ru-RU") {
  if (total == null || total === "") return "";
  const n = Number(total);
  if (!Number.isFinite(n)) return String(total);
  const code = String(currency || "RUB").trim() || "RUB";
  try {
    return new Intl.NumberFormat(locale, {
      style: "currency",
      currency: code,
    }).format(n);
  } catch {
    return `${n} ${code}`.trim();
  }
}

export function orderLabelFileName(label, fallback = "label") {
  const raw = String(label?.fileName ?? label?.FileName ?? fallback).trim();
  const cleaned = (raw || fallback).replace(/[/\\?%*:|"<>]/g, "-");
  return cleaned || fallback;
}

export function orderLabelContentType(label) {
  const type = String(
    label?.contentType ?? label?.ContentType ?? "application/octet-stream",
  ).trim();
  return type || "application/octet-stream";
}

/** Decode marketplace label Base64; null when missing or invalid. */
export function decodeOrderLabelBytes(label) {
  const b64 = String(label?.contentBase64 ?? label?.ContentBase64 ?? "").trim();
  if (!b64) return null;
  try {
    const binary = atob(b64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i);
    return bytes.length ? bytes : null;
  } catch {
    return null;
  }
}

export function marketplaceOrderStatusUrl(connectionId, orderId) {
  return `/marketplace/connections/${connectionId}/orders/${encodeURIComponent(orderId)}/status`;
}

export function marketplaceOrderLabelUrl(connectionId, orderId) {
  return `/marketplace/connections/${connectionId}/orders/${encodeURIComponent(orderId)}/label`;
}

export function orderJobConfirmMeta(job, t, { adapterKey } = {}) {
  if (!job) {
    return { title: "", description: "", label: "", destructive: false };
  }
  if (job.kind === "label") {
    const count = job.ids?.length || 0;
    return {
      title:
        count === 1
          ? t("pages.ordersPage.actionDownloadLabel")
          : t("pages.ordersPage.bulkLabelTitle", { count }),
      description:
        count === 1
          ? t("pages.ordersPage.bulkLabelDescriptionOne")
          : t("pages.ordersPage.bulkLabelDescription", { count }),
      label: t("pages.ordersPage.actionDownloadLabel"),
      destructive: false,
    };
  }
  const spec = ORDER_ACTION_CONFIRM[job.action] || ORDER_ACTION_CONFIRM.pack;
  const count = job.ids?.length || 0;
  const actionLabel = t(ORDER_ACTION_LABEL[job.action] || ORDER_ACTION_LABEL.pack);
  const key = normalizeAdapterKey(adapterKey || job.adapterKey);
  if (count === 1) {
    const description =
      job.action === "ship" && key === "wildberries"
        ? t("pages.ordersPage.confirmShipSupplyDescription")
        : t(spec.description);
    return {
      title: t(spec.title),
      description,
      label: t(spec.label),
      destructive: Boolean(spec.destructive),
    };
  }
  return {
    title: t("pages.ordersPage.bulkConfirmTitle", {
      action: actionLabel,
      count,
    }),
    description: t("pages.ordersPage.bulkConfirmDescription"),
    label: actionLabel,
    destructive: Boolean(spec.destructive),
  };
}

export function saveOrderLabelFile(
  label,
  fallbackName,
  { t, triggerDownload } = {},
) {
  const bytes = decodeOrderLabelBytes(label);
  if (!bytes) {
    throw new Error(t("pages.ordersPage.labelEmpty"));
  }
  const blob = new Blob([bytes], { type: orderLabelContentType(label) });
  const url = URL.createObjectURL(blob);
  try {
    triggerDownload(url, orderLabelFileName(label, fallbackName));
  } finally {
    URL.revokeObjectURL(url);
  }
}

export async function postMarketplaceOrderStatus({
  http,
  connectionId,
  orderId,
  action,
  order,
  adapterKey,
}) {
  await http.post(
    marketplaceOrderStatusUrl(connectionId, orderId),
    orderStatusUpdatePayload(action, order, adapterKey),
  );
}

export async function runPendingOrderJob(
  job,
  { t, runOne, onItemError, onSuccess, onPartial } = {},
) {
  if (!job) return { ok: 0, failedIds: [] };
  let ok = 0;
  const failedIds = [];
  for (const id of job.ids || []) {
    try {
      await runOne(id, job);
      ok += 1;
    } catch (error) {
      failedIds.push(id);
      onItemError?.(error);
    }
  }
  if (failedIds.length === 0 && ok > 0) {
    onSuccess?.(
      job.kind === "label"
        ? t("pages.ordersPage.bulkLabelDownloaded", { count: ok })
        : t("pages.ordersPage.statusUpdated"),
    );
  } else if (ok > 0) {
    onPartial?.(
      t("pages.ordersPage.bulkPartialFailed", {
        ok,
        failed: failedIds.length,
      }),
    );
  }
  return { ok, failedIds };
}

export function orderConnectionStorageKey(storeId) {
  return `delegat.orders.connectionId.${storeId}`;
}

export function readStoredConnectionId(storeId) {
  if (storeId == null || typeof localStorage === "undefined") return null;
  try {
    const raw = localStorage.getItem(orderConnectionStorageKey(storeId));
    const n = Number(raw);
    return Number.isFinite(n) && n > 0 ? n : null;
  } catch {
    return null;
  }
}

export function writeStoredConnectionId(storeId, connectionId) {
  if (storeId == null || typeof localStorage === "undefined") return;
  try {
    if (!connectionId) {
      localStorage.removeItem(orderConnectionStorageKey(storeId));
      return;
    }
    localStorage.setItem(
      orderConnectionStorageKey(storeId),
      String(connectionId),
    );
  } catch {
    /* ignore quota / private mode */
  }
}

export function orderMailboxStorageKey(storeId) {
  return `delegat.orders.mailbox.${storeId}`;
}

export function readStoredMailbox(storeId) {
  if (storeId == null || typeof localStorage === "undefined") return null;
  try {
    const raw = localStorage.getItem(orderMailboxStorageKey(storeId));
    return isOrderBucket(raw) ? raw : null;
  } catch {
    return null;
  }
}

export function writeStoredMailbox(storeId, mailbox) {
  if (storeId == null || typeof localStorage === "undefined") return;
  try {
    if (!isOrderBucket(mailbox)) {
      localStorage.removeItem(orderMailboxStorageKey(storeId));
      return;
    }
    localStorage.setItem(orderMailboxStorageKey(storeId), mailbox);
  } catch {
    /* ignore quota / private mode */
  }
}

export function connectionNavLabel(
  connection,
  adapters,
  activeSameAdapterCount,
) {
  const adapterKey = connection?.adapterKey ?? connection?.AdapterKey;
  const adapter = (adapters || []).find(
    (item) => (item.key ?? item.Key) === adapterKey,
  );
  const displayName =
    adapter?.displayName ?? adapter?.DisplayName ?? adapterKey ?? "";
  const name = connection?.name ?? connection?.Name;
  if (activeSameAdapterCount > 1 && name) {
    return `${displayName} · ${name}`;
  }
  return displayName;
}
