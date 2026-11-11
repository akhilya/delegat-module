# Changelog

All notable changes to Delegat.Module are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

How we maintain this file: [docs/platform/releasing.md](docs/platform/releasing.md).

## [Unreleased]

### Added

- Product categories and store attributes support File values with ordered, product-owned attachments; the product gallery can mix images and downloaded videos. Ozon, Wildberries, and Yandex Market video imports are typed separately from image URLs. Their implemented export paths use stable, revocable HTTPS grants for selected gallery videos; Yandex supports up to six, Wildberries one MP4/MOV up to 50 MB, and Ozon applies no unverified limits. Ozon and Wildberries media payloads have fake-test coverage but have not been verified against live seller accounts.

- Adding a category now offers a reusable choice dialog for manual creation or marketplace import, with connection selection when multiple marketplaces are configured.

- Product groups support confirmed merging into an explicitly selected target group. Products and ordering are preserved. Different marketplace group links no longer block merging: each connection keeps the target's link, or the oldest source link if absent, and removes redundant local group links without changing marketplace cards or individual product links. No export is started by merging.

- Selected product groups can be deleted in one all-or-nothing request; their memberships and marketplace links are removed while products remain in the catalog.

- Bulk edit includes a permission-gated Mappings tab, using the same category-mapping picker as bulk adapter actions. Assigning mappings keeps unsaved product fields and attributes intact and does not start export.
- Catalog bulk adapter actions can assign category mappings to selected products without exporting. Choose one compatible mapping per local category; products without mappings are explicitly counted and skipped.
- Assistant chat accepts drag-and-drop file attachments using the same drop target as the file manager. Files stay in the draft until sent; folder drops show a localized hint.
- Product editing supports Ctrl+S (Cmd+S on macOS), including while typing in fields, with save notifications and protection against duplicate submissions. Browser page saving is suppressed for this shortcut.
- Categories support confirmed bulk deletion with store-scoped, all-or-nothing validation. Categories containing active products block the whole selection; trashed products remain in the trash.
- Category and global attribute lists share multi-selection, bulk required/unique/visibility/description-tooltip editing, and confirmed deletion. Bulk changes preserve other definition settings and product values; deletion removes the selected definitions and their values atomically.
- Optional Platform AI for linked instances, using the Control Plane's organization-wide monthly token quota and provider proxy; existing customer-connected (BYOK) models remain independent.
- Optional sampled Sentry traces for AI turns, with model/tool spans and aggregate usage measurements. Export is disabled without `SENTRY_DSN` and does not include prompts, tool arguments/results, or request data.
- Cabinet assistant remembers user-stated, non-sensitive preferences and useful facts across chats. Memories are scoped to the user and store, can be searched/saved/forgotten through assistant tools, are injected as untrusted context, and are capped at 40 entries.
- Cabinet assistant can read category and store attribute units through lightweight summaries or read-only SQL, distinguishes `unitCode` metadata from stored string values, and will not silently convert product data when a unit changes.

- Add store-scoped recurring task schedules with execution history and a persistent background worker. Scheduled actions remain unavailable until explicitly registered.

- Experimental Avito system adapter: OAuth client-credentials, account verification, paged listing import, dynamic Autoload category/field metadata, price import, and price update. Full listing create/edit is not exposed by these REST APIs and remains unavailable until feed hosting is implemented.
- A watch prompt lists each marketplace adapter and the official API news and documentation pages to read for upcoming, legacy, and breaking changes. Adding or removing an adapter requires a new copy of that prompt.
- Partners can list a connection’s category mappings and assign a product to one of them when a local category exports to several marketplace categories (`GET …/mappings`, `POST …/product-mappings/resolve`, `POST …/product-mappings`). The cabinet assistant uses the same choice (`resolve-mappings`, then `save-mapping-selections`) and does not edit the field canvas for it.
- Public API Swagger splits marketplace commerce into Prices, Orders, Fulfillment, Shipments, and Promotions. Paths are unchanged. Sections start collapsed, each operation is one line, and the longer contract sits inside it. Errors share one `{ error: { code, message, details } }` schema.

- Numeric category and store attributes can declare a unit. Category imports infer known units from numeric field names and descriptions when adapters omit explicit unit metadata, and imported field descriptions default to tooltip display. Marketplace field units are retained in mapping graphs, mismatches are explained in the mapping editor, and known compatible units get a reversible conversion node whose inverse is applied on import; unknown and incompatible units remain unchanged and do not block export. Unit selection uses a searchable shadcn-vue combobox, accepts custom codes, and product forms show localized unit names and configured field constraints.
- Category import localizes common marketplace attribute labels to Russian and moves recognized unit suffixes from the label into the separate unit field; for known measurements, descriptions preserve qualifiers such as “with packaging” when the marketplace name is generic. Importing into an existing category surfaces likely duplicate attributes and requires an explicit reuse/create/skip choice; reusing an attribute leaves its definition unchanged.
- Category import presents fields in the category attribute-list style, with selection checkboxes and a read-only details window for imported attribute settings.

