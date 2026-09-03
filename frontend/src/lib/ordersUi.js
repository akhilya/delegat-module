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

export const DEFAULT_ORDER_BUCKET = "new";

/**
 * Sidebar mailboxes. `statuses` are sent as separate list queries and merged.
 * `icon` is a lucide-compatible export name.
 */
export const ORDER_BUCKETS = [
  {
    id: "new",
    statuses: [ORDER_STATUS.New, ORDER_STATUS.Unpaid],
    icon: "Inbox",
    labelKey: "nav.ordersNew",
    titleKey: "routes.ordersNew",
    descriptionKey: "pages.ordersPage.mailboxNewHint",
    emptyDescriptionKey: "pages.ordersPage.emptyNewDescription",
  },
  {
    id: "pack",
    statuses: [ORDER_STATUS.Confirmed, ORDER_STATUS.AwaitingPack],
    icon: "Package",
    labelKey: "nav.ordersPack",
    titleKey: "routes.ordersPack",
    descriptionKey: "pages.ordersPage.mailboxPackHint",
    emptyDescriptionKey: "pages.ordersPage.emptyPackDescription",
  },
  {
    id: "ship",
    statuses: [ORDER_STATUS.AwaitingDeliver],
    icon: "Truck",
    labelKey: "nav.ordersShip",
    titleKey: "routes.ordersShip",
    descriptionKey: "pages.ordersPage.mailboxShipHint",
    emptyDescriptionKey: "pages.ordersPage.emptyShipDescription",
  },
  {
    id: "delivery",
    statuses: [ORDER_STATUS.Delivering],
    icon: "MapPin",
    labelKey: "nav.ordersDelivery",
    titleKey: "routes.ordersDelivery",
    descriptionKey: "pages.ordersPage.mailboxDeliveryHint",
    emptyDescriptionKey: "pages.ordersPage.emptyDeliveryDescription",
  },
  {
    id: "done",
    statuses: [ORDER_STATUS.Delivered],
    icon: "CheckCircle",
    labelKey: "nav.ordersDone",
    titleKey: "routes.ordersDone",
    descriptionKey: "pages.ordersPage.mailboxDoneHint",
    emptyDescriptionKey: "pages.ordersPage.emptyDoneDescription",
  },
  {
    id: "closed",
    statuses: [ORDER_STATUS.Cancelled, ORDER_STATUS.Returned],
    icon: "XCircle",
    labelKey: "nav.ordersClosed",
    titleKey: "routes.ordersClosed",
    descriptionKey: "pages.ordersPage.mailboxClosedHint",
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

export function statusesForBucket(id) {
  return orderBucketById(id)?.statuses?.slice() || [];
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

export function primaryActionsForStatus(status) {
  switch (orderStatusOf({ status })) {
    case ORDER_STATUS.New:
    case ORDER_STATUS.Unpaid:
      return [ORDER_ACTION.Confirm];
    case ORDER_STATUS.Confirmed:
    case ORDER_STATUS.AwaitingPack:
      return [ORDER_ACTION.Pack];
    case ORDER_STATUS.AwaitingDeliver:
      return [ORDER_ACTION.Ship];
    case ORDER_STATUS.Delivering:
      return [ORDER_ACTION.Deliver, ORDER_ACTION.Complete];
    default:
      return [];
  }
}

export function canCancelOrder(status) {
  const s = orderStatusOf({ status });
  return (
    s !== ORDER_STATUS.Cancelled &&
    s !== ORDER_STATUS.Returned &&
    s !== ORDER_STATUS.Delivered
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
