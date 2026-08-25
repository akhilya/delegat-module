# Changelog

All notable changes to Delegat.Module are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

How we maintain this file: [docs/platform/releasing.md](docs/platform/releasing.md).

## [Unreleased]

### Added

- Sign-in: TOTP second factor after password, recovery codes, and passwordless passkeys (Bitwarden / 1Password / Windows Hello)
- Extra permission `workspace.view`: browse the cabinet without changing data (plugins stay closed). Assign on a Viewer who is a store member for a demo account.
- User settings: authenticator app (QR + recovery codes) and passkey register/remove
- Instance dashboard asks search engines not to index it (`robots.txt`, HTML `noindex`, `X-Robots-Tag` on the SPA and API)
- Tools: Try Public API (`/tools/public-api-try`) sends GET `/me`, GET `/stores`, or a custom `/api/v1` path with a partner key and shows the response
- Public API v1: product size variants (`GET` product includes `variants`; list/create/patch/delete under `/products/{id}/variants`)
- Public API v1: create category accepts `hasSizes`; category list attributes include `options` and the other GET-detail fields
- Public API v1: product list `search` matches barcode, variant SKU/barcode, and marketplace ids (same as the catalog), and items include `variantCount`
- Catalog Connect import, match preview, and apply run as background operations with `x / total` progress on the tool page (survives reload and in-app navigation). Remote catalogs are paged in bulk and unlinked products are queued as pages arrive.

### Changed

- Public API `PATCH` product now replaces `attributeValues` / `storeAttributeValues` when those arrays are sent (same as the dashboard)
- Public API create store-attribute copies `options`, `defaultValue`, and `description`
- Public API sync starters reject inactive connections (`400 connection_inactive`)
- Public API error `code` values for missing products, categories, variants, store attributes, operations, and several conflicts (`sku_already_used`, `category_has_products`, …)
- Mapping canvas zoom in/out uses the same plus and minus icons as the rest of the app
- Changing your password (and setting one in Administration → Users) asks you to type the new password twice
- Mapping auto-arrange uses Dagre, then stacks card fields above price fields so the two sinks no longer overlap
- Mapping canvas toolbar is button groups: Save + more, a wider preview-product picker that can open the product, and Reset
- Buttons use the same Control Plane lift: a short white top sheen and a hairline drop shadow (link stays flat; ghost is shadow-only)
- Product create Properties uses the same Apple Settings sections as product edit (no Cards / heading mix)
- Dropdowns, context menus, selects, popovers, and tooltips use the same Apple menu material (frosted paper, hairline ring, denser rows) instead of a bordered `shadow-md` panel
- Overlays and menus share one motion curve (`ease-out-quart`): sheets open in 280ms (was 500ms), dialogs 200ms, menus 150ms; buttons press-scale; tab panels fade. Respects reduced motion
- Menu radio/checkbox rows and select lists use a trailing check (Apple picker), not a leading radio disc
- Product Notes tab is an inset grouped list like store notes: a row opens the bar sheet, Add note is the last row, and the tab grows with the page instead of stacked writing pads
- Product Sizes tab is one inset group per size (label-left fields, Add size as the last row) and grows with the page instead of a nested table pane
- Product SKU on Properties uses caption actions under the field (change on the left, copy on the right) instead of icon buttons beside the input
- Product barcode generate is a caption action under the field (same as SKU / group), not a dice icon beside the input
- Product Adapters tab lists each connection as a spacious inset group: listing id/SKU, price, stock, and last sync sit in composed Apple rows instead of one fact per line. The tab grows with the page instead of scrolling inside a nested pane
- Product Adapters tab: left-click on a connection card opens the actions menu at the cursor (same as right-click); mapping, marketplace link, sizes, and ⋮ keep their own actions
- Product Images tab uses compact photo tiles with a drag handle and dashed drop trays (on the card / not on the card)
- Product category attributes Compare layout scrolls with the page instead of a nested pane
- Administration → Users is an inset grouped list (row opens the editor). Invite tokens are inset groups in the invite dialog, not a table
- Administration → Stores is an inset grouped list (row opens store settings). Create and edit dialogs use in-flow bar close; the current store is marked in the caption
- Catalog Connect keeps the local catalog as the source of truth after the first import: the tool stays open to link remaining marketplaces one at a time, and extra unlinked cards can be imported from another connection without replacing existing products
- Form field labels, legends, and descriptions use the same small caption size as Apple group headers (not a larger heading under «Credentials»). Category import section titles (adapter category, where to import, attributes) use that same Settings caption — they no longer mix a browser legend with a group header
- Category import no longer repeats the page title and intro above the two columns; Import sits at the bottom of the destination column
- Categories, product groups, global attributes, and their edit pages use Apple inset grouped lists and Settings form sections (attribute / add-product dialogs included)
- Barcodes use the same Apple table ring and combined footer as products (selection + download in the footer). Store notes are an inset grouped list; the note sheet uses bar chrome and Settings fields (not a writing pad)
- Adapters, connection settings / mappings, category import, and mapping preview chrome use the same Apple inset grouped lists and sheet bars as Settings (the mapping graph canvas is unchanged)
- File manager: content type is a wider column so MIME types are not clipped; tags size to the chips instead of a fixed width
- Operations list: each job is its own inset group; status is the trailing icon only; Copy / AI / Retry / Delete after expand are compact rows
- Background operations open as an Apple-style sheet: bar header, inset grouped Active / History lists, and the platform overlay scrim