- Medusa 0.2.3 downloads the Ozon Delivery packing-label PDF from the shop after assemble (`GET /orders/{id}/label`).
- Cabinet assistant `search_actions` looks up allowed cabinet HTTP (MVC routes plus operator aliases) so export/import/match do not need a prompt paragraph each, and Tools is not treated as the write catalog.
- Product files can be downloaded by an agent through a short-lived private HTTP link with filename, size, SHA-256, and Range/resume support; binary bytes never pass through MCP JSON or the model.
- Cabinet chat can inspect live spreadsheet and text attachments through bounded extraction before building a data-dependent write plan; staged file bytes remain private and are not exposed to MCP.
- Connected HTTP MCP tools require an exact instance allowlist (`AI_CONNECTED_MCP_TOOL_ALLOWLIST`) in addition to the existing disabled-by-default switch. Remote calls still require a standalone user-confirmed plan.
- Corner FABs include Documentation. On desktop (`lg+`), while the assistant (or another companion sheet) is open, the buttons sit beside the stack; opening Operations or Docs places that sheet on the right and shifts the assistant left so the panels do not overlap.
- Cabinet docs sheet fetches Control Plane `/docs` markdown (catalog + articles) through `GET /api/docs/*`; per-page Help when an article exists; the assistant indexes those articles and can `get_doc`. `DOCS_PUBLIC_URL` must be the **public docs origin** (e.g. `https://module.delegat.pro/docs`), not the instance SPA. If `/docs/catalog.json` is still SPA HTML, the catalog is built from `/docs/llms.txt`
- Cabinet order tabs (overview and connection mailboxes) show a count of stored snapshots on each tab
- Operator MCP (`POST /api/mcp`) and Agent HTTP (`/api/agent`) for the cabinet assistant and external MCP clients: JWT or `dk_…`, permission `ai.use`. Thin tools (knowledge, catalog search, read-only SQL, optional Python sandbox, allowlisted platform actions). Not Public API v1; not gated by `API_USAGE_ENABLED`. Docs: `docs/api/mcp.md`
- Cabinet assistant: category and store attributes (list/create), product `attributeValues` merge, `GET /products?categoryId=`, SQL on attribute tables plus `IN (...)` and `description`
- Cabinet assistant: execution timeline interleaves thinking with each tool step instead of stacking tools above a single merged paragraph
- Cabinet AI: attach files in chat (not folders). Bytes stay downloadable for 24 hours, then they are purged (names stay on the thread). The assistant decides what to do (including putting them on products) with the usual tools after you confirm a plan
- Settings → AI: Connect mints a Public API key and shows one `mcp.json` for this instance’s operator MCP (`POST /api/mcp`). Distinct from HTTP MCP servers the cabinet assistant calls.
- Settings → AI: add HTTP MCP servers (URL + optional bearer key). The cabinet assistant lists their tools and can propose calls in a standalone plan that requires confirmation. Not the instance operator MCP (`POST /api/mcp`).
- Knowledge corpus for the assistant (docs / MEMORY / PRODUCT / DESIGN), reindexed when the API starts. Optional Python sidecar (`SANDBOX_URL`) with pandas, Pillow, and openpyxl
- Cabinet Orders: `/orders` has four all-marketplace tabs (To pack, Being packed, In delivery, Delivered). Connection mailboxes stay in the sidebar. Listed orders are stored snapshots; lists read that snapshot
- Public API `GET …/orders` reads stored snapshots (`syncedAt`); status POST still hits the marketplace then upserts. Labels and shipments stay live
- Public API v1: live marketplace prices and promotions on a connection (not stored; not a 202 operation). Orders are stored snapshots
- Cabinet Orders: stored snapshots on each GetOrders connection. The sidebar is the connection; status tabs sit in the page bar (`/orders/{connectionId}/{mailbox}`). Mailbox rows use a fixed-height overlapping product-photo stack (name + count); the order sheet lists each line with its photo
- Public API and cabinet JWT: live fulfillment locations and shipments on a connection (not stored). `GET …/fulfillment/locations`, `GET/POST …/shipments`, boxes and box/supply labels. Wildberries: seller warehouses, drop-off offices, supplies, TRBX boxes. Ozon: seller warehouses only. Missing capability → `400 adapter_capability_missing`; marketplace errors → `400 fulfillment_location_failed` / `400 shipment_failed`
- Cabinet order sheet (Wildberries): pack confirm picks an open supply or creates one, optional box; Ready to ship can download the box barcode; ship confirms handing over the **whole supply**; Delivery can download the supply QR after handover
- Public API and cabinet order items may include `barcode` (Wildberries `skus`; omitted when the marketplace has none)
- Public API and cabinet order items may include `localProductId` when the line is linked to a local catalog product (cabinet name is a link to that product)
- Cabinet Orders: select loaded rows for bulk pack / ship / confirm / deliver / complete / cancel / barcode (only actions every selected order allows). Right-click a mailbox or packing-overview row for the same actions and photo size
- Public API and cabinet JWT: packing labels / barcode stickers for a live order (`GET …/orders/{orderId}/label`). Ozon FBS PDF, Wildberries PNG sticker, Yandex Market PDF. WooCommerce and OpenCart do not support this. Not stored. Cabinet order sheet can download the file when the adapter supports it
- Tools: Update all prices and Remove products from promotions (`/tools`, job ids `export-all-prices` / `clear-promotion-products`). The Tools page splits catalog, prices, promotions, Public API, and each adapter into separate lists. A job tool is its own inset group: open the row, or run from the last row (confirm). Confirm on the catalog runs every active connection that supports the job. Wildberries cannot clear calendar promotions. Not on Public API.
- Tools catalog: each entry has a stage (`stable` / `experimental`). Catalog scrape, Update all prices, and Remove products from promotions are experimental (quiet trailing label). Connect catalogs and Try Public API stay unlabeled.
- Ozon, Wildberries, Yandex Market, WooCommerce, and OpenCart adapters can read discounted prices, list and update orders, and manage promotions (Woo bridge ≥ 1.3.0, OpenCart bridge ≥ 1.1.0)
- WooCommerce, OpenCart, and Ozon mapping fields that were English-only (stock, sale price, model, SEO, price-strategy options) now follow the UI language
- Sign-in: TOTP second factor after password, recovery codes, and passwordless passkeys (Bitwarden / 1Password / Windows Hello)
- Extra permission `workspace.view`: browse the cabinet without changing data (plugins stay closed). Assign on a Viewer who is a store member for a demo account.
- Cabinet permission `orders.manage` for marketplace orders (stored snapshots), packing labels, and shipments. Operator and Store manager receive it. Connections, mappings, prices, and promotions stay `marketplace.manage`
- Global attributes: the same select/multiselect Custom values toggle, max values, max characters, numeric min/max, and regex as category attributes
- User settings: authenticator app (QR + recovery codes) and passkey register/remove
- Instance dashboard asks search engines not to index it (`robots.txt`, HTML `noindex`, `X-Robots-Tag` on the SPA and API)
- Tools: Try Public API (`/tools/public-api-try`) sends GET `/me`, GET `/stores`, or a custom `/api/v1` path with a partner key and shows the response
- Public API v1: product size variants (`GET` product includes `variants`; list/create/patch/delete under `/products/{id}/variants`)
- Public API v1: create category accepts `hasSizes`; category list attributes include `options` and the other GET-detail fields
- Public API v1: product list `search` matches barcode, variant SKU/barcode, and marketplace ids (same as the catalog), and items include `variantCount`
- Cabinet AI generation keeps running if you reload the page: reopen the companion sheet to watch the live turn (tools, thoughts, Stop). It does not survive an API process restart
- Catalog Connect import, match preview, and apply run as background operations with `x / total` progress on the tool page (survives reload and in-app navigation). Remote catalogs are paged in bulk and unlinked products are queued as pages arrive.
- Cabinet assistant `ask_form`: a form on the reply. Marketplace keys stay in the browser and POST the same create-connection as Adapters (the model never sees the key). Clarifying questions are a short Questionnaire (up to 5 steps); answers stay on the card and continue the model without a visible chat bubble.
- Cabinet assistant requires a standalone user-confirmed plan before running Python code or any connected MCP tool; direct calls are blocked server-side, and untrusted instructions in tool output or external metadata are treated as data.
- Cabinet assistant no longer treats a nonzero Python exit code or timeout as a successful plan step; file-driven write plans require inspected attachment data and validated catalog matches.

### Changed

- The product Images tab is now Media and reuses the main gallery for photos, videos, previews, uploads, ordering, and removal. Storage media can be reattached, and product save actions are available in the Media tab.
- File attribute controls stay compact in product forms; file selection and upload use a searchable, size-bounded popover instead of rendering the product's full file list inline.
- Assistant attachment picking uses a paperclip icon. The model selector is hidden when only one enabled model is available.
- AI Model IDs use the shared badge component. Desktop toast notifications stay to the left of the companion buttons and follow their position when sheets open; mobile placement is unchanged.
- Checkboxes have larger click/touch targets, and table selection columns and outer cells have more breathing room, including headers. Form corners now match the existing buttons; file tables no longer duplicate their parent's outline.
- Form controls share one consistent corner radius. Empty states use bare icons and shorter copy; the unavailable page keeps only a heading, recovery hint, and retry action.
- Adapter product tables have stable column widths, roomier rows, readable product names, and horizontal scrolling instead of compressed columns.
- Save buttons use a simple checkmark instead of a floppy-disk icon. API Keys / Request logs tabs sit in the main navbar, with log filters above the list.
- Notifications use compact Apple-inspired banners with status icons and an inset dismiss button. API errors no longer dump technical details; server failures show one concise, localized retry-later notification.

