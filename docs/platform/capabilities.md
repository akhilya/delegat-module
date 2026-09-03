# Delegat.Module - Platform Capabilities

## Distribution Model

**Per-instance self-hosting:** each customer receives an isolated Docker stack on their own subdomain.

- Dedicated MySQL, MinIO, backend, and frontend containers
- Stack: `docker-compose.prod.yml` (mysql + minio + delegat-api + frontend/nginx)
- Deploy: Dokploy + GitHub Actions
- Isolation: physical - cross-tenant data leakage is architecturally impossible

> Selling, billing, fleet updates, Ops/Customer consoles, Platform Link **server**: Control Plane repo `delegat-module-platform` (`docs/control-plane.md`). Instance stub: [../architecture/control-plane.md](../architecture/control-plane.md).  
> Infrastructure details (provisioning, scaling, Registry draft): [../architecture/saas-architecture.md](../architecture/saas-architecture.md)

---

## Current Features

### Product Management
- Full CRUD with auto-save
- Categories with typed attributes (Text, Number, Boolean, Select, MultiSelect, Barcode)
- Global store attributes (StoreAttribute) with the same operator-facing value constraints as category attributes (`allowCustomValues`, `maxValues`, `maxLength`, `minValue` / `maxValue`, `validationPattern`). Group / group icon stays on category attributes (product-page grouping)
- Product variants with per-variant SKU, barcode, price, and stock. The Sizes tab is a local category flag (`hasSizes`), not a mapping-graph field. Export injects `_variants` (`id`, `sku`, `techSize`, barcode, price, stock). Wildberries, WooCommerce, and OpenCart consume that bag. Ozon and Yandex Market ignore `_variants`; clothing size is a mapped category attribute (`is_aspect` / distinctive). Plugin meta `_hasSizes` is stripped on category import and never appears on the canvas.
- Images (ImageReference) + files (File) stored in MinIO
- Product groups (ProductGroup) with sidebar navigation
- Product folders (ProductFolder) for hierarchical organization
- Notes attached to products; store-wide Notes page to browse, sort, and filter them
- Excel import/export (ExcelProductService)
- Advanced table: search, filters, sorting, column selector, presets, bulk edit

### Marketplace Integration
- Plugin architecture (DLL, isolated AssemblyLoadContext, hot reload)
- Plugins: Wildberries, Ozon, OpenCart (via bridge)
- Marketplace connections (MarketplaceConnection) with AES-256 encrypted credentials; create tests the adapter first and refuses to save on failure
- Several connections of the same adapter per store are allowed; create/update is rejected if the remote shop is already connected (same Client ID, store URL, campaign, or API key)
- Visual field mapping editor (node graph, MappingCanvas)
- Modifier pipeline: regex, template, calculation, value-map
- Category mapping (CategoryMapping) + background category import from marketplace (Operations)
- Two-way product sync (ProductSyncService). Import stores attribute values in local types (`.` decimals, JSON multiselect); marketplace required flags stay on the mapping graph, not on local category attributes. When the marketplace card has no selling price, import loads it from the prices API (Wildberries content cards never include price)
- Linked products: Import photos (preview picker, append-only gallery). Already-imported URLs can be queued again as extra copies; in-flight URLs are skipped. Image download worker never replaces an occupied gallery slot; file/storage quota failures are not retried.
- Price sync (PriceSyncService) and stock sync
- Live marketplace commerce (pass-through, not stored): current prices with discounts, cabinet orders and status actions, packing labels / barcode stickers (Ozon FBS, Wildberries, Yandex Market), promotions / calendar campaigns / coupons. Public API and JWT; no order tables. Cabinet SPA has live order mailboxes (`/orders/:bucket`) with an inset order sheet and product photos; still no promo screens
- Group sync (GroupSyncService)
- Bulk export/import operations
- SDK-level rate limiting (RateLimitConfig, MarketplaceRateLimiter)
- Adapter job tools: plugins declare `GetTools()`; the instance prefixes ids as `{adapterKey}.{localId}` and runs them through `POST /api/tools/{id}/run`

