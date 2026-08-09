# Changelog

All notable changes to Delegat.Module are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

How we maintain this file: [docs/platform/releasing.md](docs/platform/releasing.md).

## [Unreleased]

### Added

- Administration (Overview): plan quotas vs store usage, overage preview of data that will be permanently trimmed after Control Plane downgrade grace, and Trim now (`/settings/admin/usage` redirects here)
- Public docs deep-links for adapter credentials (Ozon, Wildberries, Yandex Market, OpenCart, WooCommerce) from create-connection and connection settings
- Instance frontend reads docs origin from runtime `DOCS_PUBLIC_URL` (injected by Control Plane at provision into `runtime-config.js`)
- File manager: upload date/time column (store timezone); infinite scroll for store and product file lists
- File manager: Tag action next to Upload (same button style), for selected files

### Fixed

- Browser tab title brand suffix follows the selected store after switch/rename (branding name always from current store; document title sync no longer does fragile string replace)
- Branding logo/favicon asset URLs (`/api/settings/branding/assets/*`) no longer 500: removed `VaryByQueryKeys` (requires Response Caching middleware, which is not registered); client still cache-busts with `?v=`
- Product clone/move (including same-store duplicate) now enforces product, file, and storage quotas on the target store
- Background operations: cascade stop for image downloads cancels InProgress rows; product-transfer Stop updates the queue; abandoned file-upload/file-delete ops are failed after ~20 minutes; ancient running ops time out after 24h
- Operations panel: Stop hidden for request-scoped sync ops that cannot actually cancel; Pause hidden for product-transfer; empty logs no longer claim entries were “cleaned up” while still running
- Client file-upload Stop persists cancelled status immediately; upload HTTP timeout reduced to 15 minutes

- File upload over plan quota: clear message (needed vs free space / file count) and preflight check before upload; no duplicate “Conflict” toasts
- Quota / conflict API errors (409) no longer show two identical toasts (create product, uploads, etc.)
- Users / Stores / Plugins / Categories / Product groups: loading and empty states fill the content height; Users no longer shows a fake empty invite state (empty list is treated as a load error with retry)
- File manager: each file in a multi-upload batch appears in the list as soon as that file finishes (not only after the whole batch)
- File manager (store storage and product files): subtle used/limit storage hint in the path bar (e.g. `2 GB / 4 GB`)
- Store storage: multi-file upload progress stays visible after early successes (refresh no longer remounts the file manager mid-batch)
- Store settings dialog danger zone only on the General tab (was visible on Currencies and Members too)
- AI settings empty state fills the page content height
- Category import empty state fills the right column without a double border
- Store settings dialog: currency add form layout and currency picker restored; member list no longer shows avatar initials
- `MINIO_PUBLIC_ENDPOINT` values pasted with `https://` are normalized to host[:port] so MinIO.NET does not reject the endpoint at startup

### Changed

- Store branding moved to Settings → Stores → store dialog **Branding** tab (removed from platform Overview); sidebar store switcher shows each store’s favicon
- Plan quotas UI lives on Settings → Administration (Overview); dedicated Usage nav item removed (`/settings/admin/usage` redirects to Overview)
- Branding is per selected store (`storeBrandingSettings`) instead of instance-wide; Setup order is Welcome → Store → Branding → Done; brand description, live preview, and editable brand name removed (display name follows the store); Setup branding uses a wider auth card without a clipped scroll strip; logo/favicon dropzones match in size
- AI settings: personal assistant settings moved into a modal opened next to Add model
- New adapter connection dialog uses a compact adapter Select instead of tall radio cards
- Marketplace image exports use `MINIO_PUBLIC_ENDPOINT` / `MINIO_PUBLIC_SECURE` (host[:port] only). Control Plane provisions `storage.{label}.{INSTANCE_HOST_BASE}` and Traefik on MinIO; self-host must set a publicly reachable host or exports fall back to the internal Docker MinIO name

### Security

- Bump `dompurify` to 3.4.13 and npm override `js-yaml` to 4.3.1 (Dependabot GHSA alerts); `nanoid` to 3.3.18 via audit fix