### Removed

- Pause / Resume on background operations (jobs are running or stopped)

### Fixed

- Collapsed product-group rail: hovering a variant photo opens the preview beside the tile, not in the top-left of the screen
- Right-clicking another product closes the previous row menu (only one context menu at a time)
- Public API `GET` category now returns a real `productsCount` (detail always showed 0 because the product collection was not loaded)
- Public API request logs keep a full rounded hairline frame, including the “turn on Public API logs” hint (the fill-height panel had clipped outer rings)
- Turning on “Products have sizes” on a category is kept after save (the flag was stored, then dropped from the category GET)
- Mapping preview-product picker sits on an opaque card so the canvas grid does not show through the field
- Product Adapters tab shows the adapter icon and name on each connection card again
- Nested dropdown and context-menu panels (user language, adapter connection submenus) open beside the parent instead of being clipped by the frosted menu
- First WooCommerce and OpenCart export of product sizes now sends variations (local variant id); WooCommerce later export stores the marketplace variation ids
- Wildberries card update applies per-size prices from the Sizes tab
- Creating a product group from product properties shows the group name in the picker instead of its id; Create group stays only in the dropdown
- Revoking an invitation token from the invite dialog no longer closes that dialog before the confirm appears
- Catalog Connect refuses inactive connections (`connection_inactive`) and hides them in the tool
- Adding products to a group moves them out of another group instead of returning 400
- Delete on an error log sheet opens the confirm from the header trash control (it no longer sits under Close, and no longer uses a `delete` event that Vue can ignore)
- Inset grouped lists keep a full hairline ring when they scroll or sit in a clipped panel (category import picker and other inset catalogs)
- Opening an adapter connection (and a mapping row) from an inset list: the row click is no longer swallowed by a nested delete / menu button
- Sheet close (operations, logs, and other side sheets) lines up with the title row and header actions
- AI chat FAB and side panel follow `/auth/me` after sign-in or Setup; they no longer stay hidden when the shell first loaded on a public/bare page
- Product tables on a connection, Catalog Connect / scrape result tables, adapter tool job tables, and log tables show a hairline border frame again
- Stop on a background job takes effect in the panel immediately and cancels remaining queue work; an in-flight marketplace call for the current item may still finish. Stop is available for Catalog Connect and catalog scrape. One-shot request-scoped sync (single product, prices, stock, Excel, adapter tools) still hides Stop because that HTTP call cannot be aborted.

## [1.6.0] - 2026-08-18

### Added