### Tools
- Tools page (`/tools`) loads `GET /api/tools`: first-party page tools, first-party job tools, plus job tools from loaded adapters. Catalog, prices, promotions, Public API, and each adapter are separate inset lists. Entries carry `stage` (`stable` or `experimental`; experimental is a quiet trailing label).
- Platform page tool: Catalog Connect (`catalog-connect` → `/tools/catalog-connect`)
- Platform page tool: Catalog scrape (`catalog-scrape` → `/tools/catalog-scrape`, stage `experimental`) — public storefront search on loaded Ozon / Wildberries / Yandex Market plugins (no connection required), import selected cards **unlinked**
- Platform page tool: Try Public API (`public-api-try` → `/tools/public-api-try`) — GET `/api/v1/me`, `/stores`, or a custom path with a key (permission `api.access`; hidden when Public API is off)
- Platform job tools: Update all prices (`export-all-prices`, experimental) and Remove products from promotions (`clear-promotion-products`, experimental). Each job is its own inset group (open row + Run now last row, confirm). Catalog quick-run starts the job on every active supporting connection. The job page still picks one connection. Not on Public API. Wildberries cannot clear calendar promotions.
- Adapter jobs: Vue form from credential-shaped `paramsSchema`, then an Operation of type `tool` (table result optional). Plugins do not ship UI.

### Multi-tenancy (within an instance)
- Stores (Store) as the primary organizational unit
- Users can be members of multiple stores
- Store switching via selectedStoreId cookie (membership verified against DB)
- All entities scoped to storeId
- **Hard-delete a store:** instance owner only (`User.isAdmin`); type exact store name to confirm; cannot delete the last store; purges DB orphans and MinIO `{storeId}/` prefix

### Authentication & Security
- JWT in HttpOnly, SameSite=Strict cookie
- Refresh tokens stored in DB with rotation on use
- ASP.NET Identity with account lockout (5 attempts, 15 min lockout)
- **Roles + permissions:** Identity roles (`Owner`, `Operator`, `StoreManager`, `Viewer`) as templates; permission claims authorize endpoints via `[RequiresPermission]` policies
- `User.isAdmin` is an **instance owner marker** only (break-glass / assign owner); API access is not granted by the flag alone
- `roles.manage` and `settings.dump` are **admin-only**: effective solely for `isAdmin`, never assignable via roles or grants. `GET /settings/database/dump` requires `settings.dump` (a full MySQL dump of users, password hashes, API keys, and marketplace credentials). `workspace.view` does not substitute for it.
- Instance owners cannot change their **own** roles or extra permissions, and cannot drop the Owner role from a user who still has `isAdmin` (prevents locking themselves out of Administration → Users). `isAdmin` always keeps the Owner role; a missing Owner role on an owner is restored on the next permission hydrate.
- `api.access` gates Public API key management UI/API and authenticating with API keys
- Effective permissions = union of role claims + per-user grants; hydrated on each JWT validation from DB
- Store-scoped permissions also require store membership (`selected_store` or `/stores/{id}`)
- Invitation tokens for registration
- CSRF protection via X-CSRF header (CsrfHeaderMiddleware)
- Rate limiting: 300 req/min global, 10 req/min on auth endpoints
- Admin seed via env vars (ADMIN_EMAIL, ADMIN_USERNAME, ADMIN_PASSWORD) → Owner role
- **Public API v1:** API keys (`dk_…`) for ERP/1C/n8n - see [docs/api/public-v1.md](../api/public-v1.md); request activity logged for key owners