## [1.5.0] - 2026-08-05

### Added
- Per-adapter brand icons (`icon.webp` in each marketplace plugin; mirrored to `frontend/public/adapter-icons`) shown in sidebar, marketplace, plugins, and product adapter lists
- Instance owner can permanently delete a store (type exact name to confirm; last store protected; catalog/adapters/files purged including MinIO)
- Instance branding (white-label): Settings modal and first-run Setup step for name, description, logo, favicon, and primary/accent colors; seeded once from `BRAND_*` env; applied at runtime without rebuild
- Default favicon matches Control Plane (`/logo-square-favicon.webp`); no default logo until upload
- Account menu link to public product docs (`VITE_DOCS_PUBLIC_URL`, default `https://docs.module.delegat.pro`)
- `ADMIN_FORCE_RESET` passthrough in production Compose for first-admin reset
- Product Adapters tab: remembered Export mode (Entire product / Price only / Stock only) per product and adapter in the database; split control runs export on the main click and changes mode via ›
- Mapping canvas: snap-to-grid and alignment guidelines toggles in the bottom-left controls
- Mapping graph: Subtract modifier («Вычесть») for number/integer lanes
- Adapter field modes in the inspector: Don't send / From graph / Bind… with a searchable source dialog (including “same as another adapter field”)
- Product bulk edit runs as a background operation (`product-bulk-update`) with live progress and detailed logs in the operations panel
- Relative timestamps on adapter, Public API, and error log tables (absolute time as secondary)
- Multi-select download (JSON) and bulk delete on adapter, Public API, and error log pages
- WooCommerce: transfer product images as ZIP archives (`POST /images/import-archive`) — host reads MinIO via `IMediaBlobReader`, bridge attaches without GD resize on the hot path (thumbs via Action Scheduler); mappings unchanged; bridge ≥ 1.2.1
- Download full request/response bodies from adapter HTTP and Public API logs when the UI preview is truncated (stored up to 2 MB)
- Admin toggle to enable/disable Public API (`/api/v1`) without restart; API request logging switch is disabled while API is off; API sidebar page and Swagger are hidden/blocked when off
- Connection settings: ⋮ menu with Test, API limits, and Clear cache (refresh dictionaries without waiting for TTL)
- Plugin loader ignores legacy non-`Delegat.Marketplace.*` DLLs; plugin version sync runs after the API is listening

### Fixed
- Frontend Docker build: sync `package-lock.json` with Hugeicons deps so `npm ci` succeeds in CI

### Security
- Frontend npm overrides: `postcss` 8.5.25, `brace-expansion` 1.1.18 (Dependabot alerts)

