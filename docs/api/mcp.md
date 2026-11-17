# Operator MCP and Agent HTTP

Machine surface for the cabinet AI agent and external MCP clients. **Not** partner Public API v1.

Gated by `AI_ENABLED` (process start). Independent of `API_USAGE_ENABLED` and the admin Public API toggle.

Implementation is **stateless JSON-RPC over HTTP** (`POST /api/mcp`). Same tool host as the in-app agent (in-process, no self-HTTP). Do not commit `.mcp.json`.

## Cabinet as MCP client

Settings → AI can store HTTP MCP servers (`aiMcpServers`, optional bearer key). The cabinet assistant lists `tools/list` and calls approved tools as `mcp{id}_{tool}`. That is **not** this operator surface: extra servers do not appear on `POST /api/mcp`. The same page can mint a Public API key and show a single `mcp.json` for `POST /api/mcp`.

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

The key owner must have `api.access` (to possess a key) and `ai.use`. Underlying resource permissions still apply. Except for protocol initialization, ping, and notifications, tool discovery and resource reads also resolve the selected store and require `ai.use` or `workspace.view` plus membership.

Read-only demo (`workspace.view` only): search, SQL, knowledge, GET `platform_request`. No Python, uploads, memory writes, connected MCP calls, or cabinet writes. The tool list omits Python and memory write tools; the host still enforces the restriction if a client sends a hidden or forged tool name.