- Tools and schedules share one page with navbar tabs; existing schedule links open the Schedules tab. Schedule creation and editing use the standard form dialog, with full-width fields and no nested list cards.
- Long filter options stay on one truncated line, with full text available on hover. Order overview and mailbox tabs align to the left.
- Navbar search and product-editor tabs stay centered between equal side tracks, with the same gutters as page content and capped search width. Adapter primary tabs stay in the main navbar; in-page product tools remain left-aligned.
- Catalog, notes, barcodes, orders, connection products, and log lists share search styling. Filters use standard shadcn-vue dropdown radio items, submenus, and reset instead of a custom popup panel. Navbar tools adapt to available panel width so Help and account actions do not overlap search.
- All cabinet tables share a thin, rounded outline, including catalog scroll panes, logs, file lists, and assistant SQL results. Sticky headers and scrolling are preserved; separate action footers no longer duplicate the table's bottom border.
- Groups, categories, and both attribute lists share the groups-style selection layout: Select all inside the list, selected count and actions below it. Row-scoped trash controls throughout lists and tables share desktop-hover/keyboard-focus behavior and remain available on touch.
- Cabinet assistant keeps file-reported diagnoses separate from the requested marketplace action: a listing refresh uses existing mappings without unrequested edits. Named exports check existing links and reject inconsistent counts or broadened selections; long plan steps remain expandable without filling the confirmation card.
- Cabinet assistant action search now ranks route names and purpose-built aliases instead of incidental words in API usage notes, making listing refreshes easier to distinguish from price changes and mapping edits.
- Assistant documentation retrieval now indexes the full text of long sections, shows the passage that matched the search, and retains the last indexed public articles through temporary source outages instead of dropping them. Dated audit snapshots and the offline evaluation protocol are no longer indexed as current operator guidance.
- Assistant documentation-answer guidance now preserves source conditions and integration scope, separates documented rules from current configuration, and cautions against inferring absent capabilities or guaranteed fallback success.
- Disabled-Python guidance no longer substitutes current helper availability for documentation questions about permissions. WooCommerce image-transfer documentation clarifies that fallback can fail after a successful product export; neither behavior is a delivery guarantee.
- Assistant `get_doc` restores identifiable section headings in indexed articles instead of returning headingless fragments; existing size limits and search ranking are unchanged.
- Ordinary lexical knowledge search and indexed article reads no longer fetch unused embedding JSON from MySQL, reducing retrieval allocations without changing ranking.
- AI reference docs distinguish the default Viewer role from effective grants and explain that private file-download tickets can be reused before their original expiry; runtime permissions and ticket behavior are unchanged.
- Add opt-in, local-only hybrid documentation retrieval that stores model-tagged vectors in MySQL and blends semantic and lexical ranking. The default and failure path, including embedding HTTP timeouts, stay lexical; caller cancellation still stops the operation. Ordinary assistant improvements require no embedding service.
- Production Compose can enable instance-local hybrid documentation retrieval with a private, version-pinned Ollama sidecar, explicit model provisioning, resource limits, and a lexical rollback path. CI/release checks validate the deployment configuration without starting services.
- Hybrid retrieval reuses one parsed-vector snapshot across requests instead of repeatedly loading every vector; refreshes follow index updates or a one-minute expiry. Query embedding waits are capped at three seconds before lexical fallback.
- Cabinet assistant ranks exact catalog identities ahead of partial matches, marks incomplete search results, and can answer an exact-SKU field lookup with one grounded read instead of two.
- Cabinet assistant rejects incomplete action plans before showing a confirmation card, rather than letting the user confirm a plan that cannot execute.
- Assistant plans show one status, compact confirmation controls, and numbered steps in execution order, with lengthy details available on demand.
- Use a migration-only database command timeout (30 minutes by default, configurable with `DB_MIGRATION_COMMAND_TIMEOUT_SECONDS`) so large index builds are not interrupted by the regular 120-second query timeout.
- Ozon's mapping canvas groups price fields and refreshes field metadata without losing saved bindings. Price import falls back to `marketing_seller_price` when `marketing_price` is unavailable.
- Cabinet assistant asks one clarification when a read-only lookup lacks its product; it does not list the broad catalog or add edit/navigation links unless requested or needed.
- Cabinet assistant reads requested price, stock, or description fields after identifying a product; omitted fields in a search result are not treated as empty values.
- Cabinet assistant follows narrow requested answer formats (for example, a number-only reply) and no longer treats `memory_search` as a way to retrieve another chat's transcript.
- Orders no longer show the experimental-preview notice.
- Product image, file-management, variant-reordering, and marketplace field-selection controls use the shared UI components and expose accessible names and keyboard reorder instructions.
- Partner operation status returns the latest 200 log lines. The cabinet shows the latest 500, so a long run stays visible at the end. Explain with AI appears after the operation finishes.
- A cabinet calculation/helper run is proposed as a standalone plan and runs only after confirmation; the assistant then answers from that result without repeating the same completed lookup. A message that only says to answer (“ответь”) replies from what is already known.
- Mapping canvas adds Regex replace and Regex find. A standard-value block asks for the shop field first, then a dictionary entry, and that source stays fixed. A category or store attribute that stores a dictionary key can send the key or the name. On a directory field the assistant prefers that standard-value block.
- On a field mapping, the cabinet assistant sees how many exports of that category succeeded or failed since the canvas was last saved, and the text the adapter returned on the failures. Results from before that save are not treated as open problems.
- Cabinet assistant can propose a confirmed calculation-helper plan to read a connected shop’s API and correct a filled value from what the shop returns. It stays on the open screen (mapping, product, order) and does not invent a directory entry. Seller documentation is downloaded once a day by Control Plane (Wildberries items and FBS orders, Ozon Seller, Yandex Market). The assistant reads that copy through the platform link and does not fetch the seller’s documentation URL itself.

- Cabinet assistant keeps a short system prompt on ordinary turns. Mapping rules, a catalog-wide price change, and the page-address list are added only when that page is open or the message is about them.
- Products page search is wider, so a product name or SKU fits in the field. Other pages keep the shorter nav search.
- `/api/ai`, `/api/agent`, and `/api/mcp` are exempt from the SPA rate-limit bucket. Authenticated catalog traffic allows 2400 requests per minute per user (was 1200).
- Docker Compose pulls MinIO from `quay.io/minio` (`minio` and `mc`). Docker Hub no longer serves those images (`pull access denied`).
- Sidebar Orders and Integrations list connections by connection name, not adapter display name.
- Assembly overview loads each connection on its own: the first adapter to return paints immediately; others keep a spinner in their group. Orders experimental notice uses body type, not an h5 title.
- Experimental badge uses the muted fill. Orders sidebar shows it only on the section, aligned right.
- Dropped the category-attribute «In the shop» switch. Storefront specs follow the mapping graph; unwired fields are not sent.
- Mapping canvas: add unlimited mapping-local adapter fields (click «Add field» or drop a wire on that row). Custom sinks persist in the graph and go out on export.
- Cabinet assistant can add mapping-canvas blocks (`POST …/graph/connect`, `…/graph/modifiers`, `…/graph/group-titles`). Merge into card (`__group__`) uses a typed from/to string map (not valueId). `GET /marketplace/mapping-graph/blocks` lists node types, pins, and layout sizes. Local group names stay; Medusa export uses the mapped collection title.
- Store-engine adapters (Medusa, WooCommerce, OpenCart) create a shop category on a dedicated page: name, parent folder, which local attributes become fields, and whether to auto-map. The mapping dialog opens that page instead of creating immediately.
- Adapter errors name the missing token rights (WB Content scope, Ozon permission, and so on) instead of dumping JSON. Export / import / unlink stay hidden without `marketplace.manage`; catalog writes stay hidden without `catalog.manage`.
- Product Adapters tab is one compact row per connection (icon, name, caption, status) instead of a fact sheet. Product Sizes use labeled SKU / price / stock fields with input chrome so they read as editable.
- Navbar page Help is a labeled «Need help?» / «Нужна помощь?» control with a question-mark icon (same expand-from-icon pattern as the account button). It is no longer a book glyph hidden below `md`.
- Below `lg`, the assistant, documentation, and operations sheets open one at a time (full width). Documentation contents move into a header menu when the sheet is narrower than 720px (resized panel or small viewport) so the article stays readable. The matching corner/navbar button uses an outline (or filled navbar) state while that sheet is open.
- Inset companion sheets (documentation, assistant, operations, and the other float panels) use a slightly deeper drop shadow than the desktop sidebar.
- The documentation FAB uses a question-mark icon. Companion FABs (and the compact navbar set) show a tooltip instead of the browser `title` tip. Below `lg`, documentation is only the navbar Help control — not a second question-mark in the compact FAB cluster.
- Docs sheet and assistant replies share one markdown reading style (disc / nested circle / numbered lists). Tailwind no longer strips those markers.
- Medusa mapping keeps the linked category’s attributes as adapter sinks. `GET /categories/{id}/fields` no longer drops the bridge list when `success` is omitted, and unmatched local category attributes are merged in (Ozon / Wildberries stay marketplace-only). Reopen the mapping after restarting the API.
- Cabinet `PATCH /products/{id}/group { groupId }` moves a product into a group (or out, when `groupId` is null). The assistant was told this path existed; only GET was implemented, so plans failed with HTTP 405.
- API JSON is compressed (Brotli/Gzip at Kestrel; nginx also gzips `application/json`). Vendor JS splits into vue / ui / http / signalr chunks. MySQL pool is capped at 50 connections. Sandbox health checks are not logged.
- Cabinet route changes use the same `.page-region-in` fade as region loaders (opacity, `--duration-overlay`). Nested Settings pages fade on inner navigation.
- Assistant questionnaire answers stay on the form card and survive reload. The follow-up still goes to the model but is not shown as a chat bubble.
- Adapter credential help and Connect catalogs docs open the in-cabinet docs sheet instead of a new tab. The credentials control is a full inset row (title + adapter subtitle + chevron), same grouped list as the adapter picker. Page Help covers orders, notes, mapping, setup, settings sub-pages, catalog scrape, and Public API when those articles exist on the public docs site.

