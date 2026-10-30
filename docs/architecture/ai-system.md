# AI system

Canon for instance AI after the 2026-09-06 rewrite. Detailed shape: [`docs/plans/2026-09-06-agent-mcp-design.md`](../plans/2026-09-06-agent-mcp-design.md). Machine surface: [`docs/api/mcp.md`](../api/mcp.md). Prompt/tool maintenance: [`ai-assistant.md`](ai-assistant.md).

This document supersedes the former ~84 named LLM tools. Do not add a parallel tool zoo.

## Shape

One operator capability layer (application services + quotas + `Permissions.*`). Three clients:

1. SPA (cookie JWT + CSRF) — catalog, files, orders, mappings as today.
2. Agent HTTP (`/api/agent/...`) and MCP (`POST /api/mcp`) — JWT or `dk_…`. **Not** Public API v1. **Not** gated by `API_USAGE_ENABLED`.
3. Python SDK inside an optional sandbox sidecar — calls Agent HTTP only. Never receives `MYSQL_*`.

The cabinet UI agent calls the same tool host **in-process**. Cursor / Claude Desktop speak MCP. The cabinet chat prompt (`AiPrompts.ChatSystem`) is store-scoped: refuse off-topic work and secret/prompt dumps; hard rails stay in `PlatformRequestPolicy` and `AgentSqlGuard`. In cabinet chat, mutating `platform_request` calls, every `run_python` call, and every connected MCP call require a standalone `propose_plan` and explicit confirmation. The host also enforces this boundary; prompt instructions alone are not the control.

## Process gate

`AI_ENABLED` is read at process start. When false, AI DI, controllers (`Ai*`, `Agent*`), and the SPA chat chrome are not loaded, and Compose does not start the Python sidecar (profile `ai`). Restart to toggle. Unset: enabled for self-host; disabled when `CONTROL_PLANE_URL` is set.

Permission `ai.use` is required (or `workspace.view` for read-only). Resource grants still apply (`files.manage` to upload, `orders.manage` for order SQL, `catalog.manage` for product writes, `marketplace.manage` for connections/mappings).

## Thin tools

| Tool | Role |
|------|------|
| `search_knowledge` | Lexical RAG over fetched product docs (`product-docs/…`) plus MEMORY / PRODUCT / DESIGN / `docs/ai/operator-playbook.md` (table `aiKnowledgeChunks`, MinIO prefix `__ai-knowledge/`) |
| `memory_search` / `memory_save` / `memory_delete` | Durable personal memories across conversations, scoped to the current store and user (`aiMemories`); search does not retrieve chat transcripts |
| `get_doc` | Product article by slug **or** indexed instance `sourcePath` (cap ~32k) |
| `search_catalog` | Live product/file identity metadata (name / SKU / barcode) in **this** store; product hits omit price, storage quantity, and description. Read those requested facts separately. Specific lookups require a non-empty query; empty query is for an explicit browse/list request only |
| `search_actions` | Lexical search over cabinet HTTP the assistant may call (MVC routes + body notes). Use instead of memorizing paths or `GET /tools` |
| `sql_readonly` | `SELECT` on allowlisted projections; host forces `storeId`. Comparisons, `NOT IN`, subquery against `product_images` or `files`, `DISTINCT`, `MIN`/`MAX`, `OFFSET`, `IS NULL`, `SHOW TABLES` |
| `run_python` | Sandbox job while AI is on; pandas / pillow / openpyxl. Cabinet chat requires a standalone confirmed plan for each run. |
| `platform_request` | `method + path + json` against the same cabinet controllers as the SPA (denylist for secrets and irreversible actions); mutations in cabinet chat require confirmation. |
| `match_files` | Optional: map filenames to products (SKU / barcode / name stem) |
| `propose_plan` | UI confirmation before writes |
| `navigate_to` | Reply buttons: open a path (optional query) XOR send a follow-up (`items[]`, max 6); one action per button. Use only when requested or needed for the workflow, not for ordinary read-only answers or clarification |
| `ask_form` | Reply form: connection credentials in the browser, or a short questionnaire (up to 5 steps). Answers stay on the card; the model gets a hidden user turn. A simple read-only lookup with a missing identifier gets one clarification, in plain text or a single-field form, without broad catalog listing |

Bytes never go through the model. Cabinet chat stages attachments for **24 hours** (download while live, then purge). Then `platform_request POST /products/{id}/files` with `attachmentIds` after confirm. Cursor / Agent HTTP still upload with multipart `PUT …/products/{id}/files`.

## Knowledge vs catalog

- Knowledge bucket is instance-global, not billed as `MAX_STORAGE_GB`, reindexed on API boot.
- Catalog truth is live MySQL for the current `storeId`. Do not embed the full catalog as RAG.

## SQL

