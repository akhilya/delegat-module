# Operator MCP and Agent HTTP

Machine surface for the cabinet AI agent and external MCP clients. **Not** partner Public API v1.

Gated by `AI_ENABLED` (process start). Independent of `API_USAGE_ENABLED` and the admin Public API toggle.

Implementation is **stateless JSON-RPC over HTTP** (`POST /api/mcp`). Same tool host as the in-app agent (in-process, no self-HTTP). Do not commit `.mcp.json`.

## Cabinet as MCP client

Settings → AI can store HTTP MCP servers (`aiMcpServers`, optional bearer key). The cabinet assistant lists `tools/list` and calls them as `mcp{id}_{tool}`. That is **not** this operator surface: extra servers do not appear on `POST /api/mcp`. The same page can mint a Public API key and show a single `mcp.json` for `POST /api/mcp`.

## Base URLs

| Surface | Path |
|---------|------|
| MCP (JSON-RPC, Streamable HTTP) | `POST /api/mcp` |
| Agent HTTP | `/api/agent/stores/{storeId}/...` |

CSRF is not required (same as `/api/v1`).

## Auth

| Client | Header |
|--------|--------|
| Cabinet SPA | `Authorization: Bearer <JWT>` + cookie session as today, plus `X-CSRF` is optional on these paths |
| Cursor / scripts | `Authorization: Bearer dk_…` |

Store for API keys:

```
X-Delegat-Store-Id: 1
```

The key owner must have `api.access` (to possess a key) and `ai.use`. Underlying resource permissions still apply.

Read-only demo (`workspace.view` only): search, SQL, knowledge, GET `platform_request`. No Python, no uploads, no writes.

## MCP tools

`initialize`, `tools/list`, `tools/call`, `resources/list`, `resources/read`, `ping`.

Tools: `search_knowledge`, `search_catalog`, `sql_readonly`, `run_python` (only when `SANDBOX_URL` is set), `platform_request`, `match_files`, `navigate_to` (cabinet UI shows path/prompt buttons on the reply). Writes go through `propose_plan` in the cabinet UI; Cursor confirms in the client. `platform_request` mutations still check permissions. Staged cabinet attachments assign with `POST /products/{id}/files` `{ attachmentIds }` and remain downloadable for 24 hours.

Resources:

- `delegat://docs/{path}`
- `delegat://openapi/operator.json`
- `delegat://store/schema`

## Agent HTTP (selected)

| Method | Path | Permission |
|--------|------|------------|
| PUT | `/api/agent/stores/{storeId}/products/{productId}/files` | `files.manage` |
| POST | `/api/agent/stores/{storeId}/catalog/search` | `ai.use` |
| POST | `/api/agent/stores/{storeId}/sql` | `ai.use` |
| POST | `/api/agent/stores/{storeId}/python` | `ai.use` (denied for view-only) |
| POST | `/api/agent/stores/{storeId}/platform` | resource perm of the route |
| POST | `/api/agent/stores/{storeId}/files/match` | `ai.use` |
| GET | `/api/agent/stores/{storeId}/knowledge` | `ai.use` |

Upload is multipart (`files`). Same quotas as the cabinet (`MAX_FILES`, `MAX_STORAGE_GB`).

## Client config

Do **not** commit `mcp.json` with a real `dk_…` key.

Standard Streamable HTTP entry (`type` is `http`):

```json
{
  "mcpServers": {
    "delegat": {
      "type": "http",
      "url": "https://YOUR_INSTANCE/api/mcp",
      "headers": {
        "Authorization": "Bearer dk_YOUR_KEY",
        "X-Delegat-Store-Id": "1"
      }
    }
  }
}
```

Settings → AI mints a key named `MCP` and shows this JSON for the current origin and selected store. Local Vite (`:5173`) is the SPA; the MCP URL uses the API origin (`:5000` when the SPA is on the Vite port).

Pair with a filesystem MCP on the same machine to match a local product-files folder, then `PUT` bytes via Agent HTTP (not through the model).

## `platform_request`

Same cabinet JSON API as the SPA (`/api` prefix optional). Not Public API v1. Permissions, store membership, and quotas are the signed-in user's. Query strings work (`GET /products?searchQuery=&categoryId=&startIndex=&pageSize=`).

Denied: `/api/v1`, `/api/mcp`, `/api/agent`, `/api/ai`, `/api/auth`, `/api/users`, `/api/plugins`, `/api/api-keys`, connection credential create/update/test, database dump, quota trim, `DELETE /stores/{id}`, permanent product delete.

Staged cabinet attachments: `POST /products/{id}/files` `{ attachmentIds }`. Multipart product files stay `PUT …/products/{id}/files` on Agent HTTP.

PATCH `/products/{id}` `attributeValues` replaces the list (GET the card first). Lists return `{ items, totalCount }` like the cabinet — there is no `/stats` shortcut. Groups are `/productgroups`. Orders: `GET /marketplace/orders?tab=` or `GET /marketplace/connections/{id}/orders`. Mappings: `GET /marketplace/connections/{id}/mappings`.

## SQL (`sql_readonly`)

Allowlisted projections (host forces `storeId`): products (incl. `description`), files, categories (`name`, `description`, `hasSizes`), `category_attributes`, `product_attribute_values`, `store_attributes`, `store_product_attribute_values`, groups, connections, mappings, orders. `WHERE` supports `=`, `!=`, `LIKE`, `IN (...)`. Aggregates: `COUNT(*)`, `COUNT(DISTINCT col)` (not mixed with row columns). No `SELECT *`. Resource `delegat://store/schema`.

## Python SDK (sandbox)

The sidecar receives `DELEGAT_BASE_URL`, `DELEGAT_TOKEN`, `DELEGAT_STORE_ID` and calls Agent HTTP. It never receives `MYSQL_*`. `from delegat_sdk import client` is prepended. Compose caps the container at half a CPU and 512 MiB (no extra swap); jobs are one-at-a-time and BLAS is single-threaded so a script cannot pin the host.

Local `pnpm dev`: compose publishes `sandbox` on `http://127.0.0.1:8091` **only when `AI_ENABLED` is on** (Compose profile `ai`). Set `SANDBOX_URL` to that URL and `SANDBOX_AGENT_BASE_URL=http://host.docker.internal:5000` so jobs inside Docker can reach the host API. Unset `SANDBOX_URL` → `tools/list` omits `run_python`. The API posts a buffered JSON body with `Content-Length` (chunked empty bodies were 413 `invalid body` on the sidecar).