- Pin GitHub Dependabot patches: sandbox Pillow 12.3.0; frontend nested `js-yaml` 4.3.2, `decode-uri-component` 0.5.0, and `@humanfs/node` 0.16.8
- Sidebar group for products, groups, trash, categories, barcodes, and notes is **Каталог** / Catalog. The products page stays **Товары** / Products.
- Adapter connection Products tab fills the pane like catalog products (`table-scroll` XY scroller, sticky `bg-background` headers). Ids/SKU are plain mono, not muted chips. Status chips stay outline (Synced uses success-muted, unlinked is muted caption) instead of inverted primary or muddy secondary fills.
- Tabs: in dark the selected pill is muted on a quiet muted well. The old shadcn `input/30` overlay matched `--muted` here and disappeared; the track is no longer a solid muted slab.
- Dark theme: default buttons are inverted paper (light fill) with a **dark** top sheen. Branding no longer pins light `--primary` on `html`, which kept ink buttons on a dark page. Catalog search matches `Input` (`dark:bg-input/30`, not page paper). Flush product-table headers match the page. Page-size selects use field chrome, not `bg-primary-foreground`. Status captions (`success` / `warning` / muted) stay lighter than their fills. Scrollbars follow the theme (`color-scheme` on `.dark`, 6px thumb from muted foreground) instead of Chrome’s light overlay.
- Navbar home crumb sits inside the trail (no negative pull), so the hover fill is not clipped by the bar.
- Cabinet assistant marketplace product import is the same cabinet write as Catalog Connect and the connection page (`import-unlinked` / `import-selected`). It should run that write after you confirm, not ask you to click Import. `POST import-unlinked` queues a background job (HTTP 200 pending is success); the process must not treat that as a failed write or wait for every card in the same turn.
- Cabinet assistant linking of existing cards to another marketplace is Catalog Connect match (`POST …/match/preview`, then `POST …/match/apply` with preview category ids). HTTP 200 with `mappingsCreated` 0 is not a finished mapping. Backfill is `POST …/match/repair-mappings` (also copies characteristics). Mapping canvas is `GET/PATCH …/mappings/{mappingId}/graph` — there is no `GET …/connections/{id}/graph`. A 404 names TOOLS paths instead of always pointing at match. A second Catalog Connect POST while one job is running **queues** (HTTP 200 pending) instead of `catalog_connect_busy`.
- `MAX_PRODUCTS` usage includes cards in trash. Moving a product to trash does not free a slot (Settings → Administration, adapter import, and the assistant quota line).
- Empty AI chat: catalog-sized title, three rotating starters (page-aware when the companion knows the card), docked above the composer instead of a centered “Hi!” list
- Controls use the `--radius` token (`rounded-md`), not stadium `rounded-full`. Exceptions: circular operations and AI FABs (bottom-right), plus geometric circles (avatars, radios, switches, dots). Search magnifier is MingCute Core Regular (hollow), not filled
- Cabinet UI icons are MingCute Core Filled (Apache-2.0) behind Lucide names — rounded filled glyphs that match `--radius`, not Phosphor. Square badges stay off the map (`CheckFilled`, `ArrowRightUpFilled`, `ClassifyFilled`, `StopCircleFilled`). Loaders stay Hugeicons `Loading03`
- On touch/pen, a tap on a context-menu target opens the menu (Reka long-press never fired on a tap). The mobile sidebar sheet closes after a nav option or route change
- Cabinet assistant `navigate_to` open buttons may include a query string (`/products?page=2&pageSize=15`). External URLs and `javascript:` stay blocked.
- Cabinet assistant `navigate_to` buttons are one action each: Open a path or Continue the chat, never both (max 6). Open shows the path; Continue shows the follow-up text.
- The cabinet navbar sits above the page scroller (full inset width, not sticky inside it). Catalog table footer actions use foreground ink (the dock no longer paints the bar muted, which made outline buttons look disabled). Fill panes (catalog, store storage) and product-edit side columns share `--page-scroll-end` so content is not flush with the inset edge. Catalog products scroll inside the table; a wide row pans the table, not the bar. In-page list searches (orders, logs, connection products, category browser) use `NavSearchField`
- Catalog navbar search matches the default input: `h-9` and `rounded-md` (not a taller pill). Select, searchable-select, and the sidebar store switcher use the same field corners. Focus on search still uses `border-ring`, not a 3px ring. The bar is `4rem`
- Cabinet navbar stacks search/tabs under the title below `lg` so the bar fits a phone; the trail is the current page only
- Cabinet page trail lives in the navbar left column. Tabs and search stay on the viewport midline (equal side columns; center track `minmax(20rem, 40rem)`). Catalog search and category sit in one `NavSearchField` with a hairline; barcode and notes search use the same field. Category picker rows stay truncated
- Cabinet assistant can propose calls to MCP servers enabled in Settings → AI for the job those tools describe (library docs, etc.); each call waits for user confirmation. Still refuses jokes, recipes, homework, and jailbreaks; does not redirect that work to the catalog.
- Catalog (and other) product thumbs load inside the cell spinner; the products table paints as soon as `/products` returns and does not wait for photos to decode
- Cabinet assistant runs a confirmed plan in the same assistant message (no “Confirmed, proceed” bubble). The plan card shows Done after the steps finish, or Failed when a write was rejected (including the product cap); older threads that stored the split are folded on load
- Public API and cabinet product create/patch: characteristic `value` accepts JSON numbers, booleans, and arrays and stores them as text (same as the product form strings)
- Cabinet assistant remaining catalog slots are in the chat context. At the cap it must not trash an existing product to free a slot. A confirmed plan that returns HTTP 4xx/5xx is a failure; if earlier steps already applied, the reply says so (including trash) instead of “nothing changed”. Oversized product-card JSON now returns `response_too_large` at the 256,000-byte cap and should be narrowed or paged.
- Cabinet assistant `platform_request` calls the same cabinet JSON API as the SPA (permissions, store, quotas). Secrets, user admin, plugins, and irreversible actions (delete a store, permanent product delete) stay blocked. Product characteristics PATCH replaces `attributeValues` like the product form
- Python sandbox Compose service starts only when AI is on (profile `ai`). `pnpm dev` follows `AI_ENABLED`; self-host `docker compose --profile ai` when the assistant is enabled
- Cabinet assistant stays on store work: jokes, recipes, homework, and jailbreak/secret dumps are refused. Operator-enabled MCP tools (Settings → AI) are in scope for the job they describe. Replies, plans, and thoughts stay in catalog language (products, characteristics, connections) and do not name SQL, HTTP, or internal tool ids
- Cabinet AI companion sheet fades as it slides; the latest turn, plan, and chat/history switch ease in (no thread stagger). After a turn, tool activity is a one-line disclosure; open it for thoughts, compact steps, and Python (script and output stay collapsed)
- Cabinet assistant offers navigation as buttons on the reply (open a page and/or send a follow-up). It is prompted to put those buttons on the reply instead of only describing the path
- Cabinet AI chat uses Apple inset chrome: history is Mail-style (title, count, date on the right). Replies strip emoji so Settings → Emoji: Don't use is visible in the thread
- Cabinet AI no longer exposes dozens of named tools; it uses the operator MCP/Agent HTTP surface (knowledge, SQL, catalog search, plan confirm). Mapping-suggest and explain-error stay as prompt skills
- Cabinet sidebar and inset sheets keep the hairline ring and use a quiet drop shadow (no large ambient cloud)
- Catalog and order mailbox pagination / bulk footers sit under the last row when the table fits the page; they pin to the inset bottom only when the page overflows. Hairline top border only (no ring, no rounding)
- Cabinet order mailbox rows have a hairline under each row
- Packing overview and order mailboxes scroll with the page. Catalog products scroll inside the table. Mailbox headers stick at the top of the page scroller. Pagination / bulk footers follow the last row unless the page overflows, then they pin to the inset bottom
- Cabinet packing overview (`/orders`): each order is one Mail-style inset row (photo stack, product name, order id, amount). Line items stay on the sheet
- Cabinet “Server temporarily unavailable” is a full-viewport system screen (large title, one Try again button, no store logo). Sign-in / sign-up also drop the store logo. The Control Plane square is no longer the default favicon; the store icon is the tab and sidebar mark, or the store’s initial if none is uploaded
- Wildberries pack uses the supply in `extra.supplyId` when the caller passes one (the cabinet sheet does); omitting it still creates a supply so Public API callers keep working. Optional `extra.boxId` / `extra.createBox` assign the order to a TRBX. Order `extra` may include `boxId` when the box membership is known
- Cabinet Orders: mailbox pages no longer show a hint under the search field
- Cabinet Orders: mailbox footer pack / ship / barcode actions use the filled button (same as the order sheet), not outline
- Product photos in the catalog, packing overview, order mailboxes, and catalog scrape can be Small (the previous 44px), Medium, or Large. The product-row and order-row context menus (Photo size submenu) and the catalog scrape toolbar keep the choice; it is no longer in the catalog column picker
- Adapter capabilities dialog names packing labels, fulfillment locations, shipments, and public catalog search (those flags were already returned by the API)
- Products catalog identity cell (photo + name) is a 44px thumb and two truncated lines on a single row height. Adapter chips stay on one line so rows no longer grow. Hover preview, SKU copy, adapters, and column presets are unchanged
- Order mailbox lists each line item (photo + name + quantity). Row height follows the number of lines; four or more collapse behind Show N more
- Wildberries FBS order mailboxes match the cabinet (Ozon-style) statuses: new assembly tasks (`supplierStatus=new`) are Assembly (`awaiting_pack`), not New; in a supply (`confirm`) is Ready to ship; `complete` is delivering until `sold`. New stays empty on WB (no accept step). The pack list uses `GET /api/v3/orders/new`; other mailboxes load `/api/v3/orders` and statuses from `/api/v3/orders/status` (the list payload has none)
- Tabs: the active pill slides between triggers (150ms, same curve as menus). Panel content still fades. Reduced motion keeps the instant jump (global token collapse)
- Desktop sidebar floats inset from the viewport like those sheets. The nav list hides its scrollbar; edge fades only appear while there is overflow past that edge. Nav groups ease open and closed; the current page pill in a group slides like tabs (150ms). The capsule handle sits on the right hairline with no full-height wash (width is kept). Collapse and expand use the 150ms menu curve. The mobile sidebar sheet is inset and not resizable.
- Page region loaders (`EmptyState loading`) have no bordered box. The spinner and the first paint of the page body fade in (200ms)
- Order sheet floats inset from the viewport like Operations; buyer, address, and items are inset groups. Action buttons have icons; every status change asks for confirmation first
- Data tables sit flush on the page (no rounded card, no outer ring), including the products catalog. Catalog and order mailboxes grow with the page scroller; table headers stick under the navbar. Horizontal overflow scrolls the page (an inner `overflow-x` wrapper would unpin the header). Row hairlines, sort, column reorder, and log footers are unchanged
- Catalog scrape: search and copy from Ozon, Wildberries, or Yandex Market without connecting that adapter. A store connection is used only when one already exists (for category import). WooCommerce and OpenCart stay cabinet-only.
- Catalog scrape: when Ozon or Yandex Market unofficial HTTP is blocked, the API opens the public page in Chromium (Playwright). It uses full Chrome headless (`CATALOG_SCRAPE_PLAYWRIGHT_CHANNEL=chromium`, not headless-shell), a reused session, and a User-Agent that matches the browser. Local Windows installs Chromium on first miss. Interactive captcha is still `storefront_blocked`. Optional installed Chrome (`channel=chrome`) or a proxy. Wildberries stays on `search.wb.ru`. The API Docker image includes Chromium.
- Public API `PATCH` product now replaces `attributeValues` / `storeAttributeValues` when those arrays are sent (same as the dashboard)
- Public API create store-attribute copies `options`, `defaultValue`, `description`, and the same value constraints as category attributes
- Public API sync starters reject inactive connections (`400 connection_inactive`)
- Public API error `code` values for missing products, categories, variants, store attributes, operations, and several conflicts (`sku_already_used`, `category_has_products`, …)
- Mapping canvas zoom in/out uses the same plus and minus icons as the rest of the app
- Changing your password (and setting one in Administration → Users) asks you to type the new password twice
- Mapping auto-arrange uses Dagre, then stacks card fields above price fields so the two sinks no longer overlap
- Mapping canvas toolbar is button groups: Save + more, a wider preview-product picker that can open the product, and Reset
- Buttons use the same Control Plane lift: a short white top sheen and a hairline drop shadow on default, outline, secondary, and destructive (ghost and link stay flat)
- Product create Properties uses the same Apple Settings sections as product edit (no Cards / heading mix)
- Dropdowns, context menus, selects, popovers, and tooltips use the same Apple menu material (frosted paper, hairline ring, denser rows) instead of a bordered `shadow-md` panel
- Overlays and menus share one motion curve (`ease-out-quart`): sheets open in 280ms (was 500ms), dialogs 200ms, menus 150ms; buttons press-scale; tab panels fade. Respects reduced motion
- Operations and AI inset sheets use a stronger hairline and a quiet drop shadow so the floating panel reads against the page. The AI companion has a visible capsule handle on the left edge to resize width
- Operations and AI panels sit 8px off the viewport with rounded corners, not flush to the screen edges. AI chat is a resizable inset sheet (not a split pane) and no longer lists every assistant tool; it has no overlay scrim, stays open while using the rest of the app, and does not close on an outside click
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
- Barcodes use the same flush table and combined footer as products (selection + download in the footer). Store notes are an inset grouped list; the note sheet uses bar chrome and Settings fields (not a writing pad)
- Adapters, connection settings / mappings, category import, and mapping preview chrome use the same Apple inset grouped lists and sheet bars as Settings (the mapping graph canvas is unchanged)
- File manager: content type is a wider column so MIME types are not clipped; tags size to the chips instead of a fixed width
- Operations list: each job is its own inset group; status is the trailing icon only; Copy / AI / Retry / Delete after expand are compact rows
- Background operations open as an Apple-style sheet: bar header, inset grouped Active / History lists, and the platform overlay scrim