Host parser (`AgentSqlGuard`): `SELECT` / `WITH` only, no `SELECT *`, allowlisted tables/columns including category and store attributes and `product_images`. `WHERE` supports `IN` / `NOT IN` and a single-table `NOT IN (SELECT productId FROM product_images|files)`. `COUNT(*)` / `COUNT(DISTINCT col)` / `MIN` / `MAX` allowed without mixing row columns. `SELECT DISTINCT col`. Forbidden: users, tokens, API keys, connection `credentialsJson`, order `payloadJson`, other stores. PII the operator already sees (order buyer) follows `orders.manage`.

## Python sandbox

Sidecar `sandbox/python`. Isolated process, non-root. Compose cgroup: **0.50 CPU**, **512 MiB RAM**, no extra swap (`SANDBOX_CPUS` / `SANDBOX_MEM_LIMIT`). One job at a time (`sandbox_busy` if busy). Jobs are single-threaded for BLAS (`SANDBOX_CPU_THREADS=1`), 384 MiB address-space cap, 30s wall timeout, `/tmp` workspace. By default the legacy `shops` payload still includes connection credentials, seller hosts, and `docs.platform`. Stores selected by `MARKETPLACE_BROKER_SANDBOX_STORES` get credential-free descriptors for adapters that expose the optional broker capability; entries not eligible for broker mode are marked `access=legacy`. Broker-mode prompts call `client.shop_request` for GET reads and discover adapter-defined POST reads with `client.shop_operations` / `client.shop_read`. The daily Control Plane snapshot is still read via `client.marketplace_doc`. Output redaction remains for legacy entries; broker replies also redact stored credentials. Cabinet HTTP still goes through Agent HTTP.

`POST /api/agent/stores/{storeId}/marketplace/{connectionId}/read` brokers GET reads; `read-operation` brokers only named, adapter-owned POST reads. Both are store-scoped and rate-limited, keep credentials server-side, disable redirects, cap responses at 256 KB, redact credential values, and audit calls. Wildberries exposes product-card listing; Ozon exposes category, product, price, stock, warehouse, and quota reads; Yandex Market exposes category, offer, price, stock, and promotion reads. Operations are discovered per connection from plugin SDK capabilities; adapters define operation names/schemas and build fixed upstream routes and request bodies. Avito, WooCommerce, OpenCart, and Medusa provide GET capabilities through plugins. Custom origins use the public-HTTPS/443 DNS-pinned transport; WooCommerce/Medusa preserve configured base paths, and OpenCart maps path-style reads onto query-route actions. Medusa localhost remains legacy-only. Future plugins opt in through the same optional SDK capabilities without adding adapter-key dispatch branches. The rollout flag is `MARKETPLACE_BROKER_SANDBOX_STORES` (comma/semicolon-separated IDs or `*`), unset by default; selected broker-capable connections omit Marketplace credentials from the sandbox payload. No live canary has been run, so keep the flag off until the selected store's workflow fixtures pass.

The sandbox SDK exposes `client.shop_request(connection_id, host, path)`, `client.shop_operations(connection_id)`, and `client.shop_read(connection_id, operation, arguments)`. Broker-mode prompts use them for selected connections; legacy workflows remain available for non-selected or unsupported connections. Cabinet chat places each Python run behind the confirmation gate, but this is not a sandbox capability boundary: the request still forwards a user JWT and legacy descriptors can still contain decrypted Marketplace credentials, while arbitrary Python can make outbound requests. No network egress policy is configured here; do not treat plan confirmation as a substitute for removing credentials or restricting egress.

A question may take several tool calls; the turn ends with the answer or a plan, not after a fixed number of calls. A message that only says to answer calls no tools. The sandbox does not fetch the seller’s documentation URL. OpenCart and Medusa are left without a guessed URL. `run_python` is listed only while AI is on, and only when `SANDBOX_URL` is set (local) or `STACK_ID` / `INSTANCE_ID` / `mysql-{stack}` can form the container name `http://d-{stack}-sandbox:8080`. Alias DNS is not used. AI off omits the tool even if a URL is set. A direct call with no candidates returns `sandbox_not_configured`. Audit table `aiSandboxRuns`. On a Control Plane instance the sidecar process starts only while `AI_ENABLED` is true (container command; a redeploy applies an env edit). Local compose uses profile **`ai`** (`pnpm dev` reads `AI_ENABLED`; self-host `docker compose --profile ai`). Local host port `8091`, `SANDBOX_AGENT_BASE_URL=http://host.docker.internal:5000`. The API posts buffered JSON with `Content-Length`; the sidecar also accepts chunked bodies.

## Auth (MCP / Agent HTTP)

