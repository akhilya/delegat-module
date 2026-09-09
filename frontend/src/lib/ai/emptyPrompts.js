/** How many starter prompts the empty chat shows. */
export const EMPTY_PROMPT_VISIBLE = 3;

/** Catalog work the assistant can actually do (not fake /stats). */
export const GENERAL_EMPTY_PROMPT_KEYS = [
  "ai.emptyPromptNoPrice",
  "ai.emptyPromptNoBarcode",
  "ai.emptyPromptNoPhoto",
  "ai.emptyPromptOutOfStock",
  "ai.emptyPromptCheapest",
  "ai.emptyPromptMostStock",
  "ai.emptyPromptCategories",
  "ai.emptyPromptTrash",
  "ai.emptyPromptConnections",
  "ai.emptyPromptNoDescription",
  "ai.emptyPromptCount",
  "ai.emptyPromptGroups",
];

/** One of these is preferred when the companion knows the current page. */
export const PAGE_EMPTY_PROMPT_KEYS = {
  product: ["ai.emptyPromptThisProductFill", "ai.emptyPromptThisProductCheck"],
  fieldMapping: [
    "ai.emptyPromptThisMappingFill",
    "ai.emptyPromptThisMappingMissing",
  ],
  connection: [
    "ai.emptyPromptThisConnectionImport",
    "ai.emptyPromptThisConnectionMappings",
  ],
  operation: ["ai.emptyPromptThisOperation"],
};

export function hashString(value) {
  let h = 2166136261;
  const s = String(value ?? "");
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function shuffled(items, rng) {
  const copy = items.slice();
  for (let i = copy.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy;
}

/**
 * Pick up to {@link EMPTY_PROMPT_VISIBLE} unique i18n keys.
 * Same page + seed → same set. A new conversation or page changes the set.
 */
export function pickEmptyPromptKeys({ page = null, seed = 0 } = {}) {
  const contextual = (PAGE_EMPTY_PROMPT_KEYS[page] ?? []).slice();
  const used = new Set();
  const rng = mulberry32(hashString(String(seed)));
  const out = [];

  if (contextual.length) {
    const key = shuffled(contextual, rng)[0];
    out.push(key);
    used.add(key);
  }

  const general = shuffled(
    GENERAL_EMPTY_PROMPT_KEYS.filter((key) => !used.has(key)),
    rng,
  );
  for (const key of general) {
    if (out.length >= EMPTY_PROMPT_VISIBLE) break;
    out.push(key);
    used.add(key);
  }

  return out.slice(0, EMPTY_PROMPT_VISIBLE);
}

export function emptyPromptScope(pageContext) {
  if (!pageContext?.page) return "none";
  return [
    pageContext.page,
    pageContext.productId ?? "",
    pageContext.mappingId ?? "",
    pageContext.connectionId ?? "",
    pageContext.operationId ?? "",
  ].join(":");
}