### Fixed

- Photos and videos can be dragged between the Media tab's selected gallery and storage sections, including empty drop targets, without deleting storage files.
- Clicking empty fields around an expanded product photo closes its preview again; media action buttons use opaque surfaces.
- Full product media previews scale to the available viewport, photo thumbnails fill their tiles consistently, and the Media tab uses larger responsive tiles without a product-delete action.
- Product gallery Main and Video labels use shared shadcn-vue badges; video tiles lazily show an initial frame without autoplay, with a play-icon fallback when decoding is unavailable.
- Marketplace category imports recognize attachment and media-link fields as File attributes in both preview and creation, normalize labels such as Ozon video/video-cover links, and omit remote URL-string constraints without changing existing definitions.
- Product attribute forms preserve scalar values when attachment IDs are present in API DTOs, instead of overwriting values with `[]`. Mixed-gallery previews expose the media classifier correctly.
- Product details send only selected dictionary labels; complete selector options load on opening, including keyboard activation. Large category dictionaries no longer inflate every product response.
- Interrupted feature migrations fail immediately with a reconciliation error instead of repeatedly attempting duplicate DDL or automatically stamping incomplete schemas.

- Bulk category deletion preserves EF's dependent-delete ordering when categories contain saved attribute values, avoiding concurrency errors from database cascades.

- Exporting from the product editor now saves pending product and image-order changes first, waits for an in-progress save, and stops if saving fails. Full, price, stock, and recreate exports refresh available mappings after saving rather than using stale category data.

- Numeric marketplace fields no longer infer inches from a quote in prose or an article title; the quote must be adjacent to a number.

- Product attribute tooltip triggers are no longer clipped in constrained form layouts, and unit labels use the error color when their numeric field is invalid.

