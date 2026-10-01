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

Tools: `search_knowledge`, `get_doc`, `search_catalog`, `search_actions`, `sql_readonly`, `run_python` (only while AI is on), `platform_request`, `match_files`, `navigate_to` (open XOR continue buttons on the reply; open urls may include a query string), `ask_form` (connection keys stay in the browser; questions are a short questionnaire on the reply; answers stay on the card, not as a visible chat bubble). Writes go through `propose_plan` in the cabinet UI; Cursor confirms in the client. `platform_request` mutations still check permissions. Staged cabinet attachments assign with `POST /products/{id}/files` `{ attachmentIds }` and remain downloadable for 24 hours.

Resources:

- `delegat://docs/{path}`
- `delegat://openapi/operator.json`
- `delegat://store/schema`
- `delegat://cabinet/actions` (optional `?q=` search)

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

Denied: `/api/v1`, `/api/mcp`, `/api/agent`, `/api/ai` except `GET /api/ai/store-settings` (personal settings: store description, communication style, brevity, emoji; `PUT` stays denied), `/api/auth`, `/api/users`, `/api/plugins`, `/api/api-keys`, connection credential create/update/test, database dump, quota trim, `DELETE /stores/{id}`, permanent product delete.

Staged cabinet attachments: `POST /products/{id}/files` `{ attachmentIds }`. Multipart product files stay `PUT …/products/{id}/files` on Agent HTTP.

PATCH `/products/{id}` `attributeValues` replaces the list (GET the card first). Lists return `{ items, totalCount }` like the cabinet — there is no `/stats` shortcut. Groups are `/productgroups`. Orders: `GET /marketplace/orders?tab=` or `GET /marketplace/connections/{id}/orders`. Mappings: `GET /marketplace/connections/{id}/mappings`. Mapping canvas: `GET/PATCH/PUT /marketplace/connections/{id}/mappings/{mappingId}/graph` (GET includes `blocks`). Block types, handles, pin types, and layout sizes: `GET /marketplace/mapping-graph/blocks`. Finish field wiring with `POST …/graph/connect` (`attribute` or `productField`) after one graph GET. Attribute groups are local characteristics. When one local category has several marketplace categories, `POST …/resolve-mappings` `{ productIds }` then `POST …/save-mapping-selections` `{ selections: [{ productId, categoryMappingId }] }` assigns the product. That does not edit the canvas. `POST …/graph/group-titles` is only Merge into card (`__group__`, typed from/to). Value transforms are `POST …/graph/modifiers`. Marketplace product import is the same as the SPA: `POST /marketplace/connections/{id}/products/import-unlinked` (Catalog Connect; HTTP 200 with `status` pending/running queues background import) or `GET …/import-preview` then `POST …/import-selected`. Linking existing local cards to another marketplace: `POST …/match/preview`, poll `GET /marketplace/catalog-connect/jobs`, then `POST …/match/apply` (include preview `externalCategoryId`). Missing mappings after a link: `POST …/match/repair-mappings`. `GET /tools` page tools are not `POST /tools/{id}/run`. `MAX_PRODUCTS` used includes trash (`GET /products/trash/count`).

## SQL (`sql_readonly`)

Allowlisted projections (host forces `storeId`): products (incl. `description`), files, `product_images`, categories (`name`, `description`, `hasSizes`), `category_attributes`, `product_attribute_values`, `store_attributes`, `store_product_attribute_values`, groups, connections, mappings, orders, plus notes, variants, group members, product links, store files/folders, operations, pending imports, adapter HTTP logs (no credential or body columns). `WHERE` supports `=`, `!=`, `<` `>` `<=` `>=`, `LIKE` / `NOT LIKE`, `IN` / `NOT IN (...)`, `IS NULL` / `IS NOT NULL`, and `NOT IN (SELECT productId FROM product_images|files)`. Aggregates: `COUNT(*)`, `COUNT(DISTINCT col)`, `MIN(col)`, `MAX(col)` (not mixed with row columns). `SELECT DISTINCT col`. `SHOW TABLES` / `SHOW COLUMNS` / `DESCRIBE`. `LIMIT` / `OFFSET`. No `SELECT *`. Resource `delegat://store/schema`.

Mass catalog export: `POST /marketplace/connections/{id}/products/export-batch` `{ where }`. Templates: `@withGallery`, `@withoutGallery`, `@withFiles`, `@withoutFiles`, `@inStock`, `@outOfStock`, `@noPrice`, `@noBarcode`, `@noCategory`. Anything else is the same allowlisted SQL WHERE as `sql_readonly`, combined with `AND`. `GET /products?where=` and `PATCH /products/bulk` take that clause too. Named sets still send `productIds`. `{ allMatching: true }` is the whole live catalog.
Raise every catalog price: `PATCH /products/bulk` `{ allMatching: true, wait: true, updates: { priceDelta: 500 } }`, then `POST /tools/export-all-prices/run` `{ connectionId }` per shop.

## Python SDK (sandbox)

The sidecar receives `DELEGAT_BASE_URL`, `DELEGAT_TOKEN`, `DELEGAT_STORE_ID`, and `shops` (this store’s connection credentials, seller hosts, how to send the key, and `docs.platform` when Control Plane keeps that seller’s daily snapshot, which the script reads with `client.marketplace_doc` instead of fetching the documentation URL). The script calls those seller hosts with `urllib` directly. It never receives `MYSQL_*`. `from delegat_sdk import client` is prepended, and `shops` is already loaded. Secret values are redacted from the job output before it returns to the model. Compose caps the container at half a CPU and 512 MiB (no extra swap); jobs are one-at-a-time and BLAS is single-threaded so a script cannot pin the host.

Local `pnpm dev`: compose publishes `sandbox` on `http://127.0.0.1:8091` **only when `AI_ENABLED` is on** (Compose profile `ai`). Set `SANDBOX_URL` to that URL and `SANDBOX_AGENT_BASE_URL=http://host.docker.internal:5000` so jobs inside Docker can reach the host API. A Control Plane stack does not need those URLs: while AI is on the API uses container name `d-{stack}-sandbox`, and the sidecar process itself exits unless `AI_ENABLED` is true. AI off omits `run_python` even if a URL is set. Unset URL with no stack id → `tools/list` omits `run_python`. The API posts a buffered JSON body with `Content-Length` (chunked empty bodies were 413 `invalid body` on the sidecar).
