# Marketplace credential broker for the AI sandbox

## Objective

Keep Marketplace reads and writes available to `run_python` workflows while preventing arbitrary sandbox code from receiving stored API credentials. Migrate in small steps. Do not remove the existing credential payload or change sandbox behavior until compatibility has been checked against real supported Marketplace workflows.

## Current state

- `PythonSandboxClient.RunAsync` reads `MARKETPLACE_BROKER_SANDBOX_STORES` for store-scoped rollout. In selected stores, adapters that expose broker capabilities receive only connection metadata; unsupported adapters/origins remain explicitly marked `access=legacy`. With the flag unset, the original credentials payload remains unchanged.
- `sandbox/python/server.py` exposes those values to the user script through `shops` / `DELEGAT_SHOPS`. The script can make direct outbound HTTP calls.
- The legacy shop descriptor now includes Avito's fixed `api` origin and read-only OAuth/path guidance. This is additive metadata; the client ID and secret are still delivered through the existing `credentials` field, and this does not wire the sandbox to the broker.
- Stdout/stderr redaction only protects the response shown to the model; it is not an egress or secret-isolation boundary.
- `MarketplaceCredentialBroker` handles server-side authenticated GET requests and adapter-defined POST-read operations. Wildberries, Ozon, and Yandex use fixed host/auth rules; Avito, WooCommerce, OpenCart, Medusa, and future adapters opt in through SDK capabilities. Custom origins use the shared public-HTTPS, DNS-pinned transport. Agent endpoints are rate-limited and store-scoped; redirects are disabled and responses are size-limited. The broker does not support writes.
- The shared Marketplace SDK now has the optional `IMarketplaceSandboxReadBroker` capability. `PluginAdapterWrapper` bridges it to the host broker; older plugins remain loadable, and future plugins can opt in without adding another adapter-key branch to the central service.
- The SDK now also has an optional named `IMarketplaceSandboxReadOperationBroker` capability and `client.shop_read(connection_id, operation, arguments)` helper for adapter-allow-listed POST reads. Operations are discovered per connection with `client.shop_operations`; the adapter owns names, descriptions, schemas, argument validation, paths, and bodies. Ozon exposes category tree/attributes/values, product list/info/attributes, prices, stocks, warehouses, and upload quota; Wildberries exposes product-card listing; Yandex exposes category tree/parameters, offer mappings/cards, prices, stocks, and promotions.
- The sandbox SDK has `client.shop_request(connection_id, host, path)`, `client.shop_operations(connection_id)`, and `client.shop_read(connection_id, operation, arguments)` helpers. Prompts switch to these helpers only for selected stores and broker-capable connections; unsupported connections retain the legacy descriptor.
- Rollout is controlled by `MARKETPLACE_BROKER_SANDBOX_STORES` (comma/semicolon-separated store IDs; `*` selects all). It is off when unset. No production store has been enabled from this repository, and no live seller request was made.

## Migration phases

1. **Read-only broker and sandbox wiring (implemented; rollout opt-in).** Test fixed host selection, server-side auth headers, store scoping, path/operation validation, response limits/redaction, audit, per-connection operation discovery, credential-free sandbox descriptors for capable adapters, and broker-aware prompts. The opt-in is deliberately not enabled by default.
2. **Compatibility inventory (repository scan complete; live workflow corpus still needed).** The repository contains no production sandbox scripts that consume `shops`; the only executable example is a test reading a Wildberries host. The current system prompt documents these patterns:
   - Wildberries: `content`, `prices`, `marketplace`, `calendar` fixed hosts; raw `Authorization` credential; sandbox/production selected from the explicit setting or token claim; docs snapshot slug `wildberries`.
   - Ozon: `seller` fixed host; `Client-Id` and `Api-Key`; docs snapshot slug `ozon`.
   - Yandex Market (`yandex` / `yandexmarket`): `partner` fixed host; `Api-Key`; docs snapshot slug `yandex`.
   - Avito: fixed `api.avito.ru`; OAuth client-credentials exchange (`clientId` + `clientSecret`) followed by Bearer auth; docs snapshot is not currently promised.
   - WooCommerce / OpenCart / Medusa: user-configured `apiBaseUrl`; WooCommerce and OpenCart use key + Bearer and `X-Delegat-Api-Key`, Medusa uses Basic auth; no daily docs snapshot is promised. All three now have opt-in broker providers. Medusa `http://localhost` and other non-public HTTP origins remain legacy-only.
   - Any future adapter is unsupported by the broker until it implements the optional SDK capability and its tests. It must not inherit generic arbitrary-URL forwarding by default.
   - Legacy-mode Marketplace lookup instructions still describe direct `urllib` reads; broker-mode prompts use the read helpers. Pagination and response parsing are workflow-specific; there are no checked-in redacted production fixtures establishing exact response shapes or size needs. `client.marketplace_doc` remains a separate Agent HTTP call.
   - **Read-method coverage:** named operations now cover the checked-in first-party POST-read workflows for WB card listing, Ozon catalog/category/price/stock/quota reads, and Yandex category/offer/price/stock/promotion reads. Other POST reads should be added only when a supported workflow needs them. No arbitrary method/path/body forwarding is allowed.