### AI Assistant
- Embedded Claude-powered agent via `AiChatPanel` (inset companion sheet, no overlay; catalog stays usable while it is open)
- 50+ tools: full platform coverage (products, categories, mappings, export, groups, analytics)
- **Auto-mapping:** on category mapping creation, AI semantically connects adapter fields to local attributes
- **Inline error explanation:** "✦ Объяснить ошибку" button next to sync/adapter errors
- **Confirmation flow:** AI always shows a plan before any mutation; user confirms or cancels
- **Undo:** every AI mutation records a snapshot; "отмени" rolls back the last operation
- **Admin model management:** `/settings/admin/ai` - connect any Anthropic or OpenAI-compatible model
- **Access:** controlled by `AI_ENABLED` env var (tariff feature). When `false`, AI DI services/controllers and UI are not loaded at process start; `/settings/admin/ai` is blocked and `llmGenerate` is hidden in the mapping palette — change the flag and **restart** the instance to enable. Unset: enabled for self-host; **disabled** when `CONTROL_PLANE_URL` is set (Control Plane must emit an explicit `true`/`false`).
- All prompts in `backend/Services/Ai/Prompts/AiPrompts.cs` - editable without rebuilding services

### Quotas
- `MAX_PRODUCTS` - max products per store (create, clone/move, and adapter import)
- Adapter product import stops at the product cap (`product_limit_reached`); already-linked cards are not counted as new products. A batch import (Catalog Connect, import-selected, Catalog scrape, Public API) queues at most the remaining slots; if the cap is hit while a queue is running, leftover rows fail in one pass and are not retried.
- `MAX_STORAGE_GB` - max storage per store
- `MAX_FILES` - max files per store
- `MAX_CONNECTIONS` - max marketplace connections
- `AI_ENABLED` - whether AI assistant is available (`true` / `false`). Unset defaults to enabled for self-host; when `CONTROL_PLANE_URL` is set, unset defaults to disabled. Toggle requires process restart.
- `API_USAGE_ENABLED` - whether Public API (`/api/v1`) usage is included in the plan (`true` / `false`). Same unset rules as `AI_ENABLED`. Lite is off, Pro+ is on. Independent of the admin `PUBLIC_API_ENABLED` toggle — both must be on for partner traffic. When the plan flag is off, `/api/v1` and `/swagger` return `503 API_USAGE_DISABLED` (restart after CP quota push / redeploy).
- **Env only** (set by Control Plane on provision / plan change). Not editable in the instance admin UI.
- **Administration** (`/settings`): shows plan limits vs usage per store; when over quota after a plan downgrade, lists concrete items that will be permanently trimmed (products, connections, largest files) and the Control Plane `OveragePurgeAt` deadline. Admins can **Trim now**; a background worker applies the same selection when the deadline is due (Platform Link required for auto-trim). Legacy `/settings/admin/usage` redirects here.
- Usage is reported on Platform Link heartbeat; entitlements (`overagePurgeAt`, plan name) are cached locally for the UI.
- Adapter HTTP log retention: `ADAPTER_HTTP_LOG_RETENTION_DAYS` (default 7)
- Queue / operations history retention: `QUEUE_RETENTION_DAYS` (default 7); cancelled export/import rows are marked Cancelled so they are not retried forever
- Logging toggles (admin UI under Administration / `/settings`, apply immediately; env vars only seed first boot):
  - `LOG_ERRORS_ENABLED` (default `true`) — API exception rows in MySQL
  - `LOG_API_REQUESTS_ENABLED` (default `false`) — Public API `/api/v1` activity including bodies
  - `LOG_ADAPTER_HTTP_ENABLED` (default `false`) — outbound marketplace adapter HTTP exchanges
  - Stored in `instanceLoggingSettings`; change via `GET/PATCH /settings/logging`
- Public API enable toggle (same Administration page; apply immediately; env seeds first boot only):
  - `PUBLIC_API_ENABLED` (default `true`)
  - Stored in `instancePublicApiSettings`; change via `GET/PATCH /settings/public-api`
  - When off, `/api/v1` and `/swagger` return `503 PUBLIC_API_DISABLED`, Public API request logging is forced off, and the API keys page is hidden from the admin sidebar
  - Plan flag `API_USAGE_ENABLED` (see Quotas) overrides this: when off, traffic is `503 API_USAGE_DISABLED` and the Administration switch is locked