- Adapter HTTP logs retain request/response details for 24 hours and at most 5,000 recent records, while keeping searchable summaries for seven days. New payloads are smaller, cleanup runs every ten minutes, and detail previews/downloads avoid loading unnecessary full bodies. The cabinet explains when detailed data is no longer available.
- Adapter log pagination no longer stops before the last page. New searches and detail selections cancel preceding requests and ignore late responses, preventing stale results, spurious errors, and incorrect loading states.

- Assistant action discovery now prioritizes concrete product fields and translated aliases, including native bulk barcode generation. Plans prefer supported server/bulk operations instead of unnecessary Python preparation; repeated equivalent scalar writes are returned for replanning before confirmation, while distinct values and dependent changes remain separate.

- Barcode assignments now reject duplicates across products and variants within a store, including trash, for cabinet/API edits, imports, transfers, and bulk updates. Concurrent writes are serialized; bulk generation avoids occupied codes. Existing duplicates are not automatically rewritten.

- Assistant catalog pagination no longer stops after a gateway-trimmed terminal page. Python guidance now states the actual list cap and SQL limits. Result review withholds provisional tables until it chooses an answer or correction, and no longer announces task completion merely because a script exited successfully.

- Assistant plan results now receive a separate review grounded in actual execution output. Failed calculations can propose a corrected plan for fresh confirmation, without automatic retries or overwriting the failed plan. New proposals discard speculative result text; failed steps no longer imply that nothing changed.
- Python instructions now describe the actual SDK methods, result shapes, and pagination instead of leaving the assistant to guess requests-style helpers that do not exist.

- Assistant work remains running when navigating to history, another conversation, or a new chat. History refreshes live activity, and returning to a running conversation reconnects immediately. Explicit Stop is shown on the reply and saved across reloads, including empty replies and interrupted plans, without marking unfinished tools successful.

- Dark mode is applied before first paint, preventing a white flash on reload. Adapter product sync timestamps without a timezone suffix are parsed as UTC, fixing incorrect relative update times in local timezones.

- Numeric form fields use the same dark-theme background as other inputs, including product prices, stock levels, and variants.
- Floating Documentation, Operations, and AI buttons keep opaque backgrounds on hover and while open in both themes. Companion tooltips appear after 100ms instead of 300ms.
- Executing an assistant plan no longer restores a cached pending card or offers the same accepted plan again. Confirmation/cancellation target the selected proposal; reconnecting does not duplicate execution details. Interrupted results and unavailable background operations stay unconfirmed rather than appearing successful, and partial completion is distinct from failure.