- Product edit: unsaved-changes confirm can save and leave; three actions stack full-width (Apple-style), two-action confirms stay side by side
- Category attribute groups can have an icon (marketplace brand or a symbol); category import assigns the adapter icon to the auto-created marketplace group
- Product category attributes: switch to a side-by-side compare view (marketplace groups as columns, similar fields on one row)
- Linked products can import marketplace photos with a picker (Adapters menu); already-imported photos can be downloaded again as extra copies; existing gallery photos are not replaced or deleted; picker tiles show the full photo
- Product groups: select rows and bulk-delete from a table footer (same chrome as products and logs)
- First-run Setup adapter step: add one or more marketplace API keys (testable, skippable); wizard resumes after store create until Done
- Creating a marketplace connection tests credentials against the adapter first; a failed test does not save the connection (Setup and Adapters)
- A store may have several connections of the same adapter, but not two that point at the same marketplace shop (same Client ID, store URL, campaign, or API key)
- Tools catalog (`GET /api/tools`): platform page tools plus job tools from loaded adapters (`{adapterKey}.{id}`); adapter jobs run with `POST /api/tools/{id}/run` (Operations type `tool`)
- Catalog Connect tool (`/tools/catalog-connect`): import one marketplace catalog as the local source of truth, then bulk-link existing cards on other marketplaces by barcode or SKU
- Catalog scrape tool (`/tools/catalog-scrape`): search a marketplace public storefront (Wildberries in v1), tick cards, and copy them into the local catalog without linking; a later export creates new listings instead of updating someone else’s card
- Category import copies marketplace field limits (max length, min/max number, max selected values, regex) onto local category attributes; the product form enforces them
- Category import does not create attributes for marketplace barcode / EAN / GTIN fields; the mapping graph binds them to the product barcode
- When several adapter fields auto-bind to the same product field (e.g. `name` and `product_name`), the mapping keeps one primary binding and merges the rest as “same as another adapter field”
- Select and multiselect category attributes can cap how many values are selected (`maxValues`); category import copies it from marketplace fields (Wildberries `MaxCount`, Ozon `MaxValueCount`), and the product form blocks extra selections
- Select and multiselect category attributes can accept typed custom values (Wildberries characteristics, Yandex `allowCustomValues`); category import copies the flag, and the product form offers Add for a value that is not in the list
- Honor Control Plane `API_USAGE_ENABLED` (Lite off, Pro+ on): `/api/v1` and `/swagger` return `503 API_USAGE_DISABLED` when the plan does not include API; Administration lock and hide API keys until upgrade
- Sidebar warning icon on Settings / Administration when the instance is over plan quota (pending trim)
- Products table **Adapters** column: ButtonGroup of store connections with status colors; each opens a sync/link menu (export, import, verify, unlink, open on marketplace / Adapters tab)
- Store-wide Notes page (`/notes`): browse product notes with sort (importance, date, product) and filters (importance, author, unified search on note text / product name / SKU); note authorship and timestamps tracked on create/update; row opens a detail sheet to view/edit/delete the note and jump to the product
- Administration (Overview): plan quotas vs store usage, overage preview of data that will be permanently trimmed after Control Plane downgrade grace, and Trim now (`/settings/admin/usage` redirects here)
- Public docs deep-links for adapter credentials (Ozon, Wildberries, Yandex Market, OpenCart, WooCommerce) from create-connection and connection settings
- Instance frontend reads docs origin from runtime `DOCS_PUBLIC_URL` (injected by Control Plane at provision into `runtime-config.js`)
- File manager: upload date/time column (store timezone); infinite scroll for store and product file lists
- File manager: Tag action next to Upload (same button style), for selected files
- Mapping editor shows staged loading text (adapter dictionaries, graph layout) instead of a silent spinner

### Changed

