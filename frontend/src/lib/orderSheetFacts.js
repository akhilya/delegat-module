function extraBag(order) {
  const raw = order?.extra ?? order?.Extra ?? {};
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return {};
  return raw;
}

function stringifyExtra(value) {
  if (value == null) return "";
  if (typeof value === "boolean") return value ? "true" : "";
  if (typeof value === "number") return Number.isFinite(value) ? String(value) : "";
  if (typeof value === "string") return value.trim();
  if (typeof value === "object") {
    if (Object.prototype.hasOwnProperty.call(value, "value")) {
      return stringifyExtra(value.value);
    }
    return "";
  }
  return String(value).trim();
}

function scalarAmount(value) {
  if (value == null || value === "") return null;
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) && n !== 0 ? n : null;
}

export function extraValue(order, key) {
  const bag = extraBag(order);
  const want = String(key || "").toLowerCase();
  if (!want) return "";
  for (const [name, raw] of Object.entries(bag)) {
    if (name.toLowerCase() !== want) continue;
    return stringifyExtra(raw);
  }
  return "";
}

export function orderSheetHeadlineFacts({ status, channel, created, total } = {}) {
  const facts = [];
  const add = (id, labelKey, value) => {
    const text = String(value || "").trim();
    if (!text) return;
    facts.push({ id, labelKey, value: text, copy: false });
  };
  add("status", "pages.ordersPage.columnStatus", status);
  add("channel", "pages.ordersPage.columnChannel", channel);
  add("created", "pages.ordersPage.columnCreated", created);
  add("total", "pages.ordersPage.columnTotal", total);
  return facts;
}

function locationId(row) {
  return String(row?.externalId ?? row?.ExternalId ?? "").trim();
}

function locationKind(row) {
  return String(row?.kind ?? row?.Kind ?? "").trim().toLowerCase();
}

export function indexFulfillmentWarehouses(rows) {
  const list = Array.isArray(rows) ? rows : [];
  const offices = {};
  const map = {};
  for (const row of list) {
    const id = locationId(row);
    if (!id) continue;
    const name = String(row?.name ?? row?.Name ?? "").trim();
    const address = String(row?.address ?? row?.Address ?? "").trim();
    const kind = locationKind(row);
    if (kind === "marketplace_office") {
      offices[id] = { name, address };
      continue;
    }
    map[id] = {
      name,
      address,
      officeId: extraValue({ extra: row?.extra ?? row?.Extra }, "officeId"),
    };
  }
  for (const loc of Object.values(map)) {
    const office = offices[loc.officeId] || offices[String(loc.officeId)];
    if (!office) continue;
    if (!loc.address && office.address) loc.address = office.address;
    if (office.name) loc.officeName = office.name;
  }
  return map;
}

function warehouseLocation(warehouseId, options) {
  const byId = options.warehouseById || {};
  const named = options.warehouseNameById || {};
  const row = byId[warehouseId] || byId[String(warehouseId)];
  if (row && typeof row === "object") {
    return {
      name: String(row.name || "").trim(),
      address: String(row.address || "").trim(),
      officeName: String(row.officeName || "").trim(),
    };
  }
  const fallback = named[warehouseId] || named[String(warehouseId)];
  return {
    name: String(fallback || "").trim(),
    address: "",
    officeName: "",
  };
}

function isWeakWarehouseName(name) {
  const n = String(name || "").trim().toLowerCase();
  return !n || n === "склад" || n === "warehouse";
}

export function orderTotalAmount(order) {
  return scalarAmount(order?.total ?? order?.Total);
}

export function orderDiscountAmount(order) {
  return scalarAmount(order?.discountTotal ?? order?.DiscountTotal);
}

export function orderItemBarcode(item) {
  return String(item?.barcode ?? item?.Barcode ?? "").trim();
}

export function orderTracking(order) {
  return (
    extraValue(order, "trackingNumber") || extraValue(order, "tracking")
  );
}

export function orderBuyer(order) {
  const name = String(
    order?.customerName ?? order?.CustomerName ?? "",
  ).trim();
  const phone = String(
    order?.customerPhone ?? order?.CustomerPhone ?? "",
  ).trim();
  const email = String(
    order?.customerEmail ?? order?.CustomerEmail ?? "",
  ).trim();
  if (!name && !phone && !email) return null;
  return { name, phone, email };
}

export function orderDelivery(order) {
  const address = String(
    order?.shippingAddress ?? order?.ShippingAddress ?? "",
  ).trim();
  const comment = String(order?.comment ?? order?.Comment ?? "").trim();
  const tracking = orderTracking(order);
  if (!address && !comment && !tracking) return null;
  return { address, comment, tracking };
}

export function orderSheetMetaFacts(order, options = {}) {
  if (!order) return [];
  const discountText = String(options.discountText || "").trim();
  const displayId = String(
    order.displayId ?? order.DisplayId ?? "",
  ).trim();
  const externalId = String(
    order.externalId ?? order.ExternalId ?? "",
  ).trim();
  const facts = [];

  if (externalId && displayId && externalId !== displayId) {
    facts.push({
      id: "posting",
      labelKey: "pages.ordersPage.sheetPostingId",
      value: externalId,
      copy: true,
    });
  }

  const cabinetOrder = extraValue(order, "orderId");
  if (
    cabinetOrder &&
    cabinetOrder !== displayId &&
    cabinetOrder !== externalId
  ) {
    facts.push({
      id: "cabinetOrder",
      labelKey: "pages.ordersPage.sheetCabinetOrderId",
      value: cabinetOrder,
      copy: true,
    });
  }

  const rid = extraValue(order, "rid");
  if (rid) {
    facts.push({
      id: "rid",
      labelKey: "pages.ordersPage.sheetRid",
      value: rid,
      copy: true,
    });
  }

  const warehouseNamed = extraValue(order, "warehouse");
  const warehouseId = extraValue(order, "warehouseId");
  const loc = warehouseLocation(warehouseId, options);
  const rawName = warehouseNamed || loc.name;
  const warehouse = isWeakWarehouseName(rawName)
    ? loc.officeName || rawName || warehouseId
    : rawName || loc.officeName || warehouseId;
  if (warehouse) {
    const details = [];
    if (loc.address) details.push(loc.address);
    if (warehouseId && warehouseId !== warehouse) details.push(warehouseId);
    facts.push({
      id: "warehouse",
      labelKey: "pages.ordersPage.sheetWarehouse",
      value: warehouse,
      detail: details.join(" · "),
      copy: false,
    });
  }

  const deliveryMethod =
    extraValue(order, "deliveryMethod") || extraValue(order, "deliveryType");
  if (deliveryMethod) {
    facts.push({
      id: "deliveryMethod",
      labelKey: "pages.ordersPage.sheetDeliveryMethod",
      value: deliveryMethod,
      copy: false,
    });
  }

  if (discountText) {
    facts.push({
      id: "discount",
      labelKey: "pages.ordersPage.sheetDiscount",
      value: discountText,
      copy: false,
    });
  }

  if (extraValue(order, "fake") === "true") {
    facts.push({
      id: "testOrder",
      labelKey: "pages.ordersPage.sheetTestOrder",
      value: "true",
      copy: false,
    });
  }

  return facts;
}