### Platform Link (optional)
- Env: `CONTROL_PLANE_URL`, `INSTANCE_ID`, `INSTANCE_LINK_SECRET` (see [platform-link.md](platform-link.md))
- HMAC-signed m2m calls to Control Plane
- Reference Catalog synced into MySQL (`referenceCurrencies`, `referenceTimezones`, `referencePlugins`)
- Instance API: `GET /reference/currencies|timezones|plugins|status` (UI has no hardcoded catalogs)
- 5xx sampled reporting + optional heartbeat
- Empty catalogs until CP sync (first-run wizard can skip and use server defaults)

### Background Services
- ImageDownloadBackgroundService - downloads images from marketplaces (append-only; quota failures are not retried)
- ProductImportBackgroundService - processes product import queue (PendingProductImport)
- ProductExportBackgroundService - processes product export queue (PendingProductExport)
- FolderJobBackgroundService - folder archive/delete jobs (`pendingFolderJobs`)
- QueueCleanupBackgroundService - deletes terminal queue rows and operations older than retention
- ReferenceCatalogSyncHostedService - pulls Reference Catalog from Control Plane
- PlatformHeartbeatHostedService - optional liveness to Control Plane
- Startup recovery: InProgress tasks reset to Pending on container restart
- (Removed) FileRepairService one-shot MinIO path repair — no longer runs on every boot

### Infrastructure
- .NET 8 + EF Core 8 + MySQL 8.4 (prod compose default InnoDB buffer pool `256M` via `MYSQL_INNODB_BUFFER_POOL_SIZE`)
- Vue 3 + Vite + Vuex (Options API)
- MinIO (S3-compatible) for file storage; buffered `GetStreamAsync` capped at 32 MiB (larger objects use streaming)
- Docker Compose + Dokploy
- Automatic EF Core migrations on startup
- Search engines: `robots.txt` Disallow `/`, HTML `noindex`, and `X-Robots-Tag` on SPA nginx and the API
- Health check: `/api/health` with MySQL + MinIO probes

### Admin Panel
- Store management (CRUD)
- User management (add/remove from store)
- Invitation token generation
- Error log viewer
- Adapter HTTP logs (admin-only)
- AI model management (when `AI_ENABLED`)
- Database dump download (`GET /settings/database/dump`)
- Break-glass admin password: `dotnet Delegat.dll --reset-admin` (or `scripts/reset-admin.sh`) prints JSON credentials to stdout without restarting the API

---

## What's Still Needed

### Critical - before first customers

#### Onboarding / First-Run Wizard
- **Current:** Authenticated admin (or `stores.create`) with zero stores, or a store whose `onboardingCompletedAt` is still null, is redirected to `/setup`. Steps: welcome → create store (name; currency and timezone when catalogs are ready, otherwise server defaults) → branding → adapters (skippable) → Done. Done opens Catalog Connect when an import-capable connection exists. Settings-created stores skip the wizard (`onboardingCompletedAt` set on create).
- **Still needed:** invite teammates, bootstrap password change in-wizard.

#### Catalog Connect
- **Current:** Tools catalog → Connect catalogs (`/tools/catalog-connect`, platform tool id `catalog-connect`). Inactive connections cannot import or match (`connection_inactive`). Empty store: import one active marketplace catalog via `import-unlinked` (category jobs first, then products). After that the **local catalog** is the source of truth; the tool home lists remaining connections to link one at a time by barcode/SKU (not the import source, and not a connection that already links every local product). Extra unlinked cards can be imported from another marketplace without replacing local products. When the local catalog is small, each unlinked article then barcode is looked up on the other adapter; a full remote catalog download is used only when local lookup would be too large. Import listing, match preview, and apply run as background operations with `x / total` on the tool page (adapter `TotalCount` when the marketplace returns it); unlinked products are queued as remote pages arrive. Confirming a link loads those marketplace cards by id in one batch, copies mapped field values into local attributes, imports that marketplace category into the existing local category (or creates a local category when the product has none), and builds a mapping graph for export. Already-linked cards without a mapping are repaired. SPA capability parsing expands `FullProducts` / `Basic` composites from adapter `Enum.ToString()`.

