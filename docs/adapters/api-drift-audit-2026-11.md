# Adapter API drift audit — November 2026

Audit of every outbound API call in the marketplace adapter plugins against the
currently published official API documentation. No code was changed; findings
only. Generated 2026-11-15.

## Reference documents used

| Adapter | Document | Notes |
| --- | --- | --- |
| Wildberries | `dev.wildberries.ru/api/swagger/yaml/ru/02-items.yaml`, `03-orders-fbs.yaml` | Official OpenAPI exports. Covers Content API, Prices & Discounts, FBS orders/supplies. **Does not cover** the promotions-calendar API (`dp-calendar-api`). |
| Ozon | `swagger.json` mirrored at `raw.githubusercontent.com/miilv/ozon-seller-api-skill` | Official Seller API swagger export (380 paths). `docs.ozon.ru` blocks datacenter fetches; findings on promo endpoints should be verified against the live portal before acting. |
| Yandex Market | `yandex.ru/dev/market/partner-api/doc/ru/llms-full.txt` | Official full-text docs export. |
| Avito | `avito-api-openapi.json` mirrored at `raw.githubusercontent.com/MissiaL/avito-api` | Official OpenAPI snapshot (03.10.2026), 248 paths. |
| Medusa, WooCommerce, OpenCart | — | Bridge adapters; see §"Bridge adapters". |

## Wildberries (`Delegat.Marketplace.Wildberries`)

~44 calls inventoried in `WildberriesApiClient.cs` and `WildberriesPlugin.cs`,
across four domains: `content-api`, `discounts-prices-api`, `marketplace-api`,
`dp-calendar-api`.

### Drift findings

1. **`PATCH /api/v3/supplies/{supplyId}/orders` (AddOrdersToSupply)** —
   no longer in the spec. The spec now documents
   `PATCH /api/marketplace/v3/supplies/{supplyId}/orders` (same semantics: add
   assembly orders to a supply). The old `/api/v3/` path likely still works
   (WB keeps compatibility), but it is undocumented — risk of silent removal.

2. **`GET /api/v3/supplies/{supplyId}/order-ids` (GetSupplyOrderIds)** —
   no longer in the spec. Documented replacement:
   `GET /api/marketplace/v3/supplies/{supplyId}/order-ids`.

3. **`PATCH /api/v3/supplies/{supplyId}/trbx/{trbxId}` (AssignOrdersToTrbxAsync)** —
   not in the spec at all. The spec keeps `GET/POST/DELETE
   /api/v3/supplies/{id}/trbx` and `POST .../trbx/stickers`, but the PATCH used
   to assign orders into a box is gone. There is no documented equivalent;
   `/api/marketplace/v3/fbs/supplies/{supplyId}/orders/transfer` moves orders
   between supplies, not into boxes. Needs verification against production —
   this may already be returning 404.

4. **Promotions calendar (`dp-calendar-api.wildberries.ru`)** —
   `GET /api/v1/calendar/promotions`,
   `GET /api/v1/calendar/promotions/nomenclatures`,
   `POST /api/v1/calendar/promotions/upload`. The published spec exports no
   longer include a promotions/calendar section at all, so these calls cannot
   be verified against docs. They may be silently removed from documentation
   while still working, or scheduled for shutdown — worth confirming in the
   seller portal release notes.

### Verified clean

All Content API calls (`/content/v2/*`, `/content/v3/media/save`), prices &
discounts (`/api/v2/list/goods/filter` GET+POST incl. `nmList`/`filterNmID`,
`/api/v2/upload/task`, `/api/v2/upload/task/size`), stocks
(`PUT/POST /api/v3/stocks/{warehouseId}` — both methods documented),
warehouses, offices, and all `/api/v3/orders*` calls including
`PATCH /api/v3/orders/{orderId}/cancel`. None of the used methods are marked
`deprecated: true` in the spec.

### Unverifiable by design

The public-catalog scraper (`WbPublicCatalogClient`) hits undocumented
storefront endpoints: `card.wb.ru/cards/v4/detail` and
`basket-{n}.wbbasket.ru/vol{vol}/part{part}/{nmId}/...`. These have no spec and
can break or get rate-limited at any time — inherent fragility, monitor for
failures.

## Ozon (`Delegat.Marketplace.Ozon`)

27 calls inventoried in `OzonApiClient.cs` and `OzonPlugin.cs` against
`api-seller.ozon.ru`.

### Drift findings (promotions)

The three promo calls target paths absent from the current swagger:

| Adapter call | In current spec |
| --- | --- |
| `POST /v2/actions/products` | `POST /v1/actions/products` |
| `POST /v1/actions/products/update` | `POST /v1/actions/products/activate` |
| `POST /v2/actions/products/deactivate` | `POST /v1/actions/products/deactivate` |

`GET /v1/actions` (list promotions) is unchanged and correct.

Impact: promotion management (`GetActionProductsAsync`,
`ActivateActionProductsAsync`, `DeactivateActionProductsAsync`) presumably
still works via the legacy paths — but if Ozon has retired them these calls
would already be failing. Needs a live check; if alive, plan a migration to the
`/v1/actions/products*` family. Caveat: the reference swagger is an official
export hosted on a community mirror — confirm on the portal.

