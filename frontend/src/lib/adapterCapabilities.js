/** Named [Flags] composites from MarketplaceCapabilities.ToString(). */
const COMPOSITE_FLAGS = {
  Basic: ["GetCategories", "GetCategoryFields", "ImportProduct", "ExportProduct"],
  FullProducts: [
    "GetCategories",
    "GetCategoryFields",
    "ImportProduct",
    "ExportProduct",
    "ImportAllProducts",
    "ImportPrices",
    "ExportPrices",
    "ImportStocks",
    "ExportStocks",
  ],
};

/**
 * Adapter capability flags from GET /marketplace/adapters (enum ToString)
 * or GET /marketplace/connections/{id}/capabilities (boolean object).
 * @param {unknown} capabilities
 * @returns {Set<string>}
 */
export function parseCapabilityFlags(capabilities) {
  const flags = new Set();
  if (capabilities == null) return flags;

  if (typeof capabilities === "object" && !Array.isArray(capabilities)) {
    if (capabilities.importProduct) flags.add("ImportProduct");
    if (capabilities.importAllProducts) flags.add("ImportAllProducts");
    if (capabilities.exportProduct) flags.add("ExportProduct");
    if (capabilities.importProductBatch) flags.add("ImportProductBatch");
    if (capabilities.searchPublicCatalog) flags.add("SearchPublicCatalog");
    if (capabilities.exportPrices) flags.add("ExportPrices");
    if (capabilities.importPrices) flags.add("ImportPrices");
    if (capabilities.getOrders) flags.add("GetOrders");
    if (capabilities.updateOrderStatus) flags.add("UpdateOrderStatus");
    if (capabilities.getOrderLabels) flags.add("GetOrderLabels");
    if (capabilities.getFulfillmentLocations) flags.add("GetFulfillmentLocations");
    if (capabilities.manageShipments) flags.add("ManageShipments");
    if (capabilities.getPromotions) flags.add("GetPromotions");
    if (capabilities.updatePromotions) flags.add("UpdatePromotions");
    expandComposites(flags);
    return flags;
  }

  for (const part of String(capabilities).split(",")) {
    const name = part.trim();
    if (name) flags.add(name);
  }
  expandComposites(flags);
  return flags;
}

function expandComposites(flags) {
  for (const [composite, atoms] of Object.entries(COMPOSITE_FLAGS)) {
    if (!flags.has(composite)) continue;
    for (const atom of atoms) flags.add(atom);
  }
}

function capsOf(adapterOrCaps) {
  if (adapterOrCaps && typeof adapterOrCaps === "object" && "capabilities" in adapterOrCaps) {
    return adapterOrCaps.capabilities;
  }
  return adapterOrCaps;
}

/** True when the adapter can pull at least one product (link/match). */
export function adapterCanImportCatalog(adapterOrCaps) {
  const flags = parseCapabilityFlags(capsOf(adapterOrCaps));
  return flags.has("ImportAllProducts") || flags.has("ImportProduct");
}

/** True when the adapter can list/import the remote catalog in bulk. */
export function adapterCanImportAll(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("ImportAllProducts");
}

/** True when a connection can be used to link local products for later export. */
export function adapterCanLink(adapterOrCaps) {
  const flags = parseCapabilityFlags(capsOf(adapterOrCaps));
  return (
    flags.has("ExportProduct") ||
    flags.has("ImportProduct") ||
    flags.has("ImportAllProducts")
  );
}

/** True when the adapter can search another seller’s public storefront. */
export function adapterCanSearchPublicCatalog(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("SearchPublicCatalog");
}

/** True when a named [Flags] capability is present (composites expanded). */
export function adapterHasCapability(adapterOrCaps, flagName) {
  if (!flagName) return false;
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has(flagName);
}

/** True when the adapter can list live marketplace orders. */
export function adapterCanGetOrders(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("GetOrders");
}

/** True when the adapter can change live marketplace order status. */
export function adapterCanUpdateOrderStatus(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("UpdateOrderStatus");
}

/** True when the adapter can download a packing label / barcode sticker. */
export function adapterCanGetOrderLabels(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("GetOrderLabels");
}

/** True when the adapter can list seller warehouses and marketplace offices. */
export function adapterCanGetFulfillmentLocations(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("GetFulfillmentLocations");
}

/** True when the adapter can list and box live shipments / supplies. */
export function adapterCanManageShipments(adapterOrCaps) {
  return parseCapabilityFlags(capsOf(adapterOrCaps)).has("ManageShipments");
}
