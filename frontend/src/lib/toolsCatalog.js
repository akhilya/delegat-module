/**
 * Tools catalog helpers. Entries come from GET /tools (platform pages + adapter jobs).
 * @param {object|null|undefined} entry
 * @param {(key: string) => string} t
 */
export function toolTitle(entry, t) {
  if (!entry) return "";
  if (entry.titleKey && typeof t === "function") return t(entry.titleKey);
  return entry.title || entry.id || "";
}

export function toolDescription(entry, t) {
  if (!entry) return "";
  if (entry.descriptionKey && typeof t === "function") return t(entry.descriptionKey);
  return entry.description || "";
}

export function toolHref(entry) {
  if (!entry) return "/tools";
  return entry.path || `/tools/${entry.id}`;
}

export function isJobTool(entry) {
  return entry?.mode === "job";
}

export function isPageTool(entry) {
  return entry?.mode === "page";
}

export function isQuickRunTool(entry) {
  return isJobTool(entry) && entry?.quickRun === true;
}

/** Maturity of a catalog tool. Stable is the default and is not labelled in the UI. */
export const TOOL_STAGES = {
  stable: "stable",
  experimental: "experimental",
  beta: "beta",
  deprecated: "deprecated",
};

export function toolStage(entry) {
  const raw = typeof entry?.stage === "string" ? entry.stage.trim().toLowerCase() : "";
  if (
    raw === TOOL_STAGES.experimental ||
    raw === TOOL_STAGES.beta ||
    raw === TOOL_STAGES.deprecated
  ) {
    return raw;
  }
  return TOOL_STAGES.stable;
}

export function toolStageLabelKey(entry) {
  const stage = toolStage(entry);
  if (stage === TOOL_STAGES.experimental) return "pages.toolsPage.stageExperimental";
  if (stage === TOOL_STAGES.beta) return "pages.toolsPage.stageBeta";
  if (stage === TOOL_STAGES.deprecated) return "pages.toolsPage.stageDeprecated";
  return "";
}

export function toolStageClass(entry) {
  const stage = toolStage(entry);
  if (stage === TOOL_STAGES.deprecated) return "text-destructive";
  if (stage === TOOL_STAGES.experimental || stage === TOOL_STAGES.beta) return "text-warning";
  return "";
}

export function toolConfirmTitleKey(entry) {
  if (!entry?.id) return "";
  if (entry.id === "export-all-prices") return "pages.toolsPage.exportAllPricesConfirmTitle";
  if (entry.id === "clear-promotion-products")
    return "pages.toolsPage.clearPromotionProductsConfirmTitle";
  return "";
}

export function toolConfirmDescriptionKey(entry) {
  if (!entry?.id) return "";
  if (entry.id === "export-all-prices")
    return "pages.toolsPage.exportAllPricesConfirmDescription";
  if (entry.id === "clear-promotion-products")
    return "pages.toolsPage.clearPromotionProductsConfirmDescription";
  return "";
}

export function toolJobConfirmDescriptionKey(entry) {
  if (!entry?.id) return "";
  if (entry.id === "export-all-prices")
    return "pages.toolsPage.exportAllPricesJobConfirmDescription";
  if (entry.id === "clear-promotion-products")
    return "pages.toolsPage.clearPromotionProductsJobConfirmDescription";
  return "";
}

export function toolQuickTooltipKey(entry) {
  if (!entry?.id) return "";
  if (entry.id === "export-all-prices") return "pages.toolsPage.exportAllPricesQuickTooltip";
  if (entry.id === "clear-promotion-products")
    return "pages.toolsPage.clearPromotionProductsQuickTooltip";
  return "";
}

/** Stable platform group ids, in Tools page order. Adapter tools use `adapter:{key}`. */
export const TOOL_GROUP_ORDER = ["catalog", "prices", "promotions", "api"];

export function adapterGroupLabel(adapterKey) {
  if (!adapterKey) return "";
  const spaced = String(adapterKey)
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[-_]+/g, " ")
    .trim();
  return spaced.replace(/\b\w/g, (ch) => ch.toUpperCase());
}

export function toolGroupId(entry) {
  if (!entry) return "catalog";
  if (entry.source === "adapter") {
    return entry.adapterKey ? `adapter:${entry.adapterKey}` : "adapters";
  }
  const group = typeof entry.group === "string" ? entry.group.trim().toLowerCase() : "";
  if (TOOL_GROUP_ORDER.includes(group)) return group;
  if (entry.id === "export-all-prices") return "prices";
  if (entry.id === "clear-promotion-products") return "promotions";
  if (entry.id === "public-api-try") return "api";
  return "catalog";
}

/**
 * Split catalog entries into labeled groups (one InsetGroup each).
 * @param {unknown} entries
 * @returns {{ id: string, tools: object[], adapterKey: string|null }[]}
 */
export function groupToolsCatalog(entries) {
  const list = Array.isArray(entries) ? entries : [];
  const byId = new Map();
  for (const entry of list) {
    if (!entry) continue;
    const id = toolGroupId(entry);
    const bucket = byId.get(id);
    if (bucket) bucket.push(entry);
    else byId.set(id, [entry]);
  }

  const sections = [];
  for (const id of TOOL_GROUP_ORDER) {
    const tools = byId.get(id);
    if (!tools?.length) continue;
    sections.push({ id, tools, adapterKey: null });
    byId.delete(id);
  }

  const adapterIds = [...byId.keys()]
    .filter((id) => id.startsWith("adapter:"))
    .sort((a, b) => a.localeCompare(b));
  for (const id of adapterIds) {
    const tools = byId.get(id);
    byId.delete(id);
    if (!tools?.length) continue;
    sections.push({
      id,
      tools,
      adapterKey: id.slice("adapter:".length),
    });
  }

  for (const [id, tools] of byId) {
    if (!tools?.length) continue;
    sections.push({ id, tools, adapterKey: null });
  }
  return sections;
}

/**
 * Split a section's tools into inset groups: consecutive page tools share a
 * list; each quick-run job is its own group (open row + run row).
 * @param {unknown} tools
 * @returns {{ key: string, type: "list"|"job", tools?: object[], entry?: object }[]}
 */
export function toolSectionBlocks(tools) {
  const list = Array.isArray(tools) ? tools : [];
  const blocks = [];
  let batch = [];
  const flush = () => {
    if (!batch.length) return;
    blocks.push({
      key: `list:${batch.map((item) => item.id).join(",")}`,
      type: "list",
      tools: batch,
    });
    batch = [];
  };
  for (const entry of list) {
    if (!entry) continue;
    if (isQuickRunTool(entry)) {
      flush();
      blocks.push({ key: `job:${entry.id}`, type: "job", entry });
    } else {
      batch.push(entry);
    }
  }
  flush();
  return blocks;
}

