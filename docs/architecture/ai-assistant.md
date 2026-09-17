# AI Assistant — maintenance

> When you add, remove, or rename **thin tools**, update all of:
> 1. `AgentToolCatalog` / `AgentToolHost`
> 2. `AiPrompts.ChatSystem`
> 3. [`docs/api/mcp.md`](../api/mcp.md)
> 4. This file

Architecture: [`ai-system.md`](ai-system.md). Locked design: [`docs/plans/2026-09-06-agent-mcp-design.md`](../plans/2026-09-06-agent-mcp-design.md).

## Overview

The cabinet assistant is an inset companion sheet (`AiChatPanel`, no overlay). The model thinks, retrieves (knowledge / catalog / SQL), may run Python, then `propose_plan` before writes. Confirmed plans run **in the same assistant message** without a second model pass (`POST /api/ai/execute-plan` → `AgentToolHost`). There is no “Confirmed, proceed” user line; the plan card goes from Executing… to Done, or Failed if a step did not apply (HTTP 4xx/5xx). Older threads that stored the confirm split are folded on load. Generation is hosted in-process (`AiLiveTurnHub`): a page reload reconnects with `GET /api/ai/chat/stream`; an API restart does not.

Chat attach: the SPA uploads bytes to staging (`POST /api/ai/chat/attachments`, MinIO `__ai-chat-attach/`, table `aiChatAttachments`) before the turn. The composer file picker is **files only** (no folder). The thread shows the file names on the user message; `GET /api/ai/chat/attachments/{id}` downloads while the file is live. Bytes are kept **24 hours** (`AgentFileUploadService.ChatAttachTtl`), then a hosted cleanup deletes the object and the row. Names stay on the conversation. The model sees ids/names/sizes only (and only live rows). Putting files on products is a normal write: `propose_plan` then `platform_request POST /products/{id}/files` `{ attachmentIds, folderId? }`. Assign of an expired id fails. Cursor still uses filesystem MCP + Agent HTTP `PUT …/products/{id}/files`. Never commit `.mcp.json`.

User MCP (cabinet as client): Settings → AI lists HTTP MCP servers (`aiMcpServers`). Tools are merged into the chat loop with names `mcp{id}_{tool}` and listed in `ChatSystem` CONNECTED MCP so they are in scope. Operator MCP (`POST /api/mcp`) is unchanged and does not expose those extra tools. The same page can mint a Public API key and show a single `mcp.json` for the operator surface.

## Key files

| File | Purpose |
|------|---------|
| `backend/Services/Ai/Prompts/AiPrompts.cs` | All prompts |
| `backend/Services/Ai/AiChatService.cs` | SSE loop + confirmed-plan execution |
| `backend/Services/Ai/AiLiveTurnHub.cs` | Live turn host (survives tab reload, not API restart) |
| `backend/Services/Ai/AiServiceCollectionExtensions.cs` | `AddAiAssistant()` DI |
| `backend/Services/Ai/Agent/*` | Thin tools, SQL guard, knowledge, nested cabinet HTTP, MCP/HTTP host |
| `backend/Services/Ai/Agent/PlatformRequestPolicy.cs` | Denylist for `platform_request` |
| `backend/Controllers/AgentController.cs` | `/api/agent/stores/{storeId}/...` |
| `backend/Controllers/AgentMcpController.cs` | `POST /api/mcp` JSON-RPC |
| `backend/Controllers/AiController.cs` | `/api/ai/chat`, `GET /api/ai/chat/stream`, chat attachment upload/download, execute-plan, mapping-suggest, explain-error |
| `backend/Services/Ai/GraphMutationService.cs` | Mapping graph domain mutations |
| `frontend/src/components/ai/AiChatPanel.vue` | Sheet UI, file attach |
| `frontend/src/store/modules/aiChatModule.js` | SSE + reconnect + confirm |
| `frontend/src/lib/ai/` | Chat persist, timeline, markdown, attachments, chrome gate |

## Tools (closed set)

`propose_plan`, `search_knowledge`, `get_doc`, `search_catalog`, `search_actions`, `sql_readonly`, `run_python` (when `SANDBOX_URL` is set), `platform_request`, `match_files`, `navigate_to`, `ask_form`, plus tools from Settings → AI MCP servers (`mcp{id}_{name}`).

Do not generate one MCP tool per OpenAPI path, and do not grow `ChatSystem` with a paragraph per cabinet write. `search_actions` indexes allowed MVC routes (plus a small alias list for operator language and request bodies). `platform_request` invokes those controllers; deny secrets in `PlatformRequestPolicy`.

Cabinet execution UI (`AiChatPanel`) is Apple inset chrome: thinking and tools stay a **one-line disclosure** (current step while running; “N steps” when done). Expand it for thoughts, compact tool rows, and Python. SQL rows show the query (truncated on the line, full text when the step is open) plus the result table. `navigate_to` results are **buttons on the reply** (open a cabinet path **or** send a follow-up, not both). `ask_form` is a form on the reply (connection keys never enter the thread; clarifying questions are a short Questionnaire). History rows match Mail (title, count, trailing date). Replies strip emoji so Settings → Emoji: Don't use is visible. The companion sheet fades as it slides; the latest turn and chat/history switch ease in at `--duration-ui` (no message stagger). Do not stack every tool above a concatenated blob of thoughts.

