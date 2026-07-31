# Changelog

All notable changes to Delegat.Module are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

How we maintain this file: [docs/platform/releasing.md](docs/platform/releasing.md).

## [Unreleased]

### Added
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

### Changed
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

[Unreleased]: https://github.com/akhilya/delegat-module/compare/v1.4.0...HEAD
[1.4.0]: https://github.com/akhilya/delegat-module/compare/v1.3.2...v1.4.0
[1.3.2]: https://github.com/akhilya/delegat-module/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/akhilya/delegat-module/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/akhilya/delegat-module/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/akhilya/delegat-module/compare/v1.1.0...v1.2.1
[1.1.0]: https://github.com/akhilya/delegat-module/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/akhilya/delegat-module/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/akhilya/delegat-module/releases/tag/v1.0.0