#### Catalog scrape
- **Current:** Tools catalog → Catalog scrape (`/tools/catalog-scrape`, platform tool id `catalog-scrape`). Lists loaded adapters with `SearchPublicCatalog` (Ozon, Wildberries, Yandex Market); a seller `MarketplaceConnection` is optional. Cabinet JWT: `POST /marketplace/catalog-scrape/{adapterKey}/search|apply` (legacy `…/connections/{id}/catalog-scrape/…` still works). Searches the public storefront (Ozon composer JSON, Wildberries `search.wb.ru` / `card.wb.ru` / basket `card.json`, Yandex Market `api/search` + `api/resolve`; HTML JSON-LD if the response is a page). If Ozon/Yandex HTTP is blocked, Chromium (Playwright) loads the public page and the same parsers run on captured JSON or HTML (full Chrome headless via `CATALOG_SCRAPE_PLAYWRIGHT_CHANNEL=chromium`, reused session). Local Windows installs Chromium on first miss (`CATALOG_SCRAPE_PLAYWRIGHT_INSTALL`). Not the seller cabinet. Selected cards are copied into the local catalog without `ProductMarketplaceLink`. When a store connection for that adapter exists, category import still uses the seller dictionaries; apply waits for that mapping, then assigns the local category and copies mapped attributes (storefront options matched by characteristic name). Without a connection, cards copy with no category job. Photos queue as usual; product quota stops the rest of the batch. Not on Public API. Woo / OpenCart do not expose this capability (connected shop). Storefront 429 → `rate_limited`; captcha/antibot after HTTP and Chromium → `storefront_blocked`. Env: `CATALOG_SCRAPE_PLAYWRIGHT`, `CATALOG_SCRAPE_PLAYWRIGHT_INSTALL`, `CATALOG_SCRAPE_PLAYWRIGHT_HEADLESS`, `CATALOG_SCRAPE_PLAYWRIGHT_CHANNEL`, `CATALOG_SCRAPE_PLAYWRIGHT_PROXY`.

#### Backup & Recovery
- **Current:** Manual MySQL dump for the instance owner (`isAdmin` / `settings.dump`) via Settings → Overview (`GET /settings/database/dump`). Docker volumes have no automated backups; MinIO/object storage is not included in the dump.
- **Needed:** Automated MySQL backups (mysqldump on a schedule) + MinIO data. Cron job or sidecar container.

---

### High Priority

#### Monitoring
- **Current:** ILogger only; logs go to container stdout only.
- **Needed:** Sentry for errors (or structured logging via Serilog → Loki).

#### Task History UI
- **Current:** Sync progress is only visible while the page is open.
- **Needed:** "Tasks" page with history of all operations (status, timestamps, errors).

---

### Medium Priority

#### Email Notifications
- Email on sync completion, critical errors, invitations.

#### Performance
- ProductImportBackgroundService: increase batch size (slow for large catalogs).
- DB indexing strategy.

#### Extended Branding
- Runtime white-label **per store** in **Settings → Stores → Branding** tab (logo, favicon, primary/accent; display name follows the store; favicon in sidebar store switcher). Seeded from `BRAND_*` env when a store’s branding row is first created; admin UI is source of truth after that. First-run Setup: store create, then branding.
- Email templates (still future).

---

### Not Applicable for Per-Instance Model

| Item | Reason |
|------|---------|
| Data isolation audits / Global Query Filters | DB is physically isolated |
| Per-store permission rows | Global perms + membership cover 1B; refine later if needed |
| Third-party plugin marketplace | After platform stabilizes |
| Audit logging | Nice-to-have |
| i18n | Russian-language market only |
