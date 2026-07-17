# Public API v1 — Design

**Goal:** Partner REST API for ERP/1C/n8n with API keys: catalog CRUD + marketplace sync.

**Architecture:** Separate façade `/api/v1` over existing services and background queues. SPA `/api/*` (JWT cookie + CSRF) unchanged. No MCP; AI `ToolDispatcher` is not the API surface.

## Decisions

| Topic | Choice |
|-------|--------|
| Audience | External partner integrations |
| Scope | Full contour: catalog + import/export/prices/stocks |
| API keys | Bound to user; access = store memberships |
| Store selection | Path `/api/v1/stores/{storeId}/...` |
| Long operations | Always `202` + poll `operations/{operationId}` |
| Approach | New Public API façade (not expose SPA routes, not wrap AI tools) |

## Why not MCP / AI tools

The product has no MCP server. `mcps/` is Cursor IDE cache. AI tools share domains but are LLM-shaped (JWT + SSE). Reuse services, `PendingProduct*` workers, and the `operations` table.

## Auth

- Table `userApiKeys`: hash (SHA-256), prefix, last4, userId, name, timestamps, revokedAt
- Key format `dk_…`; plaintext shown once on create
- `Authorization: Bearer dk_…`
- CSRF skipped for `/api/v1`
- Inactive user or revoked key → 401; no membership → 403
- Key management via JWT SPA: `/api/api-keys`

## Surface

- Meta: `GET /api/v1/me`, `GET /api/v1/stores`
- Catalog: products, categories, groups, store-attributes under `.../stores/{storeId}/...`
- Connections (no secrets), product-links, operations list/get
- Sync starters → `202` + `operationId`

**Out of v1:** field-mapping graph, AI, plugins, admin/invites, webhooks, key scopes.

## Response conventions

- Pagination: `{ items, page, pageSize, total }`
- Errors: `{ error: { code, message, details? } }`
- Async: `{ operationId, status, links: { self } }`

## Reuse

- Sync enqueue mirrors `ProductSyncController` → pending queues + `operations`
- Catalog: thin controllers; extract shared logic only when duplication hurts