3. **SDK compatibility layer and per-store rollout (implemented; opt-in).** The helpers return the broker envelope (`status`, `body`, `truncated`) or operation descriptors. Adapter opt-in is discovered dynamically through SDK capabilities; no central adapter-specific dispatch list is required. Broker-capable connections omit credential values from `shops`; unsupported adapters and local/private custom origins remain legacy. `MARKETPLACE_BROKER_SANDBOX_STORES` selects rollout stores.
4. **Read-only canary (still required per selected store).** Enable one store only after its real workflow fixtures pass; compare returned facts/results with legacy behavior. Do not duplicate writes for shadow comparison. Preserve `docs.platform` lookup and keep rollback available by removing the store ID from the flag.
5. **Confirmed writes.** Add broker operations only after read compatibility is established. The broker must receive a short-lived, server-minted capability bound to user, store, connection, and a consumed/confirmed plan. Ordinary sandbox runs must remain read-only at the broker even if their script calls the endpoint directly. Never trust a prompt instruction as write authorization.
6. **Custom adapters.** Initial WooCommerce, OpenCart, and Medusa providers plus the shared broker-only custom-origin transport are implemented. Adapter-specific compatibility fixtures and live workflow checks are still pending. Each adapter opts in independently; do not change legacy connection behavior or enable the sandbox broker until those checks pass.
7. **Credential removal.** Once workflows pass acceptance tests and canary telemetry is clean, stop serializing credentials to the sidecar, remove direct-HTTP prompt instructions, then remove the compatibility flag and legacy path in a separate release.

## Security and compatibility acceptance criteria

- Sandbox never receives Marketplace keys, generated auth headers, database secrets, or unrestricted broker capabilities.
- A normal/read-only script cannot perform broker writes; a write is accepted only with a valid one-use confirmation capability.
- Host selection is server-owned. Caller cannot supply a URL, host, port, redirect target, arbitrary headers, or HTTP method.
- Store and connection are checked on every call; inactive, missing, or cross-store connections fail closed.
- Broker has request timeouts, response byte limits, per-user/store throttling, safe audit metadata, and credential redaction.
- Existing seller-specific reads, lookups, pagination, docs snapshot behavior, and confirmed writes pass compatibility tests before the sandbox is switched.
- Feature flag rollback restores the legacy path without migration or credential re-entry.

## Custom-origin broker policy

This is implemented by the optional SDK transport and applies only to the new server-side broker, not to existing Marketplace connections or their legacy sandbox payloads:

- Require an absolute HTTPS URL on port 443; reject IP literals, user-info, fragments, and non-public or special-use destinations. A DNS lookup must return only public addresses, and the broker HTTP connection must pin the validated address rather than resolving the hostname again during connect. The existing `UrlSafety` DNS check alone is insufficient because it does not pin the checked address.
- Disable redirects. Path reads use GET; upstream POST is allowed only through a named adapter operation that constructs a fixed route and validated JSON body. Never forward caller-supplied URLs, headers, methods, or raw request bodies. Credentials and auth headers are generated server-side and are not included in audit records.
- Keep URL composition adapter-owned: WooCommerce and Medusa preserve the configured bridge path; OpenCart handles both path-action and query-route base URLs without allowing a caller to replace the origin or base route. Do not use response-provided URLs as follow-up targets.
- Medusa configurations such as `http://localhost:9000/admin/delegat` remain supported by the current legacy path but are intentionally ineligible for this broker policy. A future private-network broker needs a separate explicit deployment policy, not a general exception to public-origin validation.
- Before enabling any provider, complete fixtures for its credential schema, base-path/query-route composition, auth headers, redirects, private/literal-IP rejection, DNS rebinding/pinning, response cap, and secret redaction. Current helper tests cover origin/path composition, traversal, request headers, and response caps; DNS and real sandbox workflow fixtures remain migration gates.