### Changed
- Local `docker-compose.yml` MySQL image aligned with prod (`mysql:8.4`)
- Frontend production/dev dependency minor/patch bumps (vue-router, axios, eslint, Playwright, and related)
- Aligned default brand name and copy to **Delegat.Module** (Vite env, `index.html`, docs/tests, branding defaults)
- Sonner toasts themed to Catalog Workbench: mount `@/components/ui/sonner` (not raw vue-sonner), flat popover surface + border, Onest, `--z-toast`, compact `--radius-md` (no soft drop shadow)
- Icons switched from Lucide to **Hugeicons** (same kit as Control Plane) via `frontend/src/lib/icons.js`; denser stroke as a filled-look experiment (true solid styles need Hugeicons Pro)
- Mapping editor chrome aligned with DESIGN: fill-viewport canvas, flat node cards / controls, primary selection wires, Spinner loaders, Field forms in settings dialogs
- Product transfer dialog restyled to Catalog Workbench: bordered header/footer, mode Tabs, Field forms, semantic warning/success surfaces, store route strip
- Loaders formalized: single `Spinner` with size scale `xs`–`2xl` (default 16px for actions; `2xl` for EmptyState); ad-hoc `Loader2` + `size-*` replaced across the UI
- Dropdown/context menus: leading icons on actions; destructive items use `variant="destructive"` (Sign-out pattern)
- Destructive confirms and icon-only delete/revoke/unlink controls use destructive color tokens (`AlertDialogAction variant="destructive"`, `text-destructive` triggers)
- Checkboxes unified on shadcn `Checkbox` with `rounded-xs` (MultiSelect no longer uses a hand-rolled nearly-round box)
- Full-region loaders aligned to EmptyState `2xl` (products/trash table, storage, category browser, API keys)
- Dialog chrome unified: title scale `text-base font-semibold tracking-tight`; `DialogHeader` / `DialogFooter` `variant="bar"` for `p-0` dialogs (no per-call `px-6 py-*` / `text-lg` overrides)
- Raw search/`textarea` controls replaced with shadcn `Input` / `Textarea` (MultiSelect, SearchableSelect, mapping context menu, TemplateEditor)
- Forms unified on `FieldGroup` + `SettingsFormField` / `Field` (auth, setup, branding, admin dialogs, catalog create/edit, bulk edit, marketplace connection dialogs); vee-validate `FormField` where validated
- New adapter connection dialog restyled (bar chrome, adapter pick list, credential section, PasswordInput for secrets)
- Store permanent delete surfaced in Stores edit dialog danger zone (was only on orphaned `/settings/admin/stores/:id`)
- Dialog titles are text-only (removed decorative header icons)
- Product group sidebar collapses to a photos-only rail when the edit row is narrow (e.g. wide AI panel)
- Product edit: removed fixed action bar; Save/Delete sit in-flow at page bottom; Duplicate moved to header ⋯ menu
- Settings overview breadcrumb shows «Администрирование» (was «Настройки»)
- App pages share `AppPageChrome` (Page gutters + nav + breadcrumbs) so catalog and settings breadcrumbs align
- Page title/description headers removed; breadcrumbs carry the page name; primary actions stay as a right-aligned toolbar when needed
- Settings overview / user preference rows use Card (`SettingsSettingRow`) instead of hand-rolled bordered divs
- Control Plane docs in this repo are stubs pointing to `delegat-module-platform` (canon lives there)
- First-run setup wizard: step rail, skeleton loading, and skip catalogs when Control Plane is unlinked or catalogs are not ready (create store by name; server defaults for currency/timezone)
- Sign-in accepts email as well as username (input is trimmed)
- Connection settings: Save + actions use a Button Group (Save keeps ⌘/Ctrl+S hotkey; Kbd shown only in menus)
- Platform menus use shadcn `Kbd` / `MenuKbd` for shortcuts via `DropdownMenuShortcut` / `ContextMenuShortcut` `:keys` (not on buttons)
- Mapping canvas: primary control is «Действие» / Action (Button Group); Save is the first menu item with Kbd shortcut
- Mapping editor preview product picker searches the catalog dynamically (was capped at the first 50 with local-only filter)
- Product bulk update persists in chunks of 50 (one DB round-trip per chunk) and logs progress summaries instead of one line per product
- Product file manager context menu shows F2 / Del shortcuts with Kbd

- `PATCH /products/bulk` queues the job and returns `{ queued, operationId, totalItems }` instead of applying updates synchronously
- Export operation logs serialize marketplace error details as readable UTF-8 JSON (no `\uXXXX` escapes for Cyrillic)
- Page shell (`Page`) fills at least the viewport (`min-h-svh`) but does not lock max-height or own the scroll — the main inset scrolls
- Settings pages scroll with the main inset (no nested pane after breadcrumbs); log/API log views opt into fill-height via route meta
- Category import lists fill the page height; adapter category picker matches the create-mapping dialog (shared shadcn path breadcrumbs)
- WooCommerce bulk/single export reads product images from MinIO via host storage (no public HTTP self-download); overlaps storage reads with `products/bulk`
- Page horizontal padding is tighter on Full HD and similar widths (less empty side gutter)
- Log tables share unified column labels (Time, Method, Path, Duration, Status) and support click-to-sort
- Log tables fill the page height with internal scroll; multi-select download/delete actions live in the table footer
- WooCommerce adapter/plugin **1.2.1** + bridge **1.2.1**: bulk/single export prefer archive image transfer (48 MB zip chunks, host MinIO reads); legacy URL drain is fallback only; import-archive zip-bomb guards
- WooCommerce bulk export: default `bulkChunkSize` 50 → 20
- WooCommerce single-product background image drain: one kick only (was up to 40 sequential `images/drain` calls)
- Bulk product export loads related entities with `AsSplitQuery` to reduce DB cartesian load during large queues
- Export queue emits an info log + progress snapshot when a bulk chunk starts (UI no longer stays silent during long adapter HTTP)