### Verified clean

Category/description tree (`/v1/description-category/*`), product list/info/
import (`/v3/product/*`, `/v4/product/info/*`, `/v5/product/info/prices`),
archive/offer-id updates, stock upload (`/v2/products/stocks`), warehouses,
product limits, all FBS/FBO posting calls incl. `POST /v2/posting/fbs/
package-label`, and the request-field shapes spot-checked
(`description_category_id`/`type_id`/`language`) match the spec.

### Unverifiable by design

`OzonPublicCatalogClient` scrapes `www.ozon.ru/api/composer-api.bx/page/json/v2`
— an undocumented storefront endpoint, same fragility class as WB's scraper.

## Yandex Market (`Delegat.Marketplace.YandexMarket`)

19 calls inventoried in `YandexMarketApiClient.cs` and
`YandexMarketPlugin.cs` against `api.partner.market.yandex.ru`.

### Verified clean — no drift found

Every call matches a documented method: `GET /v2/campaigns`,
`POST /v2/auth/token`, `POST /v2/categories/tree`,
`POST /v2/category/{id}/parameters`, the `offer-mappings`/`offer-cards`/
`offer-prices` family incl. `/archive`, `POST/PUT
/v2/campaigns/{id}/offers/stocks`, the orders family
(`/v2/campaigns/{id}/orders*`, `PUT .../orders/{id}/status`,
`GET .../delivery/labels`), and the promos family
(`/v2/businesses/{id}/promos*`).

### Notes

- **Auth**: adapter sends `Api-Key` header — the method the docs now require;
  the docs explicitly mark OAuth as deprecated ("устаревший"). Compliant.
- **Content model**: adapter sends `parameterValues` + `marketCategoryId` —
  the current schema; the deprecated `params`/`category` fallback is not used.
- **Pagination**: orders listing uses `page`/`pageSize`. The docs deprecate
  `page`-style pagination in favor of `pageToken` "where both are available";
  for the orders endpoint `page` remains the documented mechanism, but this is
  a trend worth watching.
- `YandexPublicCatalogClient` scrapes undocumented storefront endpoints
  (`market.yandex.ru/api/search`, `/api/resolve/`, `/card/{id}`) — same
  fragility class as above.

## Avito (`Delegat.Marketplace.Avito`)

7 calls inventoried in `AvitoApiClient.cs` against `api.avito.ru`.

### Verified clean — no drift found

- `POST /token` (`client_credentials`) — documented.
- `GET /core/v1/accounts/self`, `GET /core/v1/items` — documented.
- `GET /autoload/v1/user-docs/tree`, `GET /autoload/v1/user-docs/node/{slug}/fields`
  — documented (autoload field specs).
- `POST /core/v1/items/{id}/update_price` — documented; request body
  `{"price": int}` matches `UpdatePriceRequest` (`price` required, integer).

### Notes

- The read-only sandbox broker (`ReadOnlyBrokerAsync`) proxies arbitrary GET
  paths under `api.avito.ru` — by design; path validation and host allowlist
  are in place.
- The spec mirror does not declare query parameters for `GET /core/v1/items`;
  the adapter's `page`/`per_page`/`status` params could not be verified from
  the spec alone.

## Bridge adapters — docs not applicable

`Delegat.Marketplace.Medusa`, `Delegat.Marketplace.WooCommerce` and
`Delegat.Marketplace.OpenCart` do **not** call the platform's public API:

- Medusa → merchant's own store via the Delegat bridge plugin
  (`/admin/delegat/*`: `health`, `categories`, `products/upsert`,
  `products/bulk`, `prices/bulk`, `stocks/bulk`, `groups*`, `orders*`).
- WooCommerce → `wp-json/delegat/*` (Delegat WP plugin).
- OpenCart → `index.php?route=extension/delegat/api` (Delegat OC extension).

For these, "the adapter matches official docs" is not meaningful — the contract
is Delegat's own bridge protocol, versioned together with the bridge plugins.
Their integration surface should be audited against the bridge plugin versions
instead.

**Follow-up note for the platform:** the `marketplace-docs` snapshot for
`medusa` (`docs.medusajs.com/llms-full.txt`) shipped to instances describes the
public Medusa API, which the adapter never calls — agents reading it may be
misled. Consider replacing it with a description of the Delegat bridge
protocol, or dropping the source.

## Summary

| Adapter | Calls | Clean | Drift | Unverifiable |
| --- | --- | --- | --- | --- |
| Wildberries | ~44 | ~37 | 3 (supplies orders, order-ids, trbx assign) | 3 calendar + public catalog |
| Ozon | 27 | 24 | 3 (promo paths → `/v1/actions/products*`) | public catalog scraper |
| Yandex Market | 19 | 19 | 0 | public catalog scraper |
| Avito | 7 | 7 | 0 | — |
| Medusa / WooCommerce / OpenCart | — | bridge protocol | n/a | n/a |

Priority items to verify live: WB trbx assign (likely broken or silently
deprecated), Ozon promo paths, WB calendar domain. Everything else is aligned
with the current published documentation.