The role template and the effective grants are different: see [Viewer and effective assistant permissions](../architecture/authentication.md#read-only-demo-workspace). Enabling a helper or confirming a plan does not grant `ai.use`.

## MCP tools

`initialize`, `tools/list`, `tools/call`, `resources/list`, `resources/read`, `ping`.

Tools: `search_knowledge`, `memory_search`, `memory_save`, `memory_delete`, `get_doc`, `search_catalog`, `search_actions`, `sql_readonly`, `run_python` (only while AI is on), `platform_request`, `create_file_download_link`, `navigate_to` (open XOR continue buttons on the reply; open urls may include a query string), `ask_form` (connection keys stay in the browser; questions are a short questionnaire on the reply; answers stay on the card, not as a visible chat bubble). Memory is scoped to the authenticated user and selected store and persists across conversations; MCP memory calls are scoped to the authenticated key owner and selected store. Cabinet chat writes, Python runs, and connected MCP calls require a standalone `propose_plan` and user confirmation. The operator MCP surface does not expose connected HTTP MCP servers; forged connected-tool names are rejected. `platform_request` mutations still check permissions. Staged cabinet attachments assign with `POST /products/{id}/files` `{ attachmentIds }` and remain downloadable for 24 hours. `import_generated_file` is cabinet-chat-only: it is not in `tools/list` here and is refused by `tools/call`, the Agent HTTP surface, review mode, and Python itself — only a new user-confirmed cabinet plan executes it. The `run_python` `outputs` declaration is likewise chat-scoped: this surface still runs Python, but captured outputs require the signed-executor flag external callers never get, so a forged `outputs` argument fails rather than staging artifacts.

`search_catalog` returns identity-only matches, with exact SKU/barcode/name before partial matches. Its `productsHasMore` and `filesHasMore` flags distinguish a limited preview from a complete result; the `limit` (maximum 100) does not mean a total count. Read price, stock, or description separately for a resolved product.

Reading staged attachment contents is a cabinet-chat-only capability; `read_chat_attachment` is intentionally omitted from operator MCP because that surface has no current-chat attachment context. The chat reader returns bounded extracted text or worksheet rows; it never returns the original binary bytes through a model/MCP payload.

### Large private file downloads

`search_actions` deliberately omits binary download routes, including `GET /files/{id}/download`: they are not JSON actions. `platform_request` only returns JSON/text; if a binary route is called directly, its response is stopped before buffering and returns `binary_response_requires_download`. This does not increase the 256,000-byte JSON limit. Oversized JSON/text returns `response_too_large` and a hint to page or narrow the request.

Use `create_file_download_link` with a product `fileId`. The small MCP result contains `fileName`, `sizeBytes`, `sha256`, `expiresAt`, `downloadUrl`, and `rangeSupported`. The service hashes the object as a bounded-memory storage stream, then returns a 10-minute ticket URL for `GET /api/agent/files/{fileId}/download?ticket=…`. Fetch the URL directly over HTTP; do not ask the model to carry file bytes or base64. A relative URL is resolved against the API origin used for `/api/mcp`; set `PUBLIC_BASE_URL` to the public API origin to return an absolute URL.

The ticket is authenticated and bound to the issuing user, store, and file. Each download request rechecks that the user is active, still belongs to the store, still has `ai.use` or `workspace.view`, and that the file still belongs to that store. It does not set `isPublic`, use the public-file route, or create a persisted share/artifact row. The URL is a bearer capability while valid; keep it private. Its short expiry is enforced statelessly, so there is no artifact cleanup job. `Range: bytes=…` (including suffix ranges) is supported for resume; multiple ranges are rejected with `416`. No file bytes pass through MCP JSON or the model.

The download ticket is **not single-use**: the same URL may be used for multiple full or single-range requests before its original expiry, provided the issuing user's access and the file/store checks still pass on each request. A download neither consumes the ticket nor extends its lifetime. Removing access can therefore block a later request even before expiry. This is separate from the 24-hour retention of staged chat attachments.

### Saving generated text to file storage

`platform_request` can save small text that is already available as one private file, into either storage destination — they are not interchangeable:

- **Store storage:** `POST /store-storage/files/from-text` with `{ fileName, text, contentType?, folderId? }` creates one private `StoreFile` of the selected store; `folderId` must be a store folder. The response carries `id`, `fileName`, `length`, and an authenticated `downloadUrl` (`/api/store-storage/files/{id}/download`).
- **Product file storage:** `POST /products/{productId}/files/from-text` with the same body creates one private file of that exact product (live, in the selected store); `folderId` must be a folder of that product. The response carries `id`, `productId`, `fileName`, `length`, and an authenticated `downloadUrl` (`/api/files/{id}/download`). Creating the file does not attach it to the gallery or characteristics and does not publish it.

`text` is limited to 262144 UTF-8 bytes; `contentType` is `text/plain` (default), `text/csv`, or `application/json`. The assistant shares the returned link instead of repeating file content in its final answer. Either write requires `files.manage` and a confirmed plan like every mutation, and the destination must be resolved before proposing it — never silently substitute store storage for a named product. Multipart uploads stay multipart-only and unreachable from `platform_request` (`PUT /store-storage/files`, `PUT /products/{id}/files`). For binary sandbox output, cabinet chat has a separate capture pipeline: `run_python` with a declared `outputs` list stages sandbox-produced files into private generated-artifact storage, and a separately confirmed `import_generated_file` step writes them into the chosen store/product destination (see `docs/ai/assistant-dialog-issues.md` §9.13). That pipeline is cabinet-chat-only: external `tools/list`, `tools/call`, and Agent HTTP have no output capture or `import_generated_file`, so to them binary sandbox artifacts still have no upload path. The cabinet plan card's saved-file download control (it binds the persisted import result to the existing authenticated store/product routes) is likewise cabinet UI only — the Agent HTTP 10-minute product tickets and this MCP surface are unchanged by it. The same applies to temporary generated outputs: a captured artifact's `downloadUrl` (`GET /api/ai/chat/generated-files/{artifactId}/download`) is a cabinet-only route under the `platform_request`-denied `/api/ai` prefix — external tickets, `tools/list`/`tools/call`, Agent HTTP, and `search_actions` are unchanged and never expose it. These are cabinet routes, not `/api/v1` and not Agent HTTP endpoints.

## Connected HTTP MCP trust policy

Connected HTTP MCP is a separate outbound cabinet-chat integration, not an Operator MCP server. It is disabled unless `AI_CONNECTED_MCP_ENABLED=true`. When enabled, the instance also requires an exact allowlist in `AI_CONNECTED_MCP_TOOL_ALLOWLIST`, for example `2:search_docs;2:fetch_document;3:render_stl`. Each entry is `<Settings MCP server id>:<exact remote tools/list name>`; commas and semicolons separate entries. There are no wildcards. An empty or malformed allowlist exposes no connected tools, and the server checks it again when a call arrives (so hiding a tool from `tools/list` is not the only enforcement).

This is an instance-wide allowlist, not a per-user grant. Connected tools still require `ai.use`; `workspace.view` is insufficient. In cabinet chat every remote call, whether read or write, remains a standalone plan step and waits for explicit user confirmation. The remote server uses the configured server credential, not a credential scoped to the current user. Use a dedicated least-privilege credential and a separate MCP server for each trust boundary; expose only read-only tools by default and allow a write tool only when its effect is intended. Disable the integration when it is not needed. Operator MCP never forwards or exposes connected HTTP MCP tools.

Resources:

- `delegat://docs/{path}`
- `delegat://openapi/operator.json`
- `delegat://store/schema`
- `delegat://cabinet/actions` (optional `?q=` search)

## Agent HTTP (selected)

| Method | Path | Permission |
|--------|------|------------|
| PUT | `/api/agent/stores/{storeId}/products/{productId}/files` | `files.manage` |
| GET | `/api/agent/files/{fileId}/download?ticket=…` | Short-lived user/store/file-bound ticket |
| POST | `/api/agent/stores/{storeId}/catalog/search` | `ai.use` |
| POST | `/api/agent/stores/{storeId}/sql` | `ai.use` |
| POST | `/api/agent/stores/{storeId}/python` | `ai.use` (denied for view-only) |
| POST | `/api/agent/stores/{storeId}/platform` | resource perm of the route |
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

Pair with a filesystem MCP on the same machine to read local files, then `PUT` bytes via Agent HTTP (not through the model).

## `platform_request`

Action discovery (`search_actions`) ranks concrete DTO fields and translated field/capability aliases above generic verbs, returning canonical request semantics and nested bulk field names. Search the specific operation and scope, such as regenerating local product barcodes rather than just updating products. Native bulk actions should be preferred only when they preserve the requested semantics; an unrelated hit does not establish support.

For native barcode generation, `PATCH /products/bulk` accepts `{ productIds, wait: true, updates: { generateBarcodes: true } }` to fill missing product barcodes. Adding `updates.barcode: ""` clears existing values first and regenerates all selected products in the same request, with server-side uniqueness checks across products and variants including trash. A nonempty common `updates.barcode` cannot be assigned to several products. Different externally specified barcode values require individual product updates, not random regeneration. Use exact IDs for an established selection; do not replace them with `allMatching`. Bulk results report counts and can partially apply; queued execution must be tracked in Operations.

Same cabinet JSON API as the SPA (`/api` prefix optional). Not Public API v1. Permissions, store membership, and quotas are the signed-in user's. Query strings work (`GET /products?searchQuery=&categoryId=&startIndex=&pageSize=`).

Do not use it to download product file bytes; use `create_file_download_link` and then GET the returned URL. Its response capture is bounded to 256,000 bytes for JSON/text, and binary responses are rejected before buffering.

The agent gateway caps top-level `items` lists to 12 and reports removed page items in `itemsTruncated`; `totalCount` is unchanged. If a capped response has a boolean `lastPage`, it becomes `false` even when the cabinet's original page was terminal. For offset-based lists, advance `startIndex` by the number actually received, not the requested page size, and check the accumulated unique count against `totalCount`. This prevents a trimmed terminal page from masquerading as a complete list. Byte limits remain enforced.

Denied: `/api/v1`, `/api/mcp`, `/api/agent`, `/api/ai` except `GET /api/ai/store-settings` (personal settings: store description, communication style, brevity, emoji; `PUT` stays denied), `/api/auth`, `/api/users`, `/api/plugins`, `/api/api-keys`, connection credential create/update/test, database dump, quota trim, `DELETE /stores/{id}`, permanent product delete.

Staged cabinet attachments: `POST /products/{id}/files` `{ attachmentIds }`. Multipart product files stay `PUT …/products/{id}/files` on Agent HTTP.

PATCH `/products/{id}` `attributeValues` replaces the list (GET the card first). Lists return `{ items, totalCount }` like the cabinet — there is no `/stats` shortcut. Category attribute definitions: `GET /categories/{id}/attributes`; lightweight metadata only (including `unitCode`): `GET /categories/{id}/attributes/summary`. Store-wide attribute definitions: `GET /store-attributes`; lightweight metadata: `GET /store-attributes/summary`. A definition's `unitCode` is separate from product string values. Changing `unitCode` through `PATCH /categories/{id}/attributes/{attributeId}` or `PATCH /store-attributes/{attributeId}` does not recalculate stored values. Values can be converted only as a separate, explicitly requested operation with known compatible units and verified targets. Mapping modifiers affect exported output, not local values or unit metadata. Groups are `/productgroups`. Orders: `GET /marketplace/orders?tab=` or `GET /marketplace/connections/{id}/orders`. Mappings: `GET /marketplace/connections/{id}/mappings`. Mapping canvas: `GET/PATCH/PUT /marketplace/connections/{id}/mappings/{mappingId}/graph` (GET includes `blocks`, mapping metadata, and `graphVersion`; `includeSchema=true` adds the marketplace field dictionaries, which can be large). A PUT may send `expectedGraphVersion`; a stale version returns 409 `mapping_graph_conflict`. Block types, handles, pin types, and layout sizes: `GET /marketplace/mapping-graph/blocks`. Finish field wiring with `POST …/graph/connect` (`attribute` or `productField`) after one graph GET. Attribute groups are local characteristics. When one local category has several marketplace categories, `POST …/resolve-mappings` `{ productIds }` then `POST …/save-mapping-selections` `{ selections: [{ productId, categoryMappingId }] }` assigns the product. That does not edit the canvas. `POST …/graph/group-titles` is only Merge into card (`__group__`, typed from/to). Value transforms are `POST …/graph/modifiers`. Marketplace product import is the same as the SPA: `POST /marketplace/connections/{id}/products/import-unlinked` (Catalog Connect; HTTP 200 with `status` pending/running queues background import) or `GET …/import-preview` then `POST …/import-selected`. Linking existing local cards to another marketplace: `POST …/match/preview`, poll `GET /marketplace/catalog-connect/jobs`, then `POST …/match/apply` (include preview `externalCategoryId`). Missing mappings after a link: `POST …/match/repair-mappings`. `GET /tools` page tools are not `POST /tools/{id}/run`. `MAX_PRODUCTS` used includes trash (`GET /products/trash/count`).

## SQL (`sql_readonly`)

Allowlisted projections (host forces `storeId`): products (incl. `description`), files, `product_images`, categories (`name`, `description`, `hasSizes`), `category_attributes` and `store_attributes` (both include `unitCode`), `product_attribute_values`, `store_product_attribute_values`, groups, connections, mappings, orders, plus notes, variants, group members, product links, store files/folders, operations, pending imports, adapter HTTP logs (no credential or body columns). Attribute `unitCode` is definition metadata; product values stay separate strings and are never converted by a unit metadata update. Unknown/custom codes are retained as opaque labels; their dimension is unknown. `WHERE` supports `=`, `!=`, `<` `>` `<=` `>=`, `LIKE` / `NOT LIKE`, `IN` / `NOT IN (...)`, `IS NULL` / `IS NOT NULL`, and `NOT IN (SELECT productId FROM product_images|files)`. Aggregates: `COUNT(*)`, `COUNT(DISTINCT col)`, `MIN(col)`, `MAX(col)` (not mixed with row columns). `SELECT DISTINCT col`. `SHOW TABLES` / `SHOW COLUMNS` / `DESCRIBE`. `LIMIT` / `OFFSET`. No `SELECT *`. Resource `delegat://store/schema`.

Mass catalog export: `POST /marketplace/connections/{id}/products/export-batch` `{ where }`. Templates: `@withGallery`, `@withoutGallery`, `@withFiles`, `@withoutFiles`, `@inStock`, `@outOfStock`, `@noPrice`, `@noBarcode`, `@noCategory`. Anything else is the same allowlisted SQL WHERE as `sql_readonly`, combined with `AND`. `GET /products?where=` and `PATCH /products/bulk` take that clause too. Named sets still send `productIds`. `{ allMatching: true }` is the whole live catalog.
Raise every catalog price: `PATCH /products/bulk` `{ allMatching: true, wait: true, updates: { priceDelta: 500 } }`, then `POST /tools/export-all-prices/run` `{ connectionId }` per shop.

## Python SDK (sandbox)

SQL defaults to `LIMIT 50` and clamps explicit limits to 200. For a complete product projection use `ORDER BY id LIMIT 200 OFFSET n`, advancing by the received row count until a short page, or verify with `COUNT(*)`. A successful SELECT without pagination does not establish full-catalog coverage. Full platform scans must also reject duplicate/repeated pages, an empty page before `totalCount`, and exhausted page budgets rather than claiming completeness.

The preloaded `client` is `DelegatClient`, not a requests session. Use `client.platform(method, path, body=None)` for cabinet JSON or `client.sql(sql)` for allowlisted read-only SQL (`rows`, not `items`). There are no `client.get`, `client.request`, or `client.post` aliases. `GET /products?startIndex=0&pageSize=100` returns `items`, `totalCount`, and `lastPage`; advance by the received item count until `lastPage`, checking errors and inconsistent/incomplete pages instead of reporting zero matches. Do not write from Python. Other public helpers are `search_catalog`, `search_knowledge`, `match_files`, `marketplace_doc`, `shop_request`, `shop_operations`, and `shop_read`. When a cabinet chat run declares `outputs`, `client.output_path(file_name)` returns the relative `outputs/` path for one declared basename (at most 180 UTF-16 code units and 255 UTF-8 bytes; invalid names are rejected, never rewritten) — writing the complete file there lets the server capture immutable bytes and return artifact metadata; it raises for undeclared names and grants no other filesystem or storage access. The model-facing contract is `PythonSdkContract.Instructions`, checked against `sandbox/python/delegat_sdk.py` by SDK tests.

Cabinet chat reviews actual Python stdout/stderr/exit code after execution. Failed or partial output is not a verified computation result. A supported corrected script is a new plan requiring fresh confirmation, not an automatic retry. This review belongs to cabinet chat; operator MCP/Agent HTTP calls retain their direct tool-result contract.

The sidecar receives `DELEGAT_BASE_URL`, `DELEGAT_TOKEN`, `DELEGAT_STORE_ID`, and `shops`. By default, `shops` retains the legacy connection credentials/hosts/auth metadata. For stores selected by `MARKETPLACE_BROKER_SANDBOX_STORES`, broker-capable connections contain metadata only and seller reads use `client.shop_request` (GET) or the adapter's discovered named operations (`client.shop_operations` / `client.shop_read`). Unsupported connections remain marked `access=legacy`. `docs.platform` is read with `client.marketplace_doc` instead of fetching documentation URLs. It never receives `MYSQL_*`. `from delegat_sdk import client` is prepended, and `shops` is already loaded. Legacy output values are redacted; broker responses redact credentials server-side. Compose caps the container at half a CPU and 512 MiB (no extra swap); jobs are one-at-a-time and BLAS is single-threaded so a script cannot pin the host.

Local `pnpm dev`: compose publishes `sandbox` on `http://127.0.0.1:8091` **only when `AI_ENABLED` is on** (Compose profile `ai`). Set `SANDBOX_URL` to that URL and `SANDBOX_AGENT_BASE_URL=http://host.docker.internal:5000` so jobs inside Docker can reach the host API. A Control Plane stack does not need those URLs: while AI is on the API uses container name `d-{stack}-sandbox`, and the sidecar process itself exits unless `AI_ENABLED` is true. AI off omits `run_python` even if a URL is set. Unset URL with no stack id → `tools/list` omits `run_python`. The API posts a buffered JSON body with `Content-Length` (chunked empty bodies were 413 `invalid body` on the sidecar).