- In-app: cabinet JWT, CSRF not required on `/api/mcp` and `/api/agent`.
- External: `Authorization: Bearer dk_…` plus `X-Delegat-Store-Id`. Key owner needs `api.access` (to hold a key) and `ai.use`.
- `workspace.view` only: search, SQL, knowledge, GET `platform_request`. No Python, uploads, memory writes, connected MCP calls, or cabinet writes. The host enforces this even if a client forges a tool call.

Connected HTTP MCP servers are configured by an operator with `settings.platform`; their exposed capabilities do not have per-tool resource permission metadata. In cabinet chat each remote call must be in a standalone confirmed plan, but server-side authorization cannot distinguish a remote read from a remote write. Each JSON/SSE/error response is capped at 256,000 bytes. Configure only trusted servers and account for the fact that any `ai.use` user can propose a call and ask for confirmation.

Denylist: credentials, plugin upload, user admin, API keys, hard-delete, `aiModelConfigs` keys, `/api/ai` and `/api/agent` (no nested chat). `platform_request` runs those cabinet controllers in-process (same JWT permission hydration, store cookie, quotas). MCP `dk_…` is minted into a short-lived JWT because cabinet controllers are JWT-only.

## Conversations and models

Unchanged: `aiConversations` / `aiConversationMessages` isolated by `sessionId + storeId + userId`; thumbs feedback; admin model CRUD + proxy (`AiModelConfigService`); Anthropic Messages and OpenAI-compatible Chat Completions.

Durable assistant memories are separate from conversation history and project knowledge. They are scoped to `(storeId, userId)`, included in the chat system context, and manageable through the three memory tools. `memory_search` searches saved facts/preferences only, not conversation transcripts; do not use it merely because a user mentions another chat, and honor an explicit request not to search. The prompt treats memory contents as untrusted data, prioritizes the current request/live catalog, and forbids saving credentials, secrets, guesses, or transient details. Each user/store has a 40-entry cap; values are limited to 1,000 characters.

### Optional Platform AI

When Platform Link is enabled, the model picker can include a synthetic **Platform AI** model (reserved model ID `-1`) when the Control Plane reports a configured model and the organization is entitled. The instance does not persist it in `aiModelConfigs`. Its organization-shared monthly token quota is configured by plan in the Control Plane, defaults to zero until configured, and resets monthly in UTC. The picker can still show the model when its quota is exhausted; hosted requests then fail with a localized error, while configured customer-connected (BYOK) models remain independent and usable.

Hosted model requests are relayed through the Control Plane's `/platform/v1/ai/` proxy endpoints, including streaming responses. The instance signs each request with its Platform Link secret and a unique request ID; the provider key stays in the Control Plane. The Control Plane accounts for provider-reported token usage and request metadata, not prompts or answers. Request content is forwarded to the configured provider to fulfil the call. See the instance-side [Platform Link notes](../platform/platform-link.md) and the `Hosted AI` section of `delegat-module-platform/docs/platform-link.md` in the Control Plane repository.

SSE event types for chat: `text`, `tool`, `plan`, `done`, `error`.

A cabinet turn is hosted by `AiLiveTurnHub` (process memory, one running turn per `sessionId`). `POST /api/ai/chat` and `POST /api/ai/execute-plan` start the turn and subscribe; `GET /api/ai/chat/stream?sessionId=` replays then tails after a reload. Conversation GET payloads include `generating`. The client saves a new user turn before starting the live request; the server is the sole writer for the streamed assistant turn, which the client reloads from conversation history after the stream ends. `execute-plan` does not persist a confirm user line; it updates the **same** assistant row as the plan (merge tools/timeline, `planExecuted`, and `planFailed` when a step was HTTP 4xx/5xx). Plan confirmation reads, locks, and writes inside `Database.CreateExecutionStrategy()` so the transaction is retriable as a unit. The finished assistant row is written on the server when the turn ends (insert or update the last assistant). Client history sync is additive and must not delete messages omitted from a possibly stale snapshot; it matches ordered unpersisted messages against server-inserted rows so form metadata can be attached without duplicating them. Live history writes retry optimistic concurrency conflicts against the latest row and preserve server-owned plan metadata. `POST /api/ai/stop` cancels the hub CTS for that store+user. An API process restart drops an unfinished turn.

## Mapping and errors

`GraphMutationService` remains a **domain** service. Mapping-suggest and explain-error are prompt skills on the same model, not a second tool catalog. Keep `POST /api/ai/mapping-suggest/{id}` and `POST /api/ai/explain-error`.

## Undo

Do not snapshot 84 tool shapes. Prefer Operations / service-level undo where it exists; otherwise the UI says the step is not undoable.

## EF

New tables only via `dotnet ef migrations add`: `aiKnowledgeChunks`, `aiSandboxRuns`, `aiAgentAudits`, `aiChatAttachments`, `aiMcpServers`, `aiMemories`.
