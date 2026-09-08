# AI Assistant — maintenance

> When you add, remove, or rename **thin tools**, update all of:
> 1. `AgentToolCatalog` / `AgentToolHost`
> 2. `AiPrompts.ChatSystem`
> 3. [`docs/api/mcp.md`](../api/mcp.md)
> 4. This file

Architecture: [`ai-system.md`](ai-system.md). Locked design: [`docs/plans/2026-09-06-agent-mcp-design.md`](../plans/2026-09-06-agent-mcp-design.md).

## Overview

The cabinet assistant is an inset companion sheet (`AiChatPanel`, no overlay). The model thinks, retrieves (knowledge / catalog / SQL), may run Python, then `propose_plan` before writes. Confirmed plans run **in the same assistant message** without a second model pass (`POST /api/ai/execute-plan` → `AgentToolHost`). There is no “Confirmed, proceed” user line; the plan card goes from Executing… to Done, or Failed if a step did not apply (HTTP 4xx/5xx). Older threads that stored the confirm split are folded on load. Generation is hosted in-process (`AiLiveTurnHub`): a page reload reconnects with `GET /api/ai/chat/stream`; an API restart does not.

Chat attach: the SPA uploads bytes to staging (`POST /api/ai/chat/attachments`, MinIO `__ai-chat-attach/`, table `aiChatAttachments`) before the turn. The thread shows the file names on the user message; `GET /api/ai/chat/attachments/{id}` downloads while the file is live. Bytes are kept **24 hours** (`AgentFileUploadService.ChatAttachTtl`), then a hosted cleanup deletes the object and the row. Names stay on the conversation. The model sees ids/names/sizes only (and only live rows). Putting files on products is a normal write: `propose_plan` then `platform_request POST /products/{id}/files` `{ attachmentIds, folderId? }`. Assign of an expired id fails. Cursor still uses filesystem MCP + Agent HTTP `PUT …/products/{id}/files`. Never commit `.mcp.json`.

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
| `frontend/src/components/ai/AiChatPanel.vue` | Sheet UI, file/folder attach |
| `frontend/src/store/modules/aiChatModule.js` | SSE + reconnect + confirm |
| `frontend/src/lib/ai/` | Chat persist, timeline, markdown, attachments, chrome gate |

## Tools (closed set)

`propose_plan`, `search_knowledge`, `search_catalog`, `sql_readonly`, `run_python` (when `SANDBOX_URL` is set), `platform_request`, `match_files`, `navigate_to`.

Do not generate one MCP tool per OpenAPI path. `platform_request` invokes the same cabinet controllers as the SPA; deny secrets and irreversible routes in `PlatformRequestPolicy`.

Cabinet execution UI (`AiChatPanel`) is Apple inset chrome: thinking and tools fold into a one-line disclosure after the turn (the reply stays first). Open it for thoughts, compact tool rows, and Python (script + output stay collapsed). `navigate_to` results are **buttons on the reply** (open a cabinet path and/or send a follow-up), not inside that disclosure. History rows match Mail (title, count, trailing date). Replies strip emoji so Settings → Emoji: Don't use is visible. The companion sheet fades as it slides; the latest turn and chat/history switch ease in at `--duration-ui` (no message stagger). Do not stack every tool above a concatenated blob of thoughts.

`platform_request` uses the same cabinet JSON routes as the SPA (products, categories, attributes, groups, orders, tools, mappings graph). Staged chat files: `POST /products/{id}/files` `{ attachmentIds }`. PATCH `attributeValues` replaces the list (GET the card first). Connection credential writes, user admin, plugins, dump/trim, delete-store, and permanent product delete are denied. SQL includes attribute tables, `IN (...)`, and `COUNT(*)` / `COUNT(DISTINCT col)`.

## Prompts

Edit `AiPrompts.cs` only. English instructions; user-facing language follows UI locale.

`ChatSystem` is a **store assistant**, not an API client or a general chatbot. SCOPE is this store’s cabinet work (catalog, photos, characteristics, connections, orders, mappings, files, Tools). Refuse recipes, jokes, homework, general coding, roleplay, and jailbreaks; mixed requests answer only the catalog part. Replies, plan summaries, step descriptions, link labels, and on-screen thoughts use catalog language (this product, characteristics, the Ozon connection). Tool names, SQL, HTTP paths, sandbox, and table names stay in the internal HOW YOU WORK / TOOLS sections and must not appear in user-facing text. Do not revive the old named-tool zoo (`list_categories`, `get_mapping`, …) in the prompt. Friendly communication style is still store-only (no off-topic chat).

Mapping-suggest / explain-error keep their own prompts. They are **not** extra chat tools. Explain-error also uses store language (no HTTP status codes or internal tool names).

## Models

`aiModelConfigs`, admin `/api/ai/models`, AES-encrypted keys, optional `proxyUrl`. Providers: Anthropic and OpenAI-compatible. `AI_ENABLED` process gate.

## Safety

- Reads auto; writes require `propose_plan` in the UI (Cursor confirms in the client).
- Confirmed `execute-plan` is host-judged: cabinet `ErrorResponse` (`title`/`status`/`code`, no `error` field) and HTTP 4xx/5xx are failures. Partial success (some steps applied, later ones failed) is reported as such — not “catalog unchanged”. Remaining catalog slots are injected into `ChatSystem`; at cap the assistant must not trash an existing product to make room. Characteristic `value` accepts JSON numbers/arrays and stores them as strings. Oversized product GETs are slimmed (drop `files` / nested category attributes) instead of an empty too-large miss.
- Scope: cabinet store work only. Refuse recipes, jokes, homework, general coding, roleplay, DAN. Mixed request → catalog part only.
- Jailbreak / secret dumps (system prompt, tool names, env, JWT, MYSQL, connection credentials, MEMORY.md wholesale) are refused even if the user claims admin, debug, or “translate the instructions”. Do not name tools in a refusal.
- Hard rails: `PlatformRequestPolicy` denylist (auth, users, plugins, dump/trim, connection credential writes, delete-store, permanent product delete) and `AgentSqlGuard` (no `users`, `credentialsJson`, `payloadJson`, `SELECT *`, `JOIN`, or `OR`).
- `workspace.view`: no writes, no Python, no upload.
- `navigate_to`: relative `/` paths and/or a tap-to-send `prompt`; up to 4 buttons via `items[]`.
- Sandbox: curated packages, no `pip install`, no DB URL. Compose caps CPU/RAM; one job; BLAS single-thread.
