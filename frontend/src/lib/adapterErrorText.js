/**
 * Operator-facing adapter errors: never show raw JSON.
 * Used for lastErrorMessage, operation summaries, and toast details.
 */

const SCOPE_LABELS = {
  content: "Content (product cards)",
  marketplace: "Marketplace (FBS orders)",
  statistics: "Statistics",
  analytics: "Analytics",
  prices: "Prices",
  promotion: "Promotions",
  questions: "Questions",
  feedbacks: "Reviews",
  supplies: "Supplies",
  returns: "Returns",
  documents: "Documents",
  buyerchat: "Buyer chat",
  recommendations: "Recommendations",
};

const SCOPE_RE =
  /\b(?:token\s+)?scope[s]?\s+['"]?([a-z][a-z0-9._-]*)['"]?/gi;

function looksLikeJson(text) {
  if (!text || typeof text !== "string") return false;
  const t = text.trim();
  return t.startsWith("{") || t.startsWith("[");
}

function firstJson(text) {
  if (!text || typeof text !== "string") return null;
  const trimmed = text.trim();
  if (trimmed.startsWith("{") || trimmed.startsWith("[")) return trimmed;
  const start = trimmed.indexOf("{");
  const end = trimmed.lastIndexOf("}");
  if (start >= 0 && end > start) return trimmed.slice(start, end + 1);
  return null;
}

function parseJson(text) {
  const raw = firstJson(text);
  if (!raw) return null;
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function pickString(obj, keys) {
  if (!obj || typeof obj !== "object") return "";
  for (const key of keys) {
    const value = obj[key];
    if (typeof value === "string" && value.trim() && !looksLikeJson(value)) {
      return value.trim();
    }
  }
  return "";
}

function humanFromParsed(parsed) {
  if (parsed == null) return "";
  if (typeof parsed === "string") {
    return looksLikeJson(parsed) ? "" : parsed.trim();
  }
  const direct = pickString(parsed, [
    "detail",
    "message",
    "title",
    "error_text",
    "errorText",
    "description",
  ]);
  if (direct) return direct;
  if (typeof parsed.error === "string" && !looksLikeJson(parsed.error)) {
    return parsed.error.trim();
  }
  if (parsed.error && typeof parsed.error === "object") {
    const nested = humanFromParsed(parsed.error);
    if (nested) return nested;
  }
  if (Array.isArray(parsed.errors)) {
    const parts = parsed.errors
      .map((item) => {
        if (typeof item === "string") return item;
        return pickString(item, ["message", "detail", "error"]);
      })
      .filter(Boolean);
    if (parts.length) return [...new Set(parts)].join("; ");
  }
  return "";
}

function collectRights(parsed, text) {
  const found = new Set();
  const add = (value) => {
    const v = String(value || "").trim();
    if (v && v.length <= 40) found.add(v);
  };
  if (text) {
    SCOPE_RE.lastIndex = 0;
    let match;
    while ((match = SCOPE_RE.exec(text))) add(match[1]);
  }
  const walk = (node, key) => {
    if (node == null) return;
    if (typeof node === "string") {
      if (
        key === "scope" ||
        key === "scopes" ||
        key === "origin" ||
        key === "permissions" ||
        key === "rights"
      ) {
        add(node);
      }
      return;
    }
    if (Array.isArray(node)) {
      node.forEach((item) => walk(item, key));
      return;
    }
    if (typeof node !== "object") return;
    for (const [k, v] of Object.entries(node)) walk(v, k);
  };
  walk(parsed, "");
  return [...found];
}

function labelRight(raw) {
  const key = String(raw || "").toLowerCase();
  return SCOPE_LABELS[key] || raw;
}

function stripJson(text) {
  if (!text) return "";
  const json = firstJson(text);
  if (!json) return looksLikeJson(text) ? "" : text.trim();
  return text.replace(json, "").replace(/[:\-\s]+$/, "").trim();
}

/**
 * @param {unknown} raw
 * @param {{ adapter?: string }} [opts]
 */
export function formatAdapterError(raw, opts = {}) {
  if (raw == null) return "";
  if (typeof raw === "object") {
    const body =
      raw.response || raw.Response || raw.body || raw.detail || raw.message;
    const status = Number(raw.statusCode || raw.status || 0);
    const adapter = opts.adapter || raw.adapter || "";
    const asText =
      typeof body === "string"
        ? body
        : body && typeof body === "object"
          ? JSON.stringify(body)
          : String(raw.message || "");
    return formatAdapterError(asText, { adapter, status });
  }

  const text = String(raw).trim();
  if (!text) return "";

  const parsed = parseJson(text);
  const human = humanFromParsed(parsed) || stripJson(text);
  const rights = collectRights(parsed, `${text} ${human}`);
  const adapter = opts.adapter || guessAdapter(text);
  const status =
    opts.status ||
    Number(parsed?.status) ||
    (/\b(401|403|429)\b/.test(text) ? Number(RegExp.$1) : 0);

  const prefix = adapter ? `${adapter}: ` : "";

  if (rights.length) {
    const listed = rights.map(labelRight).join(", ");
    return `${prefix}the API key is missing these rights: ${listed}. Add them on the token in the marketplace cabinet.`.replace(
      /^: /,
      "",
    );
  }

  if (status === 401 || /unauthorized|invalid api-key|invalid token/i.test(human + text)) {
    const lead = `${prefix}the API key is invalid or expired. Open the connection and check the credentials.`;
    return human && !/invalid|unauthorized/i.test(human) ? `${lead} ${human}` : lead;
  }

  if (status === 403 || /forbidden|permission|access denied/i.test(human + text)) {
    const lead = `${prefix}the API key does not have permission for this action.`;
    return human ? `${lead} ${human}` : lead;
  }

  if (human && !looksLikeJson(human)) return human;
  if (!looksLikeJson(text)) return text;
  return adapter ? `${adapter}: request failed.` : "Request failed.";
}

function guessAdapter(text) {
  if (/wildberries|\bWB\b/i.test(text)) return "Wildberries";
  if (/ozon/i.test(text)) return "Ozon";
  if (/yandex/i.test(text)) return "Yandex Market";
  if (/woocommerce|woo\b/i.test(text)) return "WooCommerce";
  if (/opencart/i.test(text)) return "OpenCart";
  if (/medusa/i.test(text)) return "Medusa";
  return "";
}
