# AI system

Canon for instance AI after the 2026-09-06 rewrite. Detailed shape: [`docs/plans/2026-09-06-agent-mcp-design.md`](../plans/2026-09-06-agent-mcp-design.md). Machine surface: [`docs/api/mcp.md`](../api/mcp.md). Prompt/tool maintenance: [`ai-assistant.md`](ai-assistant.md).

This document supersedes the former ~84 named LLM tools. Do not add a parallel tool zoo.

## Shape

One operator capability layer (application services + quotas + `Permissions.*`). Three clients:

1. SPA (cookie JWT + CSRF) — catalog, files, orders, mappings as today.
2. Agent HTTP (`/api/agent/...`) and MCP (`POST /api/mcp`) — JWT or `dk_…`. **Not** Public API v1. **Not** gated by `API_USAGE_ENABLED`.
3. Python SDK inside an optional sandbox sidecar — calls Agent HTTP only. Never receives `MYSQL_*`.

The cabinet UI agent calls the same tool host **in-process**. Cursor / Claude Desktop speak MCP. The cabinet chat prompt (`AiPrompts.ChatSystem`) is store-scoped: refuse off-topic work and secret/prompt dumps; hard rails stay in `PlatformRequestPolicy` and `AgentSqlGuard`.

## Process gate

`AI_ENABLED` is read at process start. When false, AI DI, controllers (`Ai*`, `Agent*`), and the SPA chat chrome are not loaded, and Compose does not start the Python sidecar (profile `ai`). Restart to toggle. Unset: enabled for self-host; disabled when `CONTROL_PLANE_URL` is set.

Permission `ai.use` is required (or `workspace.view` for read-only). Resource grants still apply (`files.manage` to upload, `orders.manage` for order SQL, `catalog.manage` for product writes, `marketplace.manage` for connections/mappings).

## Thin tools

| Tool | Role |
|------|------|
| `search_knowledge` | Lexical RAG over fetched product docs (`product-docs/…`) plus MEMORY / PRODUCT / DESIGN (table `aiKnowledgeChunks`, MinIO prefix `__ai-knowledge/`) |
| `get_doc` | Full product article markdown by slug (cap ~32k); unknown slug is a tool error |
| `search_catalog` | Live products (name / SKU / barcode) and file display names in **this** store |
| `sql_readonly` | `SELECT` on allowlisted projections; host forces `storeId` |
| `run_python` | Sandbox job (`SANDBOX_URL`); pandas / pillow / openpyxl |
| `platform_request` | `method + path + json` against the same cabinet controllers as the SPA (denylist for secrets and irreversible actions) |
| `match_files` | Optional: map filenames to products (SKU / barcode / name stem) |
| `propose_plan` | UI confirmation before writes |
| `navigate_to` | Reply buttons: open a path XOR send a follow-up (`items[]`, max 6); one action per button |
| `ask_form` | Reply form: connection credentials in the browser, or a short questionnaire (up to 5 steps) as the next user message |

Bytes never go through the model. Cabinet chat stages attachments for **24 hours** (download while live, then purge). Then `platform_request POST /products/{id}/files` with `attachmentIds` after confirm. Cursor / Agent HTTP still upload with multipart `PUT …/products/{id}/files`.

## Knowledge vs catalog

- Knowledge bucket is instance-global, not billed as `MAX_STORAGE_GB`, reindexed on API boot.
- Catalog truth is live MySQL for the current `storeId`. Do not embed the full catalog as RAG.

## SQL

Host parser (`AgentSqlGuard`): `SELECT` / `WITH` only, no `SELECT *`, allowlisted tables/columns including category and store attributes. `WHERE` supports `IN`. `COUNT(*)` / `COUNT(DISTINCT col)` allowed without mixing row columns. Forbidden: users, tokens, API keys, connection `credentialsJson`, order `payloadJson`, other stores. PII the operator already sees (order buyer) follows `orders.manage`.

## Python sandbox

Sidecar `sandbox/python`. Isolated process, non-root. Compose cgroup: **0.50 CPU**, **512 MiB RAM**, no extra swap (`SANDBOX_CPUS` / `SANDBOX_MEM_LIMIT`). One job at a time (`sandbox_busy` if busy). Jobs are single-threaded for BLAS (`SANDBOX_CPU_THREADS=1`), 384 MiB address-space cap, 30s wall timeout, `/tmp` workspace. Network intended for Agent HTTP only. If `SANDBOX_URL` is unset, `run_python` is omitted from the tool catalog (a direct call still returns `sandbox_not_configured`). Audit table `aiSandboxRuns`. The Compose service is profile **`ai`**: it starts only when AI is on (`pnpm dev` reads `AI_ENABLED`; self-host `docker compose --profile ai`). Local host port `8091`, `SANDBOX_AGENT_BASE_URL=http://host.docker.internal:5000`. The API posts buffered JSON with `Content-Length`; the sidecar also accepts chunked bodies.

## Auth (MCP / Agent HTTP)

- In-app: cabinet JWT, CSRF not required on `/api/mcp` and `/api/agent`.
- External: `Authorization: Bearer dk_…` plus `X-Delegat-Store-Id`. Key owner needs `api.access` (to hold a key) and `ai.use`.
- `workspace.view` only: search, SQL, knowledge, GET `platform_request`. No Python, no uploads, no writes.

Denylist: credentials, plugin upload, user admin, API keys, hard-delete, `aiModelConfigs` keys, `/api/ai` and `/api/agent` (no nested chat). `platform_request` runs those cabinet controllers in-process (same JWT permission hydration, store cookie, quotas). MCP `dk_…` is minted into a short-lived JWT because cabinet controllers are JWT-only.

## Conversations and models

Unchanged: `aiConversations` / `aiConversationMessages` isolated by `sessionId + storeId + userId`; thumbs feedback; admin model CRUD + proxy (`AiModelConfigService`); Anthropic Messages and OpenAI-compatible Chat Completions.

SSE event types for chat: `text`, `tool`, `plan`, `done`, `error`.

A cabinet turn is hosted by `AiLiveTurnHub` (process memory, one running turn per `sessionId`). `POST /api/ai/chat` and `POST /api/ai/execute-plan` start the turn and subscribe; `GET /api/ai/chat/stream?sessionId=` replays then tails after a reload. Conversation GET payloads include `generating`. `execute-plan` does not persist a confirm user line; it updates the **same** assistant row as the plan (merge tools/timeline, `planExecuted`, and `planFailed` when a step was HTTP 4xx/5xx). The finished assistant row is written on the server when the turn ends (insert or update the last assistant). `POST /api/ai/stop` cancels the hub CTS for that store+user. An API process restart drops an unfinished turn.

## Mapping and errors

`GraphMutationService` remains a **domain** service. Mapping-suggest and explain-error are prompt skills on the same model, not a second tool catalog. Keep `POST /api/ai/mapping-suggest/{id}` and `POST /api/ai/explain-error`.

## Undo

Do not snapshot 84 tool shapes. Prefer Operations / service-level undo where it exists; otherwise the UI says the step is not undoable.

## EF

New tables only via `dotnet ef migrations add`: `aiKnowledgeChunks`, `aiSandboxRuns`, `aiAgentAudits`, `aiChatAttachments`, `aiMcpServers`.