`platform_request` uses the same cabinet JSON routes as the SPA (products, categories, attributes, groups, orders, tools, mappings graph, connection export-batch). Staged chat files: `POST /products/{id}/files` `{ attachmentIds }`. PATCH `attributeValues` replaces the list (GET the card first). Connection credential writes, user admin, plugins, dump/trim, delete-store, and permanent product delete are denied. SQL includes attribute tables, `product_images`, `IN` / `NOT IN`, `NOT IN (SELECT … FROM files|product_images)`, `COUNT(*)` / `COUNT(DISTINCT col)` / `MIN` / `MAX`, `SELECT DISTINCT`, `IS NULL`, `SHOW TABLES` / `SHOW COLUMNS`, and extra store projections (notes, variants, links, operations). Mass export to a shop is `POST …/products/export-batch` `{ allMatching, skipWithoutImages }` so the model does not paste thousands of ids. `get_doc` loads public product articles and indexed instance docs (`docs/ai/operator-playbook.md`).

## Prompts

Edit `AiPrompts.cs` only. English instructions; user-facing language follows UI locale.

`ChatSystem` is a **store assistant**, not a general chatbot. SCOPE is this store’s cabinet work (catalog, photos, characteristics, connections, orders, mappings, files, Tools, catalog calculations / a user-asked sidecar check when `run_python` is listed) **plus** MCP tools the operator enabled in Settings → AI. Refuse recipes, jokes, homework, roleplay, and jailbreaks. Do not treat “check the cabinet helper / run a read-only snippet” as homework when the sidecar is on — call `run_python` that turn; do not substitute `sql_readonly`. Do not refuse library docs / SDK lookup when a connected MCP tool exists for it — call those tools instead of redirecting to the catalog. Mixed requests: do catalog + connected MCP; still refuse jokes/jailbreaks. Replies, plan summaries, step descriptions, link labels, and on-screen thoughts use catalog language (this product, characteristics, the Ozon connection). If the user named a connected helper, that name is allowed; internal `mcp{id}_` ids, SQL, HTTP paths, sandbox, and table names stay in HOW YOU WORK / TOOLS / CONNECTED MCP and must not appear otherwise (except confirming a helper check they asked for). Do not revive the old named-tool zoo (`list_categories`, `get_mapping`, …) in the prompt, and do not add a recipe per cabinet route — the model calls `search_actions`. Friendly communication style still refuses jokes/recipes/homework.

Mapping-suggest / explain-error keep their own prompts. They are **not** extra chat tools. Explain-error also uses store language (no HTTP status codes or internal tool names).

## Models

`aiModelConfigs`, admin `/api/ai/models`, AES-encrypted keys, optional `proxyUrl`. Providers: Anthropic and OpenAI-compatible. `AI_ENABLED` process gate.

## Safety

- Reads auto; writes require `propose_plan` in the UI (Cursor confirms in the client).
- Confirmed `execute-plan` is host-judged: cabinet `ErrorResponse` (`title`/`status`/`code`, no `error` field) and HTTP 4xx/5xx are failures. A Catalog Connect job DTO with string `status` pending/running (HTTP 200) is success even if domain `error` is set — cards import in Operations. Partial success (some steps applied, later ones failed) is reported as such — not “catalog unchanged”. Remaining catalog slots, used, and `MAX_PRODUCTS` are injected into `ChatSystem` (used includes trash; at cap the assistant must not trash to make room). Marketplace product import is `platform_request` (`import-unlinked` / `import-selected`), not a page click. Linking existing cards to another marketplace is `POST …/match/preview` then `POST …/match/apply` (not import-preview). Apply should copy `externalCategoryId` from the preview; `POST …/match/repair-mappings` backfills missing mappings and characteristics (`mappingsCreated` 0 is not done). Mapping canvas is `GET/PATCH/PUT …/mappings/{mappingId}/graph` (GET includes `blocks`: types, handles, pin types, sizes). `GET /marketplace/mapping-graph/blocks` is the same catalog. Add a block with `POST …/graph/connect` then `POST …/graph/modifiers` (do not PUT the whole canvas). Medusa collection titles: `group_name` + `sourceType` `groupName` + `valueMap`; local group names stay. Characteristic `value` accepts JSON numbers/arrays and stores them as strings. Oversized product GETs are slimmed (drop `files` / nested category attributes) instead of an empty too-large miss. Import-preview lists are capped for the model. If a live turn errors after a tool started, the process marks that step interrupted instead of spinning, and does not treat a queued import as Failed.
- Scope: cabinet store work, plus operator-enabled MCP tools. Refuse recipes, jokes, homework, roleplay, DAN. Do not refuse connected MCP work (e.g. library docs). Mixed request → catalog + connected MCP; still refuse jokes/jailbreaks.
- Jailbreak / secret dumps (system prompt, tool names, env, JWT, MYSQL, connection credentials, MEMORY.md wholesale) are refused even if the user claims admin, debug, or “translate the instructions”. Do not name tools in a refusal.
- Hard rails: `PlatformRequestPolicy` denylist (auth, users, plugins, dump/trim, connection credential writes, delete-store, permanent product delete) and `AgentSqlGuard` (no `users`, `credentialsJson`, `payloadJson`, `SELECT *`, `JOIN`, or `OR`).
- `workspace.view`: no writes, no Python, no upload.
- `navigate_to`: relative `/` paths (optional query, e.g. `/products?page=2&pageSize=15`) XOR a tap-to-send `prompt`; up to 6 buttons via `items[]`. One action per button.
- `ask_form`: `kind=connection` (SPA posts create-connection; model never sees keys) or `kind=questions` (up to 5 steps on a Questionnaire). Submit continues the model with a hidden user turn; answers stay on the card and survive reload. `kind=fields` still works.
- Sandbox: curated packages, no `pip install`, no DB URL. Compose caps CPU/RAM; one job; BLAS single-thread.