- Product SKU change dialog matches other form dialogs (Field stack, no extra padding); current SKU can be copied
- Administration → Users: edit-user dialog uses the same bar chrome, Field stack, and inset grouped lists as other Apple-style form dialogs; roles and extra permissions use trailing checks, not a primary fill on every selected row
- Administration (Overview) uses inset grouped lists: plan usage is a Settings-style list, not progress cards
- Settings pages (User, Stores, Users, API, Plugins, AI) use the same Apple inset grouped lists, Field stacks, and table rings as Administration
- App sidebar uses inset grouped lists (space between sections; parent and child rows share one outline and the same row height); the current page is a primary ink pill
- Tools catalog, Catalog Connect, and adapter job tools use the same inset grouped lists (not a grid of cards); selected connection is a primary ink pill
- Multi-select shows selected values as pills when they fit on one line, otherwise the count summary
- Form dialogs, side sheets, and the product photo lightbox use the same dimmed overlay (`bg-black/50`) as confirmation alerts
- Catalog, logs, files, and import-preview tables use the shared Table primitive (sticky lists keep the page scroller)
- Adapter connection **Create local** import dialog uses the same bar chrome and Field stack as other platform forms (category radios, full-width category picker)
- Confirmation alerts (delete, unlink, trash) use the platform Apple-style pause: more padding, centered copy, equal-width Cancel / Confirm
- Catalog Connect review table has a header checkbox to select or clear all visible rows (matched links and unmatched import-as-new)
- Catalog Connect match table has column headers (local product vs marketplace connection)
- Catalog Connect: when local products already exist, match by barcode/SKU instead of importing the other marketplace as new products; confirming a link imports that adapter’s category (or creates a local category when the product has none) and builds a mapping so export is not blocked
- Catalog Connect recognizes adapter capabilities reported as `FullProducts` / `Basic` (Wildberries, Ozon, Yandex Market), so connections no longer look like they have no import
- File manager: the name column takes leftover width; MIME/content type stays a compact truncated chip (full type on hover)
- Store edit dialog keeps a stable height when switching General / Branding / Currencies / Members
- Create-store dialog uses the same bar chrome and field stack as first-run Setup (name, currency, time zone, members switch)
- Product group create dialog uses the same bar chrome and field stack as create store; product edit/create can open it from the group field and assign the product without leaving the page
- First-run Setup is a full-viewport assistant: welcome, then store, look, marketplace, done. Connection status and catalog internals stay off the screen; failed marketplace tests still block save.
- Sign-in, sign-up, and the unavailable screen share the same full-viewport canvas as Setup (large title, no card).
- Setup wizard keeps the same column width on every step (store, look, marketplace, done).
- Setup wizard no longer flashes the previous step when Continue advances (stable chrome, instant leave, fade-in only).
- Products table reuses the last list/trash page for ~60s when navigating back from a product (avoids re-fetching large pages; list paints from cache without waiting on categories/attributes); cache clears after delete/restore/import/bulk/adapters changes
- Product image hover preview (same as products table) on product group members table, add-to-group picker, and group variants sidebar
- File manager share dialog rebuilt to Catalog Workbench patterns: Field stacks, dialog description for the item name, expiration help text, and a dedicated link field with copy
- Products table row menu: separate **Clone to store** and **Move to store** actions; transfer dialog opens locked to that mode (no clone/move tabs)
- Setup store and look steps submit through HTML forms (Continue is a real submit); marketplace add-connection uses a form too
- Create/edit fields that map to database string columns cap length in the UI (store name 40, product name 100, and the rest of the same backend limits) so overlong input never reaches a server error first
- Category import (from the category page and when importing products) runs in the background; the UI returns immediately and progress is in Operations
- Product import queues unique category jobs first; products wait until the marketplace category is mapped (or fail if that category job is exhausted)
- Catalog Connect confirm loads the chosen marketplace cards by id in one batch, copies mapped field values into local attributes, and shows live progress on the page and in Operations

### Fixed

