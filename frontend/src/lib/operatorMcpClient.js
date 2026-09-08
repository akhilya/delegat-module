export const OPERATOR_MCP_KEY_NAME = "MCP";

function trimOrigin(origin) {
  return String(origin || "").trim().replace(/\/+$/, "");
}

export function operatorMcpUrl(origin) {
  return `${trimOrigin(origin)}/api/mcp`;
}

/**
 * Origin MCP clients should call. Local Vite (`:5173`) proxies `/api` for the
 * SPA; clients should hit the API (`:5000`) directly.
 */
export function operatorMcpPublicOrigin({ pageOrigin, apiBase } = {}) {
  const base = String(apiBase ?? "").trim();
  if (/^https?:\/\//i.test(base)) {
    try {
      return new URL(base).origin;
    } catch {
      /* fall through to the page origin */
    }
  }
  const page = trimOrigin(pageOrigin);
  try {
    const url = new URL(page);
    if (url.port === "5173") {
      url.port = "5000";
      return url.origin;
    }
  } catch {
    /* keep the page origin */
  }
  return page;
}

export function operatorMcpHeaders({ apiKey, storeId } = {}) {
  const key = String(apiKey || "").trim();
  if (!key) throw new Error("mcp_key_required");
  const store = String(storeId ?? "").trim();
  const headers = { Authorization: `Bearer ${key}` };
  if (store) headers["X-Delegat-Store-Id"] = store;
  return headers;
}

/** One mcp.json entry: Streamable HTTP. `type: "http"` is part of the shared format. */
export function operatorMcpJson({ origin, apiKey, storeId } = {}) {
  return {
    mcpServers: {
      delegat: {
        type: "http",
        url: operatorMcpUrl(origin),
        headers: operatorMcpHeaders({ apiKey, storeId }),
      },
    },
  };
}

export function createdApiKeyPlaintext(body) {
  const data = body?.value ?? body;
  return String(data?.plaintext ?? "").trim();
}

export async function issueOperatorMcpKey({ post } = {}) {
  if (typeof post !== "function") throw new Error("mcp_key_missing");
  const response = await post("/api-keys", { name: OPERATOR_MCP_KEY_NAME });
  const plaintext = createdApiKeyPlaintext(response?.data ?? response);
  if (!plaintext) throw new Error("mcp_key_missing");
  return plaintext;
}
