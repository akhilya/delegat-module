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