## Read-workflow fixture inventory

This inventory is derived from checked-in prompts, adapter clients, and bridge documentation. It is **not** evidence of production sandbox usage or a live canary. No captured workflow fixtures are checked in yet.

- **Wildberries:** documented hosts are `content`, `prices`, `marketplace`, and `calendar`; auth is raw `Authorization` and environment selection depends on the configured sandbox setting/token claim. GET price filter (`/api/v2/list/goods/filter?limit=…&offset=…`) uses `shop_request`; product-card listing (`product_cards`) is now a named POST-read operation with bounded filter/cursor fields. Fixture still needed: seller lookup with docs excerpt, card pagination/cursor, and one price fact.
- **Ozon:** fixed `seller` host; `Client-Id` and `Api-Key`. Named operations now cover product list/info/attributes, category tree/attributes/values, prices, stocks, warehouses, and upload quota. Fixture still needed: paginated product lookup, category/attribute resolution, price/stock lookup, and the docs excerpt used to choose each request.
- **Yandex Market:** fixed `partner` host; `Api-Key`. Campaign list and order list/detail use GET; named operations now cover category tree/parameters, offer mappings/cards, prices, stocks, and promotions. Fixture still needed: campaign context, one product/card fact, one price or stock fact, and page continuation if used.
- **Avito:** fixed `api` host; broker performs the OAuth client-credentials exchange server-side. GET candidates are `/core/v1/accounts/self`, `/core/v1/items?page=…&per_page=…&status=…`, `/autoload/v1/user-docs/tree`, and `/autoload/v1/user-docs/node/{slug}/fields`. Fixture needs: account/listing/category-field flows, including the exact `resources` envelope and pagination behavior. Avito-provided field-value links must remain same-host/path-only.
- **WooCommerce:** custom bridge origin and configured base path; Bearer plus `X-Delegat-Api-Key`. GET candidates include `/categories`, `/categories/{id}/fields`, `/products/{id}`, `/prices`, `/orders`, and `/promotions`. Fixture needs: one product fact, one category field/value, and a paginated list response; record bridge version because the bridge contract evolves independently.
- **OpenCart:** custom bridge origin; path-style and query-route base URLs; Bearer plus `X-Delegat-Api-Key`. GET candidates include categories, category fields, products, orders, and promotions. Query-route dynamic reads must match bridge actions: `/categories/{id}/fields` → `action=category.fields&category_id={id}`, `/orders/{id}` → `action=order&order_id={id}`, `/promotions/{id}` → `action=promotion&promotion_id={id}`. Fixture both bridge URL forms and verify query preservation plus pagination.
- **Medusa:** custom bridge origin/base path; Basic auth; private/HTTP origins (including documented localhost) are intentionally legacy-only. GET candidates include categories/fields, products, groups, and orders. Fixture one product fact, one category field, and one order-list page on a public HTTPS test bridge; do not treat localhost success as broker compatibility.

Each fixture should state adapter + connection mode/bridge version, user workflow, exact broker host/path and pagination inputs, sanitized status/body, facts the workflow must extract, and whether `truncated` is acceptable. Never store credentials, auth headers, OAuth tokens, customer PII, or unsanitized seller data. Start with synthetic contract fixtures from existing adapter tests; mark them separately from redacted captures of real supported workflows. Do not claim canary readiness until the latter exist for every workflow being migrated.

## Not in scope for the first broker increment

- Switching `PythonSandboxClient` away from `shops` credentials.
- Enabling writes through the broker.
- Generic URL forwarding or support for arbitrary storefront hosts.
- Removing the legacy path or changing Marketplace user-facing access.