### Removed
- Adapter logs page hint banner about enabling HTTP logging (empty state still points to Settings)

### Security
- WooCommerce bridge `images/import-archive` rejects zip bombs (entry count, uncompressed size, compression ratio, path traversal); Delegat caps per-image size when packing archives

### Fixed
- Theme no longer jumps from light Setup to dark Products: color scheme boots once with light default (shared `theme.js`) instead of applying system dark only when the account menu mounts
- Products table shows a loading spinner from first paint (was empty while categories/attributes loaded before the products request)
- Control Plane–linked instances no longer unlock AI when `AI_ENABLED` is unset (fail closed if `CONTROL_PLANE_URL` is set; self-host unset still enables AI)
- Public API PATCH for products, categories, groups, and store attributes now persists (load used `AsNoTracking`, so updates were silent no-ops)
- Auth cookies use HTTPS/`COOKIE_SECURE` instead of `ASPNETCORE_ENVIRONMENT=Production`, so sign-in keeps refresh cookies on HTTP lab hosts
- Product edit: leaving with unsaved changes shows the discard confirmation dialog again (was blocked with no dialog)
- Mappings list product count uses products in the local category (not only marketplace links)
- Mapping canvas no longer freezes for ~1–2s after moving a block (undo history used `JSON.stringify` through Vue proxies on the full graph)
- Product bulk edit: overwriting existing select/multiselect (and other) attribute values now persists — load was `AsNoTracking`, so in-place updates were silent no-ops
- Dialog no longer dismisses when clicking portaled Select / MultiSelect / Popover menus (broke bulk-edit color and other choice fields)
- Product list bulk edit: selection IDs are sent reliably, unique attributes are blocked for multi-select, and API errors show localized messages
- Bulk edit dialog scrolls and matches the platform modal shell (bordered header/footer)
- Product list page size 250 returns 250 items (API was capping at 100); next/last page controls no longer advance past the last page
- Category mapping and import only allow terminal (leaf) marketplace categories with fields; parent/folder ids (e.g. Ozon without `desc:type`) no longer create empty mappings
- Settings overview toggles no longer animate from off→on while loading saved Public API / logging flags
- Operation duration no longer inflated by ~timezone offset (UTC timestamps without `Z` were parsed as local time in the operations panel)
- Stopping an export/import/image-download operation no longer rewrites Failed queue rows to Cancelled (summary and retry-failed stay accurate; progress counters sync on stop)
- Marketplace connection settings return non-secret credentials (e.g. Yandex businessId, Ozon clientId); product edit URLs substitute all template placeholders; saving empty API key no longer wipes the stored secret
- Pre-export mapping resolve uses POST body instead of query string (fix 414 Request-URI Too Large on large bulk selections)
- Yandex bulk export: split offer-mappings/cards requests by JSON body size to stay under Partner API ~500 KB limit
- Wildberries: honor `X-Ratelimit-Retry` (max 1 retry), pause shared limiter on 429; cache dictionaries/category fields/card limits via IMemoryCache; limits dialog no longer blocked when quotas fail
- Wildberries Content API: hold 1 in-flight lease until HTTP finishes (stop colors overlapping cards/list), single-flight global directories per connection, short negative cache on empty directory load, mark 429 retries in adapter logs; directory GETs fail-fast on 429 without pausing cards/list; lease released before retry delay; log duration excludes queue wait
- Wildberries cards/list: accept null/`long`/string/`float` `imtID` via flexible converter (was Int32-only and broke the Products tab)
- Ozon/Yandex/OpenCart: cache category trees, fields, and dictionaries via IMemoryCache; Ozon attribute/values pagination (limit 2000, has_next)
- Image download queue: mark rows Cancelled when the parent operation is cancelled (stop infinite poll of Pending leftovers)

