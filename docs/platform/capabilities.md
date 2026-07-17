# Delegat.Module — Platform Capabilities

## Distribution Model

**Per-instance self-hosting:** each customer receives an isolated Docker stack on their own subdomain.

- Dedicated MySQL, MinIO, backend, and frontend containers
- Stack: `docker-compose.prod.yml` (mysql + minio + delegat-api + frontend/nginx)
- Deploy: Dokploy + GitHub Actions
- Isolation: physical — cross-tenant data leakage is architecturally impossible

> Infrastructure details (provisioning, scaling, central portal): [../architecture/saas-architecture.md](../architecture/saas-architecture.md)

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
- Invitation tokens for registration
- CSRF protection via X-CSRF header (CsrfHeaderMiddleware)
- Rate limiting: 300 req/min global, 10 req/min on auth endpoints
- isAdmin flag (no RBAC — sufficient for per-instance model)
- Admin seed via env vars (ADMIN_EMAIL, ADMIN_USERNAME, ADMIN_PASSWORD)
- **Public API v1:** API keys (`dk_…`) for ERP/1C/n8n — see [docs/api/public-v1.md](../api/public-v1.md)

### AI Assistant
- Embedded Claude-powered agent via `AiChatPanel` (floating chat, all pages)
- 50+ tools: full platform coverage (products, categories, mappings, export, groups, analytics)
- **Auto-mapping:** on category mapping creation, AI semantically connects adapter fields to local attributes
- **Inline error explanation:** "✦ Объяснить ошибку" button next to sync/adapter errors
- **Confirmation flow:** AI always shows a plan before any mutation; user confirms or cancels
- **Undo:** every AI mutation records a snapshot; "отмени" rolls back the last operation
- **Admin model management:** `/settings/admin/ai` — connect any Anthropic or OpenAI-compatible model
- **Access:** controlled by `AI_ENABLED` env var (tariff feature — see architecture docs)
- All prompts in `backend/Services/Ai/Prompts/AiPrompts.cs` — editable without rebuilding services

### Quotas
- `MAX_PRODUCTS` — max products per store
- `MAX_STORAGE_GB` — max storage per store
- `MAX_FILES` — max files per store
- `MAX_CONNECTIONS` — max marketplace connections
- `AI_ENABLED` — whether AI assistant is available (`true` / `false`)
- Configurable via env vars; editable through admin UI

### Background Services
- ImageDownloadBackgroundService — downloads images from marketplaces
- ProductImportBackgroundService — processes product import queue (PendingProductImport)
- ProductExportBackgroundService — processes product export queue (PendingProductExport)
- Startup recovery: InProgress tasks reset to Pending on container restart

### Infrastructure
- .NET 8 + EF Core 8 + MySQL 8.4
- Vue 3 + Vite + Vuex (Options API)
- MinIO (S3-compatible) for file storage
- Docker Compose + Dokploy
- Automatic EF Core migrations on startup
- Health check: `/api/health` with MySQL + MinIO probes

### Admin Panel
- Store management (CRUD)
- User management (add/remove from store)
- Invitation token generation
- Error log viewer

---

## What's Still Needed

### Critical — before first customers

#### Onboarding / First-Run Wizard
- **Current:** Admin seed via env vars + invitation tokens. No visual wizard.
- **Needed:** Steps: account setup → store configuration → marketplace connection → first sync.

#### Backup & Recovery
- **Current:** Docker volumes with no automated backups.
- **Needed:** Automated MySQL backups (mysqldump) + MinIO data. Cron job or sidecar container.

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
| RBAC | isAdmin is sufficient at this stage |
| Third-party plugin marketplace | After platform stabilizes |
| Audit logging | Nice-to-have |
| i18n | Russian-language market only |
