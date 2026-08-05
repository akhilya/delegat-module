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
- Global store attributes (StoreAttribute)
- Product variants with per-variant price and barcode
- Images (ImageReference) + files (File) stored in MinIO
- Product groups (ProductGroup) with sidebar navigation
- Product folders (ProductFolder) for hierarchical organization
- Notes attached to products
- Excel import/export (ExcelProductService)
- Advanced table: search, filters, sorting, column selector, presets, bulk edit

### Marketplace Integration
- Plugin architecture (DLL, isolated AssemblyLoadContext, hot reload)
- Plugins: Wildberries, Ozon, OpenCart (via bridge)
- Marketplace connections (MarketplaceConnection) with AES-256 encrypted credentials
- Visual field mapping editor (node graph, MappingCanvas)
- Modifier pipeline: regex, template, calculation, value-map
- Category mapping (CategoryMapping) + category import from marketplace
- Two-way product sync (ProductSyncService)
- Price sync (PriceSyncService) and stock sync
- Group sync (GroupSyncService)
- Bulk export/import operations
- SDK-level rate limiting (RateLimitConfig, MarketplaceRateLimiter)

### Multi-tenancy (within an instance)
- Stores (Store) as the primary organizational unit
- Users can be members of multiple stores
- Store switching via selectedStoreId cookie (membership verified against DB)
- All entities scoped to storeId

### Authentication & Security
- JWT in HttpOnly, SameSite=Strict cookie
- Refresh tokens stored in DB with rotation on use
- ASP.NET Identity with account lockout (5 attempts, 15 min lockout)
- **Roles + permissions:** Identity roles (`Owner`, `Operator`, `StoreManager`, `Viewer`) as templates; permission claims authorize endpoints via `[RequiresPermission]` policies
- `User.isAdmin` is an **instance owner marker** only (break-glass / assign owner); API access is not granted by the flag alone
- `roles.manage` is **admin-only**: effective solely for `isAdmin`, never assignable via roles or grants
- `api.access` gates Public API key management UI/API and authenticating with API keys
- Effective permissions = union of role claims + per-user grants; hydrated on each JWT validation from DB
- Store-scoped permissions also require store membership (`selected_store` or `/stores/{id}`)
- Invitation tokens for registration
- CSRF protection via X-CSRF header (CsrfHeaderMiddleware)
- Rate limiting: 300 req/min global, 10 req/min on auth endpoints
- Admin seed via env vars (ADMIN_EMAIL, ADMIN_USERNAME, ADMIN_PASSWORD) → Owner role
- **Public API v1:** API keys (`dk_…`) for ERP/1C/n8n - see [docs/api/public-v1.md](../api/public-v1.md); request activity logged for key owners

### AI Assistant
- Embedded Claude-powered agent via `AiChatPanel` (floating chat, all pages)
- 50+ tools: full platform coverage (products, categories, mappings, export, groups, analytics)
- **Auto-mapping:** on category mapping creation, AI semantically connects adapter fields to local attributes
- **Inline error explanation:** "✦ Объяснить ошибку" button next to sync/adapter errors
- **Confirmation flow:** AI always shows a plan before any mutation; user confirms or cancels
- **Undo:** every AI mutation records a snapshot; "отмени" rolls back the last operation
- **Admin model management:** `/settings/admin/ai` - connect any Anthropic or OpenAI-compatible model
- **Access:** controlled by `AI_ENABLED` env var (tariff feature). When `false`, AI DI services/controllers and UI are not loaded at process start; `/settings/admin/ai` is blocked and `llmGenerate` is hidden in the mapping palette — change the flag and **restart** the instance to enable.
- All prompts in `backend/Services/Ai/Prompts/AiPrompts.cs` - editable without rebuilding services

### Quotas
- `MAX_PRODUCTS` - max products per store
- `MAX_STORAGE_GB` - max storage per store
- `MAX_FILES` - max files per store
- `MAX_CONNECTIONS` - max marketplace connections
- `AI_ENABLED` - whether AI assistant is available (`true` / `false`; unset defaults to enabled for backward compatibility). Toggle requires process restart.
- **Env only** (set by Control Plane on provision / plan change). Not editable in the instance admin UI.
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

### Platform Link (optional)
- Env: `CONTROL_PLANE_URL`, `INSTANCE_ID`, `INSTANCE_LINK_SECRET` (see [platform-link.md](platform-link.md))
- HMAC-signed m2m calls to Control Plane
- Reference Catalog synced into MySQL (`referenceCurrencies`, `referenceTimezones`, `referencePlugins`)
- Instance API: `GET /reference/currencies|timezones|plugins|status` (UI has no hardcoded catalogs)
- 5xx sampled reporting + optional heartbeat
- Empty catalogs until CP sync (first-run wizard can skip and use server defaults)

### Background Services
- ImageDownloadBackgroundService - downloads images from marketplaces
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
- **Current (MVP):** Authenticated admin (or `stores.create`) with zero stores is redirected to `/setup`. Steps: welcome + Platform Link catalog status → create store (name, base currency, timezone from `GET /reference/*`) → select store → open products. If catalogs are unavailable (no Control Plane link or not synced yet), the wizard can skip catalogs and create the store with server defaults (`RUB` / `Europe/Moscow`). Marketplace connection and first sync are deferred (skip in MVP).
- **Still needed (v2):** Marketplace connection wizard, first sync run, invite teammates, bootstrap password change in-wizard.

#### Backup & Recovery
- **Current:** Manual MySQL dump for administrators via Settings → Overview (`GET /settings/database/dump`). Docker volumes have no automated backups; MinIO/object storage is not included in the dump.
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
- Custom color scheme (primary/accent) via env vars.
- Email templates.

---

### Not Applicable for Per-Instance Model

| Item | Reason |
|------|---------|
| Data isolation audits / Global Query Filters | DB is physically isolated |
| Per-store permission rows | Global perms + membership cover 1B; refine later if needed |
| Third-party plugin marketplace | After platform stabilizes |
| Audit logging | Nice-to-have |
| i18n | Russian-language market only |