## [1.4.0] - 2026-07-22

### Added
- Dependabot version updates (npm, NuGet, Docker, Compose, GitHub Actions) with grouped weekly PRs
- Admin UI toggles for instance logging (errors, API requests, adapter HTTP) with Save-to-apply; settings persisted in MySQL
- Auto-start Vite on `:5173` when the API runs in Development
- Roles and permissions catalog; Platform Link / reference catalog plumbing
- Folder archive and delete as background jobs; AI explain UI for operations
- API request logs on the API admin page (when logging is enabled)
- vue-i18n (ru default, en fallback) across shell, products, marketplace, settings, admin, AI
- Control Plane architecture docs and MVP brief

### Changed
- Dependabot: version updates limited to minor/patch only (no auto major PRs for Node/.NET/MySQL/npm)
- Further runtime RAM/CPU cuts: MinIO stream buffer cap, InnoDB default 256M, AI gated at startup, logging env/DB toggles
- Settings nav renamed to Administration; log pages hidden when corresponding toggles are off
- Env logging flags (`LOG_*`) become seed defaults only; DB is source of truth after first boot

### Fixed
- CI checkout for Dependabot: fall back when `SUBMODULE_PAT` is unavailable; bump Actions off Node 20 runtimes
- MySQL dump for app users and UTF-8 BOM option file
- Logout handling; `ENCRYPTION_KEY` passed into the API container in production
- Product save gating for required fields; AI model dialog / DialogContent hardening
- Dependabot security bumps (including DOMPurify 3.4.12)
- Frontend minor/patch dependency updates (Vue, Tailwind, ESLint/Prettier tooling)

### Security
- Remediation from 2026-07-17 security audits (uploads, secrets, AI, injection)
- Hardened admin ops: env-only quotas, queue TTL, break-glass

## [1.3.2] - 2026-07-17

### Fixed
- Proxy Swagger static assets through nginx with `^~` priority so `/swagger/*` JS/CSS are not served as SPA 404s

## [1.3.1] - 2026-07-17

### Added
- Public API v1 with API keys for partner integrations
- Yandex adapter manufacturer data fields
- Broader test coverage for Public API auth, store access, import queue, and related helpers

### Fixed
- Enable Public API Swagger in production and polish 404 handling

## [1.3.0] - 2026-07-15

### Added
- Multi-column sorting on the products table
- Adapter HTTP request logs page and instrumentation
- Store timezone formatting for UI datetimes
- Description HTML `<br>` formatting and Yandex additional offer fields

### Fixed
- Yandex filter characteristics, ENUM params, free-text select params, price export rules
- Mapping node collapse/pin persistence across sync
- Product multi-sort query parsing and tiebreaker behavior

## [1.2.1] - 2026-07-14

### Changed
- Patch release (see GitHub compare `v1.1.0...v1.2.1`)

## [1.1.0] - 2026-07-09

### Changed
- Minor release (see GitHub compare `v1.0.1...v1.1.0`)

## [1.0.1] - 2026-07-02

### Fixed
- Patch after initial 1.0.0

## [1.0.0] - 2026-07-02

### Added
- Initial Delegat.Module release

[Unreleased]: https://github.com/akhilya/delegat-module/compare/v1.5.0...HEAD
[1.5.0]: https://github.com/akhilya/delegat-module/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/akhilya/delegat-module/compare/v1.3.2...v1.4.0
[1.3.2]: https://github.com/akhilya/delegat-module/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/akhilya/delegat-module/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/akhilya/delegat-module/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/akhilya/delegat-module/compare/v1.1.0...v1.2.1
[1.1.0]: https://github.com/akhilya/delegat-module/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/akhilya/delegat-module/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/akhilya/delegat-module/releases/tag/v1.0.0