- Catalog scrape copies the public card into a local category after the marketplace category job finishes (name, SKU, price, and mapped attributes including storefront options such as color), instead of creating the product with an empty category
- Catalog scrape: a Wildberries storefront 429 is returned as `rate_limited` (retry toast) instead of an unhandled 500
- Adapter connection menu: mapping picker uses the same row layout as other actions (icon, spacing, trailing check) instead of a radio list
- Catalog Connect no longer tries to match the same marketplace that was just imported as the source of truth (it skips that connection and fully linked catalogs, then looks up the others — e.g. Ozon import then Wildberries)
- Catalog Connect match looks up the other marketplace by local article/barcode when there are few unlinked products, instead of paging that marketplace’s whole catalog (e.g. ten local cards vs every Wildberries nmId)
- Catalog Connect empty-store import and “import as new” now queue marketplace categories first (same as import-selected), so products get a local category and mapping graph; linking a second adapter also creates a category when the local product has none, and already-linked cards without a mapping are repaired
- Catalog Connect / batch product import no longer keeps processing hundreds of leftover rows after `MAX_PRODUCTS` is reached (remaining queue fails in one pass; new batches enqueue only the free slots)
- Error logs and other settings tables show their outline again (`overflow-hidden` had clipped the ring)
- Instance owners can no longer remove their own roles or extra permissions (that dropped `users.manage` and locked them out of Administration → Users)
- File manager last row shows a bottom rule when the list does not scroll
- On narrow screens, AI and background-job buttons sit in the top bar instead of reserving a right gutter for floating actions
- Wide viewports (above 2000px, including 2K) keep 8vw page gutters so catalog tables do not stretch edge to edge; Full HD stays tight
- Sidebar Trash link shows again (it was dropped as a duplicate of Products because both use `/products`)
- Products table last row (and the actions column) shows a bottom rule when the list does not scroll; row height stays even with a single product
- Product photo lightbox close, prev/next, and the 1 / N index sit on paper chips so they stay readable on the photo
- Sidebar groups show their child links again (catalog pages, adapter connections, tools, settings). The grouped-list restyle had collapsed some lists and clipped others.
- Products table (and other catalog tables) show names and cell text again; converting them to the shared Table had left empty white rows
- Products table Adapters column import now shows the same field-change preview/confirm dialog as the product Adapters tab (it applied immediately before)
- Adapter import preview no longer treats the same select/multiselect as a change when one side is labels (`другу, любимому`) and the other is JSON or marketplace dictionary ids (`901542` vs `Серый`); the dialog shows option labels
- Adapter product import writes numbers with a dot (`1.5`, not `1,5`), booleans as `true`/`false`, and multiselect as JSON; Wildberries package size/weight and brand are copied onto the product; marketplace “required” flags stay on the mapping for export and are not copied onto local category attributes (a card often omits those fields)
- Folder archive download from Operations no longer stops after 30 seconds (the API client timeout); the browser downloads the ZIP directly
- Adapter product import no longer exceeds `MAX_PRODUCTS` (same cap as create); already-linked re-imports do not consume a slot
- Wildberries product import no longer fails when `nmID` is larger than a 32-bit integer (or sent as a string)
- Importing many products with unmapped categories no longer hits the 30s HTTP timeout; category import no longer runs inside the request
- Setup wizard language switcher sits in the top-right corner instead of crowding the step progress dots
- Marketplace product and category import clip overlong names (and related fields) to column limits instead of failing with a database “Data too long” 500
- Setup and Adapters stop offering another connection after the plan connection quota is reached (`MAX_CONNECTIONS`)
- Creating a store with a name over 40 characters is blocked in Setup and Stores; create-store API returns 400 `store_name_too_long` instead of a database error
- Sign-up / sign-in username hint and charset errors no longer throw in the browser (`@` in the allowed-character list was parsed as a vue-i18n linked message)
- Sign-up / sign-in: clearer validation errors for username charset (Latin / allowed symbols), password policy, duplicate username, and account lockout (instead of a generic “Bad request”)
- File manager: empty-row Tag control matches Share icon color; permanent public shares can be revoked from the share dialog (and via context menu)
- File manager: tag assign control is available on every file row again (not only when the file already has tags)
- Products in trash: Adapters column stays visible but disabled; API rejects marketplace export/import/link/unlink/verify/price/stock ops for soft-deleted products
- Administration Overview: Public API section shows the same section title/description typography as Logging and the other blocks
- Category import: section titles (Adapter category, Where to import, Attributes to import) use shadcn FieldLegend / FieldDescription at label scale under the page heading
- Create mapping dialog (adapter page): fixed tall height, spacing under the header before Mapping name, single bottom border (no double rule with footer), and adapter category search runs automatically after 1s idle
- Adapter product import fills local price from the marketplace prices API when the product card itself has no price (Wildberries content cards, and Ozon/Yandex when the offer payload omits it); field re-import does the same
- File manager: dragging a file/folder into another folder updates the current list immediately (no page reload)
- Products table: **Move to store** is disabled when the user has only one store membership; API rejects `mode=move` when the instance has a single store
- Product **Clone** dialog skips the target-store picker when only one store is available (same-store duplicate)
- Product **Clone** can skip copying files, folders, and gallery images (`copyFiles` flag; move always copies)
- Background operations: empty Image download ops pre-created with import no longer stay running forever when no images were queued (closed when parent import finishes / on worker poll / queue cleanup)
- User menu version no longer shows `vdev`: CI `latest` images bake SemVer from `VERSION`; API `/health` reports InformationalVersion; UI prefers health over a bake-time `dev` placeholder
- Product group sidebar (variants rail): empty product images show the same placeholder icon as the products table
- Products table Adapters column menu respects category mapping status (blocks export / shows configure when mapping is missing), matching the product Adapters tab
- Products table Adapters buttons show mapping-missing (red) from the list payload, not only after opening the cell menu
- Product Adapters tab and products-table Adapters column share one connection actions menu component
- Product Adapters tab: right-click on a connection row opens the actions menu at the cursor (same items as the ⋮ button; native menu kept for external marketplace links)
- Product Adapters tab: parallel exports to different connections keep their own loading overlays (no longer clearing the first spinner when starting another)
- Required category and store attributes always stay visible on the product page; “show on product page” cannot be turned off while the field is required
- Invite token revoke confirm works when opened from the invite dialog (AlertDialog no longer dismissed as “outside” the parent Dialog)
- App shell no longer shows a dead body scrollbar beside the real page scroll (`body` is `overflow-hidden`; only `SidebarInset` / bare-layout wrapper scroll)
- Ozon product import omitted category (and price/attributes/images/brand/barcode) on `ExternalProduct`, so “create category” during import produced an empty local «No category» folder without attributes or mapping; mapping now receives real Ozon `description_category_id:type_id`
- Import dialog no longer treats the i18n “uncategorized” label as a category create name; creating a category for products without an adapter category requires a typed name
- WooCommerce product get returns primary `category_id` / `category_name` so import can create mappings when available
- Products table Adapters column ignored list enrichment (`ProductReviewDTO` is a struct), so linked imports looked mapping-missing (red) until the cell menu opened
- Category import created attributes with “show on product page” off; new imports default to on
- Marketplace export link binding: Ozon/Yandex keep the linked external id (Ozon uses the linked card's `offer_id` for `/v3/product/import`; Yandex prefers stored `offerId` over local sku). Silent rebind to another remote id is blocked; Ozon link/relink accepts product_id, Ozon SKU, or article and stores canonical `product_id`
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

- Product list search: name and codes (SKU, barcode, marketplace external id/SKU, variant codes) are substring; product id is exact; exact code/id hits rank above partial matches
- Products table: hover preview on product images (short delay); right-click row opens the same actions menu as the Actions column
- Notes page: single top search covers note text, product name, and SKU (removed separate product filter field)
- Context menus (products, trash, group variants, file manager) use section labels and separators like adapter connection menus
- Content images show a Spinner while loading (catalog thumbs, galleries, lightbox, branding assets, mapping previews)
- Adapter maturity: Ozon, Wildberries, and Yandex Market marked Stable; OpenCart and WooCommerce marked Beta (was Experimental)
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
- Account menu link to public product docs (`DOCS_PUBLIC_URL` / `VITE_DOCS_PUBLIC_URL`, default `https://module.delegat.pro/docs`)
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

[Unreleased]: https://github.com/akhilya/delegat-module/compare/v1.6.0...HEAD
[1.6.0]: https://github.com/akhilya/delegat-module/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/akhilya/delegat-module/compare/v1.4.0...v1.5.0
[1.4.0]: https://github.com/akhilya/delegat-module/compare/v1.3.2...v1.4.0
[1.3.2]: https://github.com/akhilya/delegat-module/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/akhilya/delegat-module/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/akhilya/delegat-module/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/akhilya/delegat-module/compare/v1.1.0...v1.2.1
[1.1.0]: https://github.com/akhilya/delegat-module/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/akhilya/delegat-module/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/akhilya/delegat-module/releases/tag/v1.0.0