- Category and global attribute management loads dictionary-free SQL summaries; a category attribute's complete definition loads only when opened. Attribute deletion and bulk flag edits update rows in place without reloading the page or list. Row delete controls appear on desktop hover/keyboard focus and stay visible on touch devices.
- The product picker on the group edit page scrolls long product lists while keeping search, pagination, and action buttons visible.
- Category mappings and the field-mapping editor remain available when AI is disabled; loading mappings or local/marketplace categories no longer fails with a controller dependency error.
- Unified assistant chat chrome and message spacing with quieter user bubbles, a clearer conversation header, accessible message actions, and a composer matching dashboard controls. Enter no longer submits during IME composition.
- Adapter HTTP log retention deletes in smaller batches so pruning large log tables does not exceed the regular database command timeout.
- Cabinet assistant preserves Anthropic tool-call/result order across multi-round chat history, avoiding provider rejections on later turns.
- Cabinet assistant questionnaire submissions reliably include typed answers instead of sending a blank follow-up.
- Cabinet assistant preserves questionnaire/attachment metadata when the live server has already inserted the matching user message before client history sync.
- Conversation history sync no longer deletes server messages omitted from a stale client snapshot; it reconciles server-inserted messages with the unpersisted client tail so form metadata is retained without duplicate rows. The client persists user turns before starting live generation and reloads the server-authoritative assistant turn after streaming. Concurrent live/history writes rebase on the latest row and preserve server-owned plan metadata.
- Assistant plan confirmation becomes available only after the live turn is saved, then consumes the proposal atomically inside the MySQL retry execution strategy.
- Assistant plan confirmation errors return a safe rejection reason, refresh the server-owned conversation state, and appear in a localized alert instead of being appended as ordinary chat text.
- MCP discovery/resource reads require agent permission; workspace-view users cannot write memories or invoke connected MCP tools. Direct Python/connected MCP chat calls require a standalone confirmed plan, and oversized MCP responses are rejected at 256,000 bytes.
- `platform_request` now stops binary responses before buffering and reports `binary_response_requires_download`; JSON/text responses remain capped at 256,000 bytes. Use the short-lived file-download tool for private file bytes.
- DeepSeek Anthropic-compatible model discovery uses its `/models` endpoint, and chat requests retain the `/anthropic` base path.
- Single-connection marketplace actions now open their connection selector instead of offering an ambiguous Quick Run. Product imports validate the 1–200 limit, and schedule time zones are configurable and shown with run times. Radio choices now support arrow-key navigation.
- Connected MCP tools for the cabinet assistant are disabled by default (`AI_CONNECTED_MCP_ENABLED=true` re-enables tool calls), and their settings section is hidden.
- The marketplace credential help link now opens its documentation from the setup wizard.
- Packing or handing over an order updates the cabinet list immediately. The order leaves To pack (or whichever mailbox it left) when the action succeeds, instead of staying there until the next marketplace pull.
- A new or updated marketplace order shows up on the cabinet while Orders is open. Every adapter that lists orders is pulled again about every 20 seconds there, and about every 30 seconds in the background, instead of waiting several minutes on the stored snapshot. Older open orders are reconciled about every 5 minutes.
- A large file keeps a steady download speed for people who are not next to the server. The web proxy holds about 8 MB of the response in memory so a distant link does not stall between tiny chunks, and it still does not spool the file to a temporary disk file.
- A file upload streams to storage again. MVC was reading the multipart body before the action ran, so the file arrived with its size declared and no bytes left to store.
- A large Wildberries export no longer stops the queue for half an hour on every batch, and an article the marketplace already accepted is not uploaded again when it is slow to appear in the catalog.
- Uploading a file sends the file body. A preset multipart content type left the declared size in the header and an empty body, so the upload failed as soon as it reached the API.
- A failed file upload records the declared size, how many bytes actually arrived, and the start of the body. A cut-off upload can be told apart from a parser fault.
- Uploading a file no longer fails when the web proxy streams the body. A normal API request was marked as a WebSocket upgrade, so the file never reached the server.
- A batch of files is stored while it is still uploading. The browser no longer waits for each file to be copied again after the progress bar reaches the end, and several files upload at once.
- A file download continues from the last received byte when object storage closes the connection early, instead of failing the request.
- A slow file download is no longer cut off. The API no longer aborts a response that stays under a few hundred bytes a second, and the web proxy streams the file instead of buffering it and closing the connection.
- Wildberries bulk export uploads the product gallery. A photo list built on the mapping (gallery plus extra files) was not a plain string list, so the upload was skipped and the cards stayed without photos. A failed photo upload is written on the operation.
- A waiting assistant plan is outdated when the next message does not confirm or cancel it. Execute is hidden, and the assistant revises the plan instead of running the old steps.
- Cleaning a mapped description keeps Latin, Cyrillic, digits, and ordinary punctuation. The assistant replaces the regex on that field. It does not invent a cleanup field and does not start an export unless asked.
- An assistant plan with no summary and no steps no longer shows an empty card with only Execute. A steps list sent as text is still read.
- Ozon price export applies the category mapping when the product link has not saved one and the category has a single mapping. A raised price was sent without the mapped old price, and Ozon rejected it because the strikethrough already on the card was lower (`Old price is less than price`).
- Ozon list responses still report the full count when the marketplace sends `total_items` instead of `total`. Catalog import and removing every product from a promotion use that count. Product list, prices, and stocks switch on 23 November 2026.
- Ozon promotions use the methods that stay after 13 October 2026: product list `POST /v2/actions/products`, add or remove by price `POST /v1/actions/products/update`, and promo-code removal `POST /v2/actions/products/deactivate`. A price in the reply may be a number or a Money object.
- Ozon orders use the current FBS/FBO posting-list endpoints with cursor pagination, and structured posting prices no longer break order-list deserialization.
- Recent order refresh now fetches one unfiltered page per order-capable connection instead of repeating the recent window for each status. Ozon requests up to 200 combined FBS/FBO postings; other adapters request up to 100. UI and background pulls for a connection share the cooldown and gate.
- Wildberries card export sends a barcode or GTIN characteristic in the card field `gtin` and in the size barcodes. The same code inside characteristics was rejected (`The GTIN code must be specified in a separate "gtin" field`).
- The cabinet assistant no longer reports a mapping edit as done when the request would not change the text. A field the action does not read, or a pattern left outside `params`, is rejected before it is saved. The prompt, the action list, and the request surface share one body sentence, and a test fails when those instructions drift from the controller.
- A regex block on a mapping keeps its pattern and replacement. They belong inside `params`; a body that put them next to the modifier type used to save an empty block.
- Changing a product’s category no longer keeps a connection looking synced when that category has several mappings and none is chosen. The connection status says the mapping is not selected.
- A background job started with an API key is attributed to that key, not to the person who created it. Operation logs show JSON on its own lines, and a product named in a log opens that product.
- Cabinet assistant strips emoji on a mapped field with one .NET regular expression and removes the old literal replacements on that field in the same step. A pattern written as `\u{…}` does not match, so the preview used to keep the original text.
- A cabinet assistant turn is not cut off after a few tool calls. A complex task keeps going until it ends in a sentence or a plan. A nudge such as «и?» still asks for that reply instead of another search. A regex block on a mapping names the field it edits.
- Catalog list, bulk edit, and shop export take one SQL `where`. Frequent slices are named templates (`@withGallery`, `@withoutGallery`, `@inStock`, `@outOfStock`, `@noPrice`, `@noBarcode`, `@noCategory`, and the file pair). The assistant copies those names. There is no separate “has photos” switch.
- Checking every product link on a connection runs in the background: it reads the shop catalog once, compares the saved external ids, and shows progress as its own operation. A Yandex Market page that returns an internal error no longer fails the whole check: that page is retried in smaller pieces, and any saved links still missing are looked up directly.
- The navbar Help button stays beside product search instead of covering it. The search narrows when the bar is tight.
- Cabinet assistant answers stay in the message. Tool steps stay on their own collapsed line, and the reply is not folded into that line.
- Markdown tables in docs and the assistant use the same flush rows, caption header, and row hover as cabinet tables.
- Wildberries price export reads the current card with a single-article lookup and treats a matching buyer price as already exported.
- Cabinet assistant runs a DeepSeek tool call instead of printing its markup in the reply.
- Wildberries card description is sent as plain text. Line breaks, quotes, and emoji no longer appear as `<br>` and `&#…;` on the marketplace.
- Wildberries export no longer fails while confirming a new card when the size id (`chrtID`) is larger than a 32-bit integer. Stock updates use the same wider id.
- Deleting a folder in store storage or on a product with the Delete key or the toolbar button uses the same confirmation as the folder menu, and the folder is removed. An empty folder no longer says that 0 files will be deleted.
- Wildberries export of a new card stays running until the article appears in the catalog or the draft error list names it (up to 30 minutes). It no longer fails after half a minute with “created but not found” while the marketplace has not yet said why.
- Wildberries export sends directory values such as TN VED and VAT as text. A numeric code sent as a number was accepted by the upload call and the card never appeared. Draft errors are read from the current error list instead of an empty page.
- Wildberries export sends packaging weight only as kilograms in the card dimensions. The characteristic «Вес товара с упаковкой» is no longer sent: the marketplace rejects it.
- Mapping blocks the assistant adds use the characteristic name and start collapsed. Two fields for the same fact, such as Brand and the brand characteristic, share one value. TN VED and VAT are chosen from that field’s directory when the goods match an entry.
- Mapping price block hides discounted price (`discountedPrice`, `sale_price`) while the store’s extended price settings are off. Opening the canvas no longer puts that row back. The cabinet assistant reads personal settings (`GET /api/ai/store-settings`: store description, style, brevity, emoji), converts units when a local characteristic is millimeters or grams and the shop field is centimeters or kilograms, and asks for brand, material, occasion, and TN VED instead of leaving them empty or inventing a code.
- Cabinet assistant treats «finish the mapping» and «link these parameter groups» as wiring marketplace fields to local characteristics. Shop collection titles stay a separate step. It reads the open canvas once before that plan, and a cancelled plan is not described as done.
- Ozon price export sends at most 1000 prices per Seller API request (stock updates use 100). A catalog larger than that limit no longer fails the whole job with `ImportProductsPricesRequest.Prices` validation.
- Cabinet assistant uses the calculation helper for catalog logic: comparisons, grouping, finding products by a rule, and working out a bulk change. A single count or one named card stays a normal catalog read. A change the person already spelled out still asks for confirmation first; a change that must be calculated may run a read-only check and then ask. Applying the change still waits for confirmation.
- Cabinet calculation sidecar runs only while AI is enabled. Turning `AI_ENABLED` off and redeploying stops the process; turning it on starts it. The API does not need a sandbox URL or a DNS alias: while AI is on it uses the container name `d-{stack}-sandbox` from `STACK_ID`, `inst_{stack}`, or `mysql-{stack}` / `minio-{stack}`. With AI off, the helper is omitted even if a URL is still set. A failed run is “temporarily unreachable”, not “this cabinet has no helper”.
- Asking whether the calculation helper exists is answered only; the assistant no longer resumes the previous catalog write or opens a plan. If AI is on, it says yes instead of claiming there is no helper.
- Cabinet assistant can raise every catalog price (`PATCH /products/bulk` with `allMatching`, `wait`, and `updates.priceDelta`) and then push those prices to shops (`POST /tools/export-all-prices/run` with `connectionId`). It no longer searches indefinitely for a missing “+500” route.
- Saving a cabinet assistant turn no longer returns HTTP 500 `Duplicate entry … IX_aiConversations_sessionId` when the live stream and history sync insert the same session at once.
- Cabinet file downloads no longer fail with HTTP 500 `Error while copying content to a stream` when MinIO closes the GET early or response compression wraps the proxy stream.
- Cabinet assistant shows a confirmable plan before mapping or catalog writes. It no longer runs GET graph / `search_actions` / POST in the same turn as `propose_plan`; writes wait until you confirm.
- Mapping-canvas «Correspondences» (`valueMap`) can sit on string fields such as Merge into card: you type both sides (group name → collection title). It is no longer limited to select/valueId. Medusa export uses that mapped title while cabinet group names stay.
- Cabinet assistant SQL steps show the SELECT (and SHOW/DESCRIBE) next to “SQL”, not only the row count. Expand the step for the full query above the result table.
- When the calculation sidecar is on, asking the assistant to check it (or run a read-only snippet) uses that helper instead of substituting a catalog COUNT and claiming there is no sandbox.
- Cabinet assistant export of local products to a shop is `export-batch` (a SQL `where`, or `{ allMatching }` for the whole catalog, so a full catalog does not need thousands of ids). Photos for that filter are gallery images (`product_images`), not any file attachment. Read-only SQL now honors `>=` `<=`, `NOT IN`, `SELECT DISTINCT` (including `contentType`), `MIN`/`MAX`, `OFFSET`, `IS NULL`, `SHOW TABLES` / `SHOW COLUMNS`, `product_images`, and extra store tables (notes, variants, links, operations) instead of ignoring them. Chat tools stay one collapsed line unless you expand them. `get_doc` can open indexed instance docs (including `docs/ai/operator-playbook.md`).

