# Agent, MCP, and platform context

Locked 2026-09-06. English. This is the canon for the instance AI rewrite. It supersedes the 84 named LLM tools in [`docs/architecture/ai-system.md`](../architecture/ai-system.md) and [`docs/architecture/ai-assistant.md`](../architecture/ai-assistant.md).

## Problem

The cabinet assistant was an in-process LLM loop with ~84 C# tools that re-implemented catalog work and could not upload files, read a local folder, run code, search docs, or query the store slice of MySQL. There was no MCP server. Partner Public API v1 stays a separate ERP façade (no files, no mapping graph, no AI).

## Shape

One **operator capability layer** (existing services + quotas + `Permissions.*`). Three clients:

1. SPA (unchanged cookie JWT + CSRF).
2. Agent HTTP (`/api/agent/...`) and MCP (`/api/mcp`) — JWT **or** `dk_…` API key. Not gated by `API_USAGE_ENABLED` / Public API toggle.
3. Python SDK inside a sandbox sidecar — calls Agent HTTP only.

Thin tools (not a generated OpenAPI zoo):

- `search_knowledge` — lexical RAG over a knowledge corpus (docs, MEMORY, PRODUCT, DESIGN, operator OpenAPI).
- `search_catalog` — live products / SKU / barcode / name and product file names in this store.
- `sql_readonly` — `SELECT` against allowlisted projections; `storeId` forced in the host.
- `run_python` — sandbox job; stdout/stderr streamed back.
- `platform_request` — `method + path + json` against an allowlist of operator routes (same actions as the cabinet, permission-checked).
- `match_files` — map local filenames to products (north-star).
- `propose_plan` — UI confirmation before writes; Cursor confirms in the client.
- `navigate_to` — reply buttons: safe in-app path and/or a tap-to-send follow-up.

**Bytes do not go through the model.** Matching may use filenames. Cabinet chat stages bytes, then assigns via allowlisted `POST /products/{id}/files`. Cursor uses multipart Agent HTTP or the cabinet `PUT /products/{id}/files`.

## Auth

- Cabinet UI agent: JWT + `ai.use` + store membership (selected store).
- External MCP / Agent HTTP: Bearer `dk_…` (user must have `api.access` to hold a key) **and** effective `ai.use`. Store from `X-Delegat-Store-Id` or `/stores/{storeId}/...`. Membership required.
- Resource grants still apply (`files.manage` to upload, `orders.manage` to read orders, `catalog.manage` to mutate products, `marketplace.manage` for connections/mappings).
- `workspace.view` only → read-only tools; no `run_python`, no mutating `platform_request`, no upload.
- Denylist (same spirit as the old AI §11): credentials, plugin upload, user admin, API keys, hard-delete, `aiModelConfigs` keys.

MCP is **not** Public API v1. See [`docs/api/mcp.md`](../api/mcp.md).

## Knowledge corpus

Existing MinIO, prefix `__ai-knowledge/` (not store product files, not `MAX_STORAGE_GB`). Source of truth for search is table `aiKnowledgeChunks` (lexical). Reindex on API boot from shipped `/knowledge` (Docker) or repo `docs/` + `MEMORY.md` / `PRODUCT.md` / `DESIGN.md` (dev). Catalog data is **live SQL**, not embeddings. Optional embeddings later if an OpenAI-compatible embed model is configured.

## SQL

Host parses a small `SELECT` grammar. Allowlisted tables/columns only. `storeId` always applied. No MySQL connection string in the sandbox. Forbidden: users, tokens, API keys, `credentialsJson`, `payloadJson` on orders, other stores.

## Python sandbox

Sidecar (`sandbox/python`). Curated packages: pandas, pillow, openpyxl. No `pip install`. Network to the instance Agent HTTP only. Short-lived token. `/tmp` workspace. Audit `aiSandboxRuns`. Compose cgroup 0.50 CPU / 512 MiB / no extra swap; one job at a time; BLAS single-thread. If `SANDBOX_URL` is unset, `run_python` is omitted from the tool catalog (a direct call still returns `sandbox_not_configured`).

## Local files (north-star)

- **Cursor:** filesystem MCP on the PC + instance MCP (`search_catalog` / `match_files` / SQL → Agent HTTP upload).
- **UI agent:** multi-file picker in the companion sheet (files only, no folder); bytes stage on the server (`aiChatAttachments`, MinIO `__ai-chat-attach/`) for 24 hours, then purge. The assistant chooses what to do. Product photos go through `platform_request POST /products/{id}/files` `{ attachmentIds }` after confirm — not a dedicated browser upload step. The sandbox never sees `C:\Users\...`.

## UI

Keep the companion inset sheet (no overlay). Stream thinking as today (`text` / `tool` / `plan`). Attach chip on the composer. Mapping-suggest / explain-error remain prompt skills on the same model, not a second tool zoo.

`GraphMutationService` stays a domain service. Old `Tools/Handlers/*` and `ToolDispatcher` are removed from the product surface.

## EF

New tables via `dotnet ef migrations add` only: `aiKnowledgeChunks`, `aiSandboxRuns`, `aiAgentAudits`, `aiChatAttachments`, `aiMcpServers`.

## Amendment (2026-09-06): cabinet controllers, not a second CRUD layer

`platform_request` is still one thin tool (`method + path + json`). It must not grow a handwritten handler switch. The host invokes the same JWT cabinet controllers as the SPA (in-process nested pipeline, store cookie, CSRF header, permission hydration). A denylist blocks secrets (connection credential writes, API keys, auth, users, plugins, AI model keys), `/api/ai` / `/api/agent` / `/api/mcp` / `/api/v1`, dump/trim, delete-store, and permanent product delete. PATCH `attributeValues` follows the product form (replace, not merge). MCP `dk_…` is minted into a short-lived JWT because those controllers are JWT-only.

## Amendment (2026-09-08): cabinet as MCP client

Settings → AI stores Streamable HTTP MCP servers (`aiMcpServers`, optional bearer). The cabinet chat loop lists `tools/list` and calls them as `mcp{id}_{sanitized}` (max 20 servers, 32 tools each). Those tools are in the chat **scope** for the job they describe (not “catalog only”). That is not operator `POST /api/mcp` — extra tools are not advertised there. No stdio (the API runs in Docker).