- Yandex Market Catalog Connect now reads the Market category from Partner API `mapping.marketCategoryId` when `offer.marketCategoryId` is missing or `0`, so linking can create a category mapping
- Catalog Connect `repair-mappings` copies marketplace characteristics after it creates the missing mapping (same as apply)
- Keyboard focus rings sit inside inputs, buttons, and dropdowns so the shell / navbar / sheet `overflow-hidden` no longer clips them
- Cabinet Help catalog (`GET /api/docs/catalog`) no longer treats Control Plane SPA HTML as JSON. Public docs origin must serve `/docs/catalog.json` (and `/docs/en/catalog.json`) as JSON, not `index.html`
- Order list thumbs for a linked catalog product use `/api/files/{id}/thumbnail`, not the marketplace URL (Yandex CDN / expired MinIO). Adapter photos stay only when the line has no local file
- Order line photos no longer keep expired MinIO/S3 presigned URLs (mapping export used a 2-hour `storage.printo.studio` signature). Linked products get `/api/files/{id}/thumbnail`; Yandex skips those seller `pictures` and uses Market-hosted `mediaFiles` instead
- Yandex Market offer-mappings photos: Partner API may send `pictures` as `{ url }` objects (not strings) and put `offerMappings` at the response root. Either used to drop the whole mapping, so order thumbs stayed empty. `shopSku` is used when `offerId` is missing
- Wildberries order and catalog photos used a stale CDN basket table, so newer nmIds 404'd (e.g. `1097975275` went to basket-46 instead of basket-42). Hosts now follow the live `cdn.wbbasket.ru` mediabasket map
- Yandex Market order (and catalog import) photos now read Partner API `mediaFiles.pictures[].url` when `pictures` is empty — Market-hosted card photos live there, not on the seller `pictures` list
- Marketplace product thumbs (`AppImage` / `ImageFile`) load with `referrerpolicy=no-referrer` so WB and Yandex CDNs are not blocked by the cabinet origin
- Cabinet mailbox “Load more” paginates stored snapshots by page (it used to send a live-API cursor and reload page 1)
- Cabinet AI chat shows attached file names on the sent message (they used to vanish from the composer before the bubble appeared)
- Cabinet `run_python` no longer returns HTTP 413 `invalid body`: the API sends a buffered JSON body with `Content-Length` (chunked `JsonContent` was empty to the sidecar)
- `sql_readonly` accepts `COUNT(*)` and `COUNT(DISTINCT col)` (not mixed with row columns)
- Failed API startup (port 5000 already in use) no longer crashes a second time while disposing catalog-scrape Chromium
- Cabinet `run_python` is omitted when `SANDBOX_URL` is unset (instead of advertising a tool that always fails). Local compose already publishes the sidecar on `8091`; set `SANDBOX_URL=http://127.0.0.1:8091` and `SANDBOX_AGENT_BASE_URL=http://host.docker.internal:5000`
- Yandex Market order lines include the product photo (`imageUrl`) from Partner API offer-mappings `pictures`. Order list/get have no photos; unlinked cards still get the marketplace CDN photo. A linked catalog product uses the local thumb instead
- Yandex Market order list no longer 500s when the cabinet mailbox sends normalized statuses (`new`, `awaiting_pack`). Those map to Partner API `UNPAID` / `PROCESSING` (Yandex has no `NEW` / `AWAITING_PACK`)
- Yandex Market order list no longer 500s when Partner API sends `updatedAt` as `dd-MM-yyyy HH:mm:ss` (the same cabinet date as `creationDate`)
- Order sheet (and Public API) packing label / barcode is only offered when that marketplace can print it: Ozon FBS in Ready to ship (`awaiting_deliver`), not Assembly or Delivery, and never FBO; Wildberries after the order is in a supply (`confirm`) or in delivery (`complete`); Yandex Market while packing (`PROCESSING`, FBS/DBS), not FBY. GET label at the wrong status → `400 order_label_failed`
- Order sheet (and Public API `deliver` / `complete`) no longer pretends Ozon or Wildberries can mark an in-transit order delivered. Those marketplaces record delivery themselves; the seller can pack and hand over the parcel (`pack` / `ship`), and cancel before handover. FBO / FBY have no seller status buttons. Shop adapters (Woo, OpenCart) still complete the order. Yandex DBS can mark delivered
- Searching an order by number keeps that row in the table (it used to reload the whole mailbox tab after opening the sheet). The typed Ozon order number (`30625619-0031`) also looks up the first posting (`…-1`)
- Ozon order search by the number in the table (`76728543-0063`) 404’d: that is the order number, while get/list used the posting number (`…-1`). An unmatched posting filter then returned the whole mailbox instead of that order
- Searching orders by number finds the order even when it sits in another mailbox (Assembly search used to query only that tab, so a Ready to ship id looked missing)
- Missing or 404 product photos (order mailboxes, catalog, scrape) show the empty photo placeholder instead of a broken image with “Product image” alt text
- Wildberries FBS order list showed the internal `rid` (`eP.…`) instead of the numeric order id, and line items had no title (the cabinet payload has no name). The id is the WB order number; titles come from the storefront card, or the local linked product when present.
- Ozon order lines include the product photo (`imageUrl`) from Seller API product info. The posting list does not send images; unlinked cards still get the marketplace CDN photo. Local linked photos remain the fallback.
- Ozon order list maps cabinet statuses (`confirmed`, `awaiting_pack`, …) to Ozon posting aliases instead of sending them raw (that was a 500)
- Plugin host no longer copies or loads adapter test assemblies (`*Tests.dll`). WooCommerce unit-test DLLs were treated as a plugin on local `pnpm dev`
- Local `pnpm dev` starts Vite immediately. Waiting for `/api/health` (3 minutes) often expired while `dotnet watch` was still compiling, so the web pane never started. The Vite proxy answers `503` while port 5000 is closed instead of logging `ECONNREFUSED`.
- Local `dotnet watch` no longer selects a preview SDK (10 preview stuck on GenerateWatchList and never bound `:5000`). `global.json` rolls forward only on stable SDKs; plugin copy is skipped during design-time / watch-list builds. GenerateWatchList also excludes `backend/artifacts` (and other local dump folders) so MSBuild does not glob every test output file.
- Database dump (`GET /settings/database/dump`) is limited to the instance owner (`settings.dump`). A demo `workspace.view` user can no longer download it.
- Collapsed product-group rail: hovering a variant photo opens the preview beside the tile, not in the top-left of the screen
- Right-clicking another product closes the previous row menu (only one context menu at a time)
- Public API `GET` category now returns a real `productsCount` (detail always showed 0 because the product collection was not loaded)
- Public API request logs fill the page height without clipping the footer or the “turn on Public API logs” hint
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
- Align `System.IdentityModel.Tokens.Jwt` with Fido2's IdentityModel 8 stack so access-token validation sees `exp` again
- Apply security headers before the rest of the pipeline so empty responses still get `X-Robots-Tag`
- Sync `package-lock.json` with passkey/QR deps so frontend Docker `npm ci` succeeds

### Removed

- Optional `match_files` assistant tool and its Agent HTTP endpoint. Staged chat files can still be attached to a product through the confirmed attachment flow.
- Pause / Resume on background operations (jobs are running or stopped)
- Unused `PageHeader` primitive — page titles are the navbar trail

### Security

- A catalog `where` is not sent to the database as SQL. Named templates expand to a fixed predicate, and any other text is accepted only as an allowlisted comparison. Statement separators, comments, and a predicate the server cannot apply are rejected. Values stay query parameters.
- Cabinet frontend uses axios 1.20.0, DOMPurify 3.4.16, and brace-expansion 1.1.21. Those are the versions that close the open dependency alerts.
- Update Vue I18n to 11.4.16 and pin MySqlConnector to 2.6.1 to address published dependency advisories.

- Pin `Microsoft.Bcl.Memory` to 9.0.19 (CVE-2026-26127). Fido2 4.0 was pulling the vulnerable 9.0.0 transitive.
- Python sandbox cannot pin the host: Compose caps it at half a CPU and 512 MiB RAM (no extra swap). Jobs run one at a time, numpy/BLAS stays single-threaded, and each job has a memory/CPU-time ceiling

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
- Catalog scrape tool (`/tools/catalog-scrape`): search a public storefront on Ozon, Wildberries, or Yandex Market (HTTP JSON first, HTML JSON-LD if the site returned a page), tick cards, and copy them into the local catalog without linking; a later export creates new listings instead of updating someone else’s card. Antibot/captcha is `storefront_blocked`, not a 500. WooCommerce and OpenCart stay cabinet import only
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
- Settings pages (User, Stores, Users, API, Plugins, AI) use the same Apple inset grouped lists, Field stacks, and flush tables as Administration
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
